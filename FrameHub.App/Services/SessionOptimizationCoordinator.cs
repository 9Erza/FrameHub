using System.Threading;
using FrameHub.Core.Logging;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Models.SessionOptimization;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Benchmarking;
using FrameHub.Core.Services.Library;
using FrameHub.Core.Services.SessionOptimization;

namespace FrameHub.App.Services;

public sealed record SessionOptimizationStartResult
{
    public bool Success { get; init; }
    public string ErrorCode { get; init; } = string.Empty;
    public int SuspendedCount { get; init; }
    public int FailedCount { get; init; }
    public bool TaskbarHidden { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed record SessionOptimizationRestoreResult
{
    public bool Success { get; init; }
    public string ErrorCode { get; init; } = string.Empty;
    public int ResumedCount { get; init; }
    public int FailedCount { get; init; }
    public int RemainingCount { get; init; }
    public bool TaskbarRestored { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Authoritative application-level coordinator for Session Optimization.
/// Manages active session state, process suspension/resumption, taskbar hiding,
/// persistence and concurrency gating across Desktop UI and remote Companion requests.
/// </summary>
public sealed class SessionOptimizationCoordinator : IDisposable, IAsyncDisposable
{
    private const string ExplorerRuleId = "explorer";

    private readonly SessionStateService _stateService;
    private readonly SessionOptimizationSettingsService _settingsService;
    private readonly ProcessSuspendService _suspendService;
    private readonly TaskbarVisibilityService _taskbarService;
    private readonly ProcessScannerService _processScanner;
    private readonly LibraryService _libraryService;
    private readonly IBenchmarkOperationArbiter? _benchmarkArbiter;
    private readonly ILogger _logger;

    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly object _stateLock = new();
    private readonly object _lifecycleLock = new();
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly TaskCompletionSource _operationsDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private ActiveSessionState? _activeSession;
    private int _activeOperations;
    private bool _shutdownStarted;
    private bool _gatesDisposed;
    private bool _loadedStateRequiresConservativeRecovery;

    public ActiveSessionState? ActiveSession
    {
        get
        {
            lock (_stateLock)
            {
                return _activeSession;
            }
        }
        private set
        {
            lock (_stateLock)
            {
                _activeSession = value;
            }
            SessionStateChanged?.Invoke(this, value);
        }
    }

    public bool IsSessionActive => ActiveSession?.IsActive == true;

    public event EventHandler<ActiveSessionState?>? SessionStateChanged;

    public SessionOptimizationCoordinator(
        ProcessScannerService? processScanner = null,
        SessionStateService? stateService = null,
        SessionOptimizationSettingsService? settingsService = null,
        ProcessSuspendService? suspendService = null,
        TaskbarVisibilityService? taskbarService = null,
        LibraryService? libraryService = null,
        ILogger? logger = null,
        IBenchmarkOperationArbiter? benchmarkArbiter = null)
    {
        _processScanner = processScanner ?? new ProcessScannerService(new ProcessService());
        _stateService = stateService ?? new SessionStateService();
        _settingsService = settingsService ?? new SessionOptimizationSettingsService();
        _suspendService = suspendService ?? new ProcessSuspendService();
        _taskbarService = taskbarService ?? new TaskbarVisibilityService();
        _libraryService = libraryService ?? new LibraryService();
        _benchmarkArbiter = benchmarkArbiter;
        _logger = logger ?? LoggerService.Instance;

        _activeSession = _stateService.Load();
        _loadedStateRequiresConservativeRecovery = _activeSession != null;
    }

    public SessionOptimizationSettings LoadSettings()
    {
        return _settingsService.Load();
    }

    public void SaveSettings(SessionOptimizationSettings settings)
    {
        _settingsService.Save(settings);
    }

    public async Task<SessionProcessSnapshot> CaptureProcessSnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (!TryEnterOperation())
        {
            throw new ObjectDisposedException(nameof(SessionOptimizationCoordinator));
        }

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);
            return await CaptureProcessSnapshotCoreAsync(linkedCts.Token).ConfigureAwait(false);
        }
        finally
        {
            ExitOperation();
        }
    }

    private async Task<SessionProcessSnapshot> CaptureProcessSnapshotCoreAsync(CancellationToken cancellationToken)
    {
        await _scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _suspendService.CaptureProcessSnapshotAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _scanGate.Release();
        }
    }

