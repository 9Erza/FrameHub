using FrameHub.Companion.Models;
using FrameHub.Companion.Providers;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Services.Benchmarking;
using FrameHub.Core.Services.Library;
using System.Reflection;

namespace FrameHub.App.Services;

public sealed class AppBenchmarkProvider : ICompanionBenchmarkProvider
{
    private readonly AppRuntimeService _runtime;
    private readonly LibraryService _libraryService;
    private readonly BenchmarkStorageService _storageService;
    private readonly BenchmarkGameDetectionService _gameDetector;

    public AppBenchmarkProvider(
        AppRuntimeService runtime,
        LibraryService? libraryService = null,
        BenchmarkStorageService? storageService = null,
        BenchmarkGameDetectionService? gameDetector = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _libraryService = libraryService ?? new LibraryService();
        _storageService = storageService ?? new BenchmarkStorageService();
        _gameDetector = gameDetector ?? new BenchmarkGameDetectionService();
    }

    public CompanionBenchmarkStatusDto GetStatus()
    {
        BenchmarkCaptureStateSnapshot snapshot = _runtime.BenchmarkCoordinator.CurrentState;

        double? elapsed = null;
        if (snapshot.State == CoordinatorState.Capturing ||
            snapshot.State == CoordinatorState.Stopping ||
            snapshot.State == CoordinatorState.Completing)
        {
            if (snapshot.CaptureStartedAtUtc.HasValue)
            {
                elapsed = Math.Max(0, (DateTimeOffset.UtcNow - snapshot.CaptureStartedAtUtc.Value).TotalSeconds);
            }
        }

        return new CompanionBenchmarkStatusDto
        {
            State = snapshot.State.ToString(),
            IsActive = snapshot.IsActive,
            RemainingCountdownSeconds = snapshot.RemainingCountdownSeconds,
            TargetDisplayName = snapshot.TargetDisplayName,
            CaptureStartedAtUtc = snapshot.CaptureStartedAtUtc,
            ElapsedSeconds = elapsed,
            ErrorCode = snapshot.ErrorCode
        };
    }

    public IReadOnlyList<CompanionBenchmarkTargetDto> GetEligibleTargets()
    {
        List<LibraryItem> items = _libraryService.LoadItems();
        IReadOnlyList<BenchmarkRunningGame> runningGames = _gameDetector.Detect(items);

        return runningGames
            .Select(g => new CompanionBenchmarkTargetDto
            {
                TargetId = g.LibraryItem.Id,
                DisplayName = g.LibraryItem.DisplayName
            })
            .ToList();
    }

    public Task<CompanionBenchmarkStartResultDto> StartBenchmarkAsync(CompanionBenchmarkStartRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);

