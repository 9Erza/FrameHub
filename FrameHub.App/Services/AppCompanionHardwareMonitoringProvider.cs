using FrameHub.Companion.Models;
using FrameHub.Companion.Providers;

namespace FrameHub.App.Services;

public sealed class AppCompanionHardwareMonitoringProvider : ICompanionHardwareMonitoringProvider
{
    private readonly AppRuntimeService _runtime;

    public AppCompanionHardwareMonitoringProvider(AppRuntimeService runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public HardwareMonitoringStatusDto GetStatus()
    {
        return new HardwareMonitoringStatusDto(
            Enabled: _runtime.Settings.HardwareMonitorEnabled,
            Active: _runtime.IsHardwareMonitoringActive
        );
    }

    public HardwareMonitoringStatusDto SetEnabled(bool enabled)
    {
        _runtime.SetHardwareMonitorEnabled(enabled);
        return GetStatus();
    }
}