    public IReadOnlyList<RunningProcessGroup> GetRunningProcessGroups(
        SessionProcessSnapshot snapshot,
        LibraryItem? currentGame,
        IEnumerable<LibraryItem>? allGames = null)
    {
        var protectedNames = GetProtectedProcessNames(currentGame, allGames);
        return _suspendService.GetRunningProcessGroups(snapshot, protectedNames);
    }

    public IReadOnlyList<SuspendCandidate> BuildCandidates(
        SessionProcessSnapshot snapshot,
        LibraryItem? game,
        SessionOptimizationSettings settings,
        IEnumerable<LibraryItem>? allGames = null)
    {
        var gameSettings = GetGameSettings(game?.Id, settings);
        var allRules = BackgroundProcessRuleFactory.CreateDefaultRules(settings, gameSettings);
        var enabledRules = allRules.Where(x => x.IsEnabled);
        var ruleCoveredProcessNames = GetRuleCoveredProcessNames(allRules);

        IEnumerable<string> customProcesses = gameSettings.ManualProcessRulesEnabled
            ? gameSettings.CustomProcessEnabledStates
                .Where(x => x.Value)
                .Select(x => x.Key)
                .Where(x => !ruleCoveredProcessNames.Contains(ProcessSuspendService.NormalizeProcessName(x)))
            : Enumerable.Empty<string>();

        var protectedNames = GetProtectedProcessNames(game, allGames);
        return _suspendService.BuildCandidates(snapshot, enabledRules, customProcesses, protectedNames);
    }

