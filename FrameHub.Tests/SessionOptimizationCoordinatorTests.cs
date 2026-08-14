using FrameHub.App.Services;
using FrameHub.Core.Logging;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Models.SessionOptimization;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Benchmarking;
using FrameHub.Core.Services.Library;
using FrameHub.Core.Services.SessionOptimization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public sealed class SessionOptimizationCoordinatorTests
{
    private string _tempDir = null!;
    private string _stateFilePath = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FrameHub_CoordinatorTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _stateFilePath = Path.Combine(_tempDir, "active_session.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    [TestMethod]
    public async Task StartSessionAsync_NoGameItem_ReturnsNoGame()
    {
        using var coordinator = CreateCoordinator();
        var result = await coordinator.StartSessionAsync("Manual", null);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("no_game", result.ErrorCode);
    }

    [TestMethod]
    public async Task StartSessionAsync_WhenAlreadyActive_ReturnsAlreadyActive()
    {
        using var coordinator = CreateCoordinator();
        var game = new LibraryItem { Id = "g1", DisplayName = "Game 1", ProcessName = "game1.exe" };

        // Fake active session
        var field = typeof(SessionOptimizationCoordinator).GetField("_activeSession", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field!.SetValue(coordinator, new ActiveSessionState { IsActive = true, GameId = "g1", GameName = "Game 1" });

        var result = await coordinator.StartSessionAsync("Manual", game);
        Assert.IsFalse(result.Success);
        Assert.AreEqual("already_active", result.ErrorCode);
    }

    [TestMethod]
    public async Task StopSessionAsync_WhenNotActive_ReturnsNotActive()
    {
        using var coordinator = CreateCoordinator();
        var result = await coordinator.StopSessionAsync();

        Assert.IsFalse(result.Success);
        Assert.AreEqual("not_active", result.ErrorCode);
    }

    [TestMethod]
    public async Task ConcurrencyGate_RejectsConcurrentOperations()
    {
        using var coordinator = CreateCoordinator();
        var gateField = typeof(SessionOptimizationCoordinator).GetField("_mutationGate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var gate = (SemaphoreSlim)gateField!.GetValue(coordinator)!;

        // Acquire lock to simulate operation in progress
        await gate.WaitAsync();

        try
        {
            var game = new LibraryItem { Id = "g1", DisplayName = "Game 1" };
            var startResult = await coordinator.StartSessionAsync("Manual", game);
            Assert.IsFalse(startResult.Success);
            Assert.AreEqual("operation_in_progress", startResult.ErrorCode);

            var stopResult = await coordinator.StopSessionAsync();
            Assert.IsFalse(stopResult.Success);
            Assert.AreEqual("operation_in_progress", stopResult.ErrorCode);
        }
        finally
        {
            gate.Release();
        }
    }

    [TestMethod]
    public void LoadAndSaveSettings_DelegatesToSettingsService()
    {
        using var coordinator = CreateCoordinator();
        var settings = coordinator.LoadSettings();
        Assert.IsNotNull(settings);

        settings.AutoModeEnabled = !settings.AutoModeEnabled;
        coordinator.SaveSettings(settings);

        var reloaded = coordinator.LoadSettings();
        Assert.AreEqual(settings.AutoModeEnabled, reloaded.AutoModeEnabled);
    }

    [TestMethod]
    public async Task StartSessionAsync_InitialJournalFailure_PerformsZeroOsMutation()
    {
        var suspendService = new TestProcessSuspendService();
        var taskbarService = new TestTaskbarVisibilityService();
        var stateService = new TestSessionStateService(_stateFilePath) { FailSaveCalls = [1] };
        using var coordinator = CreateCoordinator(stateService: stateService, suspendService: suspendService, taskbarService: taskbarService);
        coordinator.SaveSettings(new SessionOptimizationSettings { HideTaskbarDuringSession = true });

        SessionOptimizationStartResult result = await coordinator.StartSessionAsync("Manual", CreateGame());

        Assert.IsFalse(result.Success);
        Assert.AreEqual("state_persist_failed", result.ErrorCode);
        Assert.AreEqual(0, suspendService.SuspendCalls);
        Assert.AreEqual(0, suspendService.ResumeCalls);
        Assert.AreEqual(0, taskbarService.HideCalls);
        Assert.AreEqual(0, taskbarService.ShowCalls);
        Assert.IsNull(coordinator.ActiveSession);
    }

    [TestMethod]
    public async Task StartSessionAsync_MutationOccursOnlyAfterPreparedJournalIsDurable()
    {
        var stateService = new TestSessionStateService(_stateFilePath);
        var suspendService = new TestProcessSuspendService
        {
            BeforeSuspend = () =>
            {
                ActiveSessionState? durable = stateService.Load();
                Assert.IsNotNull(durable);
                Assert.AreEqual(SessionRecoveryPhase.Applying, durable.RecoveryPhase);
                Assert.IsNotNull(durable.PendingSuspension);
            }
        };
        using var coordinator = CreateCoordinator(stateService: stateService, suspendService: suspendService);

        SessionOptimizationStartResult result = await coordinator.StartSessionAsync("Manual", CreateGame());

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, suspendService.SuspendCalls);
    }

    [TestMethod]
    public async Task StartSessionAsync_PostMutationPersistFailure_PartialRollbackRemainsDurable()
    {
        var stateService = new TestSessionStateService(_stateFilePath) { FailSaveCalls = [3] };
        var suspendService = new TestProcessSuspendService { ResumeResult = new SessionActionResult { FailedCount = 1 } };
        using var coordinator = CreateCoordinator(stateService: stateService, suspendService: suspendService);

        SessionOptimizationStartResult result = await coordinator.StartSessionAsync("Manual", CreateGame());

        Assert.IsFalse(result.Success);
        Assert.AreEqual("state_persist_failed", result.ErrorCode);
        Assert.AreEqual(1, suspendService.SuspendCalls);
        Assert.AreEqual(1, suspendService.ResumeCalls);
        Assert.IsNotNull(coordinator.ActiveSession);
        Assert.IsTrue(coordinator.ActiveSession.IsRecoveryPending);
        Assert.AreEqual(0, coordinator.ActiveSession.SuspendedProcesses.Count);
        Assert.AreEqual(1, coordinator.ActiveSession.AmbiguousProcesses.Count);
        Assert.AreEqual(4242, coordinator.ActiveSession.AmbiguousProcesses[0].ProcessId);

        ActiveSessionState? reloaded = new SessionStateService(_stateFilePath).Load();
        Assert.IsNotNull(reloaded);
        Assert.AreEqual(SessionRecoveryPhase.RollingBack, reloaded.RecoveryPhase);
        Assert.AreEqual(0, reloaded.SuspendedProcesses.Count);
        Assert.AreEqual(1, reloaded.AmbiguousProcesses.Count);
    }

    [TestMethod]
    public async Task StopSessionAsync_ClearFailure_KeepsCleanupRetryStateAndDoesNotReturnSuccess()
    {
        var stateService = new TestSessionStateService(_stateFilePath) { ClearFailuresRemaining = 1 };
        Assert.IsTrue(stateService.Save(CreateActiveSession()));
        var suspendService = new TestProcessSuspendService();
        using var coordinator = CreateCoordinator(stateService: stateService, suspendService: suspendService, taskbarService: new TestTaskbarVisibilityService());

        SessionOptimizationRestoreResult result = await coordinator.StopSessionAsync();

        Assert.IsFalse(result.Success);
        Assert.AreEqual("state_clear_failed", result.ErrorCode);
        Assert.IsTrue(File.Exists(_stateFilePath));
        Assert.IsNotNull(coordinator.ActiveSession);
        Assert.IsTrue(coordinator.ActiveSession.IsRecoveryPending);
        Assert.AreEqual(0, coordinator.ActiveSession.SuspendedProcesses.Count);
        Assert.IsFalse(coordinator.ActiveSession.TaskbarHidden);

        SessionOptimizationRestoreResult retry = await coordinator.StopSessionAsync();
        Assert.IsTrue(retry.Success);
        Assert.IsNull(coordinator.ActiveSession);
        Assert.IsFalse(File.Exists(_stateFilePath));
        Assert.IsFalse(File.Exists(_stateFilePath + ".bak"));
    }

    [TestMethod]
    public async Task PreparedJournalReload_DoesNotResumeUnmutatedPlannedProcess()
    {
        var stateService = new SessionStateService(_stateFilePath);
        Assert.IsTrue(stateService.Save(new ActiveSessionState
        {
            IsActive = true,
            RecoveryPhase = SessionRecoveryPhase.Prepared,
            IsRecoveryPending = true,
            PlannedProcesses = [CreateProcessRecord()]
        }));
        var suspendService = new TestProcessSuspendService();
        using var coordinator = CreateCoordinator(suspendService: suspendService);

        SessionOptimizationRestoreResult result = await coordinator.StopSessionAsync();

        Assert.IsTrue(result.Success);
        Assert.AreEqual(0, suspendService.ResumeCalls);
        Assert.IsFalse(File.Exists(_stateFilePath));
        Assert.IsFalse(File.Exists(_stateFilePath + ".bak"));
    }

    [TestMethod]
    public async Task ConfirmedSuspensionReload_IsConservativeAndNeverRepeatsResume()
    {
        SuspendedProcessRecord record = CreateProcessRecord();
        Assert.IsTrue(new SessionStateService(_stateFilePath).Save(new ActiveSessionState
        {
            IsActive = true,
            RecoveryPhase = SessionRecoveryPhase.Active,
            SuspendedProcesses = [record]
        }));
        var suspendService = new TestProcessSuspendService
        {
            ResolveWithoutResumeResult = new SessionActionResult { FailedCount = 1 }
        };
        using var coordinator = CreateCoordinator(suspendService: suspendService);

        Assert.AreEqual(record.ProcessStartTimeUtc, coordinator.ActiveSession?.SuspendedProcesses.Single().ProcessStartTimeUtc);
        SessionOptimizationRestoreResult result = await coordinator.StopSessionAsync();

        Assert.IsFalse(result.Success);
        Assert.AreEqual("restore_manual_required", result.ErrorCode);
        Assert.AreEqual(0, suspendService.ResumeCalls);
        Assert.AreEqual(1, suspendService.ResolveWithoutResumeCalls);
        ActiveSessionState? durable = new SessionStateService(_stateFilePath).Load();
        Assert.IsNotNull(durable);
        Assert.AreEqual(record.ProcessStartTimeUtc, durable.AmbiguousProcesses.Single().ProcessStartTimeUtc);
    }

    [TestMethod]
    public async Task PendingSuspensionReload_RemainsAmbiguousAndNeverResumesAnyEntry()
    {
        SuspendedProcessRecord pending = CreateProcessRecord();
        var untouched = new SuspendedProcessRecord
        {
            ProcessId = 5151,
            ProcessName = "untouched",
            ProcessStartTimeUtc = pending.ProcessStartTimeUtc.AddMinutes(1)
        };
        Assert.IsTrue(new SessionStateService(_stateFilePath).Save(new ActiveSessionState
        {
            IsActive = true,
            RecoveryPhase = SessionRecoveryPhase.Applying,
            IsRecoveryPending = true,
            PendingSuspension = pending,
            PlannedProcesses = [untouched]
        }));
        var suspendService = new TestProcessSuspendService
        {
            ResolveWithoutResumeResult = new SessionActionResult { FailedCount = 1 }
        };
        using var coordinator = CreateCoordinator(suspendService: suspendService);

        SessionOptimizationRestoreResult result = await coordinator.StopSessionAsync();

        Assert.IsFalse(result.Success);
        Assert.AreEqual("restore_manual_required", result.ErrorCode);
        Assert.AreEqual(0, suspendService.ResumeCalls);
        Assert.AreEqual(1, suspendService.ResolveWithoutResumeCalls);
        ActiveSessionState? durable = new SessionStateService(_stateFilePath).Load();
        Assert.IsNotNull(durable);
        CollectionAssert.AreEqual(new[] { 4242 }, durable.AmbiguousProcesses.Select(item => item.ProcessId).ToArray());
        Assert.IsFalse(durable.AmbiguousProcesses.Any(item => item.ProcessId == 5151));
    }

    [TestMethod]
    public async Task TaskbarPendingCrash_OriginalVisible_RestoresVisible()
    {
        Assert.IsTrue(new SessionStateService(_stateFilePath).Save(new ActiveSessionState
        {
            IsActive = true,
            RecoveryPhase = SessionRecoveryPhase.Applying,
            IsRecoveryPending = true,
            TaskbarMutationPending = true,
            OriginalTaskbarVisibility = VisibleTaskbarState()
        }));
        var taskbarService = new TestTaskbarVisibilityService();
        using var coordinator = CreateCoordinator(taskbarService: taskbarService);

        SessionOptimizationRestoreResult result = await coordinator.StopSessionAsync();

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, taskbarService.RestoreCalls);
        Assert.IsTrue(taskbarService.LastRestoreState?.PrimaryTaskbarVisible);
        Assert.AreEqual(0, taskbarService.ShowCalls);
        Assert.IsFalse(File.Exists(_stateFilePath));
    }

    [TestMethod]
    public async Task TaskbarPendingCrash_AfterHide_RestoresOriginalVisibleState()
    {
        Assert.IsTrue(new SessionStateService(_stateFilePath).Save(new ActiveSessionState
        {
            IsActive = true,
            RecoveryPhase = SessionRecoveryPhase.Applying,
            IsRecoveryPending = true,
            TaskbarMutationPending = true,
            TaskbarHidden = true,
            OriginalTaskbarVisibility = VisibleTaskbarState()
        }));
        var taskbarService = new TestTaskbarVisibilityService();
        using var coordinator = CreateCoordinator(taskbarService: taskbarService);

        SessionOptimizationRestoreResult result = await coordinator.StopSessionAsync();

        Assert.IsTrue(result.Success);
        Assert.IsTrue(taskbarService.LastRestoreState?.PrimaryTaskbarVisible);
        Assert.AreEqual(0, taskbarService.ShowCalls);
    }

    [TestMethod]
    public async Task TaskbarPendingCrash_OriginalHidden_RemainsHidden()
    {
        Assert.IsTrue(new SessionStateService(_stateFilePath).Save(new ActiveSessionState
        {
            IsActive = true,
            RecoveryPhase = SessionRecoveryPhase.Applying,
            IsRecoveryPending = true,
            TaskbarMutationPending = true,
            OriginalTaskbarVisibility = HiddenTaskbarState()
        }));
        var taskbarService = new TestTaskbarVisibilityService();
        using var coordinator = CreateCoordinator(taskbarService: taskbarService);

        SessionOptimizationRestoreResult result = await coordinator.StopSessionAsync();

        Assert.IsTrue(result.Success);
        Assert.IsFalse(taskbarService.LastRestoreState?.PrimaryTaskbarVisible);
        Assert.AreEqual(0, taskbarService.ShowCalls);
    }

    [TestMethod]
    public async Task TaskbarNormalMutation_OriginalHidden_IsRestoredHidden()
    {
        var taskbarService = new TestTaskbarVisibilityService { CaptureResult = HiddenTaskbarState() };
        using var coordinator = CreateCoordinator(taskbarService: taskbarService);
        coordinator.SaveSettings(new SessionOptimizationSettings { HideTaskbarDuringSession = true });

        Assert.IsTrue((await coordinator.StartSessionAsync("Manual", CreateGame())).Success);
        SessionOptimizationRestoreResult result = await coordinator.StopSessionAsync();

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, taskbarService.HideCalls);
        Assert.AreEqual(1, taskbarService.RestoreCalls);
        Assert.IsFalse(taskbarService.LastRestoreState?.PrimaryTaskbarVisible);
        Assert.AreEqual(0, taskbarService.ShowCalls);
    }

    [TestMethod]
    public async Task PartialRestore_ReturnsFailureAndKeepsJournal()
    {
        Assert.IsTrue(new SessionStateService(_stateFilePath).Save(CreateActiveSession()));
        var suspendService = new TestProcessSuspendService
        {
            ResolveWithoutResumeResult = new SessionActionResult { FailedCount = 1 }
        };
        using var coordinator = CreateCoordinator(suspendService: suspendService);

        SessionOptimizationRestoreResult result = await coordinator.StopSessionAsync();

        Assert.IsFalse(result.Success);
        Assert.AreEqual("restore_manual_required", result.ErrorCode);
        Assert.AreEqual(1, result.RemainingCount);
        Assert.IsTrue(File.Exists(_stateFilePath));
        Assert.IsTrue(new SessionStateService(_stateFilePath).Load()?.IsRecoveryPending);
    }

    [TestMethod]
    public async Task RestoreProgressPersistFailure_RetainsOlderConservativeJournal()
    {
        var stateService = new TestSessionStateService(_stateFilePath) { FailSaveCalls = [3] };
        Assert.IsTrue(stateService.Save(CreateActiveSession()));
        using var coordinator = CreateCoordinator(
            stateService: stateService,
            suspendService: new TestProcessSuspendService
            {
                ResolveWithoutResumeResult = new SessionActionResult { FailedCount = 1 }
            });

        SessionOptimizationRestoreResult result = await coordinator.StopSessionAsync();

        Assert.IsFalse(result.Success);
        Assert.AreEqual("state_persist_failed", result.ErrorCode);
        Assert.IsTrue(File.Exists(_stateFilePath));
        ActiveSessionState? durable = new SessionStateService(_stateFilePath).Load();
        Assert.IsNotNull(durable);
        Assert.AreEqual(1, durable.AmbiguousProcesses.Count, "The last durable record must remain conservative when progress cannot be saved.");
    }

    [TestMethod]
    public async Task TaskbarRestoreFailure_ReturnsPartialAndKeepsTaskbarRecoveryDurable()
    {
        Assert.IsTrue(new SessionStateService(_stateFilePath).Save(new ActiveSessionState
        {
            IsActive = true,
            RecoveryPhase = SessionRecoveryPhase.Active,
            TaskbarHidden = true,
            OriginalTaskbarVisibility = VisibleTaskbarState()
        }));
        using var coordinator = CreateCoordinator(taskbarService: new TestTaskbarVisibilityService { RestoreResult = false });

        SessionOptimizationRestoreResult result = await coordinator.StopSessionAsync();

        Assert.IsFalse(result.Success);
        Assert.AreEqual("restore_partial", result.ErrorCode);
        Assert.IsFalse(result.TaskbarRestored);
        ActiveSessionState? durable = new SessionStateService(_stateFilePath).Load();
        Assert.IsNotNull(durable);
        Assert.IsTrue(durable.TaskbarHidden);
        Assert.IsTrue(durable.IsRecoveryPending);
    }

    [TestMethod]
    public async Task ResumeSucceededButProgressSaveFailed_RestartNeverResumesAgain()
    {
        var stateService = new TestSessionStateService(_stateFilePath) { FailSaveCalls = [7] };
        var firstSuspendService = new TestProcessSuspendService();
        using (var firstCoordinator = CreateCoordinator(stateService: stateService, suspendService: firstSuspendService))
        {
            Assert.IsTrue((await firstCoordinator.StartSessionAsync("Manual", CreateGame())).Success);

            SessionOptimizationRestoreResult interrupted = await firstCoordinator.StopSessionAsync();

            Assert.IsFalse(interrupted.Success);
            Assert.AreEqual("state_persist_failed", interrupted.ErrorCode);
            Assert.AreEqual(1, firstSuspendService.ResumeCalls);
            ActiveSessionState? durableAfterResume = new SessionStateService(_stateFilePath).Load();
            Assert.IsNotNull(durableAfterResume?.PendingResume);
            Assert.AreEqual(0, durableAfterResume.SuspendedProcesses.Count);
        }

        var restartedSuspendService = new TestProcessSuspendService
        {
            ResolveWithoutResumeResult = new SessionActionResult { FailedCount = 1 }
        };
        using var restartedCoordinator = CreateCoordinator(suspendService: restartedSuspendService);
        SessionOptimizationRestoreResult restarted = await restartedCoordinator.StopSessionAsync();

        Assert.IsFalse(restarted.Success);
        Assert.AreEqual("restore_manual_required", restarted.ErrorCode);
        Assert.AreEqual(0, restartedSuspendService.ResumeCalls);
        Assert.AreEqual(1, restartedSuspendService.ResolveWithoutResumeCalls);
        Assert.AreEqual(1, restartedCoordinator.ActiveSession?.AmbiguousProcesses.Count);
    }

    [TestMethod]
    public async Task BenchmarkActive_ManualAndAutomaticStartAreRejectedBeforeOsMutation()
    {
        var arbiter = new TestBenchmarkArbiter { IsBenchmarkActive = true };
        var suspendService = new TestProcessSuspendService();
        var taskbarService = new TestTaskbarVisibilityService();
        using var coordinator = CreateCoordinator(
            suspendService: suspendService,
            taskbarService: taskbarService,
            benchmarkArbiter: arbiter);

        SessionOptimizationStartResult manual = await coordinator.StartSessionAsync("Manual", CreateGame());
        SessionOptimizationStartResult automatic = await coordinator.StartSessionAsync("Automatic", CreateGame());

        Assert.AreEqual("benchmark_active", manual.ErrorCode);
        Assert.AreEqual("benchmark_active", automatic.ErrorCode);
        Assert.AreEqual(0, suspendService.CaptureCalls);
        Assert.AreEqual(0, suspendService.SuspendCalls);
        Assert.AreEqual(0, taskbarService.HideCalls);
    }

    [TestMethod]
    public async Task BenchmarkActive_RestoreIsRejectedBeforeOsMutation_ThenWorksAfterTerminal()
    {
        Assert.IsTrue(new SessionStateService(_stateFilePath).Save(CreateActiveSession()));
        var arbiter = new TestBenchmarkArbiter { IsBenchmarkActive = true };
        var suspendService = new TestProcessSuspendService
        {
            ResolveWithoutResumeResult = new SessionActionResult { FailedCount = 1 }
        };
        var taskbarService = new TestTaskbarVisibilityService();
        using var coordinator = CreateCoordinator(
            suspendService: suspendService,
            taskbarService: taskbarService,
            benchmarkArbiter: arbiter);

        SessionOptimizationRestoreResult blocked = await coordinator.StopSessionAsync();
        Assert.AreEqual("benchmark_active", blocked.ErrorCode);
        Assert.AreEqual(0, suspendService.ResumeCalls);
        Assert.AreEqual(0, suspendService.ResolveWithoutResumeCalls);
        Assert.AreEqual(0, taskbarService.RestoreCalls);

        arbiter.IsBenchmarkActive = false;
        SessionOptimizationRestoreResult afterTerminal = await coordinator.StopSessionAsync();
        Assert.AreEqual("restore_manual_required", afterTerminal.ErrorCode);
        Assert.AreEqual(0, suspendService.ResumeCalls);
        Assert.AreEqual(1, suspendService.ResolveWithoutResumeCalls);
    }

    [TestMethod]
    public async Task ShutdownAsync_WaitsForMutationOwner_RejectsNewWorkAndDisposesAfterCompletion()
    {
        var suspendService = new TestProcessSuspendService { BlockCapture = true };
        var coordinator = CreateCoordinator(_stateFilePath, suspendService, new TestTaskbarVisibilityService());
        Task<SessionOptimizationStartResult> owner = coordinator.StartSessionAsync("Manual", CreateGame());
        await suspendService.CaptureStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task<bool> shutdown = coordinator.ShutdownAsync(TimeSpan.FromSeconds(5));
        SessionOptimizationStartResult rejected = await coordinator.StartSessionAsync("Manual", CreateGame());

        Assert.IsFalse(rejected.Success);
        Assert.AreEqual("coordinator_stopping", rejected.ErrorCode);
        Assert.IsFalse(shutdown.IsCompleted, "Shutdown must wait for the mutation owner before disposing gates.");

        suspendService.ReleaseCapture.TrySetResult();
        SessionOptimizationStartResult ownerResult = await owner;
        Assert.AreEqual("coordinator_stopping", ownerResult.ErrorCode);
        Assert.IsTrue(await shutdown);
        AssertSynchronizationGatesDisposed(coordinator);
    }

    [TestMethod]
    public async Task ShutdownAsync_WaitsForScanOwnerBeforeDisposingGate()
    {
        var suspendService = new TestProcessSuspendService { BlockCapture = true };
        var coordinator = CreateCoordinator(_stateFilePath, suspendService, new TestTaskbarVisibilityService());
        Task<SessionProcessSnapshot> owner = coordinator.CaptureProcessSnapshotAsync();
        await suspendService.CaptureStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task<bool> shutdown = coordinator.ShutdownAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(shutdown.IsCompleted);

        suspendService.ReleaseCapture.TrySetResult();
        await owner;
        Assert.IsTrue(await shutdown);
        AssertSynchronizationGatesDisposed(coordinator);
    }

    private SessionOptimizationCoordinator CreateCoordinator(
        string? statePath = null,
        ProcessSuspendService? suspendService = null,
        TaskbarVisibilityService? taskbarService = null,
        SessionStateService? stateService = null,
        IBenchmarkOperationArbiter? benchmarkArbiter = null)
    {
        return new SessionOptimizationCoordinator(
            stateService: stateService ?? new SessionStateService(statePath ?? _stateFilePath),
            settingsService: new SessionOptimizationSettingsService(Path.Combine(_tempDir, "session_optimization.json")),
            suspendService: suspendService,
            taskbarService: taskbarService,
            libraryService: new LibraryService(Path.Combine(_tempDir, "library.json")),
            benchmarkArbiter: benchmarkArbiter);
    }

    private static LibraryItem CreateGame() => new()
    {
        Id = "game-1",
        DisplayName = "Game One",
        ProcessName = "game.exe",
        Type = LibraryItemType.Game,
        IsEnabled = true
    };

    private static ActiveSessionState CreateActiveSession() => new()
    {
        IsActive = true,
        GameId = "game-1",
        GameName = "Game One",
        SuspendedProcesses =
        [
            new SuspendedProcessRecord
            {
                ProcessId = 4242,
                ProcessName = "background",
                ProcessStartTimeUtc = DateTime.UtcNow
            }
        ]
    };

    private static SuspendedProcessRecord CreateProcessRecord() => new()
    {
        ProcessId = 4242,
        ProcessName = "background",
        ProcessStartTimeUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
        ExecutablePath = "C:\\Tools\\background.exe",
        RuleId = "test",
        RuleName = "Test"
    };

    private static TaskbarVisibilityState VisibleTaskbarState() => new()
    {
        PrimaryTaskbarFound = true,
        PrimaryTaskbarVisible = true,
        SecondaryTaskbarsVisible = [true]
    };

    private static TaskbarVisibilityState HiddenTaskbarState() => new()
    {
        PrimaryTaskbarFound = true,
        PrimaryTaskbarVisible = false,
        SecondaryTaskbarsVisible = [false]
    };

    private static void AssertSynchronizationGatesDisposed(SessionOptimizationCoordinator coordinator)
    {
        foreach (string fieldName in new[] { "_mutationGate", "_scanGate" })
        {
            var field = typeof(SessionOptimizationCoordinator).GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var gate = (SemaphoreSlim)field!.GetValue(coordinator)!;
            Assert.ThrowsException<ObjectDisposedException>(() => gate.Wait(0));
        }
    }

    private sealed class TestProcessSuspendService : ProcessSuspendService
    {
        private readonly SuspendedProcessRecord _record = new()
        {
            ProcessId = 4242,
            ProcessName = "background",
            ProcessStartTimeUtc = DateTime.UtcNow
        };

        public bool BlockCapture { get; init; }
        public TaskCompletionSource CaptureStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseCapture { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SessionActionResult? ResumeResult { get; init; }
        public Action? BeforeSuspend { get; init; }
        public int SuspendCalls { get; private set; }
        public int ResumeCalls { get; private set; }
        public int ResolveWithoutResumeCalls { get; private set; }
        public int CaptureCalls { get; private set; }
        public IReadOnlyList<SuspendedProcessRecord> LastResumeRecords { get; private set; } = [];
        public SessionActionResult? ResolveWithoutResumeResult { get; init; }

        public override async Task<SessionProcessSnapshot> CaptureProcessSnapshotAsync(CancellationToken cancellationToken)
        {
            CaptureCalls++;
            CaptureStarted.TrySetResult();
            if (BlockCapture)
            {
                await ReleaseCapture.Task.ConfigureAwait(false);
            }
            return new SessionProcessSnapshot();
        }

        public override IReadOnlyList<SuspendCandidate> BuildCandidates(
            SessionProcessSnapshot snapshot,
            IEnumerable<BackgroundProcessRule> enabledRules,
            IEnumerable<string> customProcessNames,
            IEnumerable<string> protectedProcessNames) =>
            [new SuspendCandidate { ProcessId = _record.ProcessId, ProcessName = _record.ProcessName, ProcessStartTimeUtc = _record.ProcessStartTimeUtc }];

        public override SessionActionResult SuspendProcesses(IEnumerable<SuspendCandidate> candidates)
        {
            BeforeSuspend?.Invoke();
            SuspendCalls++;
            return new SessionActionResult
            {
                SuccessCount = 1,
                Records = [_record]
            };
        }

        public override SessionActionResult ResumeProcesses(IEnumerable<SuspendedProcessRecord> records)
        {
            ResumeCalls++;
            LastResumeRecords = records.ToList();
            return ResumeResult ?? new SessionActionResult
            {
                SuccessCount = LastResumeRecords.Count,
                Records = LastResumeRecords.ToList()
            };
        }

        public override SessionActionResult ResolveProcessesWithoutResume(IEnumerable<SuspendedProcessRecord> records)
        {
            ResolveWithoutResumeCalls++;
            return ResolveWithoutResumeResult ?? new SessionActionResult
            {
                ResolvedCount = records.Count(),
                Records = records.ToList()
            };
        }
    }

    private sealed class TestTaskbarVisibilityService : TaskbarVisibilityService
    {
        public bool HideResult { get; init; } = true;
        public bool ShowResult { get; init; } = true;
        public bool RestoreResult { get; init; } = true;
        public TaskbarVisibilityState? CaptureResult { get; init; } = VisibleTaskbarState();
        public int ShowCalls { get; private set; }
        public int HideCalls { get; private set; }
        public int CaptureCalls { get; private set; }
        public int RestoreCalls { get; private set; }
        public TaskbarVisibilityState? LastRestoreState { get; private set; }

        public override TaskbarVisibilityState? CaptureVisibilityState()
        {
            CaptureCalls++;
            return CaptureResult;
        }

        public override bool HideTaskbars()
        {
            HideCalls++;
            return HideResult;
        }
        public override bool ShowTaskbars()
        {
            ShowCalls++;
            return ShowResult;
        }

        public override bool RestoreVisibilityState(TaskbarVisibilityState state)
        {
            RestoreCalls++;
            LastRestoreState = state;
            return RestoreResult;
        }
    }

    private sealed class TestBenchmarkArbiter : IBenchmarkOperationArbiter
    {
        public bool IsBenchmarkActive { get; set; }

        public bool TryAcquireExternalMutation(out IDisposable? lease)
        {
            if (IsBenchmarkActive)
            {
                lease = null;
                return false;
            }

            lease = new TestLease();
            return true;
        }

        private sealed class TestLease : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class TestSessionStateService(string filePath) : SessionStateService(filePath)
    {
        public HashSet<int> FailSaveCalls { get; init; } = [];
        public int ClearFailuresRemaining { get; set; }
        public int SaveCalls { get; private set; }

        public override bool Save(ActiveSessionState state)
        {
            SaveCalls++;
            return !FailSaveCalls.Contains(SaveCalls) && base.Save(state);
        }

        public override bool Clear()
        {
            if (ClearFailuresRemaining > 0)
            {
                ClearFailuresRemaining--;
                return false;
            }
            return base.Clear();
        }
    }
}
