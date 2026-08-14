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
    private static readonly TimeSpan LaunchCooldown = TimeSpan.FromSeconds(3);

    private readonly LibraryService _libraryService;
    private readonly ProcessScannerService _processScanner;
    private readonly IBenchmarkCaptureCoordinator _benchmarkCoordinator;
    private readonly IAppLibraryLaunchService _launchService;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILogger _logger;

    private readonly SemaphoreSlim _launchGate = new(1, 1);
    private readonly object _cooldownLock = new();
    private readonly Dictionary<string, DateTimeOffset> _recentLaunches = new(StringComparer.OrdinalIgnoreCase);

    public AppLibraryProvider(
        AppRuntimeService runtime,
        IAppLibraryLaunchService? launchService = null,
        Func<DateTimeOffset>? clock = null,
        ILogger? logger = null)
        : this(
            runtime.ProcessScanner,
            runtime.BenchmarkCoordinator,
            launchService ?? new AppLibraryLaunchService(),
            new LibraryService(),
            clock,
            logger)
    {
    }

    public AppLibraryProvider(
        ProcessScannerService processScanner,
        IBenchmarkCaptureCoordinator benchmarkCoordinator,
        IAppLibraryLaunchService launchService,
        LibraryService? libraryService = null,
        Func<DateTimeOffset>? clock = null,
        ILogger? logger = null)
    {
        _processScanner = processScanner ?? throw new ArgumentNullException(nameof(processScanner));
        _benchmarkCoordinator = benchmarkCoordinator ?? throw new ArgumentNullException(nameof(benchmarkCoordinator));
        _launchService = launchService ?? throw new ArgumentNullException(nameof(launchService));
        _libraryService = libraryService ?? new LibraryService();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
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
                dtos.Add(new CompanionLibraryItemDto
                {
                    Id = item.Id,
                    DisplayName = item.DisplayName,
                    Source = item.Source.ToString(),
                    Type = item.Type.ToString(),
                    IsRunning = runningIds.Contains(item.Id)
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
            DateTimeOffset now = _clock();
            lock (_cooldownLock)
            {
                if (_recentLaunches.TryGetValue(targetItem.Id, out var lastLaunchTime))
                {
                    if (now - lastLaunchTime < LaunchCooldown)
                    {
                        return new CompanionLaunchResultDto { Success = false, ErrorCode = "launch_in_progress" };
                    }
                }
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
                lock (_cooldownLock)
                {
                    _recentLaunches[targetItem.Id] = now;
                }

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
