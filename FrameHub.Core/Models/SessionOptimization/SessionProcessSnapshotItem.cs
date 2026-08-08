namespace FrameHub.Core.Models.SessionOptimization;

public sealed class SessionProcessSnapshotItem
{
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string NormalizedProcessName { get; init; } = string.Empty;
    public string? ExecutablePath { get; init; }
    public DateTime ProcessStartTimeUtc { get; init; }
}
