namespace FrameHub.App.ViewModels;

public sealed class MetricCardViewModel
{
    public required string Title { get; init; }
    public required string Value { get; init; }
    public required string Detail { get; init; }
    public string Accent { get; init; } = "#3B82F6";

    /// <summary>
    /// Shell navigation key opened when the card is activated. Empty disables navigation.
    /// </summary>
    public string TargetKey { get; init; } = string.Empty;
}
