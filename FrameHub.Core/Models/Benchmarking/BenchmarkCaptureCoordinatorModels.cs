namespace FrameHub.Core.Models.Benchmarking;

public enum CoordinatorState
{
    Idle,
    Waiting,
    Capturing,
    Stopping,
    Completing,
    Completed,
    Cancelled,
    Failed
}

public enum CoordinatorStatus
{
    Completed,
    Cancelled,
    Failed,
    AlreadyRunning
}

public sealed record BenchmarkCaptureRequest
{
    public required BenchmarkTarget Target { get; init; }
    public required BenchmarkProcessIdentity Process { get; init; }
    public required string AppVersion { get; init; }
    public string? ProfileId { get; init; }
    public string? ProfileName { get; init; }
    public bool? SessionOptimizationActive { get; init; }
    public required int DurationSeconds { get; init; }
    public int CountdownSeconds { get; init; }
}

public sealed record BenchmarkCaptureStateSnapshot
{
    public required CoordinatorState State { get; init; }
    public required bool IsActive { get; init; }
    public int RemainingCountdownSeconds { get; init; }
    public string? TargetDisplayName { get; init; }
    public DateTimeOffset? CaptureStartedAtUtc { get; init; }
    public string? ErrorCode { get; init; }
}

public sealed record BenchmarkCaptureOutcome
{
    public required CoordinatorStatus Status { get; init; }
    public BenchmarkCaptureResult? Result { get; init; }
    public string? ErrorCode { get; init; }
    public string? TechnicalDetail { get; init; }
}
