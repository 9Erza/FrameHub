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
        var response = await client.GetAsync($"http://127.0.0.1:{port}/js/app.js");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        string content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(content.Contains("companion_credential"), "JS file must contain Companion frontend logic.");
        Assert.IsTrue(content.Contains("hasFetchedResultForCurrentCompletedState"), "JS file must contain completion polling deduplication state.");
        Assert.IsTrue(content.Contains("replaceState"), "JS file must contain URL token fragment cleanup logic.");
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
