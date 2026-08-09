using FrameHub.Core.Models.Benchmarking;

namespace FrameHub.Core.Services.Benchmarking;

public static class BenchmarkComparisonService
{
    public static IReadOnlyList<BenchmarkComparisonMetric> Compare(BenchmarkHistoryEntry first, BenchmarkHistoryEntry second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (!IsSameGame(first.Metadata.Game, second.Metadata.Game))
        {
            throw new BenchmarkException("comparison_game_mismatch", "Only sessions for the same FrameHub library game can be compared.");
        }

        BenchmarkMetricSet? a = first.Summary?.PrimaryPresentedMetrics;
        BenchmarkMetricSet? b = second.Summary?.PrimaryPresentedMetrics;
        return
        [
            Metric("average_fps", a?.AverageFps, b?.AverageFps, BenchmarkMetricDirection.HigherIsBetter),
            Metric("median_fps", a?.MedianFps, b?.MedianFps, BenchmarkMetricDirection.HigherIsBetter),
            Metric("one_percent_low", a?.OnePercentLowFps, b?.OnePercentLowFps, BenchmarkMetricDirection.HigherIsBetter),
            Metric("point_one_percent_low", a?.PointOnePercentLowFps, b?.PointOnePercentLowFps, BenchmarkMetricDirection.HigherIsBetter),
            Metric("p95_frame_time", a?.P95FrameTimeMs, b?.P95FrameTimeMs, BenchmarkMetricDirection.LowerIsBetter),
            Metric("p99_frame_time", a?.P99FrameTimeMs, b?.P99FrameTimeMs, BenchmarkMetricDirection.LowerIsBetter),
            Metric("minimum_frame_time", a?.MinimumFrameTimeMs, b?.MinimumFrameTimeMs, BenchmarkMetricDirection.Neutral),
            Metric("maximum_frame_time", a?.MaximumFrameTimeMs, b?.MaximumFrameTimeMs, BenchmarkMetricDirection.LowerIsBetter),
            Metric("valid_frame_count", a?.ValidFrameCount, b?.ValidFrameCount, BenchmarkMetricDirection.Neutral),
            Metric("analyzed_duration", first.Summary?.AnalyzedDurationSeconds, second.Summary?.AnalyzedDurationSeconds, BenchmarkMetricDirection.Neutral),
            Metric("dropped_count", first.Summary?.DroppedOrNotDisplayedCount, second.Summary?.DroppedOrNotDisplayedCount, BenchmarkMetricDirection.LowerIsBetter)
        ];
    }

    public static double? CalculatePercentageDelta(double? first, double? second)
    {
        if (!first.HasValue || !second.HasValue || !double.IsFinite(first.Value) || !double.IsFinite(second.Value) || first.Value == 0) return null;
        return (second.Value - first.Value) / Math.Abs(first.Value) * 100.0;
    }

    public static bool IsSameGame(BenchmarkTarget first, BenchmarkTarget second)
    {
        if (!string.IsNullOrWhiteSpace(first.LibraryItemId) && !string.IsNullOrWhiteSpace(second.LibraryItemId))
        {
            return first.LibraryItemId.Equals(second.LibraryItemId, StringComparison.OrdinalIgnoreCase);
        }

        return first.LibrarySource.Equals(second.LibrarySource, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(first.SourceId)
            && first.SourceId.Equals(second.SourceId, StringComparison.OrdinalIgnoreCase);
    }

    private static BenchmarkComparisonMetric Metric(string key, double? first, double? second, BenchmarkMetricDirection direction)
    {
        bool valid = first.HasValue && second.HasValue && double.IsFinite(first.Value) && double.IsFinite(second.Value);
        return new BenchmarkComparisonMetric
        {
            Key = key,
            SessionA = first,
            SessionB = second,
            Delta = valid ? second!.Value - first!.Value : null,
            PercentageDelta = CalculatePercentageDelta(first, second),
            Direction = direction
        };
    }
}
