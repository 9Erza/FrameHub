using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using FrameHub.Companion.Authentication;
using FrameHub.Companion.Models;
using FrameHub.Companion.Persistence;
using FrameHub.Companion.Providers;
using FrameHub.Companion.RateLimiting;
using FrameHub.Companion;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Companion.Tests;

[TestClass]
public sealed class SessionCpuApiTests
{
    private string _tempDir = null!;
    private DeviceRecordStore _store = null!;

    [TestInitialize]
    public void Initialize()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FrameHubSessionCpuApiTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _store = new DeviceRecordStore(Path.Combine(_tempDir, "devices.json"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    // ------------------------------------------------------------------
    // Middleware authorization (direct invocation, LAN and loopback policy)
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task Middleware_LanCpuGetRequiresReadOptimizationCpuScope()
    {
        Assert.AreEqual(StatusCodes.Status401Unauthorized,
            await InvokeMiddlewareAsync(HttpMethods.Get, token: null, path: "/api/v1/session-optimization/cpu"));
        string wrong = AddDevice(CompanionScopes.ReadOptimization);
        Assert.AreEqual(StatusCodes.Status403Forbidden,
            await InvokeMiddlewareAsync(HttpMethods.Get, wrong, path: "/api/v1/session-optimization/cpu"),
            "read:optimization must NOT authorize the dedicated session CPU read scope.");
        string allowed = AddDevice(CompanionScopes.ReadOptimizationCpu);
        Assert.AreEqual(StatusCodes.Status204NoContent,
            await InvokeMiddlewareAsync(HttpMethods.Get, allowed, path: "/api/v1/session-optimization/cpu"));
    }

    [TestMethod]
    public async Task Middleware_CpuPostRequiresWriteScopeEvenOnLoopback()
    {
        // No localhost write bypass: unauthenticated loopback POST is rejected.
        Assert.AreEqual(StatusCodes.Status401Unauthorized,
            await InvokeMiddlewareAsync(HttpMethods.Post, token: null, path: "/api/v1/session-optimization/cpu", loopback: true));

        // A read-only session CPU device is still forbidden from mutating.
        string readOnly = AddDevice(CompanionScopes.ReadOptimizationCpu);
        Assert.AreEqual(StatusCodes.Status403Forbidden,
            await InvokeMiddlewareAsync(HttpMethods.Post, readOnly, path: "/api/v1/session-optimization/cpu", loopback: true));

        // Even the general write:optimization scope must not reach CPU mutation.
        string generalWrite = AddDevice(CompanionScopes.ReadOptimization, CompanionScopes.WriteOptimization);
        Assert.AreEqual(StatusCodes.Status403Forbidden,
            await InvokeMiddlewareAsync(HttpMethods.Post, generalWrite, path: "/api/v1/session-optimization/cpu", loopback: true),
            "write:optimization must NOT authorize session CPU mutation.");

        string allowed = AddDevice(CompanionScopes.ReadOptimizationCpu, CompanionScopes.WriteOptimizationCpu);
        Assert.AreEqual(StatusCodes.Status204NoContent,
            await InvokeMiddlewareAsync(HttpMethods.Post, allowed, path: "/api/v1/session-optimization/cpu", loopback: true));
    }

    [TestMethod]
    public async Task Middleware_CpuResetRequiresWriteScopeEvenOnLoopback()
    {
        Assert.AreEqual(StatusCodes.Status401Unauthorized,
            await InvokeMiddlewareAsync(HttpMethods.Post, token: null, path: "/api/v1/session-optimization/cpu/reset", loopback: true));
        string readOnly = AddDevice(CompanionScopes.ReadOptimizationCpu);
        Assert.AreEqual(StatusCodes.Status403Forbidden,
            await InvokeMiddlewareAsync(HttpMethods.Post, readOnly, path: "/api/v1/session-optimization/cpu/reset", loopback: true));
        string allowed = AddDevice(CompanionScopes.ReadOptimizationCpu, CompanionScopes.WriteOptimizationCpu);
        Assert.AreEqual(StatusCodes.Status204NoContent,
            await InvokeMiddlewareAsync(HttpMethods.Post, allowed, path: "/api/v1/session-optimization/cpu/reset", loopback: true));
    }

    [TestMethod]
    public async Task Middleware_LoopbackCpuGetRemainsOpenLikeOtherReadEndpoints()
    {
        Assert.AreEqual(StatusCodes.Status204NoContent,
            await InvokeMiddlewareAsync(HttpMethods.Get, token: null, path: "/api/v1/session-optimization/cpu", loopback: true));
    }

    // ------------------------------------------------------------------
    // Full server integration with a fake provider
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task SessionCpuEndpoints_LoopbackAccessPolicy()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_store);
        server.ConfigureSessionOptimizationProvider(new FakeSessionCpuProvider());

        Assert.IsTrue(await server.StartAsync(new CompanionOptions { Enabled = true, Port = port }));
        using var client = new HttpClient();

        // 1. GET on loopback is readable without auth (established read-only loopback policy).
        var getResp = await client.GetAsync($"http://127.0.0.1:{port}/api/v1/session-optimization/cpu");
        Assert.AreEqual(HttpStatusCode.OK, getResp.StatusCode);
        string json = await getResp.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        Assert.IsTrue(document.RootElement.TryGetProperty("sessionToken", out _));
        Assert.IsTrue(document.RootElement.TryGetProperty("topology", out _));
        Assert.IsTrue(document.RootElement.TryGetProperty("source", out _));

        // 2. POST unauthenticated => 401 even on loopback.
        var postUnauth = await client.PostAsJsonAsync($"http://127.0.0.1:{port}/api/v1/session-optimization/cpu",
            new CompanionSessionCpuApplyRequestDto { SessionToken = Guid.NewGuid().ToString("N"), Mode = "affinity", Indices = [0, 1] });
        Assert.AreEqual(HttpStatusCode.Unauthorized, postUnauth.StatusCode);

        // 3. POST with read-only session CPU scope => 403.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AddDevice(CompanionScopes.ReadOptimizationCpu));
        var postForbidden = await client.PostAsJsonAsync($"http://127.0.0.1:{port}/api/v1/session-optimization/cpu",
            new CompanionSessionCpuApplyRequestDto { SessionToken = Guid.NewGuid().ToString("N"), Mode = "affinity", Indices = [0] });
        Assert.AreEqual(HttpStatusCode.Forbidden, postForbidden.StatusCode);

        var resetForbidden = await client.PostAsJsonAsync($"http://127.0.0.1:{port}/api/v1/session-optimization/cpu/reset",
            new CompanionSessionCpuResetRequestDto { SessionToken = Guid.NewGuid().ToString("N") });
        Assert.AreEqual(HttpStatusCode.Forbidden, resetForbidden.StatusCode);

        // 4. POST with write scope succeeds.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            AddDevice(CompanionScopes.ReadOptimizationCpu, CompanionScopes.WriteOptimizationCpu));
        var postAllowed = await client.PostAsJsonAsync($"http://127.0.0.1:{port}/api/v1/session-optimization/cpu",
            new CompanionSessionCpuApplyRequestDto { SessionToken = Guid.NewGuid().ToString("N"), Mode = "affinity", Indices = [0, 2] });
        Assert.AreEqual(HttpStatusCode.OK, postAllowed.StatusCode);

        var resetAllowed = await client.PostAsJsonAsync($"http://127.0.0.1:{port}/api/v1/session-optimization/cpu/reset",
            new CompanionSessionCpuResetRequestDto { SessionToken = Guid.NewGuid().ToString("N") });
        Assert.AreEqual(HttpStatusCode.OK, resetAllowed.StatusCode);
    }

