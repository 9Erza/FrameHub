namespace FrameHub.Companion.Models;

public sealed record CompanionSessionOptimizationStateDto
{
    public bool IsSessionActive { get; init; }
    public string? SessionId { get; init; }
    public string? GameId { get; init; }
    public string? GameDisplayName { get; init; }
    public DateTimeOffset? StartedAtUtc { get; init; }
    public int SuspendedProcessCount { get; init; }
    public bool TaskbarHidden { get; init; }
    public bool IsRecoveryPending { get; init; }
    public string Trigger { get; init; } = "Manual";
}

public sealed record CompanionOptimizationResultDto
{
    public bool Success { get; init; }
    public string ErrorCode { get; init; } = string.Empty;
    public int SuspendedProcessCount { get; init; }
}
