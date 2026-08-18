using System.IO;
using FrameHub.Companion.Models;
using FrameHub.Companion.Providers;
using FrameHub.Core.Logging;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Benchmarking;
using FrameHub.Core.Services.Library;

namespace FrameHub.App.Services;

public sealed class AppLibraryProvider : ICompanionLibraryProvider
{
    private readonly LibraryService _libraryService;
    private readonly ProcessScannerService _processScanner;
    private readonly IBenchmarkCaptureCoordinator _benchmarkCoordinator;
    private readonly IAppLibraryLaunchService _launchService;
    private readonly LibraryLaunchReservationService _launchReservations;
    private readonly ILogger _logger;

    private readonly SemaphoreSlim _launchGate = new(1, 1);

    public AppLibraryProvider(
        AppRuntimeService runtime,
        IAppLibraryLaunchService? launchService = null,
        LibraryLaunchReservationService? launchReservations = null,
        Func<DateTimeOffset>? clock = null,
        ILogger? logger = null)
        : this(
            runtime.ProcessScanner,
            runtime.BenchmarkCoordinator,
            launchService ?? new AppLibraryLaunchService(),
            new LibraryService(),
            clock,
            logger,
            launchReservations: launchReservations)
    {
    }

    public AppLibraryProvider(
        ProcessScannerService processScanner,
        IBenchmarkCaptureCoordinator benchmarkCoordinator,
        IAppLibraryLaunchService launchService,
        LibraryService? libraryService = null,
        Func<DateTimeOffset>? clock = null,
        ILogger? logger = null,
        LibraryLaunchReservationService? launchReservations = null)
    {
        _processScanner = processScanner ?? throw new ArgumentNullException(nameof(processScanner));
        _benchmarkCoordinator = benchmarkCoordinator ?? throw new ArgumentNullException(nameof(benchmarkCoordinator));
        _launchService = launchService ?? throw new ArgumentNullException(nameof(launchService));
        _libraryService = libraryService ?? new LibraryService();
        _launchReservations = launchReservations ?? new LibraryLaunchReservationService(clock);
        _logger = logger ?? LoggerService.Instance;
    }

    public async Task<IReadOnlyList<CompanionLibraryItemDto>> GetLibraryItemsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var allItems = _libraryService.LoadItems();
            var exposedItems = FilterExposedItems(allItems);

            if (exposedItems.Count == 0)
            {
                return Array.Empty<CompanionLibraryItemDto>();
            }

            var runningIds = await _processScanner.FindRunningLibraryItemIdsAsync(exposedItems).ConfigureAwait(false);

            var dtos = new List<CompanionLibraryItemDto>(exposedItems.Count);
            foreach (var item in exposedItems)
            {
                bool isMissing = !File.Exists(item.LaunchPath ?? item.ExecutablePath);
                bool hasIcon = !string.IsNullOrWhiteSpace(item.IconPath) ? File.Exists(item.IconPath) : (!string.IsNullOrWhiteSpace(item.ExecutablePath) && File.Exists(item.ExecutablePath));

                dtos.Add(new CompanionLibraryItemDto
                {
                    Id = item.Id,
                    DisplayName = item.DisplayName,
                    Source = item.Source.ToString(),
                    Type = item.Type.ToString(),
                    IsRunning = runningIds.Contains(item.Id),
                    HasIcon = hasIcon,
                    IsExecutableMissing = isMissing
                });
            }

