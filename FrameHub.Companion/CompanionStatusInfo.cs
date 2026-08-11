namespace FrameHub.Companion;

public sealed record CompanionStatusInfo
{
    public CompanionServiceState State { get; init; } = CompanionServiceState.Stopped;
    public string? BoundAddress { get; init; }
    public int Port { get; init; } = 47821;
    public string? LastErrorMessage { get; init; }
}
