using FrameHub.Companion.Models;

namespace FrameHub.Companion.Providers;

public sealed class NullTelemetrySnapshotProvider : ITelemetrySnapshotProvider
{
    public CompanionTelemetrySnapshot CurrentSnapshot { get; } = new(
        CapturedAtUtc: DateTimeOffset.UtcNow,
        Hardware: null,
        CurrentGame: null
    );
}
