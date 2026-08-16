using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using FrameHub.Companion.Authentication;
using FrameHub.Companion.Models;
using FrameHub.Companion.Pairing;
using FrameHub.Companion.Persistence;
using FrameHub.Companion.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Companion.Tests;

[TestClass]
public sealed class CompanionFrontendStaticFilesTests
{
    private string _tempDirectory = null!;
    private string _tempStorePath = null!;
    private DeviceRecordStore _deviceStore = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.StaticTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _tempStorePath = Path.Combine(_tempDirectory, "paired-devices.json");
        _deviceStore = new DeviceRecordStore(_tempStorePath);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { }
        }
    }

    private static int GetFreePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    private (PairedDeviceRecord Device, string Token) AddTestDevice(params string[] scopes)
    {
        string token = $"test_cred_{Guid.NewGuid():N}";
        string hash = PairingEngine.HashCredential(token);
        var record = new PairedDeviceRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test Browser Phone",
            CredentialHash = hash,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Scopes = scopes.ToList()
        };
        _deviceStore.AddDevice(record);
        return (record, token);
    }

    [TestMethod]
    public async Task GetRoot_ServesIndexHtml()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        var response = await client.GetAsync($"http://127.0.0.1:{port}/");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        string content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(content.Contains("FrameHub Companion"), "Root request must serve index.html with FrameHub Companion title.");
    }

    [TestMethod]
    public async Task GetIndexHtml_ServesIndexHtml()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        var response = await client.GetAsync($"http://127.0.0.1:{port}/index.html");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        string content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(content.Contains("FrameHub Companion"));
    }

    [TestMethod]
    public async Task GetCss_ServesStylesCss()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        var response = await client.GetAsync($"http://127.0.0.1:{port}/css/styles.css");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        string content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(content.Contains("--bg-main"), "CSS file must contain FrameHub theme variables.");
    }

    [TestMethod]
    public async Task GetJs_ServesAppJs()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        string[] moduleNames = ["app.js", "auth-transport.js", "telemetry.js", "benchmarks.js", "library.js", "session-optimization.js"];
        HttpResponseMessage[] responses = await Task.WhenAll(moduleNames.Select(name =>
            client.GetAsync($"http://127.0.0.1:{port}/js/{name}")));
        Assert.IsTrue(responses.All(response => response.StatusCode == HttpStatusCode.OK), "Every Companion frontend module must be served.");
        string content = string.Join("\n", await Task.WhenAll(responses.Select(response => response.Content.ReadAsStringAsync())));
        Assert.IsTrue(content.Contains("companion_credential"), "JS file must contain Companion frontend logic.");
        Assert.IsTrue(content.Contains("hasFetchedResultForCurrentCompletedState"), "JS file must contain completion polling deduplication state.");
        Assert.IsTrue(content.Contains("replaceState"), "JS file must contain URL token fragment cleanup logic.");
        Assert.IsTrue(content.Contains("renderTelemetry"), "JS file must contain live telemetry rendering logic.");
        Assert.IsTrue(content.Contains("livePerformance"), "JS file must consume M9.1 livePerformance model.");
        Assert.IsTrue(content.Contains("currentFps"), "JS file must format currentFps metric.");
        Assert.IsTrue(content.Contains("currentFrametimeMs"), "JS file must format currentFrametimeMs metric.");
        Assert.IsTrue(content.Contains("onePercentLowFps"), "JS file must format onePercentLowFps metric.");
        Assert.IsTrue(content.Contains("pointOnePercentLowFps"), "JS file must format pointOnePercentLowFps metric.");
        Assert.IsTrue(content.Contains("resetLivePerformanceMetrics"), "JS file must contain fallback reset logic for null/stale metrics.");
    }

    [TestMethod]
    public async Task BenchmarkTargets_ForbiddenHandling_ClearsSelectWithPermissionState()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        string benchmarksJs = await (await client.GetAsync($"http://127.0.0.1:{port}/js/benchmarks.js")).Content.ReadAsStringAsync();

        // 403 branch must clear stale options and render a permission-specific disabled state,
        // not a generic "unavailable" badge over untouched select contents.
        int forbiddenBranch = benchmarksJs.IndexOf("response.status === 403", StringComparison.Ordinal);
        Assert.IsTrue(forbiddenBranch >= 0, "benchmarks.js must handle 403 when loading targets.");
        string branchBody = benchmarksJs.Substring(forbiddenBranch, Math.Min(900, benchmarksJs.Length - forbiddenBranch));
        Assert.IsTrue(branchBody.Contains("targetSelect.innerHTML = ''"), "403 must clear existing target options.");
        Assert.IsTrue(branchBody.Contains("targetSelect.disabled = true"), "403 must disable the target select.");
        Assert.IsTrue(branchBody.Contains("benchmark.targetsPermissionRequired"), "403 must render the permission-specific option text.");
        Assert.IsTrue(branchBody.Contains("benchmark.permissionRequired"), "403 badge must state missing permission, not a server error.");
        Assert.IsFalse(branchBody.Contains("updateAuthUi(false", StringComparison.Ordinal), "403 (authenticated, insufficient scope) must not trigger pairing UI.");

        // 200-empty remains distinct from 403: dedicated no-targets wording and "0 available" badge.
        Assert.IsTrue(benchmarksJs.Contains("benchmark.noTargets"), "200-empty must keep its dedicated no-running-games option.");
        Assert.IsTrue(benchmarksJs.Contains("benchmark.targetsAvailable"), "200-empty must keep the availability count badge.");
    }

    [TestMethod]
    public async Task BenchmarkTabActivation_ReloadsTargetsWithoutPolling()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        string appJs = await (await client.GetAsync($"http://127.0.0.1:{port}/js/app.js")).Content.ReadAsStringAsync();

        // Benchmarks tab activation must trigger a one-shot loadTargets call.
        int benchmarksBranch = appJs.IndexOf("activeTab === 'benchmarks'", StringComparison.Ordinal);
        Assert.IsTrue(benchmarksBranch >= 0, "app.js switchTab must special-case the benchmarks tab.");
        string branchBody = appJs.Substring(benchmarksBranch, Math.Min(400, appJs.Length - benchmarksBranch));
        Assert.IsTrue(branchBody.Contains("loadTargets()"), "Activating the Benchmarks tab must reload targets.");

        // No polling may be introduced for targets.
        Assert.IsFalse(appJs.Contains("loadTargets", StringComparison.Ordinal) && appJs.Contains("setInterval(loadTargets", StringComparison.Ordinal),
            "Target loading must remain one-shot per tab activation, never interval-based.");
        Assert.IsFalse(appJs.Contains("setInterval(fetchStatus", StringComparison.Ordinal) && appJs.Contains("setInterval", StringComparison.Ordinal) && appJs.Contains("setInterval(loadTargets", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task GetIndexHtml_ContainsM92LiveDashboardElements()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        var response = await client.GetAsync($"http://127.0.0.1:{port}/index.html");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        string content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(content.Contains("live-dashboard-section"), "HTML must contain live dashboard section.");
        Assert.IsTrue(content.Contains("live-game-name"), "HTML must contain live game element.");
        Assert.IsTrue(content.Contains("live-fps"), "HTML must contain live FPS element.");
        Assert.IsTrue(content.Contains("live-frametime"), "HTML must contain live frametime element.");
        Assert.IsTrue(content.Contains("live-one-low"), "HTML must contain 1% low FPS element.");
        Assert.IsTrue(content.Contains("live-point-one-low"), "HTML must contain 0.1% low FPS element.");
        Assert.IsTrue(content.Contains("hw-cpu-load"), "HTML must contain CPU load hardware element.");
        Assert.IsTrue(content.Contains("hw-gpu-load"), "HTML must contain GPU load hardware element.");
        Assert.IsTrue(content.Contains("benchmark-active-notice"), "HTML must contain benchmark active notice element.");
    }

    [TestMethod]
    public async Task GetCss_ContainsM92LiveDashboardStyles()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        var response = await client.GetAsync($"http://127.0.0.1:{port}/css/styles.css");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        string content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(content.Contains(".live-dashboard-card"), "CSS must contain live dashboard card class.");
        Assert.IsTrue(content.Contains(".live-metrics-grid"), "CSS must contain live metrics grid class.");
        Assert.IsTrue(content.Contains(".hardware-grid"), "CSS must contain hardware grid class.");
        Assert.IsTrue(content.Contains(".notice-banner"), "CSS must contain notice banner class.");
    }

    [TestMethod]
    public async Task GetI18nJs_ServesI18nJs()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        var response = await client.GetAsync($"http://127.0.0.1:{port}/js/i18n.js");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        string content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(content.Contains("FrameHubI18n"), "i18n.js must expose FrameHubI18n namespace.");
        Assert.IsTrue(content.Contains("companion_language"), "i18n.js must use companion_language storage key.");
        Assert.IsTrue(content.Contains("translateState"), "i18n.js must provide translateState function.");
    }

    [TestMethod]
    public async Task GetIndexHtml_ContainsM93NavigationAndI18nElements()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        var response = await client.GetAsync($"http://127.0.0.1:{port}/index.html");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        string content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(content.Contains("home-view"), "HTML must contain home-view wrapper.");
        Assert.IsTrue(content.Contains("library-view"), "HTML must contain library-view wrapper.");
        Assert.IsTrue(content.Contains("benchmarks-view"), "HTML must contain benchmarks-view wrapper.");
        Assert.IsTrue(content.Contains("settings-view"), "HTML must contain settings-view wrapper.");
        Assert.IsTrue(content.Contains("app-nav"), "HTML must contain bottom nav shell.");
        Assert.IsTrue(content.Contains("nav-tab-library"), "HTML must contain library nav button.");
        Assert.IsTrue(content.Contains("data-i18n"), "HTML must contain data-i18n localization attributes.");
        Assert.IsTrue(content.Contains("language-select"), "HTML must contain language selector.");
    }

    [TestMethod]
    public async Task GetIndexHtml_ContainsM94LibraryElements()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        var response = await client.GetAsync($"http://127.0.0.1:{port}/index.html");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        string content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(content.Contains("library-section"), "HTML must contain library section.");
        Assert.IsTrue(content.Contains("btn-refresh-library"), "HTML must contain refresh library button.");
        Assert.IsTrue(content.Contains("library-loading"), "HTML must contain library loading indicator.");
        Assert.IsTrue(content.Contains("library-empty"), "HTML must contain library empty container.");
        Assert.IsTrue(content.Contains("library-error"), "HTML must contain library error container.");
        Assert.IsTrue(content.Contains("library-list"), "HTML must contain library list container.");
    }

    [TestMethod]
    public async Task GetCss_ContainsM94LibraryStyles()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        var response = await client.GetAsync($"http://127.0.0.1:{port}/css/styles.css");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        string content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(content.Contains(".library-cards-grid"), "CSS must contain library cards grid.");
        Assert.IsTrue(content.Contains(".library-card"), "CSS must contain library card class.");
        Assert.IsTrue(content.Contains(".btn-launch"), "CSS must contain launch button class.");
        Assert.IsTrue(content.Contains(".badge-running"), "CSS must contain badge running class.");
        Assert.IsTrue(content.Contains(".launch-feedback"), "CSS must contain launch feedback class.");
    }

    [TestMethod]
    public async Task GetI18nJs_ContainsM94LibraryAndLaunchKeys()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        var response = await client.GetAsync($"http://127.0.0.1:{port}/js/i18n.js");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        string content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(content.Contains("nav.library"), "i18n must contain nav.library key.");
        Assert.IsTrue(content.Contains("library.title"), "i18n must contain library.title key.");
        Assert.IsTrue(content.Contains("library.refresh"), "i18n must contain library.refresh key.");
        Assert.IsTrue(content.Contains("library.launch"), "i18n must contain library.launch key.");
        Assert.IsTrue(content.Contains("launch.launched"), "i18n must contain launch.launched key.");
        Assert.IsTrue(content.Contains("launch.already_running"), "i18n must contain launch.already_running key.");
        Assert.IsTrue(content.Contains("launch.benchmark_active"), "i18n must contain launch.benchmark_active key.");
        Assert.IsTrue(content.Contains("launch.launch_in_progress"), "i18n must contain launch.launch_in_progress key.");
    }

    [TestMethod]
    public async Task GetJs_ContainsM94LibraryAndLaunchLogic()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        var response = await client.GetAsync($"http://127.0.0.1:{port}/js/library.js");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        string content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(content.Contains("fetchLibraryItems"), "JS must contain fetchLibraryItems function.");
        Assert.IsTrue(content.Contains("renderLibraryItems"), "JS must contain renderLibraryItems function.");
        Assert.IsTrue(content.Contains("handleLaunchItem"), "JS must contain handleLaunchItem function.");
        Assert.IsTrue(content.Contains("/api/v1/library/"), "JS must call /api/v1/library/ route.");
        Assert.IsTrue(content.Contains("textContent"), "JS must use textContent for safe DOM assignment.");
        Assert.IsFalse(content.Contains("item.executablePath"), "JS must NOT reference executablePath from items.");
        Assert.IsFalse(content.Contains("item.ExecutablePath"), "JS must NOT reference ExecutablePath from items.");
    }

    [TestMethod]
    public async Task GetCss_ContainsM93NavigationStyles()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        var response = await client.GetAsync($"http://127.0.0.1:{port}/css/styles.css");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        string content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(content.Contains(".view-wrapper"), "CSS must contain view-wrapper class.");
        Assert.IsTrue(content.Contains(".bottom-nav"), "CSS must contain bottom-nav class.");
        Assert.IsTrue(content.Contains(".nav-item"), "CSS must contain nav-item class.");
    }

    [TestMethod]
    public async Task GetStatus_ReturnsDesktopLanguage()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        server.ConfigurePresentationPreferencesProvider(new TestPreferencesProvider("pl"));

        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        var response = await client.GetAsync($"http://127.0.0.1:{port}/api/v1/status");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<CompanionStatusDto>();
        Assert.IsNotNull(status);
        Assert.AreEqual("pl", status.DesktopLanguage, "Status endpoint must return normalized desktopLanguage from provider.");
    }

    private sealed class TestPreferencesProvider : ICompanionPresentationPreferencesProvider
    {
        public string DesktopLanguage { get; }
        public TestPreferencesProvider(string language) => DesktopLanguage = language;
    }

    [TestMethod]
    public async Task GetPairRoute_FailsClosedWhileRootFrontendIsPublic()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();

        // Root pairing frontend stays reachable pre-auth (token arrives via URL fragment, never sent to the server)
        var rootResponse = await client.GetAsync($"http://127.0.0.1:{port}/");
        Assert.AreEqual(HttpStatusCode.OK, rootResponse.StatusCode);

        // No /pair page or route exists; unknown paths must fail closed with 401, with or without query/fragment-style parameters
        var pairResponse = await client.GetAsync($"http://127.0.0.1:{port}/pair");
        Assert.AreEqual(HttpStatusCode.Unauthorized, pairResponse.StatusCode, "Unknown /pair path must remain protected by default authentication.");

        var pairQueryResponse = await client.GetAsync($"http://127.0.0.1:{port}/pair?v=1&t=abc123");
        Assert.AreEqual(HttpStatusCode.Unauthorized, pairQueryResponse.StatusCode, "A token in a query string must not make an unknown path public (and must never appear in URLs anyway).");
    }

    [TestMethod]
    public async Task StaticFiles_DoesNotBypassApiAuth()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        var fakeProvider = new TestFakeBenchmarkProvider();
        server.ConfigureBenchmarkProvider(fakeProvider);

        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();

        // 1. Static file is public
        var staticResp = await client.GetAsync($"http://127.0.0.1:{port}/index.html");
        Assert.AreEqual(HttpStatusCode.OK, staticResp.StatusCode);

        // 2. Unauthenticated POST to /api/v1/benchmarks/start fails with 401 Unauthorized
        var startReq = new CompanionBenchmarkStartRequestDto { TargetId = "target-1", DurationSeconds = 10 };
        var postResp = await client.PostAsJsonAsync($"http://127.0.0.1:{port}/api/v1/benchmarks/start", startReq);
        Assert.AreEqual(HttpStatusCode.Unauthorized, postResp.StatusCode, "Static files hosting must NOT make POST /api/v1/benchmarks/start public.");

        // 3. Authenticated POST with write scope succeeds
        var (_, token) = AddTestDevice(CompanionScopes.ReadBenchmarks, CompanionScopes.WriteBenchmarks);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var authPostResp = await client.PostAsJsonAsync($"http://127.0.0.1:{port}/api/v1/benchmarks/start", startReq);
        Assert.AreEqual(HttpStatusCode.Accepted, authPostResp.StatusCode);
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task StaticFrontend_ServesRegardlessOfCurrentDirectory()
    {
        string originalCwd = Directory.GetCurrentDirectory();
        string tempCwd = Path.Combine(Path.GetTempPath(), "FrameHub.CwdTest." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempCwd);

        try
        {
            Directory.SetCurrentDirectory(tempCwd);

            int port = GetFreePort();
            await using var server = new CompanionServer(_deviceStore);
            bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
            Assert.IsTrue(started);

            using var client = new HttpClient();

            var respRoot = await client.GetAsync($"http://127.0.0.1:{port}/");
            Assert.AreEqual(HttpStatusCode.OK, respRoot.StatusCode);
            string contentRoot = await respRoot.Content.ReadAsStringAsync();
            Assert.IsTrue(contentRoot.Contains("FrameHub Companion"));

            var respIndex = await client.GetAsync($"http://127.0.0.1:{port}/index.html");
            Assert.AreEqual(HttpStatusCode.OK, respIndex.StatusCode);

            var respCss = await client.GetAsync($"http://127.0.0.1:{port}/css/styles.css");
            Assert.AreEqual(HttpStatusCode.OK, respCss.StatusCode);

            var respJs = await client.GetAsync($"http://127.0.0.1:{port}/js/app.js");
            Assert.AreEqual(HttpStatusCode.OK, respJs.StatusCode);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            if (Directory.Exists(tempCwd))
            {
                try { Directory.Delete(tempCwd, true); } catch { }
            }
        }
    }

    [TestMethod]
    public async Task GetIndexHtml_ContainsM95SessionOptimizationElements()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        var response = await client.GetAsync($"http://127.0.0.1:{port}/index.html");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(content.Contains("session-optimization-section"), "index.html must include session-optimization-section.");
        Assert.IsTrue(content.Contains("opt-state-badge"), "index.html must include opt-state-badge.");
        Assert.IsTrue(content.Contains("btn-apply-optimization"), "index.html must include btn-apply-optimization.");
        Assert.IsTrue(content.Contains("btn-restore-optimization"), "index.html must include btn-restore-optimization.");
    }

    [TestMethod]
    public async Task GetCss_ContainsM95OptimizationStyles()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        var response = await client.GetAsync($"http://127.0.0.1:{port}/css/styles.css");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(content.Contains("opt-info-row"), "styles.css must include .opt-info-row.");
        Assert.IsTrue(content.Contains("optimization-feedback"), "styles.css must include .optimization-feedback.");
    }

    [TestMethod]
    public async Task GetI18nJs_ContainsM95OptimizationKeys()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        var response = await client.GetAsync($"http://127.0.0.1:{port}/js/i18n.js");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        string content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(content.Contains("optimization.title"), "i18n.js must include optimization.title.");
        Assert.IsTrue(content.Contains("optimization.applied"), "i18n.js must include optimization.applied.");
        Assert.IsTrue(content.Contains("optimization.restored"), "i18n.js must include optimization.restored.");
        Assert.IsTrue(content.Contains("optimization.restore_manual_required"), "i18n.js must expose truthful manual recovery state.");
        Assert.IsTrue(content.Contains("Recovery state could not be saved. Recovery may remain pending."), "Persistence failure wording must remain neutral about whether OS mutation occurred.");
        Assert.IsFalse(content.Contains("No unsafe changes were started"), "Persistence failure wording must not claim that no OS change occurred.");
        Assert.IsTrue(content.Contains("Optymalizacja Sesji"), "i18n.js must include Polish translation Optymalizacja Sesji.");
    }

    [TestMethod]
    public async Task GetJs_ContainsM95OptimizationLogic()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        string[] moduleNames = ["session-optimization.js", "benchmarks.js", "auth-transport.js"];
        HttpResponseMessage[] responses = await Task.WhenAll(moduleNames.Select(name =>
            client.GetAsync($"http://127.0.0.1:{port}/js/{name}")));
        Assert.IsTrue(responses.All(response => response.StatusCode == HttpStatusCode.OK));
        string content = string.Join("\n", await Task.WhenAll(responses.Select(response => response.Content.ReadAsStringAsync())));
        Assert.IsTrue(content.Contains("fetchOptimizationState"), "app.js must include fetchOptimizationState.");
        Assert.IsTrue(content.Contains("handleApplyOptimization"), "app.js must include handleApplyOptimization.");
        Assert.IsTrue(content.Contains("handleRestoreOptimization"), "app.js must include handleRestoreOptimization.");
        Assert.IsTrue(content.Contains("authStateChanged"), "Repeated status polls must not retrigger active-tab network loads.");
        Assert.IsTrue(content.Contains("stopTelemetryPolling"), "A successful WebSocket connection must stop fallback HTTP polling.");
        Assert.IsTrue(content.Contains("telemetryReconnectTimeout"), "WebSocket reconnect scheduling must be deduplicated and cancellable.");
        Assert.IsTrue(content.Contains("scheduleTelemetryReconnect(30000, generation)"), "A telemetry-scope 403 must schedule a throttled retry independently of fallback success.");
        Assert.IsTrue(content.Contains("teardownTelemetryConnection(true)"), "Transitioning to unpaired must tear down telemetry ownership.");
        Assert.IsTrue(content.Contains("generation !== telemetryConnectionGeneration"), "Stale asynchronous telemetry callbacks must be generation-gated.");
        Assert.IsTrue(content.Contains("const generation = telemetryConnectionGeneration;"), "HTTP telemetry requests must capture their transport generation.");
        Assert.IsTrue(content.Contains("requestId === telemetryHttpRequestId"), "Only the currently owned HTTP telemetry request may update presentation.");
        Assert.IsTrue(content.Contains("if (!ownsRequest()) return;"), "HTTP telemetry results must be discarded after asynchronous boundaries when ownership changed.");
        Assert.IsTrue(content.Contains("resetTelemetryTransportForNewCredential"), "A new credential must bypass a prior ticket denial throttle.");
    }

    [TestMethod]
    public async Task ClientCredentialStorage_UsesCentralizedPersistentHelper_WithLegacyMigration()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        string authTransportJs = await (await client.GetAsync($"http://127.0.0.1:{port}/js/auth-transport.js")).Content.ReadAsStringAsync();

        // Must define centralized storage functions
        Assert.IsTrue(authTransportJs.Contains("function getStoredCredential()"), "auth-transport.js must define getStoredCredential.");
        Assert.IsTrue(authTransportJs.Contains("function setStoredCredential("), "auth-transport.js must define setStoredCredential.");
        Assert.IsTrue(authTransportJs.Contains("function clearStoredCredential()"), "auth-transport.js must define clearStoredCredential.");

        // Migration logic: checks localStorage first, falls back to legacy sessionStorage, promotes to localStorage, removes from sessionStorage
        Assert.IsTrue(authTransportJs.Contains("localStorage.getItem(STORAGE_KEY)"), "getStoredCredential must read localStorage.");
        Assert.IsTrue(authTransportJs.Contains("sessionStorage.getItem(STORAGE_KEY)"), "getStoredCredential must check legacy sessionStorage for migration.");
        Assert.IsTrue(authTransportJs.Contains("localStorage.setItem(STORAGE_KEY, legacy)"), "getStoredCredential must promote legacy credential to localStorage.");
        Assert.IsTrue(authTransportJs.Contains("sessionStorage.removeItem(STORAGE_KEY)"), "getStoredCredential must clean up legacy sessionStorage.");

        // getAuthHeaders and handlePairingSubmit must use centralized helper
        Assert.IsTrue(authTransportJs.Contains("const credential = getStoredCredential();"), "getAuthHeaders must use getStoredCredential.");
        Assert.IsTrue(authTransportJs.Contains("setStoredCredential(data.credential);"), "handlePairingSubmit must use setStoredCredential.");

        // Verify no other files directly use raw sessionStorage for credential
        string[] otherModules = ["benchmarks.js", "library.js", "session-optimization.js", "app.js", "telemetry.js"];
        foreach (string name in otherModules)
        {
            string content = await (await client.GetAsync($"http://127.0.0.1:{port}/js/{name}")).Content.ReadAsStringAsync();
            Assert.IsFalse(content.Contains("sessionStorage.getItem(STORAGE_KEY)"), $"{name} must not directly call sessionStorage.getItem(STORAGE_KEY).");
            Assert.IsFalse(content.Contains("sessionStorage.setItem(STORAGE_KEY"), $"{name} must not directly call sessionStorage.setItem(STORAGE_KEY).");
        }
    }

    [TestMethod]
    public async Task ClientCredential_401ClearsStoredCredential_403PreservesStoredCredential()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        string authTransportJs = await (await client.GetAsync($"http://127.0.0.1:{port}/js/auth-transport.js")).Content.ReadAsStringAsync();
        string benchmarksJs = await (await client.GetAsync($"http://127.0.0.1:{port}/js/benchmarks.js")).Content.ReadAsStringAsync();
        string libraryJs = await (await client.GetAsync($"http://127.0.0.1:{port}/js/library.js")).Content.ReadAsStringAsync();
        string optJs = await (await client.GetAsync($"http://127.0.0.1:{port}/js/session-optimization.js")).Content.ReadAsStringAsync();

        // 401 paths must clear credential
        Assert.IsTrue(authTransportJs.Contains("clearStoredCredential();"), "auth-transport.js must call clearStoredCredential on 401.");
        Assert.IsTrue(benchmarksJs.Contains("clearStoredCredential();"), "benchmarks.js must call clearStoredCredential on 401.");
        Assert.IsTrue(libraryJs.Contains("clearStoredCredential();"), "library.js must call clearStoredCredential on 401.");
        Assert.IsTrue(optJs.Contains("clearStoredCredential();"), "session-optimization.js must call clearStoredCredential on 401.");

        // 403 paths must preserve credential and not call clearStoredCredential
        int fetchStatusStart = benchmarksJs.IndexOf("async function fetchStatus", StringComparison.Ordinal);
        Assert.IsTrue(fetchStatusStart >= 0);
        int fetchStatus403 = benchmarksJs.IndexOf("response.status === 403", fetchStatusStart, StringComparison.Ordinal);
        Assert.IsTrue(fetchStatus403 >= 0);
        string fetchStatus403Block = benchmarksJs.Substring(fetchStatus403, 150);
        Assert.IsTrue(fetchStatus403Block.Contains("if (getStoredCredential()) updateAuthUi(true, 'Paired Device');"),
            "403 on status check must keep UI paired if stored credential exists.");
        Assert.IsFalse(fetchStatus403Block.Contains("clearStoredCredential"),
            "403 must never clear stored credential.");
    }

    [TestMethod]
    public async Task WebSocketTicketFlow_UsesBearerAuthHeader_DoesNotExposeCredentialInUrl()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        string authTransportJs = await (await client.GetAsync($"http://127.0.0.1:{port}/js/auth-transport.js")).Content.ReadAsStringAsync();

        // Ticket request must use getAuthHeaders (Bearer auth)
        int ticketRequest = authTransportJs.IndexOf("fetch('/api/v1/telemetry/ws-ticket'", StringComparison.Ordinal);
        Assert.IsTrue(ticketRequest >= 0, "auth-transport.js must request ws-ticket.");
        string ticketBlock = authTransportJs.Substring(ticketRequest, 150);
        Assert.IsTrue(ticketBlock.Contains("headers: getAuthHeaders()"), "Ticket request must pass Authorization header.");

        // WebSocket URL must not have query strings containing token/credential
        Assert.IsTrue(authTransportJs.Contains("const wsUrl = protocol + '//' + window.location.host + '/api/v1/telemetry/ws';"),
            "WebSocket URL must be clean without credential query parameters.");
        Assert.IsTrue(authTransportJs.Contains("new WebSocket(wsUrl, ['framehub.v1', 'ticket.' + ticket])"),
            "WebSocket must pass ephemeral ticket via subprotocol.");
    }

    [TestMethod]
    public async Task StaticAssets_HaveNoCacheHeaders_PreventingStaleClientCache()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        string[] staticPaths =
        [
            "/",
            "/index.html",
            "/js/auth-transport.js",
            "/js/app.js",
            "/js/benchmarks.js",
            "/css/styles.css"
        ];

        foreach (string path in staticPaths)
        {
            var response = await client.GetAsync($"http://127.0.0.1:{port}{path}");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"Static asset {path} must return 200 OK.");

            // Cache-Control must prevent caching
            var cacheControl = response.Headers.CacheControl?.ToString()
                               ?? (response.Headers.TryGetValues("Cache-Control", out var ccVals) ? string.Join(", ", ccVals) : string.Empty);
            Assert.IsTrue(cacheControl.Contains("no-store", StringComparison.OrdinalIgnoreCase), $"{path} Cache-Control must contain no-store.");
            Assert.IsTrue(cacheControl.Contains("no-cache", StringComparison.OrdinalIgnoreCase), $"{path} Cache-Control must contain no-cache.");
            Assert.IsTrue(cacheControl.Contains("must-revalidate", StringComparison.OrdinalIgnoreCase), $"{path} Cache-Control must contain must-revalidate.");

            // Pragma must be no-cache
            Assert.IsTrue(response.Headers.TryGetValues("Pragma", out var pragmaVals) && pragmaVals.Any(v => v.Contains("no-cache", StringComparison.OrdinalIgnoreCase)),
                $"{path} must include Pragma: no-cache header.");

            // Expires must be 0 or in the past
            bool hasExpires = (response.Headers.TryGetValues("Expires", out var exp1) && exp1.Any(v => v.Contains("0")))
                              || (response.Content.Headers.TryGetValues("Expires", out var exp2) && exp2.Any(v => v.Contains("0")))
                              || response.Content.Headers.Expires != null;
            Assert.IsTrue(hasExpires, $"{path} must include Expires: 0 header.");
        }
    }

    [TestMethod]
    public async Task IndexHtml_LoadsAuthTransportBeforeDependentModules()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        string indexHtml = await (await client.GetAsync($"http://127.0.0.1:{port}/index.html")).Content.ReadAsStringAsync();

        int authTransportIdx = indexHtml.IndexOf("/js/auth-transport.js", StringComparison.Ordinal);
        int telemetryIdx = indexHtml.IndexOf("/js/telemetry.js", StringComparison.Ordinal);
        int benchmarksIdx = indexHtml.IndexOf("/js/benchmarks.js", StringComparison.Ordinal);
        int libraryIdx = indexHtml.IndexOf("/js/library.js", StringComparison.Ordinal);
        int optIdx = indexHtml.IndexOf("/js/session-optimization.js", StringComparison.Ordinal);
        int appIdx = indexHtml.IndexOf("/js/app.js", StringComparison.Ordinal);

        Assert.IsTrue(authTransportIdx >= 0, "index.html must include auth-transport.js");
        Assert.IsTrue(telemetryIdx >= 0, "index.html must include telemetry.js");
        Assert.IsTrue(benchmarksIdx >= 0, "index.html must include benchmarks.js");
        Assert.IsTrue(libraryIdx >= 0, "index.html must include library.js");
        Assert.IsTrue(optIdx >= 0, "index.html must include session-optimization.js");
        Assert.IsTrue(appIdx >= 0, "index.html must include app.js");

        Assert.IsTrue(authTransportIdx < telemetryIdx, "auth-transport.js must load before telemetry.js");
        Assert.IsTrue(authTransportIdx < benchmarksIdx, "auth-transport.js must load before benchmarks.js");
        Assert.IsTrue(authTransportIdx < libraryIdx, "auth-transport.js must load before library.js");
        Assert.IsTrue(authTransportIdx < optIdx, "auth-transport.js must load before session-optimization.js");
        Assert.IsTrue(authTransportIdx < appIdx, "auth-transport.js must load before app.js");
    }

    [TestMethod]
    public async Task ApiEndpoints_DoNotInheritStaticFileNoCacheHeadersAutomatically()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        server.ConfigureBenchmarkProvider(new TestFakeBenchmarkProvider());
        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        // Anonymous status endpoint returns API response
        var response = await client.GetAsync($"http://127.0.0.1:{port}/api/v1/benchmarks/status");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    private sealed class TestFakeBenchmarkProvider : ICompanionBenchmarkProvider
    {
        public CompanionBenchmarkStatusDto GetStatus()
        {
            return new CompanionBenchmarkStatusDto
            {
                State = "Idle",
                IsActive = false
            };
        }

        public IReadOnlyList<CompanionBenchmarkTargetDto> GetEligibleTargets()
        {
            return new[]
            {
                new CompanionBenchmarkTargetDto { TargetId = "target-1", DisplayName = "Target Game" }
            };
        }

        public Task<CompanionBenchmarkStartResultDto> StartBenchmarkAsync(CompanionBenchmarkStartRequestDto request)
        {
            return Task.FromResult(new CompanionBenchmarkStartResultDto { Accepted = true });
        }

        public Task<CompanionBenchmarkStopResultDto> StopBenchmarkAsync()
        {
            return Task.FromResult(new CompanionBenchmarkStopResultDto { Success = true, WasActive = false });
        }

        public CompanionBenchmarkHistoryListDto GetHistory(int limit)
        {
            return new CompanionBenchmarkHistoryListDto { Sessions = Array.Empty<CompanionBenchmarkHistorySummaryDto>(), TotalCount = 0 };
        }

        public CompanionBenchmarkHistoryDetailDto? GetHistoryDetail(Guid sessionId)
        {
            return null;
        }

        public CompanionBenchmarkChartDto? GetHistoryChart(Guid sessionId, int buckets)
        {
            return null;
        }

        public CompanionBenchmarkComparisonDto CompareHistorySessions(Guid sessionAId, Guid sessionBId)
        {
            throw new KeyNotFoundException();
        }
    }
}
