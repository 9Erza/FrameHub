using System.Text.Json;
using System.Collections.Concurrent;
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
        var firstBackend = new TestFakeBackend(storage, BackendMode.WaitForCancellation);
        IBenchmarkCaptureBackend CreateBackend()
        {
            Interlocked.Increment(ref backendCalls);
            return firstBackend;
        }

        var coordinator = new BenchmarkCaptureCoordinator(storage, CreateBackend, new TestFakeIdentityProvider());

        Task<BenchmarkCaptureOutcome> first = coordinator.StartCaptureAsync(CreateSampleRequest());
        Task<BenchmarkCaptureOutcome> second = coordinator.StartCaptureAsync(CreateSampleRequest());

        BenchmarkCaptureOutcome secondOutcome = await second;
        Assert.AreEqual(CoordinatorStatus.AlreadyRunning, secondOutcome.Status);
        Assert.AreEqual("already_running", secondOutcome.ErrorCode);

        await firstBackend.WaitUntilCaptureStartedAsync();
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
    public async Task ThrowingStateSubscriber_DoesNotFailCaptureOrBlockOtherSubscribers()
    {
        var storage = new BenchmarkStorageService(_tempDir);
        var coordinator = new BenchmarkCaptureCoordinator(storage, () => new TestFakeBackend(storage, BackendMode.Success), new TestFakeIdentityProvider());
        BenchmarkCaptureStateSnapshot? completed = null;

        coordinator.StateChanged += (_, _) => throw new InvalidOperationException("subscriber failure");
        coordinator.StateChanged += (_, snapshot) =>
        {
            if (snapshot.State == CoordinatorState.Completed)
            {
                completed = snapshot;
            }
        };

        BenchmarkCaptureOutcome outcome = await coordinator.StartCaptureAsync(CreateSampleRequest());

        Assert.AreEqual(CoordinatorStatus.Completed, outcome.Status);
        Assert.IsNotNull(completed, "One faulty observer must not prevent other state observers from receiving terminal state.");
    }

    [TestMethod]
    public async Task LivePreemptionTimeout_FailsCaptureBeforeBackendAcquisition()
    {
        var storage = new BenchmarkStorageService(_tempDir);
        int backendCalls = 0;
        var coordinator = new BenchmarkCaptureCoordinator(
            storage,
            () =>
            {
                Interlocked.Increment(ref backendCalls);
                return new TestFakeBackend(storage, BackendMode.Success);
            },
            new TestFakeIdentityProvider());
        coordinator.ConfigureLivePresentMonPreemption(new NeverReleasingPreemption(), TimeSpan.FromMilliseconds(50));

        BenchmarkCaptureOutcome outcome = await coordinator.StartCaptureAsync(CreateSampleRequest());

        Assert.AreEqual(CoordinatorStatus.Failed, outcome.Status);
        Assert.AreEqual("live_telemetry_preemption_timeout", outcome.ErrorCode);
        Assert.AreEqual(0, backendCalls);
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
        var firstBackend = new TestFakeBackend(storage, BackendMode.WaitForCancellation);

        IBenchmarkCaptureBackend CreateBackend()
        {
            int current = Interlocked.Increment(ref backendCalls);
            return current == 1
                ? firstBackend
                : new TestFakeBackend(storage, BackendMode.Success);
        }

        var coordinator = new BenchmarkCaptureCoordinator(storage, CreateBackend, new TestFakeIdentityProvider());

        Task<BenchmarkCaptureOutcome> taskA = coordinator.StartCaptureAsync(CreateSampleRequest());
        await firstBackend.WaitUntilCaptureStartedAsync();
        await coordinator.StopAsync();
        var outcomeA = await taskA;
        Assert.AreEqual(CoordinatorStatus.Cancelled, outcomeA.Status);

        var outcomeB = await coordinator.StartCaptureAsync(CreateSampleRequest());
        Assert.AreEqual(CoordinatorStatus.Completed, outcomeB.Status);
        Assert.AreEqual(2, backendCalls, "Both capture A and capture B must instantiate backends.");
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
        var completedNotification = new TaskCompletionSource<BenchmarkCaptureStateSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator1.StateChanged += (_, s) => { if (s.State == CoordinatorState.Completed) completedNotification.TrySetResult(s); };
        await coordinator1.StartCaptureAsync(CreateSampleRequest());
        BenchmarkCaptureStateSnapshot completedSnapshot = await completedNotification.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.IsFalse(completedSnapshot.IsActive, "Completed state snapshot must have IsActive = false.");

        // 2. Cancelled
        var coordinator2 = new BenchmarkCaptureCoordinator(storage, () => new TestFakeBackend(storage, BackendMode.WaitForCancellation), new TestFakeIdentityProvider());
        var cancelledNotification = new TaskCompletionSource<BenchmarkCaptureStateSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator2.StateChanged += (_, s) => { if (s.State == CoordinatorState.Cancelled) cancelledNotification.TrySetResult(s); };
        Task<BenchmarkCaptureOutcome> task2 = coordinator2.StartCaptureAsync(CreateSampleRequest());
        await coordinator2.StopAsync();
        await task2;
        BenchmarkCaptureStateSnapshot cancelledSnapshot = await cancelledNotification.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.IsFalse(cancelledSnapshot.IsActive, "Cancelled state snapshot must have IsActive = false.");

        // 3. Failed
        var coordinator3 = new BenchmarkCaptureCoordinator(storage, () => new TestFakeBackend(storage, BackendMode.Failure), new TestFakeIdentityProvider());
        var failedNotification = new TaskCompletionSource<BenchmarkCaptureStateSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator3.StateChanged += (_, s) => { if (s.State == CoordinatorState.Failed) failedNotification.TrySetResult(s); };
        await coordinator3.StartCaptureAsync(CreateSampleRequest());
        BenchmarkCaptureStateSnapshot failedSnapshot = await failedNotification.Task.WaitAsync(TimeSpan.FromSeconds(1));
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

    [TestMethod]
    public async Task TryStartCapture_WhenIdle_ReturnsAcceptedWithTask()
    {
        var storage = new BenchmarkStorageService(_tempDir);
        var coordinator = new BenchmarkCaptureCoordinator(storage, () => new TestFakeBackend(storage, BackendMode.Success), new TestFakeIdentityProvider());

        var handle = coordinator.TryStartCapture(CreateSampleRequest());

        Assert.IsTrue(handle.Accepted);
        Assert.IsNull(handle.ErrorCode);
        Assert.IsNotNull(handle.CompletionTask);
        Assert.IsTrue(handle.Start());

        var outcome = await handle.CompletionTask;
        Assert.AreEqual(CoordinatorStatus.Completed, outcome.Status);
    }

    [TestMethod]
    public async Task TryStartCapture_BlockedPreemption_IsActiveBeforeAcceptanceReturnsAndClearsOnFailure()
    {
        var storage = new BenchmarkStorageService(_tempDir);
        var preemption = new BlockingFailedPreemption();
        var coordinator = new BenchmarkCaptureCoordinator(storage, () => new TestFakeBackend(storage, BackendMode.Success), new TestFakeIdentityProvider());
        coordinator.ConfigureLivePresentMonPreemption(preemption, TimeSpan.FromSeconds(2));

        BenchmarkCaptureStartHandle handle = coordinator.TryStartCapture(CreateSampleRequest());

        Assert.IsTrue(handle.Accepted);
        Assert.IsTrue(coordinator.IsActive, "An accepted capture must be authoritative before TryStartCapture returns.");
        Assert.IsTrue(handle.Start());
        await preemption.Entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.IsTrue(coordinator.IsActive);

        preemption.Release.TrySetResult(false);
        BenchmarkCaptureOutcome outcome = await handle.CompletionTask!;

        Assert.AreEqual(CoordinatorStatus.Failed, outcome.Status);
        Assert.AreEqual("live_telemetry_preemption_failed", outcome.ErrorCode);
        Assert.IsFalse(coordinator.IsActive);
    }

    [TestMethod]
    public async Task TryStartCapture_WhileActive_ReturnsRejectedWithoutSecondBackendOrSession()
    {
        var storage = new BenchmarkStorageService(_tempDir);
        int backendCalls = 0;
        var firstBackend = new TestFakeBackend(storage, BackendMode.WaitForCancellation);
        IBenchmarkCaptureBackend CreateBackend()
        {
            Interlocked.Increment(ref backendCalls);
            return firstBackend;
        }

        var coordinator = new BenchmarkCaptureCoordinator(storage, CreateBackend, new TestFakeIdentityProvider());

        var handle1 = coordinator.TryStartCapture(CreateSampleRequest());
        Assert.IsTrue(handle1.Accepted);
        Assert.IsTrue(handle1.Start());

        var handle2 = coordinator.TryStartCapture(CreateSampleRequest());
        Assert.IsFalse(handle2.Accepted);
        Assert.AreEqual("already_running", handle2.ErrorCode);
        Assert.IsNull(handle2.CompletionTask);

        await firstBackend.WaitUntilCaptureStartedAsync();
        await coordinator.StopAsync();
        await handle1.CompletionTask!;

        Assert.AreEqual(1, backendCalls, "Second rejected start must not create backend");
        Assert.AreEqual(1, storage.EnumerateSessions().Sessions.Count, "Second rejected start must not create session");
    }

    [TestMethod]
    public async Task TryStartCapture_FastCompletion_AcceptanceDoesNotDependOnTaskIsCompleted()
    {
        var storage = new BenchmarkStorageService(_tempDir);
        var coordinator = new BenchmarkCaptureCoordinator(storage, () => new TestFakeBackend(storage, BackendMode.Success), new TestFakeIdentityProvider());

        var handle = coordinator.TryStartCapture(CreateSampleRequest());
        Assert.IsTrue(handle.Accepted);
        Assert.IsTrue(handle.Start());

        // Await completion so task is completed
        await handle.CompletionTask!;

        // The handle itself remains Accepted=true regardless of completion timing
        Assert.IsTrue(handle.Accepted);
    }

    [TestMethod]
    public async Task ImmediatePreemptionFailure_PublishesWaitingBeforeFailed()
    {
        var storage = new BenchmarkStorageService(_tempDir);
        var states = new ConcurrentQueue<CoordinatorState>();
        var failedNotification = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool activeAtAcceptedPublication = false;
        var coordinator = new BenchmarkCaptureCoordinator(
            storage,
            () => new TestFakeBackend(storage, BackendMode.Success),
            new TestFakeIdentityProvider());
        coordinator.ConfigureLivePresentMonPreemption(new ImmediateFailedPreemption());
        coordinator.StateChanged += (_, snapshot) =>
        {
            states.Enqueue(snapshot.State);
            if (snapshot.State == CoordinatorState.Failed) failedNotification.TrySetResult();
            if (snapshot.State == CoordinatorState.Waiting)
            {
                activeAtAcceptedPublication = coordinator.IsActive;
            }
        };

        BenchmarkCaptureStartHandle handle = coordinator.TryStartCapture(CreateSampleRequest());
        Assert.IsTrue(handle.Start());
        BenchmarkCaptureOutcome outcome = await handle.CompletionTask!;
        await failedNotification.Task.WaitAsync(TimeSpan.FromSeconds(1));
        CoordinatorState[] published = states.ToArray();

        Assert.IsTrue(handle.Accepted);
        Assert.IsTrue(activeAtAcceptedPublication, "Capture ownership must be authoritative at accepted-state publication.");
        Assert.AreEqual(CoordinatorStatus.Failed, outcome.Status);
        Assert.AreEqual(CoordinatorState.Waiting, published.First());
        Assert.AreEqual(CoordinatorState.Failed, published.Last());
        Assert.IsFalse(published.SkipWhile(state => state != CoordinatorState.Failed).Skip(1).Contains(CoordinatorState.Waiting));
    }

    [TestMethod]
    public async Task ImmediateCompletion_PublishesWaitingBeforeTerminal()
    {
        var storage = new BenchmarkStorageService(_tempDir);
        var states = new ConcurrentQueue<CoordinatorState>();
        var completedNotification = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new BenchmarkCaptureCoordinator(
            storage,
            () => new TestFakeBackend(storage, BackendMode.Success),
            new TestFakeIdentityProvider());
        coordinator.StateChanged += (_, snapshot) =>
        {
            states.Enqueue(snapshot.State);
            if (snapshot.State == CoordinatorState.Completed) completedNotification.TrySetResult();
        };

        BenchmarkCaptureStartHandle handle = coordinator.TryStartCapture(CreateSampleRequest());
        Assert.IsTrue(handle.Start());
        BenchmarkCaptureOutcome outcome = await handle.CompletionTask!;
        await completedNotification.Task.WaitAsync(TimeSpan.FromSeconds(1));
        CoordinatorState[] published = states.ToArray();

        Assert.IsTrue(handle.Accepted);
        Assert.AreEqual(CoordinatorStatus.Completed, outcome.Status);
        Assert.AreEqual(CoordinatorState.Waiting, published.First());
        Assert.AreEqual(CoordinatorState.Completed, published.Last());
        int terminalIndex = Array.IndexOf(published, CoordinatorState.Completed);
        Assert.IsTrue(terminalIndex > 0);
        Assert.IsFalse(published.Skip(terminalIndex + 1).Contains(CoordinatorState.Waiting));
    }

    [TestMethod]
    public async Task ExternalMutationLease_RejectsBenchmarkUntilLeaseReleased()
    {
        var storage = new BenchmarkStorageService(_tempDir);
        int backendCalls = 0;
        var coordinator = new BenchmarkCaptureCoordinator(
            storage,
            () =>
            {
                Interlocked.Increment(ref backendCalls);
                return new TestFakeBackend(storage, BackendMode.Success);
            },
            new TestFakeIdentityProvider());

        Assert.IsTrue(coordinator.TryAcquireExternalMutation(out IDisposable? lease));
        BenchmarkCaptureStartHandle blocked = coordinator.TryStartCapture(CreateSampleRequest());
        Assert.IsFalse(blocked.Accepted);
        Assert.AreEqual("operation_in_progress", blocked.ErrorCode);
        Assert.AreEqual(0, backendCalls);

        lease!.Dispose();
        BenchmarkCaptureStartHandle accepted = coordinator.TryStartCapture(CreateSampleRequest());
        Assert.IsTrue(accepted.Accepted);
        Assert.IsTrue(accepted.Start());
        Assert.AreEqual(CoordinatorStatus.Completed, (await accepted.CompletionTask!).Status);
        Assert.AreEqual(1, backendCalls);
    }

    [TestMethod]
    public async Task AcceptedReservation_RejectsExternalMutationUntilBenchmarkEnds()
    {
        var storage = new BenchmarkStorageService(_tempDir);
        var coordinator = new BenchmarkCaptureCoordinator(
            storage,
            () => new TestFakeBackend(storage, BackendMode.Success),
            new TestFakeIdentityProvider());

        BenchmarkCaptureStartHandle handle = coordinator.TryStartCapture(CreateSampleRequest());
        Assert.IsTrue(handle.Accepted);
        Assert.IsFalse(coordinator.TryAcquireExternalMutation(out IDisposable? blockedLease));
        Assert.IsNull(blockedLease);

        Assert.IsTrue(handle.Start());
        Assert.AreEqual(CoordinatorStatus.Completed, (await handle.CompletionTask!).Status);
        Assert.IsTrue(coordinator.TryAcquireExternalMutation(out IDisposable? leaseAfter));
        leaseAfter!.Dispose();
    }

    [TestMethod]
    public async Task AcceptanceCannotLoseAuthoritativeReservationBeforePublicHandoff()
    {
        var storage = new BenchmarkStorageService(_tempDir);
        int backendCalls = 0;
        var coordinator = new BenchmarkCaptureCoordinator(
            storage,
            () =>
            {
                Interlocked.Increment(ref backendCalls);
                return new TestFakeBackend(storage, BackendMode.Success);
            },
            new TestFakeIdentityProvider());

        BenchmarkCaptureStartHandle handle = coordinator.TryStartCapture(CreateSampleRequest());

        Assert.IsTrue(handle.Accepted);
        Assert.IsTrue(coordinator.IsActive);
        Assert.IsFalse(handle.CompletionTask!.IsCompleted);
        Assert.AreEqual(0, backendCalls, "The worker cannot start before the accepted reservation is handed to the caller.");
        Assert.AreEqual("already_running", coordinator.TryStartCapture(CreateSampleRequest()).ErrorCode);

        Assert.IsTrue(handle.Start());
        Assert.IsFalse(handle.Start(), "An accepted reservation can start its worker only once.");
        Assert.AreEqual(CoordinatorStatus.Completed, (await handle.CompletionTask).Status);
        Assert.AreEqual(1, backendCalls);
    }

    [TestMethod]
    public async Task WaitingSubscriberCallingStopSynchronously_DoesNotDeadlock()
    {
        var storage = new BenchmarkStorageService(_tempDir);
        var states = new ConcurrentQueue<CoordinatorState>();
        var cancelledNotification = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new BenchmarkCaptureCoordinator(
            storage,
            () => new TestFakeBackend(storage, BackendMode.Success),
            new TestFakeIdentityProvider());
        bool synchronousStopReturned = false;
        coordinator.StateChanged += (_, snapshot) =>
        {
            states.Enqueue(snapshot.State);
            if (snapshot.State == CoordinatorState.Cancelled) cancelledNotification.TrySetResult();
            if (snapshot.State == CoordinatorState.Waiting)
            {
                coordinator.StopAsync().GetAwaiter().GetResult();
                synchronousStopReturned = true;
            }
        };

        BenchmarkCaptureStartHandle handle = await Task.Run(
            () => coordinator.TryStartCapture(CreateSampleRequest())).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsTrue(synchronousStopReturned);
        Assert.IsFalse(handle.Accepted, "A reservation cancelled during Waiting delivery must not be returned as accepted.");
        await cancelledNotification.Task.WaitAsync(TimeSpan.FromSeconds(1));
        CollectionAssert.AreEqual(
            new[] { CoordinatorState.Waiting, CoordinatorState.Stopping, CoordinatorState.Cancelled },
            states.ToArray());
        Assert.IsFalse(coordinator.IsActive);
    }

    [TestMethod]
    public async Task StopStateNotification_DoesNotExecuteUnderCoordinatorMutationLock()
    {
        var storage = new BenchmarkStorageService(_tempDir);
        var backend = new TestFakeBackend(storage, BackendMode.WaitForCancellation);
        var coordinator = new BenchmarkCaptureCoordinator(storage, () => backend, new TestFakeIdentityProvider());
        bool lockProbeCompleted = false;
        var stoppingNotification = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.StateChanged += (_, snapshot) =>
        {
            if (snapshot.State != CoordinatorState.Stopping) return;
            Task<BenchmarkCaptureStateSnapshot> probe = Task.Run(() => coordinator.CurrentState);
            lockProbeCompleted = probe.Wait(TimeSpan.FromSeconds(1));
            stoppingNotification.TrySetResult();
        };

        BenchmarkCaptureStartHandle handle = coordinator.TryStartCapture(CreateSampleRequest());
        Assert.IsTrue(handle.Start());
        await backend.WaitUntilCaptureStartedAsync();

        await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await stoppingNotification.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.IsTrue(lockProbeCompleted, "Stopping observers must execute after the coordinator mutation lock is released.");
        Assert.AreEqual(CoordinatorStatus.Cancelled, (await handle.CompletionTask!).Status);
    }

    [TestMethod]
    public async Task ThrowingWaitingSubscriber_DoesNotBlockWorkerOrLaterSubscribers()
    {
        var storage = new BenchmarkStorageService(_tempDir);
        var coordinator = new BenchmarkCaptureCoordinator(
            storage,
            () => new TestFakeBackend(storage, BackendMode.Success),
            new TestFakeIdentityProvider());
        int laterWaitingNotifications = 0;
        coordinator.StateChanged += (_, snapshot) =>
        {
            if (snapshot.State == CoordinatorState.Waiting) throw new InvalidOperationException("subscriber failure");
        };
        coordinator.StateChanged += (_, snapshot) =>
        {
            if (snapshot.State == CoordinatorState.Waiting) Interlocked.Increment(ref laterWaitingNotifications);
        };

        BenchmarkCaptureStartHandle handle = coordinator.TryStartCapture(CreateSampleRequest());
        Assert.IsTrue(handle.Accepted);
        Assert.AreEqual(1, laterWaitingNotifications);
        Assert.IsTrue(handle.Start());

        Assert.AreEqual(CoordinatorStatus.Completed, (await handle.CompletionTask!).Status);
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

    private sealed class NeverReleasingPreemption : ILivePresentMonPreemption
    {
        public async Task<bool> RequestPresentMonReleaseAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return false;
        }

        public void ReleasePresentMonPreemption() { }
    }

    private sealed class BlockingFailedPreemption : ILivePresentMonPreemption
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> RequestPresentMonReleaseAsync(CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            return Release.Task.WaitAsync(cancellationToken);
        }

        public void ReleasePresentMonPreemption() { }
    }

    private sealed class ImmediateFailedPreemption : ILivePresentMonPreemption
    {
        public Task<bool> RequestPresentMonReleaseAsync(CancellationToken cancellationToken) => Task.FromResult(false);
        public void ReleasePresentMonPreemption() { }
    }
}
