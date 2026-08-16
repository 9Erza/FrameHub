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
public sealed class CompanionBenchmarkApiTests
{
    private string _tempDirectory = null!;
    private string _tempStorePath = null!;
    private DeviceRecordStore _deviceStore = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.BenchmarkTests", Guid.NewGuid().ToString("N"));
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
            DisplayName = "Test Phone",
            CredentialHash = hash,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Scopes = scopes.ToList()
        };
        _deviceStore.AddDevice(record);
        return (record, token);
    }

    [TestMethod]
    public async Task ProviderNotConfigured_Returns503()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        var response = await client.GetAsync($"http://127.0.0.1:{port}/api/v1/benchmarks/status");

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<CompanionBenchmarkErrorDto>();
        Assert.IsNotNull(error);
        Assert.AreEqual("benchmark_provider_unavailable", error.ErrorCode);
    }

    [TestMethod]
    public async Task LoopbackGet_UnauthenticatedAllowed()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        var fakeProvider = new TestFakeBenchmarkProvider();
        server.ConfigureBenchmarkProvider(fakeProvider);

        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        var response = await client.GetAsync($"http://127.0.0.1:{port}/api/v1/benchmarks/status");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<CompanionBenchmarkStatusDto>();
        Assert.IsNotNull(status);
        Assert.AreEqual("Idle", status.State);
    }

    [TestMethod]
    public async Task LocalhostPost_RequiresBearerTokenAndWriteScope()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        var fakeProvider = new TestFakeBenchmarkProvider();
        server.ConfigureBenchmarkProvider(fakeProvider);

        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();

        // 1. Unauthenticated POST -> 401
        var startReq = new CompanionBenchmarkStartRequestDto { TargetId = "game-1", DurationSeconds = 10 };
        var resp1 = await client.PostAsJsonAsync($"http://127.0.0.1:{port}/api/v1/benchmarks/start", startReq);
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp1.StatusCode);

        // 2. Authenticated with read:benchmarks scope only -> 403
        var (_, readToken) = AddTestDevice(CompanionScopes.ReadBenchmarks);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", readToken);
        var resp2 = await client.PostAsJsonAsync($"http://127.0.0.1:{port}/api/v1/benchmarks/start", startReq);
        Assert.AreEqual(HttpStatusCode.Forbidden, resp2.StatusCode);

        // 3. Authenticated with write:benchmarks scope -> 202 Accepted
        var (_, writeToken) = AddTestDevice(CompanionScopes.ReadBenchmarks, CompanionScopes.WriteBenchmarks);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", writeToken);
        var resp3 = await client.PostAsJsonAsync($"http://127.0.0.1:{port}/api/v1/benchmarks/start", startReq);
        Assert.AreEqual(HttpStatusCode.Accepted, resp3.StatusCode);
    }

    [TestMethod]
    public async Task Start_WhenAlreadyRunning_Returns409Conflict()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        var fakeProvider = new TestFakeBenchmarkProvider { ActiveState = true };
        server.ConfigureBenchmarkProvider(fakeProvider);

        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        var (_, token) = AddTestDevice(CompanionScopes.WriteBenchmarks);
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var startReq = new CompanionBenchmarkStartRequestDto { TargetId = "game-1", DurationSeconds = 10 };
        var response = await client.PostAsJsonAsync($"http://127.0.0.1:{port}/api/v1/benchmarks/start", startReq);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<CompanionBenchmarkErrorDto>();
        Assert.IsNotNull(error);
        Assert.AreEqual("already_running", error.ErrorCode);
    }

    [TestMethod]
    public async Task Start_RejectsUnboundedDurationAndCountdownBeforeProviderInvocation()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        var provider = new TestFakeBenchmarkProvider();
        server.ConfigureBenchmarkProvider(provider);
        Assert.IsTrue(await server.StartAsync(new CompanionOptions { Enabled = true, Port = port }));

        var (_, token) = AddTestDevice(CompanionScopes.WriteBenchmarks);
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var durationResponse = await client.PostAsJsonAsync(
            $"http://127.0.0.1:{port}/api/v1/benchmarks/start",
            new CompanionBenchmarkStartRequestDto { TargetId = "game-1", DurationSeconds = 601 });
        var countdownResponse = await client.PostAsJsonAsync(
            $"http://127.0.0.1:{port}/api/v1/benchmarks/start",
            new CompanionBenchmarkStartRequestDto { TargetId = "game-1", DurationSeconds = 10, CountdownSeconds = 31 });

        Assert.AreEqual(HttpStatusCode.BadRequest, durationResponse.StatusCode);
        Assert.AreEqual("invalid_duration", (await durationResponse.Content.ReadFromJsonAsync<CompanionBenchmarkErrorDto>())?.ErrorCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, countdownResponse.StatusCode);
        Assert.AreEqual("invalid_countdown", (await countdownResponse.Content.ReadFromJsonAsync<CompanionBenchmarkErrorDto>())?.ErrorCode);
        Assert.IsFalse(provider.ActiveState, "Rejected requests must not reach the benchmark provider.");
    }

    [TestMethod]
    public async Task Stop_ActiveAndIdle_Returns200OK()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        var fakeProvider = new TestFakeBenchmarkProvider();
        server.ConfigureBenchmarkProvider(fakeProvider);

        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        var (_, token) = AddTestDevice(CompanionScopes.WriteBenchmarks);
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Idle Stop
        var resp1 = await client.PostAsync($"http://127.0.0.1:{port}/api/v1/benchmarks/stop", null);
        Assert.AreEqual(HttpStatusCode.OK, resp1.StatusCode);
        var stopResult1 = await resp1.Content.ReadFromJsonAsync<CompanionBenchmarkStopResultDto>();
        Assert.IsNotNull(stopResult1);
        Assert.IsTrue(stopResult1.Success);
        Assert.IsFalse(stopResult1.WasActive);

        // Active Stop
        fakeProvider.ActiveState = true;
        var resp2 = await client.PostAsync($"http://127.0.0.1:{port}/api/v1/benchmarks/stop", null);
        Assert.AreEqual(HttpStatusCode.OK, resp2.StatusCode);
        var stopResult2 = await resp2.Content.ReadFromJsonAsync<CompanionBenchmarkStopResultDto>();
        Assert.IsNotNull(stopResult2);
        Assert.IsTrue(stopResult2.Success);
        Assert.IsTrue(stopResult2.WasActive);
    }

    [TestMethod]
    public async Task History_PaginationAndSanitization()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        var fakeProvider = new TestFakeBenchmarkProvider();
        server.ConfigureBenchmarkProvider(fakeProvider);

        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();

        // 1. Invalid Limit -> 400
        var resp1 = await client.GetAsync($"http://127.0.0.1:{port}/api/v1/benchmarks/history?limit=0");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp1.StatusCode);

        // 2. Valid Limit -> 200
        var resp2 = await client.GetAsync($"http://127.0.0.1:{port}/api/v1/benchmarks/history?limit=5");
        Assert.AreEqual(HttpStatusCode.OK, resp2.StatusCode);
        var history = await resp2.Content.ReadFromJsonAsync<CompanionBenchmarkHistoryListDto>();
        Assert.IsNotNull(history);
        Assert.AreEqual(1, history.Sessions.Count);
        Assert.AreEqual("Cyberpunk 2077", history.Sessions[0].GameDisplayName);

        // 3. History Detail Valid -> 200
        Guid validId = history.Sessions[0].SessionId;
        var resp3 = await client.GetAsync($"http://127.0.0.1:{port}/api/v1/benchmarks/history/{validId}");
        Assert.AreEqual(HttpStatusCode.OK, resp3.StatusCode);
        var detail = await resp3.Content.ReadFromJsonAsync<CompanionBenchmarkHistoryDetailDto>();
        Assert.IsNotNull(detail);
        Assert.AreEqual(validId, detail.SessionId);
        Assert.AreEqual(120.5, detail.AverageFps);

        // 4. History Detail Unknown -> 404
        var resp4 = await client.GetAsync($"http://127.0.0.1:{port}/api/v1/benchmarks/history/{Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.NotFound, resp4.StatusCode);

        // 5. History Detail Invalid Guid -> 400
        var resp5 = await client.GetAsync($"http://127.0.0.1:{port}/api/v1/benchmarks/history/not-a-guid");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp5.StatusCode);
    }

    [TestMethod]
    public async Task LanGetTargets_RequiresReadBenchmarksScope()
    {
        var candidates = FrameHub.Companion.Network.LanAddressService.GetAvailableLanAddresses();
        if (candidates.Count == 0)
        {
            Assert.Inconclusive("No RFC1918 LAN interface is available on this machine.");
        }
        string lanIp = candidates[0].IpAddress;
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        var provider = new TestFakeBenchmarkProvider();
        server.ConfigureBenchmarkProvider(provider);
        Assert.IsTrue(await server.StartAsync(new CompanionOptions { Enabled = true, LanEnabled = true, LanAddress = lanIp, Port = port }));

        using var client = new HttpClient();

        // 1. Paired device with read:status only (no read:benchmarks) -> 403 before the controller
        var (_, statusOnlyToken) = AddTestDevice(CompanionScopes.ReadStatus);
        using var req1 = new HttpRequestMessage(HttpMethod.Get, $"http://{lanIp}:{port}/api/v1/benchmarks/targets");
        req1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", statusOnlyToken);
        var res1 = await client.SendAsync(req1);
        Assert.AreEqual(HttpStatusCode.Forbidden, res1.StatusCode, "GET /targets over LAN requires read:benchmarks.");

        // 2. Same request with read:benchmarks -> reaches the provider and returns 200
        var (_, readBenchmarksToken) = AddTestDevice(CompanionScopes.ReadStatus, CompanionScopes.ReadBenchmarks);
        using var req2 = new HttpRequestMessage(HttpMethod.Get, $"http://{lanIp}:{port}/api/v1/benchmarks/targets");
        req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", readBenchmarksToken);
        var res2 = await client.SendAsync(req2);
        Assert.AreEqual(HttpStatusCode.OK, res2.StatusCode);
        var targets = await res2.Content.ReadFromJsonAsync<List<CompanionBenchmarkTargetDto>>();
        Assert.IsNotNull(targets);
        Assert.AreEqual(1, targets.Count);
        Assert.AreEqual("cp2077", targets[0].TargetId);
    }

    [TestMethod]
    public async Task GetTargets_ProviderThrows_ReturnsStructured500WithoutExceptionDetails()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        server.ConfigureBenchmarkProvider(new ThrowingTargetsProvider());
        Assert.IsTrue(await server.StartAsync(new CompanionOptions { Enabled = true, Port = port }));

        using var client = new HttpClient();
        var response = await client.GetAsync($"http://127.0.0.1:{port}/api/v1/benchmarks/targets");

        Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        string json = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(json, "benchmark_targets_failed", "Provider failure must return the structured error code.");
        Assert.IsFalse(json.Contains("C:\\", StringComparison.Ordinal), "No local paths may leak in the error response.");
        Assert.IsFalse(json.Contains(" at FrameHub", StringComparison.Ordinal), "No stack frames may leak in the error response.");
        Assert.IsFalse(json.Contains("SecretPath", StringComparison.Ordinal), "No exception internals may leak in the error response.");
    }

    private sealed class ThrowingTargetsProvider : ICompanionBenchmarkProvider
    {
        private readonly TestFakeBenchmarkProvider _inner = new();

        public CompanionBenchmarkStatusDto GetStatus() => _inner.GetStatus();
        public IReadOnlyList<CompanionBenchmarkTargetDto> GetEligibleTargets()
        {
            throw new InvalidOperationException("SecretPath: C:\\internal\\failure detail");
        }
        public Task<CompanionBenchmarkStartResultDto> StartBenchmarkAsync(CompanionBenchmarkStartRequestDto request) => _inner.StartBenchmarkAsync(request);
        public Task<CompanionBenchmarkStopResultDto> StopBenchmarkAsync() => _inner.StopBenchmarkAsync();
        public CompanionBenchmarkHistoryListDto GetHistory(int limit) => _inner.GetHistory(limit);
        public CompanionBenchmarkHistoryDetailDto? GetHistoryDetail(Guid sessionId) => _inner.GetHistoryDetail(sessionId);
        public CompanionBenchmarkChartDto? GetHistoryChart(Guid sessionId, int buckets) => _inner.GetHistoryChart(sessionId, buckets);
        public CompanionBenchmarkComparisonDto CompareHistorySessions(Guid sessionAId, Guid sessionBId) => _inner.CompareHistorySessions(sessionAId, sessionBId);
    }

    private sealed class TestFakeBenchmarkProvider : ICompanionBenchmarkProvider
    {
        public bool ActiveState { get; set; }
        public static Guid SampleSessionId { get; } = Guid.Parse("11111111-1111-1111-1111-111111111111");

        public CompanionBenchmarkStatusDto GetStatus()
        {
            return new CompanionBenchmarkStatusDto
            {
                State = ActiveState ? "Capturing" : "Idle",
                IsActive = ActiveState,
                TargetDisplayName = ActiveState ? "Cyberpunk 2077" : null,
                ElapsedSeconds = ActiveState ? 5.2 : null
            };
        }

        public IReadOnlyList<CompanionBenchmarkTargetDto> GetEligibleTargets()
        {
            return new[]
            {
                new CompanionBenchmarkTargetDto { TargetId = "cp2077", DisplayName = "Cyberpunk 2077" }
            };
        }

        public Task<CompanionBenchmarkStartResultDto> StartBenchmarkAsync(CompanionBenchmarkStartRequestDto request)
        {
            if (ActiveState)
            {
                return Task.FromResult(new CompanionBenchmarkStartResultDto { Accepted = false, ErrorCode = "already_running" });
            }

            ActiveState = true;
            return Task.FromResult(new CompanionBenchmarkStartResultDto { Accepted = true });
        }

        public Task<CompanionBenchmarkStopResultDto> StopBenchmarkAsync()
        {
            bool wasActive = ActiveState;
            ActiveState = false;
            return Task.FromResult(new CompanionBenchmarkStopResultDto { Success = true, WasActive = wasActive });
        }

        public CompanionBenchmarkHistoryListDto GetHistory(int limit)
        {
            return new CompanionBenchmarkHistoryListDto
            {
                Sessions = new[]
                {
                    new CompanionBenchmarkHistorySummaryDto
                    {
                        SessionId = SampleSessionId,
                        GameDisplayName = "Cyberpunk 2077",
                        CapturedAtUtc = DateTime.UtcNow.AddHours(-1),
                        Status = "Completed",
                        DurationSeconds = 60.0,
                        AverageFps = 120.5
                    }
                },
                TotalCount = 1
            };
        }

        public CompanionBenchmarkHistoryDetailDto? GetHistoryDetail(Guid sessionId)
        {
            if (sessionId != SampleSessionId) return null;

            return new CompanionBenchmarkHistoryDetailDto
            {
                SessionId = SampleSessionId,
                GameDisplayName = "Cyberpunk 2077",
                CapturedAtUtc = DateTime.UtcNow.AddHours(-1),
                Status = "Completed",
                DurationSeconds = 60.0,
                AverageFps = 120.5,
                OnePercentLowFps = 95.2,
                PointOnePercentLowFps = 80.1,
                P99FrameTimeMs = 8.3,
                ProfileName = "Ultra 4K",
                SessionOptimizationActive = true,
                QualityLevel = "Valid"
            };
        }

        public CompanionBenchmarkChartDto? GetHistoryChart(Guid sessionId, int buckets)
        {
            if (sessionId != SampleSessionId) return null;

            return new CompanionBenchmarkChartDto
            {
                SessionId = SampleSessionId,
                Points = new[]
                {
                    new CompanionBenchmarkChartPointDto { ElapsedSeconds = 0.0, FrameTimeMs = 8.3 },
                    new CompanionBenchmarkChartPointDto { ElapsedSeconds = 1.0, FrameTimeMs = 8.5 }
                },
                TotalPointCount = 2
            };
        }

        public CompanionBenchmarkComparisonDto CompareHistorySessions(Guid sessionAId, Guid sessionBId)
        {
            if (sessionAId == Guid.Empty || sessionBId == Guid.Empty)
            {
                throw new KeyNotFoundException("One or both benchmark sessions were not found.");
            }

            if (sessionAId == sessionBId)
            {
                throw new FrameHub.Core.Models.Benchmarking.BenchmarkException("comparison_game_mismatch", "Only sessions for the same FrameHub library game can be compared.");
            }

            return new CompanionBenchmarkComparisonDto
            {
                SessionA = new CompanionBenchmarkHistorySummaryDto
                {
                    SessionId = sessionAId,
                    GameDisplayName = "Cyberpunk 2077",
                    CapturedAtUtc = DateTime.UtcNow.AddHours(-2),
                    Status = "Completed",
                    DurationSeconds = 60.0,
                    AverageFps = 100.0
                },
                SessionB = new CompanionBenchmarkHistorySummaryDto
                {
                    SessionId = sessionBId,
                    GameDisplayName = "Cyberpunk 2077",
                    CapturedAtUtc = DateTime.UtcNow.AddHours(-1),
                    Status = "Completed",
                    DurationSeconds = 60.0,
                    AverageFps = 120.0
                },
                Metrics = new[]
                {
                    new CompanionBenchmarkComparisonMetricDto
                    {
                        Key = "average_fps",
                        SessionA = 100.0,
                        SessionB = 120.0,
                        Delta = 20.0,
                        PercentageDelta = 20.0,
                        Direction = "HigherIsBetter"
                    }
                }
            };
        }
    }

    [TestMethod]
    public async Task GetHistoryChart_ReturnsChartData()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        server.ConfigureBenchmarkProvider(new TestFakeBenchmarkProvider());

        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        var response = await client.GetAsync($"http://127.0.0.1:{port}/api/v1/benchmarks/history/{TestFakeBenchmarkProvider.SampleSessionId}/chart?buckets=100");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var chart = await response.Content.ReadFromJsonAsync<CompanionBenchmarkChartDto>();
        Assert.IsNotNull(chart);
        Assert.AreEqual(TestFakeBenchmarkProvider.SampleSessionId, chart.SessionId);
        Assert.AreEqual(2, chart.Points.Count);
    }

    [TestMethod]
    public async Task GetHistoryChart_InvalidBuckets_ReturnsBadRequest()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        server.ConfigureBenchmarkProvider(new TestFakeBenchmarkProvider());

        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        var response = await client.GetAsync($"http://127.0.0.1:{port}/api/v1/benchmarks/history/{TestFakeBenchmarkProvider.SampleSessionId}/chart?buckets=5");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task CompareHistorySessions_ReturnsComparisonData()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        server.ConfigureBenchmarkProvider(new TestFakeBenchmarkProvider());

        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        Guid idA = Guid.NewGuid();
        Guid idB = Guid.NewGuid();

        using var client = new HttpClient();
        var response = await client.GetAsync($"http://127.0.0.1:{port}/api/v1/benchmarks/history/compare?sessionA={idA}&sessionB={idB}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var comp = await response.Content.ReadFromJsonAsync<CompanionBenchmarkComparisonDto>();
        Assert.IsNotNull(comp);
        Assert.AreEqual(idA, comp.SessionA.SessionId);
        Assert.AreEqual(idB, comp.SessionB.SessionId);
        Assert.AreEqual(1, comp.Metrics.Count);
    }

    [TestMethod]
    public async Task CompareHistorySessions_GameMismatch_ReturnsBadRequest()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        server.ConfigureBenchmarkProvider(new TestFakeBenchmarkProvider());

        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        Guid idA = Guid.NewGuid();

        using var client = new HttpClient();
        var response = await client.GetAsync($"http://127.0.0.1:{port}/api/v1/benchmarks/history/compare?sessionA={idA}&sessionB={idA}");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<CompanionBenchmarkErrorDto>();
        Assert.IsNotNull(error);
        Assert.AreEqual("comparison_game_mismatch", error.ErrorCode);
    }
}
