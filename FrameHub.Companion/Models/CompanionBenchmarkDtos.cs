namespace FrameHub.Companion.Models;

public sealed record CompanionBenchmarkStatusDto
{
    public required string State { get; init; }
    public required bool IsActive { get; init; }
    public int RemainingCountdownSeconds { get; init; }
    public string? TargetDisplayName { get; init; }
    public DateTimeOffset? CaptureStartedAtUtc { get; init; }
    public double? ElapsedSeconds { get; init; }
    public string? ErrorCode { get; init; }
}

public sealed record CompanionBenchmarkTargetDto
{
    public required string TargetId { get; init; }
    public required string DisplayName { get; init; }
}

public sealed record CompanionBenchmarkStartRequestDto
{
    public required string TargetId { get; init; }
    public required int DurationSeconds { get; init; }
    public int CountdownSeconds { get; init; }
}

public sealed record CompanionBenchmarkStartResultDto
{
    public required bool Accepted { get; init; }
    public string? ErrorCode { get; init; }
}

public sealed record CompanionBenchmarkStopResultDto
{
    public required bool Success { get; init; }
    public required bool WasActive { get; init; }
}

public sealed record CompanionBenchmarkHistorySummaryDto
{
    public required Guid SessionId { get; init; }
    public required string GameDisplayName { get; init; }
    public required DateTime CapturedAtUtc { get; init; }
    public required string Status { get; init; }
    public double? DurationSeconds { get; init; }
    public double? AverageFps { get; init; }
}

public sealed record CompanionBenchmarkHistoryDetailDto
{
    public required Guid SessionId { get; init; }
    public required string GameDisplayName { get; init; }
    public required DateTime CapturedAtUtc { get; init; }
    public required string Status { get; init; }
    public double? DurationSeconds { get; init; }
    public double? AverageFps { get; init; }
    public double? OnePercentLowFps { get; init; }
    public double? PointOnePercentLowFps { get; init; }
    public double? P99FrameTimeMs { get; init; }
    public string? ProfileName { get; init; }
    public bool? SessionOptimizationActive { get; init; }
    public string? QualityLevel { get; init; }
}

public sealed record CompanionBenchmarkHistoryListDto
{
    public required IReadOnlyList<CompanionBenchmarkHistorySummaryDto> Sessions { get; init; }
    public required int TotalCount { get; init; }
}

public sealed record CompanionBenchmarkErrorDto
{
    public required string ErrorCode { get; init; }
    public required string Message { get; init; }
}

public sealed record CompanionBenchmarkChartPointDto
{
    public required double ElapsedSeconds { get; init; }
    public required double FrameTimeMs { get; init; }
}

public sealed record CompanionBenchmarkChartDto
{
    public required Guid SessionId { get; init; }
    public required IReadOnlyList<CompanionBenchmarkChartPointDto> Points { get; init; }
    public required int TotalPointCount { get; init; }
}

public sealed record CompanionBenchmarkComparisonMetricDto
{
    public required string Key { get; init; }
    public double? SessionA { get; init; }
    public double? SessionB { get; init; }
    public double? Delta { get; init; }
    public double? PercentageDelta { get; init; }
    public required string Direction { get; init; }
}

public sealed record CompanionBenchmarkComparisonDto
{
    public required CompanionBenchmarkHistorySummaryDto SessionA { get; init; }
    public required CompanionBenchmarkHistorySummaryDto SessionB { get; init; }
    public required IReadOnlyList<CompanionBenchmarkComparisonMetricDto> Metrics { get; init; }
}
