using FrameHub.Companion.Models;

namespace FrameHub.Companion.Providers;

public interface ICompanionHardwareMonitoringProvider
{
    HardwareMonitoringStatusDto GetStatus();
    HardwareMonitoringStatusDto SetEnabled(bool enabled);
}
