using System.Globalization;
using FrameHub.Core.Models.Benchmarking;

namespace FrameHub.Core.Services.Benchmarking;

public static class BenchmarkComparisonService
{
    public const string EnvironmentKeyOs = "os";
    public const string EnvironmentKeyCpu = "cpu";
    public const string EnvironmentKeyGpu = "gpu";
    public const string EnvironmentKeyGpuDriver = "gpu_driver";
    public const string EnvironmentKeyMemory = "memory";
    public const string EnvironmentKeyDisplayResolution = "display_resolution";
    public const string EnvironmentKeyDisplayRefreshRate = "display_refresh_rate";
    public const string EnvironmentKeyFrameHubVersion = "framehub_version";
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

    /// <summary>
    /// Compares the best-effort environment metadata of two benchmark sessions.
    /// A field is only reported when both sessions recorded it and the values differ;
    /// unavailable values never fabricate a difference. Differences are advisory context
    /// only — they never block or invalidate a performance comparison.
    /// </summary>
    public static IReadOnlyList<BenchmarkEnvironmentDifference> CompareEnvironments(BenchmarkHistoryEntry first, BenchmarkHistoryEntry second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        BenchmarkEnvironmentSnapshot? a = first.Metadata.Environment;
        BenchmarkEnvironmentSnapshot? b = second.Metadata.Environment;
        var differences = new List<BenchmarkEnvironmentDifference>();

        AddStringDifference(differences, EnvironmentKeyOs,
            FormatOs(a), FormatOs(b),
            string.Equals(Normalize(a?.OsDescription), Normalize(b?.OsDescription), StringComparison.OrdinalIgnoreCase)
                && string.Equals(Normalize(a?.OsBuild), Normalize(b?.OsBuild), StringComparison.OrdinalIgnoreCase));
        AddStringDifference(differences, EnvironmentKeyCpu, Normalize(a?.CpuName), Normalize(b?.CpuName));
        AddStringDifference(differences, EnvironmentKeyGpu, Normalize(a?.GpuName), Normalize(b?.GpuName));
        AddStringDifference(differences, EnvironmentKeyGpuDriver, Normalize(a?.GpuDriverVersion), Normalize(b?.GpuDriverVersion));

        if (a?.TotalMemoryBytes is ulong firstMemory && b?.TotalMemoryBytes is ulong secondMemory && firstMemory != secondMemory)
        {
            differences.Add(new BenchmarkEnvironmentDifference
            {
                Key = EnvironmentKeyMemory,
                FirstValue = FormatGigabytes(firstMemory),
                SecondValue = FormatGigabytes(secondMemory)
            });
        }

        if (HasDisplaySize(a) && HasDisplaySize(b)
            && (a!.DisplayWidth != b!.DisplayWidth || a.DisplayHeight != b.DisplayHeight))
        {
            differences.Add(new BenchmarkEnvironmentDifference
            {
                Key = EnvironmentKeyDisplayResolution,
                FirstValue = FormatResolution(a),
                SecondValue = FormatResolution(b)
            });
        }

        if (a?.DisplayRefreshRateHz is int firstRefresh && firstRefresh > 0
            && b?.DisplayRefreshRateHz is int secondRefresh && secondRefresh > 0
            && firstRefresh != secondRefresh)
        {
            differences.Add(new BenchmarkEnvironmentDifference
            {
                Key = EnvironmentKeyDisplayRefreshRate,
                FirstValue = FormatRefreshRate(firstRefresh),
                SecondValue = FormatRefreshRate(secondRefresh)
            });
        }

        string firstVersion = Normalize(first.Metadata.FrameHubVersion) ?? string.Empty;
        string secondVersion = Normalize(second.Metadata.FrameHubVersion) ?? string.Empty;
        if (firstVersion.Length > 0 && secondVersion.Length > 0
            && !string.Equals(firstVersion, secondVersion, StringComparison.OrdinalIgnoreCase))
        {
            differences.Add(new BenchmarkEnvironmentDifference
            {
                Key = EnvironmentKeyFrameHubVersion,
                FirstValue = firstVersion,
                SecondValue = secondVersion
            });
        }

        return differences;
    }

    private static void AddStringDifference(List<BenchmarkEnvironmentDifference> differences, string key, string? first, string? second, bool? equal = null)
    {
        if (first is null || second is null) return;
        if (equal ?? string.Equals(first, second, StringComparison.OrdinalIgnoreCase)) return;
        differences.Add(new BenchmarkEnvironmentDifference { Key = key, FirstValue = first, SecondValue = second });
    }

    private static string? Normalize(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string? FormatOs(BenchmarkEnvironmentSnapshot? snapshot)
    {
        string? description = Normalize(snapshot?.OsDescription);
        string? build = Normalize(snapshot?.OsBuild);
        if (description is null && build is null) return null;
        if (description is null) return build;
        if (build is null) return description;
        return description.Contains(build, StringComparison.OrdinalIgnoreCase) ? description : $"{description} (build {build})";
    }

    private static bool HasDisplaySize(BenchmarkEnvironmentSnapshot? snapshot) =>
        snapshot?.DisplayWidth is > 0 && snapshot.DisplayHeight is > 0;

    private static string FormatResolution(BenchmarkEnvironmentSnapshot snapshot) =>
        $"{snapshot.DisplayWidth} × {snapshot.DisplayHeight}";

    private static string FormatRefreshRate(int refreshRateHz) =>
        $"{refreshRateHz.ToString(CultureInfo.InvariantCulture)} Hz";

    private static string FormatGigabytes(ulong bytes) =>
        (bytes / (1024.0 * 1024 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " GB";

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
