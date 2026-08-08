using FrameHub.Core.Models;

namespace FrameHub.Core.Services;

/// <summary>Pure migration for legacy startup settings stored before schema version 2.</summary>
public static class StartupSettingsMigration
{
    public const int CurrentVersion = 2;

    public static void Apply(AppSettings settings)
    {
        if (settings.StartupSettingsVersion >= CurrentVersion) return;

        settings.StartupWindowMode = settings.LegacyStartMinimized == true
            ? StartupWindowMode.Minimized
            : StartupWindowMode.Normal;
        settings.StartupRunElevated = settings.LegacyRunAsAdministrator == true;
        settings.LegacyStartMinimized = null;
        settings.LegacyRunAsAdministrator = null;
        settings.StartupSettingsVersion = CurrentVersion;
    }
}