    public async Task<SessionOptimizationStartResult> StartSessionAsync(
        string trigger,
        LibraryItem? gameItem,
        CancellationToken cancellationToken = default)
    {
        if (!TryEnterOperation())
        {
            return CoordinatorStoppingStartResult();
        }

        try
        {
            if (gameItem == null)
            {
                return new SessionOptimizationStartResult
                {
                    Success = false,
                    ErrorCode = "no_game",
                    Message = "No game item specified."
                };
            }

            // Non-queuing gate: reject concurrent start/restore immediately
            if (!_mutationGate.Wait(0))
            {
                return new SessionOptimizationStartResult
                {
                    Success = false,
                    ErrorCode = "operation_in_progress",
                    Message = "Another session optimization operation is in progress."
                };
            }

            ActiveSessionState? journal = null;
            int failedCount = 0;
            IDisposable? benchmarkLease = null;
            try
            {
                if (_benchmarkArbiter != null && !_benchmarkArbiter.TryAcquireExternalMutation(out benchmarkLease))
                {
                    return BenchmarkActiveStartResult();
                }

                if (IsSessionActive)
                {
                    return new SessionOptimizationStartResult
                    {
                        Success = false,
                        ErrorCode = "already_active",
                        Message = "A session optimization session is already active."
                    };
                }

                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);
                var settings = LoadSettings();
                var snapshot = await CaptureProcessSnapshotCoreAsync(linkedCts.Token).ConfigureAwait(false);
                linkedCts.Token.ThrowIfCancellationRequested();
                var candidates = BuildCandidates(snapshot, gameItem, settings).ToList();

                bool taskbarConfigured = settings.HideTaskbarDuringSession;
                TaskbarVisibilityState? originalTaskbarVisibility = taskbarConfigured
                    ? _taskbarService.CaptureVisibilityState()
                    : null;
                bool taskbarRequested = taskbarConfigured && originalTaskbarVisibility != null;
                if (candidates.Count == 0 && !taskbarRequested)
                {
                    return new SessionOptimizationStartResult
                    {
                        Success = false,
                        ErrorCode = "no_candidates",
                        Message = "No active background processes to suspend."
                    };
                }

                DateTime now = DateTime.UtcNow;
                journal = new ActiveSessionState
                {
                    IsActive = true,
                    Trigger = trigger,
                    GameId = gameItem.Id,
                    GameName = gameItem.DisplayName,
                    GameProcessName = gameItem.ProcessName,
                    StartedAtUtc = now,
                    LastUpdatedAtUtc = now,
                    RecoveryPhase = SessionRecoveryPhase.Prepared,
                    TaskbarHideRequested = taskbarRequested,
                    OriginalTaskbarVisibility = originalTaskbarVisibility,
                    IsRecoveryPending = true,
                    PlannedProcesses = candidates.Select(CreatePlannedRecord).ToList()
                };

                // The write-ahead record is the authority: no external mutation may occur before this succeeds.
                if (!SaveJournal(journal))
                {
                    return StatePersistFailedStartResult(failedCount: 0);
                }

                foreach (SuspendCandidate candidate in candidates)
                {
                    linkedCts.Token.ThrowIfCancellationRequested();

                    SuspendedProcessRecord planned = FindRecord(journal.PlannedProcesses, candidate.ProcessId)
                        ?? CreatePlannedRecord(candidate);
                    journal.RecoveryPhase = SessionRecoveryPhase.Applying;
                    journal.PendingSuspension = planned;
                    if (!SaveJournal(journal))
                    {
                        // The intent was not durable and the suspend call has not begun.
                        journal.PendingSuspension = null;
                        return RollBackFailedStart(journal, failedCount);
                    }

                    SessionActionResult processResult = _suspendService.SuspendProcesses([candidate]);
                    failedCount += processResult.FailedCount;
                    journal.PlannedProcesses.RemoveAll(record => SameProcessIdentity(record, planned));

                    if (processResult.Records.Count > 0)
                    {
                        journal.SuspendedProcesses.Add(processResult.Records[0]);
                    }
                    journal.PendingSuspension = null;

                    if (!SaveJournal(journal))
                    {
                        return RollBackFailedStart(journal, failedCount);
                    }
                }

                if (taskbarRequested)
                {
                    journal.TaskbarMutationPending = true;
                    if (!SaveJournal(journal))
                    {
                        // The taskbar call has not begun, so this in-memory intent is not ambiguous.
                        journal.TaskbarMutationPending = false;
                        return RollBackFailedStart(journal, failedCount);
                    }

                    bool hidden = _taskbarService.HideTaskbars();
                    journal.TaskbarHidden = hidden;
                    // A false result cannot prove that no taskbar window was changed before an error.
                    journal.TaskbarMutationPending = !hidden;
                    if (!SaveJournal(journal))
                    {
                        return RollBackFailedStart(journal, failedCount);
                    }

                    if (!hidden && journal.SuspendedProcesses.Count == 0)
                    {
                        return RollBackFailedStart(journal, failedCount, "no_candidates");
                    }
                }

                journal.RecoveryPhase = SessionRecoveryPhase.Active;
                journal.IsRecoveryPending = false;
                journal.TaskbarHideRequested = false;
                if (!SaveJournal(journal))
                {
                    return RollBackFailedStart(journal, failedCount);
                }

                ActiveSession = journal;
                _loadedStateRequiresConservativeRecovery = false;
                _logger.Info($"Session optimization started ({trigger}) for '{gameItem.DisplayName}'. Suspended: {journal.SuspendedProcesses.Count}, Failed: {failedCount}, Taskbar: {journal.TaskbarHidden}.");

                return new SessionOptimizationStartResult
                {
                    Success = true,
                    ErrorCode = "applied",
                    SuspendedCount = journal.SuspendedProcesses.Count,
                    FailedCount = failedCount,
                    TaskbarHidden = journal.TaskbarHidden,
                    Message = $"Session started. Suspended {journal.SuspendedProcesses.Count} processes."
                };
            }
            catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
            {
                if (journal != null)
                {
                    return RollBackFailedStart(journal, failedCount, "coordinator_stopping");
                }
                return CoordinatorStoppingStartResult();
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to start session optimization: {ex.Message}", ex);
                if (journal != null)
                {
                    return RollBackFailedStart(journal, failedCount, "apply_failed");
                }
                return new SessionOptimizationStartResult
                {
                    Success = false,
                    ErrorCode = "apply_failed",
                    Message = "Session optimization could not be started."
                };
            }
            finally
            {
                benchmarkLease?.Dispose();
                _mutationGate.Release();
            }
        }
        finally
        {
            ExitOperation();
        }
    }

    public async Task<SessionOptimizationRestoreResult> StopSessionAsync(CancellationToken cancellationToken = default)
    {
        if (!TryEnterOperation())
        {
            return CoordinatorStoppingRestoreResult();
        }

        try
        {
            // Non-queuing gate: reject concurrent start/restore immediately
            if (!_mutationGate.Wait(0))
            {
                return new SessionOptimizationRestoreResult
                {
                    Success = false,
                    ErrorCode = "operation_in_progress",
                    Message = "Another session optimization operation is in progress."
                };
            }

            IDisposable? benchmarkLease = null;
            try
            {
                if (_benchmarkArbiter != null && !_benchmarkArbiter.TryAcquireExternalMutation(out benchmarkLease))
                {
                    return BenchmarkActiveRestoreResult();
                }

                var currentSession = ActiveSession;
                if (currentSession?.IsActive != true)
                {
                    return new SessionOptimizationRestoreResult
                    {
                        Success = false,
                        ErrorCode = "not_active",
                        Message = "No active session optimization session to restore."
                    };
                }

                return RestoreStateCore(currentSession, SessionRecoveryPhase.Restoring);
            }
            catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
            {
                return CoordinatorStoppingRestoreResult();
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to restore session optimization: {ex.Message}", ex);
                return new SessionOptimizationRestoreResult
                {
                    Success = false,
                    ErrorCode = "restore_failed",
                    Message = "Session optimization could not be restored."
                };
            }
            finally
            {
                benchmarkLease?.Dispose();
                _mutationGate.Release();
            }
        }
        finally
        {
            ExitOperation();
        }
    }

    private SessionOptimizationStartResult RollBackFailedStart(
        ActiveSessionState journal,
        int failedCount,
        string errorCode = "state_persist_failed")
    {
        SessionOptimizationRestoreResult rollback = RestoreStateCore(journal, SessionRecoveryPhase.RollingBack);
        bool recoveryPending = ActiveSession?.IsRecoveryPending == true;

        return new SessionOptimizationStartResult
        {
            Success = false,
            ErrorCode = errorCode,
            SuspendedCount = recoveryPending ? RecoveryProcessCount(ActiveSession!) : 0,
            FailedCount = failedCount + rollback.FailedCount,
            TaskbarHidden = recoveryPending && (ActiveSession!.TaskbarHidden || ActiveSession.TaskbarMutationPending),
            Message = recoveryPending
                ? "Session start failed and rollback is incomplete; durable recovery remains pending."
                : errorCode == "no_candidates"
                    ? "No active background processes to suspend."
                    : errorCode == "state_persist_failed"
                        ? "Session state could not be persisted; applied changes were rolled back."
                        : "Session start failed; applied changes were rolled back."
        };
    }

    private SessionOptimizationRestoreResult RestoreStateCore(
        ActiveSessionState state,
        SessionRecoveryPhase phase)
    {
        state.RecoveryPhase = phase;
        state.IsRecoveryPending = true;
        state.PlannedProcesses.Clear();
        state.TaskbarHideRequested = false;

        // A journal loaded after restart cannot prove whether a prior resume already happened.
        // Likewise, PendingSuspension and PendingResume straddle native calls. Move every such
        // record into a durable, non-mutating recovery inventory before doing any OS work.
        if (_loadedStateRequiresConservativeRecovery)
        {
            foreach (SuspendedProcessRecord record in state.SuspendedProcesses.ToList())
            {
                AddAmbiguousProcess(state, record);
            }
            state.SuspendedProcesses.Clear();
        }
        if (state.PendingSuspension != null)
        {
            AddAmbiguousProcess(state, state.PendingSuspension);
            state.PendingSuspension = null;
        }
        if (state.PendingResume != null)
        {
            AddAmbiguousProcess(state, state.PendingResume);
            state.PendingResume = null;
        }
        _loadedStateRequiresConservativeRecovery = false;

        // Persist the recovery intent before resuming/showing anything. If this fails, the older
        // journal remains untouched and recovery is retried without introducing another mutation.
        if (!SaveJournal(state))
        {
            ActiveSession = state;
            return new SessionOptimizationRestoreResult
            {
                Success = false,
                ErrorCode = "state_persist_failed",
                RemainingCount = RecoveryProcessCount(state),
                TaskbarRestored = false,
                Message = "Recovery could not start because its durable state could not be updated."
            };
        }

        bool taskbarRecoveryRequired = state.TaskbarHidden || state.TaskbarMutationPending;
        SessionActionResult result = new();

        // Ambiguous records are never resumed. They can only be resolved without mutation after
        // the recorded process exits or the PID is proven to belong to a different instance.
        foreach (SuspendedProcessRecord recoveryRecord in state.AmbiguousProcesses.ToList())
        {
            SessionActionResult processResult;
            try
            {
                processResult = _suspendService.ResolveProcessesWithoutResume([recoveryRecord]);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to inspect ambiguous process during session recovery: {ex.Message}", ex);
                processResult = new SessionActionResult { FailedCount = 1 };
            }

            AccumulateResult(result, processResult);

            bool resolved = processResult.Records.Any(record => SameProcessIdentity(record, recoveryRecord));
            if (!resolved) continue;

            state.AmbiguousProcesses.RemoveAll(record => SameProcessIdentity(record, recoveryRecord));
            if (!SaveJournal(state))
            {
                ActiveSession = state;
                return RecoveryProgressPersistFailed(state, result, taskbarRestored: false);
            }
        }

        // Confirmed records created by this live coordinator may be resumed once. Persisting
        // PendingResume first makes a crash on either side of NtResumeProcess conservative:
        // restart will never repeat the native resume.
        foreach (SuspendedProcessRecord recoveryRecord in state.SuspendedProcesses.ToList())
        {
            state.SuspendedProcesses.RemoveAll(record => SameProcessIdentity(record, recoveryRecord));
            state.PendingResume = recoveryRecord;
            if (!SaveJournal(state))
            {
                state.PendingResume = null;
                state.SuspendedProcesses.Add(recoveryRecord);
                ActiveSession = state;
                return RecoveryProgressPersistFailed(state, result, taskbarRestored: false);
            }

            SessionActionResult processResult;
            try
            {
                processResult = _suspendService.ResumeProcesses([recoveryRecord]);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to resume process during session recovery: {ex.Message}", ex);
                processResult = new SessionActionResult { FailedCount = 1 };
            }
            AccumulateResult(result, processResult);

            bool resolved = processResult.Records.Any(record => SameProcessIdentity(record, recoveryRecord));
            state.PendingResume = null;
            if (!resolved)
            {
                AddAmbiguousProcess(state, recoveryRecord);
            }

            if (!SaveJournal(state))
            {
                ActiveSession = state;
                return RecoveryProgressPersistFailed(state, result, taskbarRestored: false);
            }
        }

        bool taskbarRestored = !taskbarRecoveryRequired;
        bool taskbarManualRecoveryRequired = false;
        if (taskbarRecoveryRequired)
        {
            if (state.OriginalTaskbarVisibility == null)
            {
                taskbarRestored = false;
                taskbarManualRecoveryRequired = true;
            }
            else
            {
                try { taskbarRestored = _taskbarService.RestoreVisibilityState(state.OriginalTaskbarVisibility); }
                catch (Exception ex) { _logger.Error($"Failed to restore original taskbar state during session recovery: {ex.Message}", ex); }
            }
        }
        if (taskbarRestored)
        {
            state.TaskbarHidden = false;
            state.TaskbarMutationPending = false;
            state.OriginalTaskbarVisibility = null;
            if (taskbarRecoveryRequired && !SaveJournal(state))
            {
                ActiveSession = state;
                return RecoveryProgressPersistFailed(state, result, taskbarRestored: true);
            }
        }

        int remainingCount = RecoveryProcessCount(state);
        if (remainingCount > 0 || !taskbarRestored)
        {
            state.IsRecoveryPending = true;
            ActiveSession = state;
            if (!SaveJournal(state))
            {
                return new SessionOptimizationRestoreResult
                {
                    Success = false,
                    ErrorCode = "state_persist_failed",
                    ResumedCount = result.SuccessCount,
                    FailedCount = result.FailedCount,
                    RemainingCount = remainingCount,
                    TaskbarRestored = taskbarRecoveryRequired && taskbarRestored,
                    Message = "Recovery remains pending and its updated state could not be persisted."
                };
            }

            _logger.Warn($"Session restoration partial. Resumed: {result.SuccessCount}, Remaining: {remainingCount}, Taskbar restored: {taskbarRestored}.");
            bool manualRecoveryRequired = state.AmbiguousProcesses.Count > 0
                || state.PendingResume != null
                || state.PendingSuspension != null
                || taskbarManualRecoveryRequired;
            return new SessionOptimizationRestoreResult
            {
                Success = false,
                ErrorCode = manualRecoveryRequired ? "restore_manual_required" : "restore_partial",
                ResumedCount = result.SuccessCount,
                FailedCount = result.FailedCount,
                RemainingCount = remainingCount,
                TaskbarRestored = taskbarRecoveryRequired && taskbarRestored,
                Message = manualRecoveryRequired
                    ? "Automatic recovery stopped because prior OS-change ownership is ambiguous. End listed processes or restore taskbar state manually, then retry."
                    : "Restore incomplete; durable recovery remains pending."
            };
        }

        // Persist a truthful, fully recovered record before deleting it. If deletion fails, the
        // empty recovery journal is safe to retry and cannot cause a second OS mutation.
        state.IsRecoveryPending = true;
        if (!SaveJournal(state))
        {
            ActiveSession = state;
            return RecoveryProgressPersistFailed(state, result, taskbarRecoveryRequired && taskbarRestored);
        }

        if (!_stateService.Clear())
        {
            state.IsRecoveryPending = true;
            ActiveSession = state;
            return new SessionOptimizationRestoreResult
            {
                Success = false,
                ErrorCode = "state_clear_failed",
                ResumedCount = result.SuccessCount,
                FailedCount = result.FailedCount,
                RemainingCount = 0,
                TaskbarRestored = taskbarRecoveryRequired && taskbarRestored,
                Message = "System state was restored, but persistent recovery metadata could not be cleared."
            };
        }

        ActiveSession = null;
        _logger.Info($"Session optimization restored. Resumed: {result.SuccessCount}, Failed: {result.FailedCount}, Taskbar restored: {taskbarRecoveryRequired && taskbarRestored}.");
        return new SessionOptimizationRestoreResult
        {
            Success = true,
            ErrorCode = "restored",
            ResumedCount = result.SuccessCount,
            FailedCount = result.FailedCount,
            RemainingCount = 0,
            TaskbarRestored = taskbarRecoveryRequired && taskbarRestored,
            Message = $"Session restored. Resumed {result.SuccessCount} processes."
        };
    }

    private static void AccumulateResult(SessionActionResult destination, SessionActionResult source)
    {
        destination.SuccessCount += source.SuccessCount;
        destination.ResolvedCount += source.ResolvedCount;
        destination.StaleProcessCount += source.StaleProcessCount;
        destination.FailedCount += source.FailedCount;
        destination.Records.AddRange(source.Records);
        destination.Messages.AddRange(source.Messages);
    }

    private static SessionOptimizationRestoreResult RecoveryProgressPersistFailed(
        ActiveSessionState state,
        SessionActionResult result,
        bool taskbarRestored) => new()
    {
        Success = false,
        ErrorCode = "state_persist_failed",
        ResumedCount = result.SuccessCount,
        FailedCount = result.FailedCount,
        RemainingCount = RecoveryProcessCount(state),
        TaskbarRestored = taskbarRestored,
        Message = "Recovery progress could not be persisted; durable recovery remains retryable."
    };

    private bool SaveJournal(ActiveSessionState state)
    {
        state.LastUpdatedAtUtc = DateTime.UtcNow;
        return _stateService.Save(state);
    }

    private static SuspendedProcessRecord CreatePlannedRecord(SuspendCandidate candidate) => new()
    {
        ProcessId = candidate.ProcessId,
        ProcessName = candidate.ProcessName,
        ProcessStartTimeUtc = candidate.ProcessStartTimeUtc,
        RuleId = candidate.RuleId,
        RuleName = candidate.RuleName,
        ExecutablePath = candidate.ExecutablePath,
        SuspendedAtUtc = default
    };

    private static SuspendedProcessRecord? FindRecord(IEnumerable<SuspendedProcessRecord> records, int processId) =>
        records.FirstOrDefault(record => record.ProcessId == processId);

    private static List<SuspendedProcessRecord> GetRecoveryProcessRecords(ActiveSessionState state)
    {
        var records = state.SuspendedProcesses.ToList();
        foreach (SuspendedProcessRecord ambiguous in state.AmbiguousProcesses)
        {
            if (!records.Any(record => SameProcessIdentity(record, ambiguous))) records.Add(ambiguous);
        }
        if (state.PendingSuspension != null && !records.Any(record => SameProcessIdentity(record, state.PendingSuspension)))
        {
            records.Add(state.PendingSuspension);
        }
        if (state.PendingResume != null && !records.Any(record => SameProcessIdentity(record, state.PendingResume)))
        {
            records.Add(state.PendingResume);
        }
        return records;
    }

    private static void AddAmbiguousProcess(ActiveSessionState state, SuspendedProcessRecord record)
    {
        if (!state.AmbiguousProcesses.Any(existing => SameProcessIdentity(existing, record)))
        {
            state.AmbiguousProcesses.Add(record);
        }
    }

    private static int RecoveryProcessCount(ActiveSessionState state) => GetRecoveryProcessRecords(state).Count;

    private static bool SameProcessIdentity(SuspendedProcessRecord left, SuspendedProcessRecord right) =>
        left.ProcessId == right.ProcessId && left.ProcessStartTimeUtc == right.ProcessStartTimeUtc;

    private static SessionOptimizationStartResult StatePersistFailedStartResult(int failedCount) => new()
    {
        Success = false,
        ErrorCode = "state_persist_failed",
        SuspendedCount = 0,
        FailedCount = failedCount,
        TaskbarHidden = false,
        Message = "Session recovery state could not be persisted; no system changes were made."
    };

    private static SessionGameSuspendSettings GetGameSettings(string? gameId, SessionOptimizationSettings settings)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return new SessionGameSuspendSettings();
        }

        if (!settings.GameSettings.TryGetValue(gameId, out var gameSettings) || gameSettings == null)
        {
            gameSettings = new SessionGameSuspendSettings();
            settings.GameSettings[gameId] = gameSettings;
        }

        return gameSettings;
    }

    private static HashSet<string> GetRuleCoveredProcessNames(IEnumerable<BackgroundProcessRule> rules)
    {
        return rules
            .SelectMany(rule => rule.ProcessNames)
            .Select(ProcessSuspendService.NormalizeProcessName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<string> GetProtectedProcessNames(LibraryItem? currentGame, IEnumerable<LibraryItem>? allGames)
    {
        var names = new List<string>();

        if (!string.IsNullOrWhiteSpace(currentGame?.ProcessName))
        {
            names.Add(currentGame.ProcessName!);
        }

        if (allGames != null)
        {
            names.AddRange(allGames
                .Where(x => !string.IsNullOrWhiteSpace(x.ProcessName))
                .Select(x => x.ProcessName!));
        }
        else
        {
            try
            {
                var libraryGames = _libraryService.LoadItems()
                    .Where(x => x.Type == LibraryItemType.Game && !string.IsNullOrWhiteSpace(x.ProcessName))
                    .Select(x => x.ProcessName!);
                names.AddRange(libraryGames);
            }
            catch
            {
                // Fall back to current game only if library load fails
            }
        }

        return names;
    }

    private bool TryEnterOperation()
    {
        lock (_lifecycleLock)
        {
            if (_shutdownStarted)
            {
                return false;
            }

            _activeOperations++;
            return true;
        }
    }

    private void ExitOperation()
    {
        lock (_lifecycleLock)
        {
            _activeOperations--;
            if (_shutdownStarted && _activeOperations == 0)
            {
                _operationsDrained.TrySetResult();
            }
        }
    }

    public async Task<bool> ShutdownAsync(TimeSpan? timeout = null)
    {
        Task drainTask;
        lock (_lifecycleLock)
        {
            if (!_shutdownStarted)
            {
                _shutdownStarted = true;
                if (_activeOperations == 0)
                {
                    _operationsDrained.TrySetResult();
                }
            }
            drainTask = _operationsDrained.Task;
        }

        try { _shutdownCts.Cancel(); } catch (ObjectDisposedException) { }

        TimeSpan waitTimeout = timeout ?? TimeSpan.FromSeconds(10);
        try
        {
            await drainTask.WaitAsync(waitTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _ = drainTask.ContinueWith(
                _ => DisposeSynchronizationPrimitives(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return false;
        }

        DisposeSynchronizationPrimitives();
        return true;
    }

    private void DisposeSynchronizationPrimitives()
    {
        lock (_lifecycleLock)
        {
            if (_gatesDisposed)
            {
                return;
            }

            _gatesDisposed = true;
            _mutationGate.Dispose();
            _scanGate.Dispose();
            _shutdownCts.Dispose();
        }
    }

    private static SessionOptimizationStartResult CoordinatorStoppingStartResult() => new()
    {
        Success = false,
        ErrorCode = "coordinator_stopping",
        Message = "Session Optimization is shutting down."
    };

    private static SessionOptimizationStartResult BenchmarkActiveStartResult() => new()
    {
        Success = false,
        ErrorCode = "benchmark_active",
        Message = "Session Optimization cannot change system state while a benchmark is active."
    };

    private static SessionOptimizationRestoreResult CoordinatorStoppingRestoreResult() => new()
    {
        Success = false,
        ErrorCode = "coordinator_stopping",
        Message = "Session Optimization is shutting down."
    };

    private static SessionOptimizationRestoreResult BenchmarkActiveRestoreResult() => new()
    {
        Success = false,
        ErrorCode = "benchmark_active",
        Message = "Session Optimization cannot change system state while a benchmark is active."
    };

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        Task.Run(async () => await ShutdownAsync().ConfigureAwait(false)).GetAwaiter().GetResult();
    }
}
