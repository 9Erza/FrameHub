namespace FrameHub.Companion.Models;

public sealed record CompanionLibraryItemDto
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public bool IsRunning { get; init; }
    public bool HasIcon { get; init; }
    public bool IsExecutableMissing { get; init; }
}

public sealed record CompanionLaunchResultDto
{
    public bool Success { get; init; }
    public string ErrorCode { get; init; } = string.Empty;
}

public sealed record CompanionLibraryIconResult
{
    public byte[] Bytes { get; init; } = Array.Empty<byte>();
    public string ContentType { get; init; } = "image/png";
}
