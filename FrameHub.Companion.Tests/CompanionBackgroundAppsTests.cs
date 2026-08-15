using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using FrameHub.Companion.Authentication;
using FrameHub.Companion.Models;
using FrameHub.Companion.Pairing;
using FrameHub.Companion.Persistence;
using FrameHub.Companion.Providers;
using FrameHub.Companion.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Companion.Tests;

[TestClass]
public sealed class CompanionBackgroundAppsTests
{
    private string _tempDirectory = null!;
    private DeviceRecordStore _store = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.BackgroundApiTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _store = new DeviceRecordStore(Path.Combine(_tempDirectory, "devices.json"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_tempDirectory, recursive: true); } catch { }
    }

    [TestMethod]
    public async Task Api_LoopbackReadIsSafe_ButWritesRequireDedicatedScope()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_store);
        var provider = new FakeProvider();
        server.ConfigureBackgroundAppsProvider(provider);
        Assert.IsTrue(await server.StartAsync(new CompanionOptions { Enabled = true, Port = port }));
        using var client = new HttpClient();

        HttpResponseMessage get = await client.GetAsync($"http://127.0.0.1:{port}/api/v1/background-apps");
        Assert.AreEqual(HttpStatusCode.OK, get.StatusCode);
        string json = await get.Content.ReadAsStringAsync();
        StringAssert.Contains(json, "Trusted App");
        Assert.IsFalse(json.Contains("processId", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("executablePath", StringComparison.OrdinalIgnoreCase));

        HttpResponseMessage unauthenticated = await client.PostAsync($"http://127.0.0.1:{port}/api/v1/background-apps/trusted-1/start", null);
        Assert.AreEqual(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        string launchToken = AddDevice(CompanionScopes.WriteLaunch);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", launchToken);
        HttpResponseMessage wrongScope = await client.PostAsync($"http://127.0.0.1:{port}/api/v1/background-apps/trusted-1/start", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, wrongScope.StatusCode);
        HttpResponseMessage wrongStopScope = await client.PostAsync($"http://127.0.0.1:{port}/api/v1/background-apps/trusted-1/stop", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, wrongStopScope.StatusCode);

        string writeToken = AddDevice(CompanionScopes.ReadBackgroundApps, CompanionScopes.WriteBackgroundApps);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", writeToken);
        Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsync($"http://127.0.0.1:{port}/api/v1/background-apps/trusted-1/start", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsync($"http://127.0.0.1:{port}/api/v1/background-apps/trusted-1/stop", null)).StatusCode);

        HttpResponseMessage malformed = await client.PostAsync($"http://127.0.0.1:{port}/api/v1/background-apps/bad%24id/start", null);
        Assert.AreEqual(HttpStatusCode.BadRequest, malformed.StatusCode);
    }

    [TestMethod]
    public async Task Middleware_LanGetRequiresReadBackgroundAppsScope()
    {
        Assert.AreEqual(StatusCodes.Status401Unauthorized, await InvokeMiddlewareAsync(HttpMethods.Get, token: null));
        string wrong = AddDevice(CompanionScopes.ReadLibrary);
        Assert.AreEqual(StatusCodes.Status403Forbidden, await InvokeMiddlewareAsync(HttpMethods.Get, wrong));
        string allowed = AddDevice(CompanionScopes.ReadBackgroundApps);
        Assert.AreEqual(StatusCodes.Status204NoContent, await InvokeMiddlewareAsync(HttpMethods.Get, allowed));
    }

    [TestMethod]
    public async Task Middleware_HostileRoutePrefixes_DoNotReceiveLoopbackReadExemption()
    {
        Assert.AreEqual(StatusCodes.Status204NoContent,
            await InvokeMiddlewareAsync(HttpMethods.Get, token: null, path: "/api/v1/background-apps", loopback: true));
        Assert.AreEqual(StatusCodes.Status401Unauthorized,
            await InvokeMiddlewareAsync(HttpMethods.Get, token: null, path: "/api/v1/background-appsevil", loopback: true));
        Assert.AreEqual(StatusCodes.Status401Unauthorized,
            await InvokeMiddlewareAsync(HttpMethods.Get, token: null, path: "/api/v1/background-apps-evil", loopback: true));
    }

    [TestMethod]
    public void ExistingDeviceDoesNotGainScopes_AndPermissionDependencyCascades()
    {
        var record = new PairedDeviceRecord
        {
            Id = Guid.NewGuid(), DisplayName = "Existing", CredentialHash = PairingEngine.HashCredential("existing"),
            Scopes = new List<string> { CompanionScopes.ReadStatus }
        };
        _store.AddDevice(record);
        PairedDeviceRecord loaded = _store.GetDeviceById(record.Id)!;
        Assert.IsFalse(loaded.Scopes.Contains(CompanionScopes.ReadBackgroundApps));
        Assert.IsFalse(loaded.Scopes.Contains(CompanionScopes.WriteBackgroundApps));
        Assert.IsTrue(CompanionScopes.IsValidScope(CompanionScopes.ReadBackgroundApps));
        Assert.IsTrue(CompanionScopes.IsValidScope(CompanionScopes.WriteBackgroundApps));
    }

    [TestMethod]
    public async Task Frontend_ContainsLocalizedSafeInFlightControlFlow()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_store);
        Assert.IsTrue(await server.StartAsync(new CompanionOptions { Enabled = true, Port = port }));
        using var client = new HttpClient();

        string html = await client.GetStringAsync($"http://127.0.0.1:{port}/index.html");
        string app = await client.GetStringAsync($"http://127.0.0.1:{port}/js/app.js");
        string i18n = await client.GetStringAsync($"http://127.0.0.1:{port}/js/i18n.js");

        StringAssert.Contains(html, "background-apps-section");
        StringAssert.Contains(app, "backgroundAppOperations.has(item.id)");
        StringAssert.Contains(app, "'/api/v1/background-apps/' + encodeURIComponent(item.id) + '/' + action");
        StringAssert.Contains(app, "title.textContent = item.displayName");
        StringAssert.Contains(app, "await fetchBackgroundApps()");
        StringAssert.Contains(i18n, "'backgroundApps.title': 'Background Apps'");
        Assert.AreEqual(2, Count(i18n, "'backgroundApps.title':"), "EN and PL dictionaries must both define the feature title.");

        int actionStart = app.IndexOf("async function controlBackgroundApp", StringComparison.Ordinal);
        int actionEnd = app.IndexOf("// Session Optimization Logic", actionStart, StringComparison.Ordinal);
        string actionCode = app[actionStart..actionEnd];
        StringAssert.Contains(actionCode, "if (resp.status === 401)");
        StringAssert.Contains(actionCode, "updateAuthUi(false");
        StringAssert.Contains(actionCode, "if (resp.status === 403)");
        StringAssert.Contains(actionCode, "backgroundApps.permissionUnavailable");

        int forbiddenStart = actionCode.IndexOf("if (resp.status === 403)", StringComparison.Ordinal);
        int responseParsingStart = actionCode.IndexOf("let data = null", forbiddenStart, StringComparison.Ordinal);
        string forbiddenBranch = actionCode[forbiddenStart..responseParsingStart];
        Assert.IsFalse(forbiddenBranch.Contains("updateAuthUi(false", StringComparison.Ordinal),
            "A missing write scope must not mark a still-paired device as unpaired.");
    }

    private async Task<int> InvokeMiddlewareAsync(
        string method,
        string? token,
        string path = "/api/v1/background-apps",
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
        await middleware.InvokeAsync(context, _store, new PairingEngine(_store), options, new PairingRateLimiter());
        return context.Response.StatusCode;
    }

    private string AddDevice(params string[] scopes)
    {
        string token = "token_" + Guid.NewGuid().ToString("N");
        _store.AddDevice(new PairedDeviceRecord
        {
            Id = Guid.NewGuid(), DisplayName = "Phone", CredentialHash = PairingEngine.HashCredential(token),
            Scopes = scopes.ToList()
        });
        return token;
    }

    private static int GetFreePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    private static int Count(string source, string value)
    {
        int count = 0;
        for (int index = 0; (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length) count++;
        return count;
    }

    private sealed class FakeProvider : ICompanionBackgroundAppsProvider
    {
        public Task<IReadOnlyList<CompanionBackgroundAppDto>> GetBackgroundAppsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CompanionBackgroundAppDto>>(new[]
            {
                new CompanionBackgroundAppDto { Id = "trusted-1", DisplayName = "Trusted App", CanStart = true }
            });
        public Task<CompanionBackgroundAppOperationDto> StartBackgroundAppAsync(string id, CancellationToken cancellationToken = default) => Success("started");
        public Task<CompanionBackgroundAppOperationDto> StopBackgroundAppAsync(string id, CancellationToken cancellationToken = default) => Success("stop_succeeded");
        private static Task<CompanionBackgroundAppOperationDto> Success(string code) =>
            Task.FromResult(new CompanionBackgroundAppOperationDto { Success = true, ErrorCode = code });
    }
}
