using FrameHub.Core.Logging;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Benchmarking;
using FrameHub.Core.Services.Library;

namespace FrameHub.App.Services;

public sealed record GamingQuickActionResult
{
    public bool Success { get; init; }
    public string ErrorCode { get; init; } = string.Empty;
    public string Stage { get; init; } = string.Empty;
    public bool Launched { get; init; }
    public bool AlreadyRunning { get; init; }
    public int SuspendedCount { get; init; }
    public string Message { get; init; } = string.Empty;
}

public interface IGamingQuickActionService
{
    Task<GamingQuickActionResult> StartGamingModeAsync(LibraryItem? item, CancellationToken cancellationToken = default);
    Task<SessionOptimizationRestoreResult> StopGamingModeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Stateless Desktop orchestration boundary for the Dashboard Gaming Mode quick action.
/// Composes existing authorities only: trusted launch via <see cref="IAppLibraryLaunchService"/>,
/// cooldown via the shared <see cref="LibraryLaunchReservationService"/>, Session lifecycle via
/// <see cref="SessionOptimizationCoordinator"/> (which owns benchmark arbitration), and
/// non-destructive already-running discovery via <see cref="ProcessScannerService"/>.
/// </summary>
public sealed class GamingQuickActionService : IGamingQuickActionService
{
    private readonly ProcessScannerService _processScanner;
    private readonly IBenchmarkCaptureCoordinator _benchmarkCoordinator;
    private readonly IAppLibraryLaunchService _launchService;
    private readonly LibraryLaunchReservationService _launchReservations;
    private readonly SessionOptimizationCoordinator _sessionCoordinator;
    private readonly ILogger _logger;

    private readonly SemaphoreSlim _actionGate = new(1, 1);

    public GamingQuickActionService(
        ProcessScannerService processScanner,
        IBenchmarkCaptureCoordinator benchmarkCoordinator,
        IAppLibraryLaunchService launchService,
        LibraryLaunchReservationService launchReservations,
        SessionOptimizationCoordinator sessionCoordinator,
        ILogger? logger = null)
    {
        _processScanner = processScanner ?? throw new ArgumentNullException(nameof(processScanner));
        _benchmarkCoordinator = benchmarkCoordinator ?? throw new ArgumentNullException(nameof(benchmarkCoordinator));
        _launchService = launchService ?? throw new ArgumentNullException(nameof(launchService));
        _launchReservations = launchReservations ?? throw new ArgumentNullException(nameof(launchReservations));
        _sessionCoordinator = sessionCoordinator ?? throw new ArgumentNullException(nameof(sessionCoordinator));
        _logger = logger ?? LoggerService.Instance;
    }

    public async Task<GamingQuickActionResult> StartGamingModeAsync(LibraryItem? item, CancellationToken cancellationToken = default)
    {
        if (!IsGamingModeGameItem(item))
        {
            return new GamingQuickActionResult
            {
                Success = false,
                ErrorCode = "not_launchable",
                Stage = "Launch"
            };
        }

        // Non-queuing gate: one Gaming Mode action at a time.
        if (!_actionGate.Wait(0))
        {
            return new GamingQuickActionResult
            {
                Success = false,
                ErrorCode = "operation_in_progress",
                Stage = "Launch"
            };
        }

        try
        {
            LibraryItem game = item!;

            // Early benchmark gate; Session start re-arbitrates authoritatively inside the coordinator.
            if (_benchmarkCoordinator.IsActive)
            {
                return new GamingQuickActionResult
                {
                    Success = false,
                    ErrorCode = "benchmark_active",
                    Stage = "Launch"
                };
            }

            // Non-destructive discovery only; the result never authorizes process mutation.
            var runningIds = await _processScanner.FindRunningLibraryItemIdsAsync(new[] { game }, cancellationToken).ConfigureAwait(false);
            bool alreadyRunning = runningIds.Contains(game.Id);
            bool launched = false;

            if (!alreadyRunning)
            {
                DateTimeOffset now = _launchReservations.Now;
                if (_launchReservations.IsCoolingDown(game.Id, now))
                {
                    return new GamingQuickActionResult
                    {
                        Success = false,
                        ErrorCode = "launch_in_progress",
                        Stage = "Launch"
                    };
                }

                if (_benchmarkCoordinator.IsActive)
                {
                    return new GamingQuickActionResult
                    {
                        Success = false,
                        ErrorCode = "benchmark_active",
                        Stage = "Launch"
                    };
                }

                var launchResult = _launchService.Launch(game);
                if (!launchResult.Success)
                {
                    return new GamingQuickActionResult
                    {
                        Success = false,
                        ErrorCode = string.IsNullOrWhiteSpace(launchResult.ErrorCode) ? "launch_failed" : launchResult.ErrorCode,
                        Stage = "Launch"
                    };
                }

                launched = true;
                _launchReservations.RecordSuccessfulLaunch(game.Id, now);
            }

            // The game may still be starting; Session Optimization builds its own fresh snapshot
            // and rules projection inside the coordinator. Manual trigger keeps existing semantics.
            var sessionResult = await _sessionCoordinator.StartSessionAsync("Manual", game, cancellationToken).ConfigureAwait(false);
            if (!sessionResult.Success)
            {
                _logger.Warn($"Gaming Mode session start failed for '{game.DisplayName}' ({sessionResult.ErrorCode}); launched={launched}.");
            }

            return new GamingQuickActionResult
            {
                Success = sessionResult.Success,
                ErrorCode = sessionResult.ErrorCode,
                Stage = "SessionStart",
                Launched = launched,
                AlreadyRunning = alreadyRunning,
                SuspendedCount = sessionResult.SuspendedCount,
                Message = sessionResult.Message
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"Gaming Mode quick action failed for item '{item?.DisplayName ?? "<null>"}': {ex.Message}", ex);
            return new GamingQuickActionResult
            {
                Success = false,
                ErrorCode = "apply_failed",
                Stage = "SessionStart"
            };
        }
        finally
        {
            _actionGate.Release();
        }
    }

    public Task<SessionOptimizationRestoreResult> StopGamingModeAsync(CancellationToken cancellationToken = default)
    {
        return _sessionCoordinator.StopSessionAsync(cancellationToken);
    }

    private static bool IsGamingModeGameItem(LibraryItem? item)
    {
        return item != null
            && item.IsEnabled
            && item.Type == LibraryItemType.Game
            && LibraryItemFilter.IsSupportedLibraryItem(item);
    }
}
