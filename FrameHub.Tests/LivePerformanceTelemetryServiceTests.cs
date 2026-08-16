using System.Buffers.Binary;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Services.Benchmarking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public sealed class LivePerformanceTelemetryServiceTests
{
    private string _tempDirectory = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.LiveTelemetryTests", Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, recursive: true);
        }
        catch { }
    }

    [TestMethod]
    public async Task LiveService_TracksCorrectPid_AndCalculatesRollingMetrics()
    {
        var game = CreateActiveGame(9999, "g1", "Game One");
        var activeGameMonitor = new FakeActiveGameMonitor(game);
        var coordinator = CreateTestCoordinator();
        var fakeApi = new TestFakeApi();

        using var service = new LivePerformanceTelemetryService(
            activeGameMonitor,
            coordinator,
            apiFactory: () => fakeApi,
            delayProvider: InstantDelayProvider);

        service.Start();
        await Task.Delay(100);

        var snapshot = service.CurrentSnapshot;
        Assert.IsNotNull(snapshot);
        Assert.AreEqual(9999, snapshot.ProcessId);
        Assert.AreEqual("g1", snapshot.LibraryItemId);
        Assert.IsNotNull(snapshot.CurrentFps);
        Assert.IsTrue(snapshot.CurrentFps > 0);
        Assert.IsNotNull(snapshot.OnePercentLowFps);
        Assert.IsNotNull(snapshot.PointOnePercentLowFps);

        await service.StopAsync();
        Assert.IsTrue(fakeApi.Calls.Contains("stop:9999"));
        Assert.IsTrue(fakeApi.Calls.Contains("close"));
    }

    [TestMethod]
    public async Task LiveService_PreemptsWhenCoordinatorBecomesActive()
    {
        var game = CreateActiveGame(8888, "g2", "Game Two");
        var activeGameMonitor = new FakeActiveGameMonitor(game);
        var coordinator = CreateTestCoordinator();
        var fakeApi = new TestFakeApi();

        using var service = new LivePerformanceTelemetryService(
            activeGameMonitor,
            coordinator,
            apiFactory: () => fakeApi,
            delayProvider: InstantDelayProvider);

        service.Start();
        await Task.Delay(100);
        Assert.IsNotNull(service.CurrentSnapshot);

        // Trigger coordinator capture
        coordinator.TryStartCapture(CreateSampleRequest(30)).Start();

        await Task.Delay(100);
        Assert.IsNull(service.CurrentSnapshot);

        await service.StopAsync();
    }

    [TestMethod]
    public async Task LiveService_ResumesAfterCoordinatorBecomesInactive()
    {
        var game = CreateActiveGame(8877, "g-resume", "Resume Game");
        var activeGameMonitor = new FakeActiveGameMonitor(game);
        var coordinator = CreateTestCoordinator();
        var fakeApis = new List<TestFakeApi>();

        using var service = new LivePerformanceTelemetryService(
            activeGameMonitor,
            coordinator,
            apiFactory: () =>
            {
                var api = new TestFakeApi();
                fakeApis.Add(api);
                return api;
            },
            delayProvider: InstantDelayProvider);

        service.Start();
        await WaitUntilAsync(() => service.CurrentSnapshot != null);

        coordinator.TryStartCapture(CreateSampleRequest(30)).Start();
        await WaitUntilAsync(() => service.CurrentSnapshot == null);

        await coordinator.StopAsync();
        Assert.IsFalse(coordinator.IsActive);

        await WaitUntilAsync(() => service.CurrentSnapshot != null);
        Assert.AreEqual(8877, service.CurrentSnapshot?.ProcessId);
        Assert.IsTrue(fakeApis.Count >= 2, "Live telemetry must establish a fresh private PresentMon session after benchmark preemption.");

        await service.StopAsync();
    }

    [TestMethod]
    public async Task BenchmarkBackendAcquisition_WaitsForActualLivePresentMonTeardown_ThenLiveResumes()
    {
        var game = CreateActiveGame(7788, "g-order", "Ordering Game");
        var activeGameMonitor = new FakeActiveGameMonitor(game);
        var storage = new BenchmarkStorageService(_tempDirectory);
        TestFakeApi? firstApi = null;
        int apiCount = 0;
        var backendAcquired = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new BenchmarkCaptureCoordinator(
            storage,
            backendFactory: () =>
            {
                backendAcquired.TrySetResult(firstApi?.Calls.ToList() ?? []);
                return new BlockingBackend();
            });

        using var service = new LivePerformanceTelemetryService(
            activeGameMonitor,
            coordinator,
            apiFactory: () =>
            {
                var api = new TestFakeApi();
                if (Interlocked.Increment(ref apiCount) == 1) firstApi = api;
                return api;
            },
            delayProvider: InstantDelayProvider);
        coordinator.ConfigureLivePresentMonPreemption(service, TimeSpan.FromSeconds(1));

        service.Start();
        await WaitUntilAsync(() => service.CurrentSnapshot != null);

        Task<BenchmarkCaptureOutcome> capture = coordinator.StartCaptureAsync(CreateSampleRequest(30));
        IReadOnlyList<string> callsAtBackendAcquisition = await backendAcquired.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var orderedCalls = callsAtBackendAcquisition.ToList();

        CollectionAssert.IsSubsetOf(new[] { "stop:7788", "free", "close", "dispose" }, orderedCalls);
        Assert.IsTrue(orderedCalls.IndexOf("stop:7788") < orderedCalls.IndexOf("free"));
        Assert.IsTrue(orderedCalls.IndexOf("free") < orderedCalls.IndexOf("close"));
        Assert.IsTrue(orderedCalls.IndexOf("close") < orderedCalls.IndexOf("dispose"));

        await coordinator.StopAsync();
        Assert.AreEqual(CoordinatorStatus.Cancelled, (await capture).Status);
        await WaitUntilAsync(() => service.CurrentSnapshot != null && Volatile.Read(ref apiCount) >= 2);
        await service.StopAsync();
    }

    [TestMethod]
    public async Task BenchmarkPreemption_NoLiveSession_CompletesImmediately()
    {
        var activeGameMonitor = new FakeActiveGameMonitor(null);
        var storage = new BenchmarkStorageService(_tempDirectory);
        var backendAcquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new BenchmarkCaptureCoordinator(
            storage,
            backendFactory: () =>
            {
                backendAcquired.TrySetResult();
                return new BlockingBackend();
            });
        using var service = new LivePerformanceTelemetryService(activeGameMonitor, coordinator);
        coordinator.ConfigureLivePresentMonPreemption(service, TimeSpan.FromMilliseconds(200));

        Task<BenchmarkCaptureOutcome> capture = coordinator.StartCaptureAsync(CreateSampleRequest(30));
        await backendAcquired.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await coordinator.StopAsync();
        Assert.AreEqual(CoordinatorStatus.Cancelled, (await capture).Status);
    }

    [TestMethod]
    public async Task BenchmarkPreemption_CloseFailure_FailsClosedAndBlocksAllFutureNativeOwnership()
    {
        var game = CreateActiveGame(6677, "g-failure", "Failure Game");
        var activeGameMonitor = new FakeActiveGameMonitor(game);
        var storage = new BenchmarkStorageService(_tempDirectory);
        int backendCalls = 0;
        int apiCount = 0;
        var coordinator = new BenchmarkCaptureCoordinator(
            storage,
            backendFactory: () =>
            {
                Interlocked.Increment(ref backendCalls);
                return new BlockingBackend();
            });
        using var service = new LivePerformanceTelemetryService(
            activeGameMonitor,
            coordinator,
            apiFactory: () =>
            {
                Interlocked.Increment(ref apiCount);
                return new TestFakeApi { CloseStatus = PmStatus.Failure };
            },
            delayProvider: InstantDelayProvider);
        coordinator.ConfigureLivePresentMonPreemption(service, TimeSpan.FromSeconds(1));

        service.Start();
        await WaitUntilAsync(() => service.CurrentSnapshot != null);

        BenchmarkCaptureOutcome outcome = await coordinator.StartCaptureAsync(CreateSampleRequest(30));

        Assert.AreEqual(CoordinatorStatus.Failed, outcome.Status);
        Assert.AreEqual("live_telemetry_preemption_failed", outcome.ErrorCode);
        Assert.AreEqual(0, backendCalls);

        BenchmarkCaptureOutcome secondOutcome = await coordinator.StartCaptureAsync(CreateSampleRequest(30));

        Assert.AreEqual(CoordinatorStatus.Failed, secondOutcome.Status);
        Assert.AreEqual("live_telemetry_preemption_failed", secondOutcome.ErrorCode);
        Assert.AreEqual(0, backendCalls);
        Assert.AreEqual(1, apiCount, "An uncertain CloseSession result must block fresh live acquisition for the process lifetime.");
        Assert.IsFalse(coordinator.IsActive);
        await service.StopAsync();
    }

    [TestMethod]
    public async Task BenchmarkPreemption_StopTrackingWarning_RecoversWithoutRestartAfterCloseSucceeds()
    {
        var game = CreateActiveGame(6678, "g-recoverable", "Recoverable Game");
        var activeGameMonitor = new FakeActiveGameMonitor(game);
        var storage = new BenchmarkStorageService(_tempDirectory);
        int backendCalls = 0;
        int apiCount = 0;
        var coordinator = new BenchmarkCaptureCoordinator(
            storage,
            backendFactory: () =>
            {
                Interlocked.Increment(ref backendCalls);
                return new BlockingBackend();
            });
        using var service = new LivePerformanceTelemetryService(
            activeGameMonitor,
            coordinator,
            apiFactory: () => new TestFakeApi
            {
                StopStatus = Interlocked.Increment(ref apiCount) == 1 ? PmStatus.Failure : PmStatus.Success,
                CloseStatus = PmStatus.Success
            },
            delayProvider: InstantDelayProvider);
        coordinator.ConfigureLivePresentMonPreemption(service, TimeSpan.FromSeconds(1));

        service.Start();
        await WaitUntilAsync(() => service.CurrentSnapshot != null);

        BenchmarkCaptureOutcome firstOutcome = await coordinator.StartCaptureAsync(CreateSampleRequest(30));
        Assert.AreEqual(CoordinatorStatus.Failed, firstOutcome.Status);
        Assert.AreEqual("live_telemetry_preemption_failed", firstOutcome.ErrorCode);
        Assert.AreEqual(0, backendCalls);

        await WaitUntilAsync(() => apiCount >= 2 && service.CurrentSnapshot != null);
        Task<BenchmarkCaptureOutcome> secondCapture = coordinator.StartCaptureAsync(CreateSampleRequest(30));
        await WaitUntilAsync(() => Volatile.Read(ref backendCalls) == 1);
        await coordinator.StopAsync();

        Assert.AreEqual(CoordinatorStatus.Cancelled, (await secondCapture).Status);
        Assert.IsFalse(coordinator.IsActive);
        await service.StopAsync();
    }

    [TestMethod]
    public async Task LiveService_CoordinatorActiveAtStart_PreventsTracking()
    {
        var game = CreateActiveGame(7777, "g3", "Game Three");
        var activeGameMonitor = new FakeActiveGameMonitor(game);
        var coordinator = CreateTestCoordinator();
        var fakeApi = new TestFakeApi();

        // Start coordinator first
        coordinator.TryStartCapture(CreateSampleRequest(30)).Start();

        for (int i = 0; i < 50 && !coordinator.IsActive; i++)
        {
            await Task.Delay(10);
        }
        Assert.IsTrue(coordinator.IsActive, "Coordinator must be active before test continues");

        using var service = new LivePerformanceTelemetryService(
            activeGameMonitor,
            coordinator,
            apiFactory: () => fakeApi,
            delayProvider: InstantDelayProvider);

        service.Start();
        await Task.Delay(100);

        Assert.IsNull(service.CurrentSnapshot);
        Assert.IsFalse(fakeApi.Calls.Any(c => c.StartsWith("start:", StringComparison.Ordinal)));

        await service.StopAsync();
    }

    [TestMethod]
    public async Task LiveService_ProcessChange_CausesDetachAndReattach()
    {
        var game1 = CreateActiveGame(1111, "g1", "Game One");
        var activeGameMonitor = new FakeActiveGameMonitor(game1);
        var coordinator = CreateTestCoordinator();
        var fakeApi1 = new TestFakeApi();
        var fakeApi2 = new TestFakeApi();
        int factoryCalls = 0;

        using var service = new LivePerformanceTelemetryService(
            activeGameMonitor,
            coordinator,
            apiFactory: () => factoryCalls++ == 0 ? fakeApi1 : fakeApi2,
            delayProvider: InstantDelayProvider);

        service.Start();
        await Task.Delay(100);
        Assert.AreEqual(1111, service.CurrentSnapshot?.ProcessId);

        // Change process identity
        var game2 = CreateActiveGame(2222, "g2", "Game Two");
        activeGameMonitor.SetGame(game2);
        await Task.Delay(100);

        Assert.AreEqual(2222, service.CurrentSnapshot?.ProcessId);
        Assert.IsTrue(fakeApi1.Calls.Contains("stop:1111"));
        Assert.IsTrue(fakeApi2.Calls.Contains("start:2222"));

        await service.StopAsync();
    }

    [TestMethod]
    public async Task LiveService_StaleMetricsDisappear_WhenNoGamesDetected()
    {
        var game = CreateActiveGame(5555, "g5", "Game Five");
        var activeGameMonitor = new FakeActiveGameMonitor(game);
        var coordinator = CreateTestCoordinator();
        var fakeApi = new TestFakeApi();

        using var service = new LivePerformanceTelemetryService(
            activeGameMonitor,
            coordinator,
            apiFactory: () => fakeApi,
            delayProvider: InstantDelayProvider);

        service.Start();
        await Task.Delay(100);
        Assert.IsNotNull(service.CurrentSnapshot);

        // Remove active game
        activeGameMonitor.SetGame(null);
        await Task.Delay(100);

        Assert.IsNull(service.CurrentSnapshot);

        await service.StopAsync();
    }

    [TestMethod]
    public async Task LiveService_RiotActiveGame_NeverCreatesPresentMonSession()
    {
        var league = CreateRiotActiveGame(4242, "riot-lol", "League of Legends", allowBenchmark: false);
        var activeGameMonitor = new FakeActiveGameMonitor(league);
        var coordinator = CreateTestCoordinator();
        int factoryCalls = 0;

        using var service = new LivePerformanceTelemetryService(
            activeGameMonitor,
            coordinator,
            apiFactory: () => { Interlocked.Increment(ref factoryCalls); return new TestFakeApi(); },
            delayProvider: InstantDelayProvider);

        service.Start();
        await Task.Delay(150);

        Assert.AreEqual(0, factoryCalls, "A benchmark-ineligible Riot active game must never create a PresentMon session.");
        Assert.IsNull(service.CurrentSnapshot, "No live FPS data may be fabricated for a Riot game.");

        await service.StopAsync();
    }

    [TestMethod]
    public async Task LiveService_ProtectedRiotProcessName_NeverCreatesPresentMonSession()
    {
        var manualRiot = CreateRiotActiveGame(5000, "manual-lol", "My League shortcut", allowBenchmark: true);
        var activeGameMonitor = new FakeActiveGameMonitor(manualRiot);
        var coordinator = CreateTestCoordinator();
        int factoryCalls = 0;

        using var service = new LivePerformanceTelemetryService(
            activeGameMonitor,
            coordinator,
            apiFactory: () => { Interlocked.Increment(ref factoryCalls); return new TestFakeApi(); },
            delayProvider: InstantDelayProvider);

        service.Start();
        await Task.Delay(150);

        Assert.AreEqual(0, factoryCalls, "A protected Riot process name must never receive live PresentMon, even if the item kept AllowBenchmark == true.");
        Assert.IsNull(service.CurrentSnapshot);

        await service.StopAsync();
    }

    [TestMethod]
    public async Task LiveService_TransitionFromEligibleGameToRiot_TearsDownWithoutReattach()
    {
        var cs2 = CreateActiveGame(3333, "cs2", "Counter-Strike 2");
        var league = CreateRiotActiveGame(4242, "riot-lol", "League of Legends", allowBenchmark: false);
        var activeGameMonitor = new FakeActiveGameMonitor(cs2);
        var coordinator = CreateTestCoordinator();
        var firstApi = new TestFakeApi();
        int factoryCalls = 0;

        using var service = new LivePerformanceTelemetryService(
            activeGameMonitor,
            coordinator,
            apiFactory: () => { Interlocked.Increment(ref factoryCalls); return firstApi; },
            delayProvider: InstantDelayProvider);

        service.Start();
        await WaitUntilAsync(() => service.CurrentSnapshot != null);
        Assert.AreEqual(3333, service.CurrentSnapshot?.ProcessId);
        int callsAfterCs2 = factoryCalls;
        Assert.AreEqual(1, callsAfterCs2, "CS2 must keep its live PresentMon session.");

        activeGameMonitor.SetGame(league);
        await WaitUntilAsync(() => service.CurrentSnapshot == null);
        await Task.Delay(100);

        Assert.IsTrue(firstApi.Calls.Contains("stop:3333"), "The CS2 PresentMon session must be torn down.");
        Assert.IsTrue(firstApi.Calls.Contains("close"));
        Assert.IsFalse(firstApi.Calls.Any(c => c.StartsWith("start:4242", StringComparison.Ordinal)), "No PresentMon session may be created for the Riot game.");
        Assert.AreEqual(1, factoryCalls, "No new PresentMon API session may be created after switching to a Riot game.");
        Assert.IsNull(service.CurrentSnapshot);

        await service.StopAsync();
    }

    private BenchmarkCaptureCoordinator CreateTestCoordinator() => new(
        storage: new BenchmarkStorageService(_tempDirectory),
        backendFactory: () => new BlockingBackend(),
        delayProvider: InstantDelayProvider
    );

    private sealed class BlockingBackend : IBenchmarkCaptureBackend
    {
        public async Task<BenchmarkCaptureResult> CaptureAsync(BenchmarkSession session, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new TaskCanceledException();
        }
    }

    private static BenchmarkCaptureRequest CreateSampleRequest(int durationSeconds) => new()
    {
        Target = new BenchmarkTarget { LibraryItemId = "game-id", DisplayName = "Test Game", LibrarySource = "Manual" },
        Process = new BenchmarkProcessIdentity { ProcessId = 9999, ProcessName = "testgame", StartTimeUtc = DateTime.UtcNow },
        AppVersion = "1.0.0",
        DurationSeconds = durationSeconds,
        CountdownSeconds = 0
    };

    private static Task InstantDelayProvider(TimeSpan delay, CancellationToken cancellationToken)
    {
        return Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.IsTrue(condition(), "Expected asynchronous condition was not reached within the test timeout.");
    }

    private static ActiveGameSnapshot CreateActiveGame(int pid, string id, string name) => new(
        new LibraryItem { Id = id, DisplayName = name, ExecutablePath = $"{name}.exe", Type = LibraryItemType.Game, IsEnabled = true },
        new BenchmarkProcessIdentity { ProcessId = pid, ProcessName = name, ExecutablePath = $"C:\\{name}.exe", StartTimeUtc = DateTime.UtcNow }
    );

    private static ActiveGameSnapshot CreateRiotActiveGame(int pid, string id, string name, bool allowBenchmark) => new(
        new LibraryItem { Id = id, DisplayName = name, ExecutablePath = $"C:\\Riot Games\\{name}\\Game\\{name}.exe", Type = LibraryItemType.Game, IsEnabled = true, AllowBenchmark = allowBenchmark },
        new BenchmarkProcessIdentity { ProcessId = pid, ProcessName = "League of Legends", ExecutablePath = $"C:\\Riot Games\\{name}\\Game\\{name}.exe", StartTimeUtc = DateTime.UtcNow }
    );

    private sealed class FakeActiveGameMonitor : IActiveGameMonitor
    {
        public ActiveGameSnapshot? CurrentSnapshot { get; private set; }
        public FakeActiveGameMonitor(ActiveGameSnapshot? initial) => CurrentSnapshot = initial;
        public void SetGame(ActiveGameSnapshot? game) => CurrentSnapshot = game;
        public void Start() { }
        public Task StopAsync() => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class TestFakeApi : IPresentMonApi
    {
        public List<string> Calls { get; } = new();
        public PmStatus CloseStatus { get; init; } = PmStatus.Success;
        public PmStatus StopStatus { get; init; } = PmStatus.Success;
        private PmQueryElement[] _elements = Array.Empty<PmQueryElement>();
        private bool _consumedOnce;

        public PmStatus OpenSession(out nint session) { Calls.Add("open"); session = 10; return PmStatus.Success; }
        public PmStatus CloseSession(nint session) { Calls.Add("close"); return CloseStatus; }
        public PmStatus StartTrackingProcess(nint session, uint processId) { Calls.Add($"start:{processId}"); return PmStatus.Success; }
        public PmStatus StopTrackingProcess(nint session, uint processId) { Calls.Add($"stop:{processId}"); return StopStatus; }
        public PmStatus FlushFrames(nint session, uint processId) { Calls.Add($"flush:{processId}"); return PmStatus.Success; }
        public PmStatus RegisterFrameQuery(nint session, PmQueryElement[] elements, out nint query, out uint blobSize)
        {
            Calls.Add("register");
            query = 20;
            blobSize = 32;
            _elements = elements;
            for (int i = 0; i < elements.Length; i++)
            {
                elements[i].DataOffset = (ulong)(i * 8);
                elements[i].DataSize = 8;
            }
            return PmStatus.Success;
        }
        public PmStatus ConsumeFrames(nint query, uint processId, byte[] blobs, ref uint frameCount)
        {
            Calls.Add($"consume:{processId}");
            if (_consumedOnce)
            {
                frameCount = 0;
                return PmStatus.Success;
            }
            _consumedOnce = true;
            frameCount = 5;
            for (int i = 0; i < 5; i++)
            {
                var span = blobs.AsSpan(i * 32, 32);
                foreach (var el in _elements)
                {
                    var val = span.Slice((int)el.DataOffset, (int)el.DataSize);
                    if (el.Metric == PmMetric.SwapChainAddress)
                    {
                        BinaryPrimitives.WriteUInt64LittleEndian(val, 0x1000u);
                    }
                    else if (el.Metric == PmMetric.BetweenPresents)
                    {
                        BitConverter.TryWriteBytes(val, 16.67d); // 60 FPS
                    }
                }
            }
            return PmStatus.Success;
        }
        public PmStatus FreeFrameQuery(nint query) { Calls.Add("free"); return PmStatus.Success; }
        public PmStatus GetApiVersion(out PmVersion version)
        {
            Calls.Add("version");
            version = new PmVersion { Major = 2, Minor = 5, Patch = 1, Tag = new byte[22], Hash = new byte[8], Config = new byte[4] };
            return PmStatus.Success;
        }
        public IReadOnlyDictionary<PmMetric, PmFrameMetricInfo> GetFrameMetricInfo(nint session)
        {
            Calls.Add("introspection");
            return new Dictionary<PmMetric, PmFrameMetricInfo>
            {
                [PmMetric.SwapChainAddress] = new PmFrameMetricInfo(PmMetricType.FrameEvent, PmDataType.UInt64),
                [PmMetric.BetweenPresents] = new PmFrameMetricInfo(PmMetricType.FrameEvent, PmDataType.Double)
            };
        }
        public void Dispose() { Calls.Add("dispose"); }
    }
}