            return dtos;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get companion library items: {ex.Message}", ex);
            return Array.Empty<CompanionLibraryItemDto>();
        }
    }

    public Task<CompanionLibraryIconResult?> GetItemIconAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Task.FromResult<CompanionLibraryIconResult?>(null);
        }

        try
        {
            var allItems = _libraryService.LoadItems();
            var targetItem = FilterExposedItems(allItems).FirstOrDefault(x => x.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));
            if (targetItem == null)
            {
                return Task.FromResult<CompanionLibraryIconResult?>(null);
            }

            string? path = !string.IsNullOrWhiteSpace(targetItem.IconPath) ? targetItem.IconPath : targetItem.ExecutablePath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return Task.FromResult<CompanionLibraryIconResult?>(null);
            }

            string ext = Path.GetExtension(path);
            if (string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<CompanionLibraryIconResult?>(new CompanionLibraryIconResult
                {
                    Bytes = File.ReadAllBytes(path),
                    ContentType = "image/png"
                });
            }

            if (string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase) || string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<CompanionLibraryIconResult?>(new CompanionLibraryIconResult
                {
                    Bytes = File.ReadAllBytes(path),
                    ContentType = "image/jpeg"
                });
            }

            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon == null)
            {
                return Task.FromResult<CompanionLibraryIconResult?>(null);
            }

            using var bmp = icon.ToBitmap();
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);

            return Task.FromResult<CompanionLibraryIconResult?>(new CompanionLibraryIconResult
            {
                Bytes = ms.ToArray(),
                ContentType = "image/png"
            });
        }
        catch (Exception ex)
        {
            _logger.Debug($"Failed to extract icon for library item '{id}': {ex.Message}");
            return Task.FromResult<CompanionLibraryIconResult?>(null);
        }
    }

    public async Task<CompanionLaunchResultDto> LaunchItemAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return new CompanionLaunchResultDto { Success = false, ErrorCode = "not_found" };
        }

        // Non-queuing gate: reject if another launch is in flight
        if (!_launchGate.Wait(0))
        {
            return new CompanionLaunchResultDto { Success = false, ErrorCode = "launch_in_progress" };
        }

        try
        {
            // 1. Reload server-side library items
            var allItems = _libraryService.LoadItems();
            var exposedItems = FilterExposedItems(allItems);

            var targetItem = exposedItems.FirstOrDefault(x => x.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));
            if (targetItem == null)
            {
                return new CompanionLaunchResultDto { Success = false, ErrorCode = "not_found" };
            }

            // 2. Early Benchmark check
            if (_benchmarkCoordinator.IsActive)
            {
                return new CompanionLaunchResultDto { Success = false, ErrorCode = "benchmark_active" };
            }

            // 3. Process Scanner running check
            var runningIds = await _processScanner.FindRunningLibraryItemIdsAsync(new[] { targetItem }).ConfigureAwait(false);
            if (runningIds.Contains(targetItem.Id))
            {
                return new CompanionLaunchResultDto { Success = false, ErrorCode = "already_running" };
            }

            // 4. Cooldown check
            DateTimeOffset now = _launchReservations.Now;
            if (_launchReservations.IsCoolingDown(targetItem.Id, now))
            {
                return new CompanionLaunchResultDto { Success = false, ErrorCode = "launch_in_progress" };
            }

            // 5. Immediate Benchmark recheck
            if (_benchmarkCoordinator.IsActive)
            {
                return new CompanionLaunchResultDto { Success = false, ErrorCode = "benchmark_active" };
            }

            // 6. Launch execution via shared App launch service
            var launchResult = _launchService.Launch(targetItem);

            if (launchResult.Success)
            {
                _launchReservations.RecordSuccessfulLaunch(targetItem.Id, now);

                return new CompanionLaunchResultDto { Success = true, ErrorCode = "launched" };
            }

            return new CompanionLaunchResultDto
            {
                Success = false,
                ErrorCode = string.IsNullOrWhiteSpace(launchResult.ErrorCode) ? "launch_failed" : launchResult.ErrorCode
            };
        }
        catch (Exception ex)
        {
            _logger.Error($"Unexpected error during remote launch of item '{id}': {ex.Message}", ex);
            return new CompanionLaunchResultDto { Success = false, ErrorCode = "launch_failed" };
        }
        finally
        {
            _launchGate.Release();
        }
    }

    private static List<LibraryItem> FilterExposedItems(IEnumerable<LibraryItem> items)
    {
        return items
            .Where(item => item != null
                && item.IsEnabled
                && !string.IsNullOrWhiteSpace(item.Id)
                && (item.Type == LibraryItemType.Game || item.Type == LibraryItemType.App)
                && LibraryItemFilter.IsSupportedLibraryItem(item))
            .ToList();
    }
}
