namespace FrameHub.Core.Models
{
    public enum StartupWindowMode
    {
        Normal,
        Minimized,
        Tray
    }

    /// <summary>
    /// Represents persistent user configuration.
    /// </summary>
    public class AppSettings
    {
        public bool StartWithWindows { get; set; } = false;
        public StartupWindowMode StartupWindowMode { get; set; } = StartupWindowMode.Normal;
        public bool StartupRunElevated { get; set; } = false;
        public int StartupSettingsVersion { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("StartMinimized")]
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public bool? LegacyStartMinimized { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("RunAsAdministrator")]
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public bool? LegacyRunAsAdministrator { get; set; }

        public bool MinimizeToTray { get; set; } = true;
        public bool CloseToTray { get; set; } = true;
        public string Language { get; set; } = "en";

        /// <summary>
        /// RealTime priority can make Windows feel frozen. It is hidden unless the user explicitly enables it.
        /// </summary>
        public bool AllowRealtimePriority { get; set; } = false;

        public bool LogEnabled { get; set; } = true;
        public int LogLevelValue { get; set; } = 1; // Debug=0, Info=1, Warn=2, Error=3, Fatal=4
        public string LogFilePath { get; set; } = "FrameHub.log";
        public bool EnableConsoleOutput { get; set; } = false;
        public string LogSourceName { get; set; } = "FrameHub";

        public int ProcessListRefreshSeconds { get; set; } = 2;
        public int ProfileWatcherSeconds { get; set; } = 2;
        public int HardwareRefreshSeconds { get; set; } = 1;
        /// <summary>
        /// Hardware sensors are opt-in. The background profile watcher does not depend on this.
        /// </summary>
        public bool HardwareMonitorEnabled { get; set; } = false;
        public bool EnableStorageSensors { get; set; } = false;
        public bool CheckForUpdates { get; set; } = true;
        public string? Cs2SteamUserdataId { get; set; }
        public string? Cs2SteamUserdataPath { get; set; }

        /// <summary>
        /// User-selected folders scanned by the Library module. Launcher manifests are still detected separately.
        /// </summary>
        public List<string> CustomLibraryLocations { get; set; } = new();
    }
}
