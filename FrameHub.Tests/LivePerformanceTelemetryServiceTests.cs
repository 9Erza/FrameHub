using System.Buffers.Binary;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Services.Benchmarking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public sealed class LivePerformanceTelemetryServiceTests
{
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
        coordinator.TryStartCapture(CreateSampleRequest(30));

        await Task.Delay(100);
        Assert.IsNull(service.CurrentSnapshot);

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
        coordinator.TryStartCapture(CreateSampleRequest(30));

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

    private static BenchmarkCaptureCoordinator CreateTestCoordinator() => new(
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

    private static ActiveGameSnapshot CreateActiveGame(int pid, string id, string name) => new(
        new LibraryItem { Id = id, DisplayName = name, ExecutablePath = $"{name}.exe", Type = LibraryItemType.Game, IsEnabled = true },
        new BenchmarkProcessIdentity { ProcessId = pid, ProcessName = name, ExecutablePath = $"C:\\{name}.exe", StartTimeUtc = DateTime.UtcNow }
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
        private PmQueryElement[] _elements = Array.Empty<PmQueryElement>();
        private bool _consumedOnce;

        public PmStatus OpenSession(out nint session) { Calls.Add("open"); session = 10; return PmStatus.Success; }
        public PmStatus CloseSession(nint session) { Calls.Add("close"); return PmStatus.Success; }
        public PmStatus StartTrackingProcess(nint session, uint processId) { Calls.Add($"start:{processId}"); return PmStatus.Success; }
        public PmStatus StopTrackingProcess(nint session, uint processId) { Calls.Add($"stop:{processId}"); return PmStatus.Success; }
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
