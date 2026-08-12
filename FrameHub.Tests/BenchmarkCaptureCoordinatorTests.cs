using System.Text.Json;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Services.Benchmarking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public sealed class BenchmarkCaptureCoordinatorTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Initialize()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FrameHubCoordinatorTests", Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    private static readonly DateTime FixedStartTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static BenchmarkCaptureRequest CreateSampleRequest(int duration = 30, int countdown = 0) => new()
    {
        Target = new BenchmarkTarget { LibraryItemId = "game-id", DisplayName = "Test Game", LibrarySource = "Manual" },
        Process = new BenchmarkProcessIdentity { ProcessId = 9999, ProcessName = "testgame", StartTimeUtc = FixedStartTime },
        AppVersion = "1.0.0",
        ProfileId = "prof-1",
        ProfileName = "Profile 1",
        SessionOptimizationActive = true,
        DurationSeconds = duration,
        CountdownSeconds = countdown
    };

    [TestMethod]
    public async Task Countdown_ExposesWaitingAndDecrementsRemainingSeconds()
    {
        var storage = new BenchmarkStorageService(_tempDir);
        int backendCalls = 0;
        bool backendCalledBeforeCountdownFinished = false;

        IBenchmarkCaptureBackend CreateBackend()
        {
            Interlocked.Increment(ref backendCalls);
            return new TestFakeBackend(storage, BackendMode.Success);
        }

        var identityProvider = new TestFakeIdentityProvider();
        var coordinator = new BenchmarkCaptureCoordinator(
            storage,
            CreateBackend,
            identityProvider,
            delayProvider: (_, _) => Task.CompletedTask);

        var snapshots = new List<BenchmarkCaptureStateSnapshot>();
        coordinator.StateChanged += (_, s) =>
        {
            snapshots.Add(s);
            if (s.State == CoordinatorState.Waiting && backendCalls > 0)
            {
                backendCalledBeforeCountdownFinished = true;
            }
        };

        var outcome = await coordinator.StartCaptureAsync(CreateSampleRequest(countdown: 3));

        Assert.AreEqual(CoordinatorStatus.Completed, outcome.Status);
        Assert.AreEqual(1, backendCalls);
        Assert.IsFalse(backendCalledBeforeCountdownFinished, "Backend must start only after countdown finishes.");
        Assert.IsTrue(snapshots.Any(s => s.State == CoordinatorState.Waiting && s.RemainingCountdownSeconds == 3));
        Assert.IsTrue(snapshots.Any(s => s.State == CoordinatorState.Waiting && s.RemainingCountdownSeconds == 2));
        Assert.IsTrue(snapshots.Any(s => s.State == CoordinatorState.Waiting && s.RemainingCountdownSeconds == 1));
        Assert.IsTrue(snapshots.Any(s => s.State == CoordinatorState.Waiting && s.RemainingCountdownSeconds == 0));
        Assert.IsTrue(identityProvider.RevalidateCount >= 3, "Identity provider must be revalidated during countdown");
    }

    [TestMethod]
    public async Task CountdownCancellation_CreatesNoSessionAndNoBackend()
    {
        var storage = new BenchmarkStorageService(_tempDir);
        int backendCalls = 0;
        IBenchmarkCaptureBackend CreateBackend()
        {
            Interlocked.Increment(ref backendCalls);
            return new TestFakeBackend(storage, BackendMode.Success);
        }

        var tcs = new TaskCompletionSource();
        var coordinator = new BenchmarkCaptureCoordinator(
            storage,
            CreateBackend,
            new TestFakeIdentityProvider(),
            delayProvider: async (delay, ct) =>
            {
                tcs.TrySetResult();
                await Task.Delay(10000, ct);
            });

        Task<BenchmarkCaptureOutcome> startTask = coordinator.StartCaptureAsync(CreateSampleRequest(countdown: 5));
        await tcs.Task;

        await coordinator.StopAsync();
        BenchmarkCaptureOutcome outcome = await startTask;

        Assert.AreEqual(CoordinatorStatus.Cancelled, outcome.Status);
        Assert.AreEqual(0, backendCalls, "Backend must not be created on cancelled countdown");
        Assert.AreEqual(0, storage.EnumerateSessions().Sessions.Count, "No session must be created on cancelled countdown");
    }

    [TestMethod]
    public async Task Concurrency_TwoSimultaneousStarts_OnlyOneAccepted()
    {
        var storage = new BenchmarkStorageService(_tempDir);
        int backendCalls = 0;
        IBenchmarkCaptureBackend CreateBackend()
        {
            Interlocked.Increment(ref backendCalls);
            return new TestFakeBackend(storage, BackendMode.WaitForCancellation);
        }

        var coordinator = new BenchmarkCaptureCoordinator(storage, CreateBackend, new TestFakeIdentityProvider());

        Task<BenchmarkCaptureOutcome> first = coordinator.StartCaptureAsync(CreateSampleRequest());
        Task<BenchmarkCaptureOutcome> second = coordinator.StartCaptureAsync(CreateSampleRequest());

        BenchmarkCaptureOutcome secondOutcome = await second;
        Assert.AreEqual(CoordinatorStatus.AlreadyRunning, secondOutcome.Status);
        Assert.AreEqual("already_running", secondOutcome.ErrorCode);

        await coordinator.StopAsync();
        BenchmarkCaptureOutcome firstOutcome = await first;
        Assert.AreEqual(CoordinatorStatus.Cancelled, firstOutcome.Status);
        Assert.AreEqual(1, backendCalls, "Exactly one backend must be instantiated");
        Assert.AreEqual(1, storage.EnumerateSessions().Sessions.Count, "Exactly one session directory must be created");
    }

    [TestMethod]
    public async Task Stop_WhileIdle_IsSafeNoOp()
    {
        var coordinator = new BenchmarkCaptureCoordinator(new BenchmarkStorageService(_tempDir));
        await coordinator.StopAsync();
        Assert.IsFalse(coordinator.IsActive);
        Assert.AreEqual(CoordinatorState.Idle, coordinator.CurrentState.State);
    }

    [TestMethod]
    public async Task Stop_DuringBackendCapture_SignalsCancellationAndAwaitsBackendTask()
    {
        var storage = new BenchmarkStorageService(_tempDir);
        var backend = new TestFakeBackend(storage, BackendMode.WaitForCancellation);
        var coordinator = new BenchmarkCaptureCoordinator(storage, () => backend, new TestFakeIdentityProvider());

        Task<BenchmarkCaptureOutcome> captureTask = coordinator.StartCaptureAsync(CreateSampleRequest());
        await backend.WaitUntilCaptureStartedAsync();

        Assert.IsTrue(coordinator.IsActive);
        Task stopTask = coordinator.StopAsync();
        await stopTask;
        BenchmarkCaptureOutcome outcome = await captureTask;

        Assert.AreEqual(CoordinatorStatus.Cancelled, outcome.Status);
        Assert.IsFalse(coordinator.IsActive);
        Assert.AreEqual(CoordinatorState.Cancelled, coordinator.CurrentState.State);
    }

    [TestMethod]
    public async Task TerminalReuse_AllowsNewCaptureAfterCompletedCancelledAndFailed()
    {
        var storage = new BenchmarkStorageService(_tempDir);

        // 1. Success
        var coordinator1 = new BenchmarkCaptureCoordinator(storage, () => new TestFakeBackend(storage, BackendMode.Success), new TestFakeIdentityProvider());
        var outcome1 = await coordinator1.StartCaptureAsync(CreateSampleRequest());
        Assert.AreEqual(CoordinatorStatus.Completed, outcome1.Status);
        Assert.IsFalse(coordinator1.IsActive);

        var outcome1b = await coordinator1.StartCaptureAsync(CreateSampleRequest());
        Assert.AreEqual(CoordinatorStatus.Completed, outcome1b.Status);

        // 2. Cancellation
        var coordinator2 = new BenchmarkCaptureCoordinator(storage, () => new TestFakeBackend(storage, BackendMode.WaitForCancellation), new TestFakeIdentityProvider());
        Task<BenchmarkCaptureOutcome> task2 = coordinator2.StartCaptureAsync(CreateSampleRequest());
        await coordinator2.StopAsync();
        var outcome2 = await task2;
        Assert.AreEqual(CoordinatorStatus.Cancelled, outcome2.Status);

        Task<BenchmarkCaptureOutcome> task2b = coordinator2.StartCaptureAsync(CreateSampleRequest());
        await coordinator2.StopAsync();
        var outcome2b = await task2b;
        Assert.AreEqual(CoordinatorStatus.Cancelled, outcome2b.Status);

        // 3. Failure
        var coordinator3 = new BenchmarkCaptureCoordinator(storage, () => new TestFakeBackend(storage, BackendMode.Failure), new TestFakeIdentityProvider());
        var outcome3 = await coordinator3.StartCaptureAsync(CreateSampleRequest());
        Assert.AreEqual(CoordinatorStatus.Failed, outcome3.Status);

        var outcome3b = await coordinator3.StartCaptureAsync(CreateSampleRequest());
        Assert.AreEqual(CoordinatorStatus.Failed, outcome3b.Status);
    }

    [TestMethod]
    public async Task BackendException_PreservesBenchmarkExceptionCode()
    {
        var storage = new BenchmarkStorageService(_tempDir);
        var coordinator = new BenchmarkCaptureCoordinator(storage, () => new TestFakeBackend(storage, BackendMode.BenchmarkErrorCode), new TestFakeIdentityProvider());
        var outcome = await coordinator.StartCaptureAsync(CreateSampleRequest());

        Assert.AreEqual(CoordinatorStatus.Failed, outcome.Status);
        Assert.AreEqual("presentmon_unavailable", outcome.ErrorCode);
    }

    [TestMethod]
    public async Task CtsLifecycle_MultipleSequentialCaptures_FreshCtsPerCapture()
    {
        var storage = new BenchmarkStorageService(_tempDir);
        var coordinator = new BenchmarkCaptureCoordinator(storage, () => new TestFakeBackend(storage, BackendMode.Success), new TestFakeIdentityProvider());

        var outcomeA = await coordinator.StartCaptureAsync(CreateSampleRequest());
        Assert.AreEqual(CoordinatorStatus.Completed, outcomeA.Status);

        var outcomeB = await coordinator.StartCaptureAsync(CreateSampleRequest());
        Assert.AreEqual(CoordinatorStatus.Completed, outcomeB.Status);
    }

    [TestMethod]
    public async Task CtsLifecycle_CancelledCaptureFollowedByNewCapture_Works()
    {
        var storage = new BenchmarkStorageService(_tempDir);
        int backendCalls = 0;
        IBenchmarkCaptureBackend CreateBackend()
        {
            int current = Interlocked.Increment(ref backendCalls);
            return current == 1
                ? new TestFakeBackend(storage, BackendMode.WaitForCancellation)
                : new TestFakeBackend(storage, BackendMode.Success);
        }

        var coordinator = new BenchmarkCaptureCoordinator(storage, CreateBackend, new TestFakeIdentityProvider());

        Task<BenchmarkCaptureOutcome> taskA = coordinator.StartCaptureAsync(CreateSampleRequest());
        await coordinator.StopAsync();
        var outcomeA = await taskA;
        Assert.AreEqual(CoordinatorStatus.Cancelled, outcomeA.Status);

        var outcomeB = await coordinator.StartCaptureAsync(CreateSampleRequest());
        Assert.AreEqual(CoordinatorStatus.Completed, outcomeB.Status);
    }

    [TestMethod]
    public async Task CtsLifecycle_RejectedConcurrentStart_DoesNotDisposeActiveCts()
    {
        var storage = new BenchmarkStorageService(_tempDir);
        var backend = new TestFakeBackend(storage, BackendMode.WaitForCancellation);
        var coordinator = new BenchmarkCaptureCoordinator(storage, () => backend, new TestFakeIdentityProvider());

        Task<BenchmarkCaptureOutcome> taskA = coordinator.StartCaptureAsync(CreateSampleRequest());
        await backend.WaitUntilCaptureStartedAsync();

        BenchmarkCaptureOutcome outcomeB = await coordinator.StartCaptureAsync(CreateSampleRequest());
        Assert.AreEqual(CoordinatorStatus.AlreadyRunning, outcomeB.Status);

        Assert.IsTrue(coordinator.IsActive);
        await coordinator.StopAsync();
        BenchmarkCaptureOutcome outcomeA = await taskA;
        Assert.AreEqual(CoordinatorStatus.Cancelled, outcomeA.Status);
    }

    [TestMethod]
    public async Task TerminalIsActive_EmitsFalseForCompletedCancelledFailed()
    {
        var storage = new BenchmarkStorageService(_tempDir);

        // 1. Completed
        var coordinator1 = new BenchmarkCaptureCoordinator(storage, () => new TestFakeBackend(storage, BackendMode.Success), new TestFakeIdentityProvider());
        BenchmarkCaptureStateSnapshot? completedSnapshot = null;
        coordinator1.StateChanged += (_, s) => { if (s.State == CoordinatorState.Completed) completedSnapshot = s; };
        await coordinator1.StartCaptureAsync(CreateSampleRequest());
        Assert.IsNotNull(completedSnapshot);
        Assert.IsFalse(completedSnapshot.IsActive, "Completed state snapshot must have IsActive = false.");

        // 2. Cancelled
        var coordinator2 = new BenchmarkCaptureCoordinator(storage, () => new TestFakeBackend(storage, BackendMode.WaitForCancellation), new TestFakeIdentityProvider());
        BenchmarkCaptureStateSnapshot? cancelledSnapshot = null;
        coordinator2.StateChanged += (_, s) => { if (s.State == CoordinatorState.Cancelled) cancelledSnapshot = s; };
        Task<BenchmarkCaptureOutcome> task2 = coordinator2.StartCaptureAsync(CreateSampleRequest());
        await coordinator2.StopAsync();
        await task2;
        Assert.IsNotNull(cancelledSnapshot);
        Assert.IsFalse(cancelledSnapshot.IsActive, "Cancelled state snapshot must have IsActive = false.");

        // 3. Failed
        var coordinator3 = new BenchmarkCaptureCoordinator(storage, () => new TestFakeBackend(storage, BackendMode.Failure), new TestFakeIdentityProvider());
        BenchmarkCaptureStateSnapshot? failedSnapshot = null;
        coordinator3.StateChanged += (_, s) => { if (s.State == CoordinatorState.Failed) failedSnapshot = s; };
        await coordinator3.StartCaptureAsync(CreateSampleRequest());
        Assert.IsNotNull(failedSnapshot);
        Assert.IsFalse(failedSnapshot.IsActive, "Failed state snapshot must have IsActive = false.");
    }

    [TestMethod]
    public async Task StopAsync_AwaitsSameActiveOperation()
    {
        var storage = new BenchmarkStorageService(_tempDir);
        var backend = new TestFakeBackend(storage, BackendMode.WaitForCancellation);
        var coordinator = new BenchmarkCaptureCoordinator(storage, () => backend, new TestFakeIdentityProvider());

        Task<BenchmarkCaptureOutcome> captureTask = coordinator.StartCaptureAsync(CreateSampleRequest());
        await backend.WaitUntilCaptureStartedAsync();

        Task stopTask = coordinator.StopAsync();
        Assert.IsFalse(stopTask.IsCompleted, "StopAsync should not complete before active task finishes.");

        await stopTask;
        Assert.IsTrue(captureTask.IsCompleted, "Active capture task must be completed when StopAsync finishes.");
    }

    private enum BackendMode
    {
        Success,
        WaitForCancellation,
        Failure,
        BenchmarkErrorCode
    }

    private sealed class TestFakeIdentityProvider : IBenchmarkProcessIdentityProvider
    {
        public int RevalidateCount;
        public BenchmarkProcessIdentity GetCurrentIdentity(int processId, BenchmarkTarget target)
        {
            Interlocked.Increment(ref RevalidateCount);
            return new BenchmarkProcessIdentity
            {
                ProcessId = processId,
                ProcessName = "testgame",
                ExecutablePath = target.ConfiguredExecutablePath,
                StartTimeUtc = FixedStartTime
            };
        }
    }

    private sealed class TestFakeBackend : IBenchmarkCaptureBackend
    {
        private readonly BenchmarkStorageService _storage;
        private readonly BackendMode _mode;
        private readonly TaskCompletionSource _startedTcs = new();

        public TestFakeBackend(BenchmarkStorageService storage, BackendMode mode)
        {
            _storage = storage;
            _mode = mode;
        }

        public async Task WaitUntilCaptureStartedAsync() => await _startedTcs.Task;

        public async Task<BenchmarkCaptureResult> CaptureAsync(BenchmarkSession session, CancellationToken cancellationToken = default)
        {
            _startedTcs.TrySetResult();

            if (_mode == BackendMode.WaitForCancellation)
            {
                var tcs = new TaskCompletionSource();
                using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)))
                {
                    await tcs.Task;
                }
            }

            if (_mode == BackendMode.BenchmarkErrorCode)
            {
                throw new PresentMonUnavailableException("PresentMon service is not running.");
            }

            if (_mode == BackendMode.Failure)
            {
                throw new InvalidOperationException("Synthetic failure");
            }

            session.Metadata.Status = BenchmarkSessionStatus.Completed;
            session.Metadata.CaptureDurationSeconds = 1;
            session.Metadata.AnalyzedDurationSeconds = 0.016;

            var summary = new BenchmarkSummary
            {
                SessionId = session.Metadata.SessionId,
                CaptureDurationSeconds = 1,
                AnalyzedDurationSeconds = 0.016,
                SelectedSwapChainAddress = "0x1",
                PrimaryPresentedMetrics = new BenchmarkMetricSet
                {
                    ValidFrameCount = 1,
                    AverageFps = 60.0,
                    OnePercentLowFps = 50.0,
                    PointOnePercentLowFps = 40.0,
                    P99FrameTimeMs = 16.6
                },
                Quality = new BenchmarkQualityResult { Level = BenchmarkQualityLevel.Valid }
            };

            _storage.SaveSession(session);
            _storage.SaveSummary(session, summary);
            File.WriteAllText(session.RawDataPath, JsonSerializer.Serialize(new[] { new BenchmarkFrameSample { ProcessId = session.Metadata.Process.ProcessId, SwapChainAddress = "0x1", MsBetweenPresents = 16.6 } }));

            return new BenchmarkCaptureResult { Session = session, Summary = summary };
        }
    }
}
