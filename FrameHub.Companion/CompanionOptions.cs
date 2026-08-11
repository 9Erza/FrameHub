namespace FrameHub.Companion;

public sealed record CompanionOptions
{
    public bool Enabled { get; init; } = false;
    public int Port { get; init; } = 47821;
}