        List<LibraryItem> items = _libraryService.LoadItems();
        List<BenchmarkRunningGame> matches = _gameDetector.Detect(items)
            .Where(g => string.Equals(g.LibraryItem.Id, request.TargetId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
        {
            return Task.FromResult(new CompanionBenchmarkStartResultDto
            {
                Accepted = false,
                ErrorCode = "no_running_target"
            });
        }

        if (matches.Count > 1)
        {
            return Task.FromResult(new CompanionBenchmarkStartResultDto
            {
                Accepted = false,
                ErrorCode = "target_ambiguous"
            });
        }

        BenchmarkRunningGame game = matches[0];
        var profile = _runtime.Profiles.FirstOrDefault(p => string.Equals(p.Id, game.LibraryItem.LinkedProfileId, StringComparison.OrdinalIgnoreCase));
        bool optActive = !string.IsNullOrEmpty(_runtime.LastAppliedProfile) &&
                          string.Equals(_runtime.LastAppliedProfile, profile?.Id, StringComparison.OrdinalIgnoreCase);

        string appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.7.0";

        var captureRequest = new BenchmarkCaptureRequest
        {
            Target = game.Target,
            Process = game.Process,
            AppVersion = appVersion,
            ProfileId = profile?.Id,
            ProfileName = profile?.DisplayName,
            SessionOptimizationActive = optActive,
            DurationSeconds = request.DurationSeconds,
            CountdownSeconds = request.CountdownSeconds
        };

        BenchmarkCaptureStartHandle handle = _runtime.BenchmarkCoordinator.TryStartCapture(captureRequest);

        return Task.FromResult(new CompanionBenchmarkStartResultDto
        {
            Accepted = handle.Accepted,
            ErrorCode = handle.ErrorCode
        });
    }

    public async Task<CompanionBenchmarkStopResultDto> StopBenchmarkAsync()
    {
        bool wasActive = _runtime.BenchmarkCoordinator.IsActive;
        await _runtime.BenchmarkCoordinator.StopAsync().ConfigureAwait(false);
        return new CompanionBenchmarkStopResultDto
        {
            Success = true,
            WasActive = wasActive
        };
    }

    public CompanionBenchmarkHistoryListDto GetHistory(int limit)
    {
        int boundedLimit = Math.Clamp(limit, 1, 100);
        BenchmarkHistoryResult result = _storageService.EnumerateSessions();

        var sessions = result.Sessions
            .OrderByDescending(s => s.Metadata.StartUtc)
            .Take(boundedLimit)
            .Select(entry => new CompanionBenchmarkHistorySummaryDto
            {
                SessionId = entry.Metadata.SessionId,
                GameDisplayName = entry.Metadata.Game.DisplayName,
                CapturedAtUtc = entry.Metadata.StartUtc,
                Status = entry.Metadata.Status.ToString(),
                DurationSeconds = entry.Metadata.CaptureDurationSeconds,
                AverageFps = entry.Summary?.PrimaryPresentedMetrics?.AverageFps
            })
            .ToList();

        return new CompanionBenchmarkHistoryListDto
        {
            Sessions = sessions,
            TotalCount = result.Sessions.Count
        };
    }

    public CompanionBenchmarkHistoryDetailDto? GetHistoryDetail(Guid sessionId)
    {
        BenchmarkHistoryResult result = _storageService.EnumerateSessions();
        BenchmarkHistoryEntry? entry = result.Sessions.FirstOrDefault(s => s.Metadata.SessionId == sessionId);

        if (entry == null)
        {
            return null;
        }

        BenchmarkSummary? summary = entry.Summary;

        return new CompanionBenchmarkHistoryDetailDto
        {
            SessionId = entry.Metadata.SessionId,
            GameDisplayName = entry.Metadata.Game.DisplayName,
            CapturedAtUtc = entry.Metadata.StartUtc,
            Status = entry.Metadata.Status.ToString(),
            DurationSeconds = entry.Metadata.CaptureDurationSeconds,
            AverageFps = summary?.PrimaryPresentedMetrics?.AverageFps,
            OnePercentLowFps = summary?.PrimaryPresentedMetrics?.OnePercentLowFps,
            PointOnePercentLowFps = summary?.PrimaryPresentedMetrics?.PointOnePercentLowFps,
            P99FrameTimeMs = summary?.PrimaryPresentedMetrics?.P99FrameTimeMs,
            ProfileName = entry.Metadata.ActiveCpuProfileName,
            SessionOptimizationActive = entry.Metadata.SessionOptimizationActive,
            QualityLevel = summary?.Quality?.Level.ToString()
        };
    }

    public CompanionBenchmarkChartDto? GetHistoryChart(Guid sessionId, int buckets)
    {
        BenchmarkHistoryResult result = _storageService.EnumerateSessions();
        BenchmarkHistoryEntry? entry = result.Sessions.FirstOrDefault(s => s.Metadata.SessionId == sessionId);

        if (entry == null)
        {
            return null;
        }

        IReadOnlyList<BenchmarkFrameSample> rawFrames;
        try
        {
            rawFrames = _storageService.LoadRawFrames(entry.SessionDirectory);
        }
        catch (Exception)
        {
            return null;
        }

        IReadOnlyList<BenchmarkChartPoint> series = BenchmarkChartData.BuildPresentedSeries(
            rawFrames,
            entry.Metadata.Process.ProcessId,
            entry.Summary?.SelectedSwapChainAddress);

        int boundedBuckets = Math.Clamp(buckets, 10, 1000);
        IReadOnlyList<BenchmarkChartPoint> downsampled = BenchmarkChartData.DownsampleMinMax(series, boundedBuckets);

        var points = downsampled
            .Select(p => new CompanionBenchmarkChartPointDto
            {
                ElapsedSeconds = Math.Round(p.ElapsedSeconds, 3),
                FrameTimeMs = Math.Round(p.FrameTimeMs, 3)
            })
            .ToList();

        return new CompanionBenchmarkChartDto
        {
            SessionId = entry.Metadata.SessionId,
            Points = points,
            TotalPointCount = points.Count
        };
    }

    public CompanionBenchmarkComparisonDto CompareHistorySessions(Guid sessionAId, Guid sessionBId)
    {
        BenchmarkHistoryResult result = _storageService.EnumerateSessions();
        BenchmarkHistoryEntry? entryA = result.Sessions.FirstOrDefault(s => s.Metadata.SessionId == sessionAId);
        BenchmarkHistoryEntry? entryB = result.Sessions.FirstOrDefault(s => s.Metadata.SessionId == sessionBId);

        if (entryA == null || entryB == null)
        {
            throw new KeyNotFoundException("One or both benchmark sessions were not found.");
        }

        IReadOnlyList<BenchmarkComparisonMetric> comparisonMetrics = BenchmarkComparisonService.Compare(entryA, entryB);

        return new CompanionBenchmarkComparisonDto
        {
            SessionA = MapToSummaryDto(entryA),
            SessionB = MapToSummaryDto(entryB),
            Metrics = comparisonMetrics.Select(m => new CompanionBenchmarkComparisonMetricDto
            {
                Key = m.Key,
                SessionA = m.SessionA.HasValue ? Math.Round(m.SessionA.Value, 2) : null,
                SessionB = m.SessionB.HasValue ? Math.Round(m.SessionB.Value, 2) : null,
                Delta = m.Delta.HasValue ? Math.Round(m.Delta.Value, 2) : null,
                PercentageDelta = m.PercentageDelta.HasValue ? Math.Round(m.PercentageDelta.Value, 2) : null,
                Direction = m.Direction.ToString()
            }).ToList()
        };
    }

    private static CompanionBenchmarkHistorySummaryDto MapToSummaryDto(BenchmarkHistoryEntry entry)
    {
        return new CompanionBenchmarkHistorySummaryDto
        {
            SessionId = entry.Metadata.SessionId,
            GameDisplayName = entry.Metadata.Game.DisplayName,
            CapturedAtUtc = entry.Metadata.StartUtc,
            Status = entry.Metadata.Status.ToString(),
            DurationSeconds = entry.Metadata.CaptureDurationSeconds,
            AverageFps = entry.Summary?.PrimaryPresentedMetrics?.AverageFps
        };
    }
}
