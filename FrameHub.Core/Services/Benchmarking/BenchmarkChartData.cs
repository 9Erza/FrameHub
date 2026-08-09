using FrameHub.Core.Models.Benchmarking;

namespace FrameHub.Core.Services.Benchmarking;

public static class BenchmarkChartData
{
    public static IReadOnlyList<BenchmarkChartPoint> BuildPresentedSeries(
        IEnumerable<BenchmarkFrameSample> frames,
        int processId,
        string? swapChainAddress)
    {
        ArgumentNullException.ThrowIfNull(frames);
        var points = new List<BenchmarkChartPoint>();
        double elapsed = 0;
        foreach (BenchmarkFrameSample frame in frames)
        {
            if (frame.ProcessId != processId
                || (!string.IsNullOrWhiteSpace(swapChainAddress) && !string.Equals(frame.SwapChainAddress, swapChainAddress, StringComparison.OrdinalIgnoreCase))
                || frame.MsBetweenPresents is not double interval
                || interval <= 0
                || !double.IsFinite(interval))
            {
                continue;
            }

            elapsed += interval / 1000.0;
            points.Add(new BenchmarkChartPoint(elapsed, interval));
        }

        return points;
    }

    public static IReadOnlyList<BenchmarkChartPoint> DownsampleMinMax(IReadOnlyList<BenchmarkChartPoint> points, int bucketCount)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (bucketCount <= 0) throw new ArgumentOutOfRangeException(nameof(bucketCount));
        if (points.Count <= bucketCount * 2) return points.ToArray();

        var result = new List<BenchmarkChartPoint>(bucketCount * 2);
        for (int bucket = 0; bucket < bucketCount; bucket++)
        {
            int start = (int)((long)bucket * points.Count / bucketCount);
            int end = (int)((long)(bucket + 1) * points.Count / bucketCount);
            if (end <= start) continue;

            BenchmarkChartPoint minimum = points[start];
            BenchmarkChartPoint maximum = points[start];
            for (int index = start + 1; index < end; index++)
            {
                if (points[index].FrameTimeMs < minimum.FrameTimeMs) minimum = points[index];
                if (points[index].FrameTimeMs > maximum.FrameTimeMs) maximum = points[index];
            }

            if (minimum.ElapsedSeconds <= maximum.ElapsedSeconds)
            {
                result.Add(minimum);
                if (maximum != minimum) result.Add(maximum);
            }
            else
            {
                result.Add(maximum);
                if (maximum != minimum) result.Add(minimum);
            }
        }

        return result;
    }
}
