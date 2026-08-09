namespace FrameHub.Core.Models.Benchmarking;

public sealed class BenchmarkHistoryEntry
{
    public required string SessionDirectory { get; init; }
    public required BenchmarkSessionMetadata Metadata { get; init; }
    public BenchmarkSummary? Summary { get; init; }
    public string? ReadError { get; init; }
}

public sealed class BenchmarkHistoryResult
{
    public IReadOnlyList<BenchmarkHistoryEntry> Sessions { get; init; } = Array.Empty<BenchmarkHistoryEntry>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public enum BenchmarkMetricDirection
{
    Neutral,
    HigherIsBetter,
    LowerIsBetter
}

public sealed class BenchmarkComparisonMetric
{
    public required string Key { get; init; }
    public double? SessionA { get; init; }
    public double? SessionB { get; init; }
    public double? Delta { get; init; }
    public double? PercentageDelta { get; init; }
    public BenchmarkMetricDirection Direction { get; init; }
}

public readonly record struct BenchmarkChartPoint(double ElapsedSeconds, double FrameTimeMs);
