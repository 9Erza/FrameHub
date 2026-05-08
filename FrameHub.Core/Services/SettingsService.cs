using Microsoft.Win32;
using FrameHub.Core.Logging;
using FrameHub.Core.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace FrameHub.Core.Services
{
    /// <summary>
    /// Handles settings persistence, Windows startup entries and elevation checks.
    /// </summary>
    public class SettingsService : IDisposable
    {
        private readonly string _filePath = AppPaths.GetUserDataFilePath("settings.json");
        private readonly string _appName = "FrameHub";
        private readonly string _exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        private readonly ILogger _logger = LoggerService.Instance;
        private bool _disposed;

        public AppSettings Load() => LoadSettings();

        public void Save(AppSettings settings) => SaveSettings(settings);

        public AppSettings LoadSettings()
        {
            AppPaths.MigrateLegacyFileIfNeeded("settings.json");

            if (!File.Exists(_filePath))
            {
                return new AppSettings();
            }

            try
            {
                string? jsonContent = AtomicFileService.ReadAllTextWithBackup(_filePath);
                if (string.IsNullOrWhiteSpace(jsonContent)) return new AppSettings();
                var settings = JsonSerializer.Deserialize<AppSettings>(jsonContent) ?? new AppSettings();
                return SanitizeSettings(settings);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to load settings. Defaults will be used. {ex.Message}");
                return new AppSettings();
            }
        }

        public void SaveSettings(AppSettings settings)
        {
            try
            {
                settings = SanitizeSettings(settings);
                string jsonContent = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                AtomicFileService.WriteAllTextAtomic(_filePath, jsonContent);
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to save settings", ex);
            }

            ApplyWindowsStartup(settings);
        }

        public bool IsRunAsAdmin()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        public void RestartAsAdmin()
        {
            if (IsRunAsAdmin() || string.IsNullOrWhiteSpace(_exePath)) return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _exePath,
                    UseShellExecute = true,
                    Verb = "runas"
                });

                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Administrator restart cancelled or failed: {ex.Message}");
            }
        }

        public void ApplyWindowsStartup(AppSettings settings)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true)
                              ?? Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);

                if (!settings.StartWithWindows)
                {
                    key?.DeleteValue(_appName, throwOnMissingValue: false);
                    RemoveScheduledTask();
                    return;
                }

                if (settings.RunAsAdministrator)
                {
                    key?.DeleteValue(_appName, throwOnMissingValue: false);

                    if (!IsRunAsAdmin())
                    {
                        _logger.Warn("Elevated autostart requires administrator rights. Restart as administrator first.");
                        return;
                    }

                    CreateAdvancedScheduledTask(settings);
                    return;
                }

                RemoveScheduledTask();
                key?.SetValue(_appName, $"{Quote(_exePath)} {BuildStartupArguments(settings.StartMinimized, settings.MinimizeToTray)}".Trim());
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to apply Windows startup configuration", ex);
            }
        }

        private static AppSettings SanitizeSettings(AppSettings settings)
        {
            settings.Language = settings.Language == "pl" ? "pl" : "en";
            settings.LogLevelValue = Math.Clamp(settings.LogLevelValue, 0, 4);
            settings.LogFilePath = string.IsNullOrWhiteSpace(settings.LogFilePath)
                ? "FrameHub.log"
                : settings.LogFilePath.Trim();
            settings.LogSourceName = string.IsNullOrWhiteSpace(settings.LogSourceName)
                ? "FrameHub"
                : settings.LogSourceName.Trim();
            settings.ProcessListRefreshSeconds = Math.Clamp(settings.ProcessListRefreshSeconds, 1, 10);
            settings.ProfileWatcherSeconds = Math.Clamp(settings.ProfileWatcherSeconds, 1, 30);
            settings.HardwareRefreshSeconds = Math.Clamp(settings.HardwareRefreshSeconds, 1, 10);
            settings.CustomLibraryLocations = settings.CustomLibraryLocations
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return settings;
        }

        private void CreateAdvancedScheduledTask(AppSettings settings)
        {
            string tempXmlFile = Path.Combine(Path.GetTempPath(), $"{_appName}_{Guid.NewGuid():N}.xml");

            try
            {
                string arguments = BuildStartupArguments(settings.StartMinimized, settings.MinimizeToTray);
                string xmlConfig = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{EscapeXml(_exePath)}</Command>
      <Arguments>{EscapeXml(arguments)}</Arguments>
    </Exec>
  </Actions>
</Task>";

                File.WriteAllText(tempXmlFile, xmlConfig, Encoding.Unicode);

                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/create /tn \"{_appName}\" /xml \"{tempXmlFile}\" /f",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                proc?.WaitForExit();
                if (proc?.ExitCode != 0)
                {
                    _logger.Warn($"schtasks.exe returned exit code {proc?.ExitCode}");
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(tempXmlFile)) File.Delete(tempXmlFile);
                }
                catch (Exception ex)
                {
                    _logger.Debug($"Could not delete temporary task XML: {ex.Message}");
                }
            }
        }

        private void RemoveScheduledTask()
        {
            try
            {
                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/delete /tn \"{_appName}\" /f",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                proc?.WaitForExit();
            }
            catch (Exception ex)
            {
                _logger.Debug($"Scheduled task cleanup skipped: {ex.Message}");
            }
        }

        private static string Quote(string value) => $"\"{value}\"";

        private static string BuildStartupArguments(bool startMinimized, bool startToTray)
        {
            if (startToTray) return "--tray";
            return startMinimized ? "--minimized" : string.Empty;
        }

        private static string EscapeXml(string value)
        {
            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
