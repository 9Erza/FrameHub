using System.Threading;
using FrameHub.Core.Logging;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Models.SessionOptimization;
using FrameHub.Core.Services;
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
public sealed class SessionOptimizationCoordinator : IDisposable
{
    private const string ExplorerRuleId = "explorer";

    private readonly SessionStateService _stateService;
    private readonly SessionOptimizationSettingsService _settingsService;
    private readonly ProcessSuspendService _suspendService;
    private readonly TaskbarVisibilityService _taskbarService;
    private readonly ProcessScannerService _processScanner;
    private readonly LibraryService _libraryService;
    private readonly ILogger _logger;

    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly object _stateLock = new();

    private ActiveSessionState? _activeSession;
    private bool _disposed;

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
        ILogger? logger = null)
    {
        _processScanner = processScanner ?? new ProcessScannerService(new ProcessService());
        _stateService = stateService ?? new SessionStateService();
        _settingsService = settingsService ?? new SessionOptimizationSettingsService();
        _suspendService = suspendService ?? new ProcessSuspendService();
        _taskbarService = taskbarService ?? new TaskbarVisibilityService();
        _libraryService = libraryService ?? new LibraryService();
        _logger = logger ?? LoggerService.Instance;

        _activeSession = _stateService.Load();
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

        try
        {
            if (IsSessionActive)
            {
                return new SessionOptimizationStartResult
                {
                    Success = false,
                    ErrorCode = "already_active",
                    Message = "A session optimization session is already active."
                };
            }

            var settings = LoadSettings();
            var snapshot = await CaptureProcessSnapshotAsync(cancellationToken).ConfigureAwait(false);
            var candidates = BuildCandidates(snapshot, gameItem, settings).ToList();

            bool taskbarRequested = settings.HideTaskbarDuringSession;
            bool taskbarHidden = false;

            if (taskbarRequested)
            {
                taskbarHidden = _taskbarService.HideTaskbars();
            }

            if (candidates.Count == 0 && !taskbarHidden)
            {
                return new SessionOptimizationStartResult
                {
                    Success = false,
                    ErrorCode = "no_candidates",
                    Message = "No active background processes to suspend."
                };
            }

            var result = _suspendService.SuspendProcesses(candidates);

            var newSession = new ActiveSessionState
            {
                IsActive = true,
                Trigger = trigger,
                GameId = gameItem.Id,
                GameName = gameItem.DisplayName,
                GameProcessName = gameItem.ProcessName,
                StartedAtUtc = DateTime.UtcNow,
                TaskbarHidden = taskbarHidden,
                SuspendedProcesses = result.Records
            };

            _stateService.Save(newSession);
            ActiveSession = newSession;

            _logger.Info($"Session optimization started ({trigger}) for '{gameItem.DisplayName}'. Suspended: {result.SuccessCount}, Failed: {result.FailedCount}, Taskbar: {taskbarHidden}.");

            return new SessionOptimizationStartResult
            {
                Success = true,
                ErrorCode = "applied",
                SuspendedCount = result.SuccessCount,
                FailedCount = result.FailedCount,
                TaskbarHidden = taskbarHidden,
                Message = $"Session started. Suspended {result.SuccessCount} processes."
            };
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to start session optimization: {ex.Message}", ex);
            return new SessionOptimizationStartResult
            {
                Success = false,
                ErrorCode = "apply_failed",
                Message = ex.Message
            };
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<SessionOptimizationRestoreResult> StopSessionAsync(CancellationToken cancellationToken = default)
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

        try
        {
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

            bool taskbarWasHidden = currentSession.TaskbarHidden;
            var result = _suspendService.ResumeProcesses(currentSession.SuspendedProcesses);

            if (taskbarWasHidden)
            {
                _taskbarService.ShowTaskbars();
            }

            var resumedProcessIds = result.Records
                .Select(record => record.ProcessId)
                .ToHashSet();

            var remainingProcesses = currentSession.SuspendedProcesses
                .Where(record => !resumedProcessIds.Contains(record.ProcessId))
                .ToList();

            if (remainingProcesses.Count > 0)
            {
                currentSession.SuspendedProcesses = remainingProcesses;
                currentSession.TaskbarHidden = false;
                currentSession.IsRecoveryPending = true;
                _stateService.Save(currentSession);
                ActiveSession = currentSession;

                _logger.Warn($"Session restoration partial. Resumed: {result.SuccessCount}, Remaining: {remainingProcesses.Count}.");

                return new SessionOptimizationRestoreResult
                {
                    Success = true,
                    ErrorCode = "restored",
                    ResumedCount = result.SuccessCount,
                    FailedCount = result.FailedCount,
                    RemainingCount = remainingProcesses.Count,
                    TaskbarRestored = taskbarWasHidden,
                    Message = $"Restore incomplete. {remainingProcesses.Count} processes remain for recovery."
                };
            }

            _stateService.Clear();
            ActiveSession = null;

            _logger.Info($"Session optimization restored. Resumed: {result.SuccessCount}, Failed: {result.FailedCount}, Taskbar restored: {taskbarWasHidden}.");

            return new SessionOptimizationRestoreResult
            {
                Success = true,
                ErrorCode = "restored",
                ResumedCount = result.SuccessCount,
                FailedCount = result.FailedCount,
                RemainingCount = 0,
                TaskbarRestored = taskbarWasHidden,
                Message = $"Session restored. Resumed {result.SuccessCount} processes."
            };
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to restore session optimization: {ex.Message}", ex);
            return new SessionOptimizationRestoreResult
            {
                Success = false,
                ErrorCode = "restore_failed",
                Message = ex.Message
            };
        }
        finally
        {
            _mutationGate.Release();
        }
    }

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _mutationGate.Dispose();
        _scanGate.Dispose();
    }
}
