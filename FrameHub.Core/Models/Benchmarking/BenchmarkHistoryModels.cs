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

/// <summary>
/// One advisory environment difference between two compared benchmark sessions.
/// Values are preformatted neutral strings; unavailable values never appear here.
/// </summary>
public sealed class BenchmarkEnvironmentDifference
{
    public required string Key { get; init; }
    public required string FirstValue { get; init; }
    public required string SecondValue { get; init; }
}

public readonly record struct BenchmarkChartPoint(double ElapsedSeconds, double FrameTimeMs);