    // ------------------------------------------------------------------
    // Contract shape: the remote request can never choose a process target.
    // ------------------------------------------------------------------

    [TestMethod]
    public void SessionCpuRequestDto_ContainsNoProcessTargetingFields()
    {
        string[] forbidden =
        [
            "pid", "processid", "processname", "pathname", "path", "executable", "executablepath",
            "commandline", "priority", "starttime", "target", "process"
        ];

        foreach (var dtoType in new[] { typeof(CompanionSessionCpuApplyRequestDto), typeof(CompanionSessionCpuResetRequestDto) })
        {
            foreach (var property in dtoType.GetProperties())
            {
                string name = property.Name.ToLowerInvariant();
                Assert.IsFalse(forbidden.Contains(name),
                    $"{dtoType.Name} must not expose '{property.Name}'; remote callers can never select a process target.");
            }
        }
    }

    [TestMethod]
    public async Task Frontend_ExposesSessionCpuEditorAndBothLanguageDictionaries()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_store);
        Assert.IsTrue(await server.StartAsync(new CompanionOptions { Enabled = true, Port = port }));
        using var client = new HttpClient();

        string html = await client.GetStringAsync($"http://127.0.0.1:{port}/index.html");
        // Separate cards
        StringAssert.Contains(html, "id=\"session-optimization-section\"");
        StringAssert.Contains(html, "id=\"game-cpu-section\"");
        StringAssert.Contains(html, "data-i18n=\"optimization.title\"");
        StringAssert.Contains(html, "data-i18n=\"optimization.cpu.title\"");

        // CPU editor and controls
        StringAssert.Contains(html, "opt-cpu-editor");
        StringAssert.Contains(html, "opt-cpu-chips");
        StringAssert.Contains(html, "optimization.cpu.sessionOnlyNotice");
        StringAssert.Contains(html, "btn-apply-cpu");
        StringAssert.Contains(html, "btn-restore-cpu");

        // Presets & Recommended badge
        StringAssert.Contains(html, "btn-cpu-preset-all");
        StringAssert.Contains(html, "btn-cpu-preset-physical");
        StringAssert.Contains(html, "btn-cpu-preset-clear");
        StringAssert.Contains(html, "optimization.cpu.recommended");
        StringAssert.Contains(html, "opt-cpu-mode-helper");
        StringAssert.Contains(html, "opt-cpu-feedback");

        string i18n = await client.GetStringAsync($"http://127.0.0.1:{port}/js/i18n.js");
        Assert.AreEqual(2, Count(i18n, "'optimization.cpu.title':"));
        Assert.AreEqual(2, Count(i18n, "'optimization.cpu.sessionOnlyNotice':"),
            "EN and PL dictionaries must both explain that session changes are not saved to the profile.");
        Assert.AreEqual(2, Count(i18n, "'optimization.cpu.edit':"));
        Assert.AreEqual(2, Count(i18n, "'optimization.cpu.protected':"));
        Assert.AreEqual(2, Count(i18n, "'optimization.cpu.presetAll':"));
        Assert.AreEqual(2, Count(i18n, "'optimization.cpu.presetPhysical':"));
        Assert.AreEqual(2, Count(i18n, "'optimization.cpu.presetClear':"));
        Assert.AreEqual(2, Count(i18n, "'optimization.cpu.recommended':"));
        Assert.AreEqual(2, Count(i18n, "'optimization.cpu.cpuSetsHelper':"));
        Assert.AreEqual(2, Count(i18n, "'optimization.description':"));

        string js = await client.GetStringAsync($"http://127.0.0.1:{port}/js/session-optimization.js");
        StringAssert.Contains(js, "getPhysicalProcessorIndices");
        StringAssert.Contains(js, "handlePresetAll");
        StringAssert.Contains(js, "handlePresetPhysical");
        StringAssert.Contains(js, "handlePresetClear");
        StringAssert.Contains(js, "updateApplyButtonState");
    }

    private static int Count(string haystack, string needle)
    {
        int count = 0;
        int index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    private string AddDevice(params string[] scopes)
    {
        string token = $"cred_{Guid.NewGuid():N}";
        _store.AddDevice(new PairedDeviceRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = "Device " + Guid.NewGuid().ToString("N")[..8],
            CredentialHash = FrameHub.Companion.Pairing.PairingEngine.HashCredential(token),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Scopes = scopes.ToList()
        });
        return token;
    }

    private async Task<int> InvokeMiddlewareAsync(
        string method,
        string? token,
        string path,
        bool loopback = false)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Host = loopback
            ? new HostString("127.0.0.1", 47821)
            : new HostString("192.168.1.10", 47821);
        context.Connection.LocalIpAddress = IPAddress.Parse(loopback ? "127.0.0.1" : "192.168.1.10");
        context.Connection.RemoteIpAddress = IPAddress.Parse(loopback ? "127.0.0.1" : "192.168.1.20");
        if (token != null) context.Request.Headers.Authorization = "Bearer " + token;

        var middleware = new CompanionAuthMiddleware(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });
        var options = new CompanionOptions { Enabled = true, LanEnabled = true, LanAddress = "192.168.1.10", Port = 47821 };
        await middleware.InvokeAsync(context, _store, new FrameHub.Companion.Pairing.PairingEngine(_store), options, new PairingRateLimiter());
        return context.Response.StatusCode;
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class FakeSessionCpuProvider : ICompanionSessionOptimizationProvider
    {
        public Task<CompanionSessionOptimizationStateDto> GetStateAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new CompanionSessionOptimizationStateDto());

        public Task<CompanionOptimizationResultDto> ApplyOptimizationAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new CompanionOptimizationResultDto { Success = true, ErrorCode = "applied" });

        public Task<CompanionOptimizationResultDto> RestoreSessionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new CompanionOptimizationResultDto { Success = true, ErrorCode = "restored" });

        public Task<CompanionSessionCpuStateDto> GetCpuStateAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new CompanionSessionCpuStateDto
            {
                Available = true,
                SessionToken = Guid.NewGuid().ToString("N"),
                Source = "system",
                Topology = new CompanionSessionCpuTopologyDto
                {
                    Processors = [new CompanionSessionCpuProcessorDto { Index = 0, CoreIndex = 0, Type = "[P]" }]
                }
            });

        public Task<CompanionSessionCpuResultDto> ApplyCpuOverrideAsync(CompanionSessionCpuApplyRequestDto request, CancellationToken cancellationToken = default)
            => Task.FromResult(new CompanionSessionCpuResultDto { Success = true, ErrorCode = "applied" });

        public Task<CompanionSessionCpuResultDto> ResetCpuOverrideAsync(CompanionSessionCpuResetRequestDto request, CancellationToken cancellationToken = default)
            => Task.FromResult(new CompanionSessionCpuResultDto { Success = true, ErrorCode = "restored" });
    }
}
