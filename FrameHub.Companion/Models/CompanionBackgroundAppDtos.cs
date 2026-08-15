namespace FrameHub.Companion.Models;

public sealed record CompanionBackgroundAppDto
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsRunning { get; init; }
    public bool CanStart { get; init; }
    public bool CanStop { get; init; }
}

public sealed record CompanionBackgroundAppOperationDto
{
    public bool Success { get; init; }
    public string ErrorCode { get; init; } = string.Empty;
}
