using Microsoft.Win32;
using FrameHub.Core.Logging;
using FrameHub.Core.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace FrameHub.Core.Services
{
    /// <summary>
    /// Handles settings persistence, Windows startup entries and elevation checks.
    /// </summary>
    public class SettingsService : IDisposable
    {
        public enum WindowsStartupState { Disabled, Registry, ElevatedScheduledTask, Conflict, Broken }
        public sealed record WindowsStartupStatus(WindowsStartupState State, string Message);
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
                settings = SanitizeSettings(settings);
                StartupSettingsMigration.Apply(settings);
                return settings;
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

        }

        public WindowsStartupStatus GetWindowsStartupStatus()
        {
            string? registryCommand = GetRegistryCommand();
            var task = QueryScheduledTask();
            if (registryCommand != null && task.Exists) return new(WindowsStartupState.Conflict, "Registry and scheduled task are both present.");
            if (registryCommand == null && !task.Exists) return new(WindowsStartupState.Disabled, "Autostart disabled.");
            if (registryCommand != null) return IsExpectedCommand(registryCommand) ? new(WindowsStartupState.Registry, "Autostart configured normally.") : new(WindowsStartupState.Broken, "Registry autostart command requires attention.");
            return task.IsElevated && IsExpectedTask(task) ? new(WindowsStartupState.ElevatedScheduledTask, "Autostart configured with administrator privileges.") : new(WindowsStartupState.Broken, "Scheduled task requires attention.");
        }

        public StartupConfigurationEvaluation EvaluateStartupConfiguration(AppSettings settings)
        {
            var desired = new DesiredStartupConfiguration(settings.StartWithWindows, settings.StartupWindowMode, settings.StartupRunElevated);
            return StartupConfigurationPlanner.Evaluate(desired, GetActualStartupConfiguration(desired));
        }

        public ActualStartupConfiguration GetActualStartupConfiguration(DesiredStartupConfiguration desired)
        {
            return new ActualStartupConfiguration(ReadRegistryStartup(desired), ReadScheduledTaskStartup(desired));
        }

        private RegistryStartupSnapshot ReadRegistryStartup(DesiredStartupConfiguration desired)
        {
            try
            {
                string? raw = GetRegistryCommand();
                if (raw == null) return new(false, null, string.Empty, null, false, false, false);
                var parsed = StartupCommandParser.Parse(raw);
                string? path = NormalizePath(parsed.ExecutablePath);
                bool expectedExe = PathsEqual(path, _exePath);
                return new(true, path, parsed.Arguments, raw, path != null && File.Exists(path), expectedExe, string.Equals(parsed.Arguments, desired.Arguments, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex) { return new(false, null, string.Empty, null, false, false, false, false, ex.Message); }
        }

        private ScheduledTaskStartupSnapshot ReadScheduledTaskStartup(DesiredStartupConfiguration desired)
        {
            var task = QueryScheduledTaskXml();
            if (!task.ReadSucceeded) return new(false, false, null, string.Empty, false, false, false, false, false, false, task.Error);
            if (!task.Exists) return new(false, false, null, string.Empty, false, false, false, false, false);
            string? path = NormalizePath(task.Command);
            return new(true, task.Enabled, path, task.Arguments ?? string.Empty, path != null && File.Exists(path), task.HasLogonTrigger, task.IsElevated, PathsEqual(path, _exePath), string.Equals(task.Arguments ?? string.Empty, desired.Arguments, StringComparison.OrdinalIgnoreCase));
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

        public WindowsStartupStatus ApplyWindowsStartup(AppSettings settings)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true)
                              ?? Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);

                if (!settings.StartWithWindows)
                {
                    key?.DeleteValue(_appName, throwOnMissingValue: false);
                    if (!RemoveScheduledTask() && QueryScheduledTask().Exists)
                        return new(WindowsStartupState.Broken, "Scheduled Task cleanup failed; autostart requires attention.");
                    return GetWindowsStartupStatus();
                }

                if (settings.StartupRunElevated)
                {
                    key?.DeleteValue(_appName, throwOnMissingValue: false);

                    if (!CreateAdvancedScheduledTask(settings)) return new(WindowsStartupState.Broken, "Elevated startup could not be configured. Run the repair action as administrator.");
                    return GetWindowsStartupStatus();
                }

                if (!RemoveScheduledTask() && QueryScheduledTask().Exists)
                    return new(WindowsStartupState.Broken, "Scheduled Task cleanup failed; autostart requires attention.");
                key?.SetValue(_appName, $"{Quote(_exePath)} {BuildStartupArguments(settings.StartupWindowMode)}".Trim());
                return GetWindowsStartupStatus();
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to apply Windows startup configuration", ex);
                return new(WindowsStartupState.Broken, ex.Message);
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

        private bool CreateAdvancedScheduledTask(AppSettings settings)
        {
            string tempXmlFile = Path.Combine(Path.GetTempPath(), $"{_appName}_{Guid.NewGuid():N}.xml");

            try
            {
                string arguments = BuildStartupArguments(settings.StartupWindowMode);
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
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                if (proc == null || !proc.WaitForExit(15000))
                {
                    _logger.Warn("schtasks.exe create timed out or could not start.");
                    return false;
                }
                string error = proc.StandardError.ReadToEnd();
                if (proc.ExitCode != 0) { _logger.Warn($"schtasks.exe create failed: {proc.ExitCode}; {error}"); return false; }
                return true;
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

        private bool RemoveScheduledTask()
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

                if (proc == null || !proc.WaitForExit(15000)) return false;
                return proc.ExitCode == 0 || proc.ExitCode == 1;
            }
            catch (Exception ex)
            {
                _logger.Debug($"Scheduled task cleanup skipped: {ex.Message}");
                return false;
            }
        }

        private string? GetRegistryCommand()
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
            return key?.GetValue(_appName) as string;
        }

        private (bool Exists, bool IsElevated, string? Command, string? Arguments) QueryScheduledTask()
        {
            try
            {
                using var proc = Process.Start(new ProcessStartInfo { FileName = "schtasks.exe", Arguments = $"/query /tn \"{_appName}\" /xml", UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true });
                if (proc == null || !proc.WaitForExit(10000) || proc.ExitCode != 0) return default;
                var xml = XDocument.Parse(proc.StandardOutput.ReadToEnd());
                XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
                return (true, string.Equals(xml.Descendants(ns + "RunLevel").FirstOrDefault()?.Value, "HighestAvailable", StringComparison.OrdinalIgnoreCase), xml.Descendants(ns + "Command").FirstOrDefault()?.Value, xml.Descendants(ns + "Arguments").FirstOrDefault()?.Value);
            }
            catch { return default; }
        }

        private (bool ReadSucceeded, bool Exists, bool Enabled, bool HasLogonTrigger, bool IsElevated, string? Command, string? Arguments, string? Error) QueryScheduledTaskXml()
        {
            try
            {
                using var proc = Process.Start(new ProcessStartInfo { FileName = "schtasks.exe", Arguments = $"/query /tn \"{_appName}\" /xml", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true });
                if (proc == null) return (false, false, false, false, false, null, null, "Could not start schtasks.exe.");
                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                var stderrTask = proc.StandardError.ReadToEndAsync();
                if (!proc.WaitForExit(10000)) return (false, false, false, false, false, null, null, "schtasks query timed out.");
                string stdout = stdoutTask.GetAwaiter().GetResult();
                string stderr = stderrTask.GetAwaiter().GetResult();
                if (proc.ExitCode != 0)
                {
                    bool missing = stderr.Contains("cannot find", StringComparison.OrdinalIgnoreCase) || stdout.Contains("cannot find", StringComparison.OrdinalIgnoreCase);
                    return missing ? (true, false, false, false, false, null, null, null) : (false, false, false, false, false, null, null, $"schtasks query failed: {proc.ExitCode}; {stderr}");
                }
                var xml = XDocument.Parse(stdout);
                XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
                bool enabled = !bool.TryParse(xml.Descendants(ns + "Settings").Elements(ns + "Enabled").FirstOrDefault()?.Value, out bool parsedEnabled) || parsedEnabled;
                bool logon = xml.Descendants(ns + "LogonTrigger").Any();
                bool elevated = string.Equals(xml.Descendants(ns + "RunLevel").FirstOrDefault()?.Value, "HighestAvailable", StringComparison.OrdinalIgnoreCase);
                return (true, true, enabled, logon, elevated, xml.Descendants(ns + "Command").FirstOrDefault()?.Value, xml.Descendants(ns + "Arguments").FirstOrDefault()?.Value, null);
            }
            catch (Exception ex) { return (false, false, false, false, false, null, null, ex.Message); }
        }

        private static string? NormalizePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try { return Path.GetFullPath(path.Trim()); } catch { return path.Trim(); }
        }

        private static bool PathsEqual(string? left, string? right) => left != null && right != null && string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

        private bool IsExpectedCommand(string command) => command.StartsWith(Quote(_exePath), StringComparison.OrdinalIgnoreCase);
        private bool IsExpectedTask((bool Exists, bool IsElevated, string? Command, string? Arguments) task) => string.Equals(task.Command, _exePath, StringComparison.OrdinalIgnoreCase);

        private static string Quote(string value) => $"\"{value}\"";

        private static string BuildStartupArguments(StartupWindowMode mode)
        {
            return DesiredStartupConfiguration.GetArguments(mode);
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
