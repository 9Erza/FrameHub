using System.IO;
using FrameHub.Companion.Models;
using FrameHub.Companion.Providers;
using FrameHub.Core.Logging;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Benchmarking;
using FrameHub.Core.Services.Library;

namespace FrameHub.App.Services;

public sealed class AppBackgroundAppProvider : ICompanionBackgroundAppsProvider
{
    private readonly LibraryService _libraryService;
    private readonly ProcessScannerService _processScanner;
    private readonly IBenchmarkCaptureCoordinator _benchmarkCoordinator;
    private readonly IAppLibraryControlService _controlService;
    private readonly LibraryLaunchReservationService _launchReservations;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    public AppBackgroundAppProvider(
        ProcessScannerService processScanner,
        IBenchmarkCaptureCoordinator benchmarkCoordinator,
        IAppLibraryLaunchService launchService,
        LibraryService? libraryService = null,
        LibraryLaunchReservationService? launchReservations = null,
        ILogger? logger = null,
        IAppLibraryControlService? controlService = null)
    {
        _processScanner = processScanner ?? throw new ArgumentNullException(nameof(processScanner));
        _benchmarkCoordinator = benchmarkCoordinator ?? throw new ArgumentNullException(nameof(benchmarkCoordinator));
        _controlService = controlService ?? new AppLibraryControlService(processScanner, launchService);
        _libraryService = libraryService ?? new LibraryService();
        _launchReservations = launchReservations ?? new LibraryLaunchReservationService();
        _logger = logger ?? LoggerService.Instance;
    }

    public async Task<IReadOnlyList<CompanionBackgroundAppDto>> GetBackgroundAppsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            List<LibraryItem> items = FilterBackgroundApps(_libraryService.LoadItems());
            IReadOnlySet<string> runningIds = await _processScanner
                .FindRunningLibraryItemIdsAsync(items, cancellationToken)
                .ConfigureAwait(false);

            return items.Select(item =>
            {
                bool isRunning = runningIds.Contains(item.Id);
                return new CompanionBackgroundAppDto
                {
                    Id = item.Id,
                    DisplayName = item.DisplayName,
                    IsRunning = isRunning,
                    CanStart = !isRunning && File.Exists(item.ExecutablePath),
                    CanStop = isRunning
                };
            }).ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get remotely controllable background apps: {ex.Message}", ex);
            return Array.Empty<CompanionBackgroundAppDto>();
        }
    }

    public Task<CompanionBackgroundAppOperationDto> StartBackgroundAppAsync(
        string id,
        CancellationToken cancellationToken = default) =>
        ControlBackgroundAppAsync(id, start: true, cancellationToken);

    public Task<CompanionBackgroundAppOperationDto> StopBackgroundAppAsync(
        string id,
        CancellationToken cancellationToken = default) =>
        ControlBackgroundAppAsync(id, start: false, cancellationToken);

    private async Task<CompanionBackgroundAppOperationDto> ControlBackgroundAppAsync(
        string id,
        bool start,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return Failure("not_found");
        if (!_operationGate.Wait(0)) return Failure("operation_busy");

        IDisposable? benchmarkLease = null;
        try
        {
            LibraryItem? item = FilterBackgroundApps(_libraryService.LoadItems())
                .FirstOrDefault(candidate => candidate.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));
            if (item == null) return Failure("not_found");

            if (_benchmarkCoordinator is IBenchmarkOperationArbiter arbiter)
            {
                if (!arbiter.TryAcquireExternalMutation(out benchmarkLease))
                {
                    return Failure(_benchmarkCoordinator.IsActive ? "benchmark_active" : "operation_busy");
                }
            }
            else if (_benchmarkCoordinator.IsActive)
            {
                return Failure("benchmark_active");
            }

            if (_benchmarkCoordinator.IsActive) return Failure("benchmark_active");

            IReadOnlyList<LibraryProcessIdentity> running = await _processScanner
                .FindRunningLibraryItemProcessesAsync(item, cancellationToken)
                .ConfigureAwait(false);
            if (start && running.Count > 0) return Failure("already_running");
            if (!start && running.Count == 0) return Failure("not_running");

            DateTimeOffset operationTime = _launchReservations.Now;
            if (start && _launchReservations.IsCoolingDown(item.Id, operationTime)) return Failure("operation_busy");
            if (_benchmarkCoordinator.IsActive) return Failure("benchmark_active");

            LibraryControlResult result = start
                ? _controlService.Start(item)
                : await _controlService.StopAsync(item, cancellationToken).ConfigureAwait(false);
            if (start && result.Success) _launchReservations.RecordSuccessfulLaunch(item.Id, operationTime);
            return new CompanionBackgroundAppOperationDto { Success = result.Success, ErrorCode = result.ErrorCode };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"Remote background-app {(start ? "start" : "stop")} failed for item '{id}': {ex.Message}", ex);
            return Failure(start ? "launch_failed" : "stop_failed");
        }
        finally
        {
            benchmarkLease?.Dispose();
            _operationGate.Release();
        }
    }

    private static CompanionBackgroundAppOperationDto Failure(string errorCode) =>
        new() { Success = false, ErrorCode = errorCode };

    internal static List<LibraryItem> FilterBackgroundApps(IEnumerable<LibraryItem> items)
    {
        return items
            .Where(item => item != null
                && !string.IsNullOrWhiteSpace(item.Id)
                && LibraryItemFilter.IsSupportedLibraryItem(item)
                && SystemTrustedProcessTerminator.IsEligibleTrustedItem(item))
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
