namespace FrameHub.Core.Models.SessionOptimization;

public sealed class SessionProcessSnapshot
{
    public DateTime CapturedAtUtc { get; init; } = DateTime.UtcNow;
    public IReadOnlyList<SessionProcessSnapshotItem> Processes { get; init; } = Array.Empty<SessionProcessSnapshotItem>();
}
