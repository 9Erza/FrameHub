using System;
using System.Linq;

namespace FrameHub.Core.Models
{
    public sealed class AppStartupOptions
    {
        public bool StartMinimized { get; set; }
        public bool StartToTray { get; set; }
        public bool DisableUpdateCheck { get; set; }
        public bool SafeMode { get; set; }

        public static AppStartupOptions Parse(string[] args)
        {
            bool Has(string name) => args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

            return new AppStartupOptions
            {
                StartMinimized = Has("--minimized"),
                StartToTray = Has("--tray"),
                DisableUpdateCheck = Has("--no-update-check"),
                SafeMode = Has("--safe-mode")
            };
        }
    }
}
