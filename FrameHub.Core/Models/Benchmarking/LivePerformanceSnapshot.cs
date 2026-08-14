namespace FrameHub.Core.Models.Benchmarking;

public sealed record LivePerformanceSnapshot(
    int ProcessId,
    string? LibraryItemId,
    string? SwapChainAddress,
    double? CurrentFps,
    double? CurrentFrametimeMs,
    double? OnePercentLowFps,
    double? PointOnePercentLowFps,
    int SampleCount,
    DateTimeOffset CapturedAtUtc
);
