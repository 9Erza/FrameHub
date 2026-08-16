using FrameHub.Companion.Models;

namespace FrameHub.Companion.Providers;

public sealed class NullCompanionHardwareMonitoringProvider : ICompanionHardwareMonitoringProvider
{
    public HardwareMonitoringStatusDto GetStatus() => new(Enabled: false, Active: false);
    public HardwareMonitoringStatusDto SetEnabled(bool enabled) => new(Enabled: false, Active: false);
}
