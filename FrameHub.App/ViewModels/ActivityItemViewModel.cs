namespace FrameHub.App.ViewModels;

public sealed class ActivityItemViewModel
{
    public required string Time { get; init; }
    public required string Message { get; init; }
    public string Level { get; init; } = "Info";
}
