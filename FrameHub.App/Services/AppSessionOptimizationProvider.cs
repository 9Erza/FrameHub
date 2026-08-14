using FrameHub.Companion.Models;
using FrameHub.Companion.Providers;
using FrameHub.Core.Logging;
using FrameHub.Core.Services.Benchmarking;

namespace FrameHub.App.Services;

public sealed class AppSessionOptimizationProvider : ICompanionSessionOptimizationProvider
{
    private readonly SessionOptimizationCoordinator _coordinator;
    private readonly IActiveGameMonitor _activeGameMonitor;
    private readonly IBenchmarkCaptureCoordinator _benchmarkCoordinator;
    private readonly ILogger _logger;

    public AppSessionOptimizationProvider(
        AppRuntimeService runtime,
        SessionOptimizationCoordinator? coordinator = null,
        IActiveGameMonitor? activeGameMonitor = null,
        IBenchmarkCaptureCoordinator? benchmarkCoordinator = null,
        ILogger? logger = null)
        : this(
            coordinator ?? runtime.SessionOptimizationCoordinator,
            activeGameMonitor ?? runtime.ActiveGameMonitor,
            benchmarkCoordinator ?? runtime.BenchmarkCoordinator,
            logger)
    {
    }

    public AppSessionOptimizationProvider(
        SessionOptimizationCoordinator coordinator,
        IActiveGameMonitor activeGameMonitor,
        IBenchmarkCaptureCoordinator benchmarkCoordinator,
        ILogger? logger = null)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _activeGameMonitor = activeGameMonitor ?? throw new ArgumentNullException(nameof(activeGameMonitor));
        _benchmarkCoordinator = benchmarkCoordinator ?? throw new ArgumentNullException(nameof(benchmarkCoordinator));
        _logger = logger ?? LoggerService.Instance;
    }

    public Task<CompanionSessionOptimizationStateDto> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var session = _coordinator.ActiveSession;
        var activeGame = _activeGameMonitor.CurrentSnapshot;

        var dto = new CompanionSessionOptimizationStateDto
        {
            IsSessionActive = session?.IsActive == true,
            SessionId = session?.SessionId,
            GameId = session?.GameId ?? activeGame?.LibraryItem.Id,
            GameDisplayName = session?.GameName ?? activeGame?.LibraryItem.DisplayName,
            StartedAtUtc = session?.StartedAtUtc,
            SuspendedProcessCount = session == null
                ? 0
                : session.SuspendedProcesses.Count
                    + session.AmbiguousProcesses.Count
                    + (session.PendingSuspension == null ? 0 : 1)
                    + (session.PendingResume == null ? 0 : 1),
            TaskbarHidden = session?.TaskbarHidden == true,
            IsRecoveryPending = session?.IsRecoveryPending == true,
            Trigger = session?.Trigger ?? "Manual"
        };

        return Task.FromResult(dto);
    }

    public async Task<CompanionOptimizationResultDto> ApplyOptimizationAsync(CancellationToken cancellationToken = default)
    {
        // 1. Early benchmark check
        if (_benchmarkCoordinator.IsActive)
        {
            return new CompanionOptimizationResultDto
            {
                Success = false,
                ErrorCode = "benchmark_active"
            };
        }

        // 2. Resolve current active game from trusted ActiveGameMonitor
        var activeGame = _activeGameMonitor.CurrentSnapshot;
        if (activeGame == null || activeGame.LibraryItem == null)
        {
            return new CompanionOptimizationResultDto
            {
                Success = false,
                ErrorCode = "no_game"
            };
        }

        // 3. Immediate benchmark recheck
        if (_benchmarkCoordinator.IsActive)
        {
            return new CompanionOptimizationResultDto
            {
                Success = false,
                ErrorCode = "benchmark_active"
            };
        }

        // 4. Delegate to authoritative coordinator
        var result = await _coordinator.StartSessionAsync("Remote", activeGame.LibraryItem, cancellationToken).ConfigureAwait(false);

        if (result.Success)
        {
            return new CompanionOptimizationResultDto
            {
                Success = true,
                ErrorCode = "applied",
                SuspendedProcessCount = result.SuspendedCount
            };
        }

        return new CompanionOptimizationResultDto
        {
            Success = false,
            ErrorCode = string.IsNullOrWhiteSpace(result.ErrorCode) ? "apply_failed" : result.ErrorCode,
            SuspendedProcessCount = result.SuspendedCount
        };
    }

    public async Task<CompanionOptimizationResultDto> RestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        // 1. Early benchmark check
        if (_benchmarkCoordinator.IsActive)
        {
            return new CompanionOptimizationResultDto
            {
                Success = false,
                ErrorCode = "benchmark_active"
            };
        }

        // 2. Delegate to authoritative coordinator
        var result = await _coordinator.StopSessionAsync(cancellationToken).ConfigureAwait(false);

        if (result.Success)
        {
            return new CompanionOptimizationResultDto
            {
                Success = true,
                ErrorCode = "restored",
                SuspendedProcessCount = result.RemainingCount
            };
        }

        return new CompanionOptimizationResultDto
        {
            Success = false,
            ErrorCode = string.IsNullOrWhiteSpace(result.ErrorCode) ? "restore_failed" : result.ErrorCode,
            SuspendedProcessCount = result.RemainingCount
        };
    }
}
