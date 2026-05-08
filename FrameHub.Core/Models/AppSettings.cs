namespace FrameHub.Core.Models
{
    /// <summary>
    /// Represents persistent user configuration.
    /// </summary>
    public class AppSettings
    {
        public bool StartWithWindows { get; set; } = false;
        public bool StartMinimized { get; set; } = false;
        public bool RunAsAdministrator { get; set; } = false;

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

        /// <summary>
        /// User-selected folders scanned by the Library module. Launcher manifests are still detected separately.
        /// </summary>
        public List<string> CustomLibraryLocations { get; set; } = new();
    }
}
