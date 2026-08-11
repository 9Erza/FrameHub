using FrameHub.Companion.Models;

namespace FrameHub.Companion.Providers;

public interface ITelemetrySnapshotProvider
{
    CompanionTelemetrySnapshot CurrentSnapshot { get; }
}
