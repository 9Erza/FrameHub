using FrameHub.Core.Models.Benchmarking;

namespace FrameHub.Core.Services.Benchmarking;

/// <summary>Single deterministic implementation for all benchmark frame-time statistics.</summary>
public static class BenchmarkStatistics
{
    /// <summary>
    /// Calculates metrics from positive frame intervals in milliseconds. Average FPS is
    /// 1000 divided by arithmetic mean frame time. The 1% and 0.1% lows are 1000 divided
    /// by the arithmetic mean of the slowest ceiling(N*p) frame times (at least one).
    /// Percentiles use the nearest-rank definition: sorted[ceiling(p*N)-1].
    /// </summary>
    public static BenchmarkMetricSet Calculate(IEnumerable<double> frameTimesMs, string methodology)
    {
        double[] sorted = frameTimesMs
            .Where(value => value > 0 && double.IsFinite(value))
            .OrderBy(value => value)
            .ToArray();

        if (sorted.Length == 0)
        {
            return new BenchmarkMetricSet { Methodology = methodology };
        }

        double totalMs = sorted.Sum();
        double meanMs = totalMs / sorted.Length;
        double medianMs = sorted.Length % 2 == 0
            ? (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2.0
            : sorted[sorted.Length / 2];

        return new BenchmarkMetricSet
        {
            Methodology = methodology,
            ValidFrameCount = sorted.Length,
            DurationSeconds = totalMs / 1000.0,
            AverageFps = 1000.0 / meanMs,
            MedianFps = 1000.0 / medianMs,
            MedianFrameTimeMs = medianMs,
            OnePercentLowFps = LowFps(sorted, 0.01),
            PointOnePercentLowFps = LowFps(sorted, 0.001),
            P95FrameTimeMs = NearestRank(sorted, 0.95),
            P99FrameTimeMs = NearestRank(sorted, 0.99),
            MinimumFrameTimeMs = sorted[0],
            MaximumFrameTimeMs = sorted[^1]
        };
    }

    private static double LowFps(IReadOnlyList<double> ascendingFrameTimes, double fraction)
    {
        int count = Math.Max(1, (int)Math.Ceiling(ascendingFrameTimes.Count * fraction));
        double slowestMeanMs = ascendingFrameTimes.Skip(ascendingFrameTimes.Count - count).Average();
        return 1000.0 / slowestMeanMs;
    }

    private static double NearestRank(IReadOnlyList<double> ascendingValues, double percentile)
    {
        int rank = Math.Clamp((int)Math.Ceiling(percentile * ascendingValues.Count), 1, ascendingValues.Count);
        return ascendingValues[rank - 1];
    }
}
