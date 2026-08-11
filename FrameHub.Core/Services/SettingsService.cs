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
    public class SettingsService : IDisposable, IStartupConfigurationBackend
    {
        public enum WindowsStartupState { Disabled, Registry, ElevatedScheduledTask, Conflict, Broken }
        public sealed record WindowsStartupStatus(WindowsStartupState State, string Message);
        private readonly string _filePath;
        private readonly string _appName = "FrameHub";
        private readonly string _exePath = GetCurrentExecutablePath();
        private readonly ITaskSchedulerQuery _taskSchedulerQuery = new TaskSchedulerComQuery();
        private readonly ILogger _logger = LoggerService.Instance;
        private bool _disposed;

        public string SettingsFilePath => _filePath;

        public SettingsService(string? settingsFilePath = null)
        {
            _filePath = !string.IsNullOrWhiteSpace(settingsFilePath)
                ? settingsFilePath
                : AppPaths.GetUserDataFilePath("settings.json");
        }

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
            var task = ReadScheduledTaskStartup(new DesiredStartupConfiguration(true, StartupWindowMode.Normal, true));
            if (!task.ReadSucceeded) return new(WindowsStartupState.Broken, "Scheduled Task state could not be read safely.");
            if (registryCommand != null && task.Exists) return new(WindowsStartupState.Conflict, "Registry and scheduled task are both present.");
            if (registryCommand == null && !task.Exists) return new(WindowsStartupState.Disabled, "Autostart disabled.");
            if (registryCommand != null) return IsExpectedCommand(registryCommand) ? new(WindowsStartupState.Registry, "Autostart configured normally.") : new(WindowsStartupState.Broken, "Registry autostart command requires attention.");
            return task.IsElevated && task.IsExpectedExecutable ? new(WindowsStartupState.ElevatedScheduledTask, "Autostart configured with administrator privileges.") : new(WindowsStartupState.Broken, "Scheduled task requires attention.");
        }

        public StartupConfigurationEvaluation EvaluateStartupConfiguration(AppSettings settings)
        {
            var desired = new DesiredStartupConfiguration(settings.StartWithWindows, settings.StartupWindowMode, settings.StartupRunElevated);
            return StartupConfigurationPlanner.Evaluate(desired, GetActualStartupConfiguration(desired));
        }

        public ActualStartupConfiguration GetActualStartupConfiguration(DesiredStartupConfiguration desired)
        {
            var actual = new ActualStartupConfiguration(ReadRegistryStartup(desired), ReadScheduledTaskStartup(desired));
            _logger.Info($"Startup read. Registry: Succeeded={actual.Registry.ReadSucceeded}; Exists={actual.Registry.Exists}. Task: Succeeded={actual.Task.ReadSucceeded}; Exists={actual.Task.Exists}; TaskError={actual.Task.Error ?? "none"}.");
            return actual;
        }

        public Task<ActualStartupConfiguration> ReadActualAsync(DesiredStartupConfiguration desired, CancellationToken cancellationToken = default)
            => Task.Run(() => { cancellationToken.ThrowIfCancellationRequested(); return GetActualStartupConfiguration(desired); }, cancellationToken);

        public Task<StartupOperationResult> ExecuteAsync(StartupOperation operation, DesiredStartupConfiguration desired, CancellationToken cancellationToken = default)
            => Task.Run(() => ExecuteStartupOperation(operation, desired), cancellationToken);

        public static int RunStartupHelper(IReadOnlyList<string> args)
        {
            if (!StartupHelperCommand.TryParse(args, out var command) || command == null) return 64;
            using var service = new SettingsService();
            bool success = command.Action == StartupHelperAction.RemoveTask
                ? service.RemoveScheduledTask()
                : service.CreateAdvancedScheduledTask(new AppSettings { StartupWindowMode = command.Mode });
            service._logger.Info($"Startup helper {command.Action} completed: {success}.");
            return success ? 0 : 1;
        }

        private StartupOperationResult ExecuteStartupOperation(StartupOperation operation, DesiredStartupConfiguration desired)
        {
            try
            {
                var result = operation switch
                {
                    StartupOperation.RemoveRegistry => new(RemoveRegistryStartup()),
                    StartupOperation.CreateOrUpdateRegistry => new(CreateOrUpdateRegistryStartup(desired)),
                    StartupOperation.CreateOrUpdateScheduledTask => ExecuteTaskOperation(new StartupHelperCommand(StartupHelperAction.CreateTask, desired.WindowMode)),
                    StartupOperation.RemoveScheduledTask => ExecuteTaskRemoval(),
                    _ => new(false, Error: "Unsupported startup operation.")
                };
                _logger.Info($"Startup operation {operation}. Success={result.Success}; DesiredMode={desired.WindowMode}; Error={result.Error ?? "none"}.");
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error("Startup operation failed", ex);
                return new(false, Error: ex.Message);
            }
        }

        private bool RemoveRegistryStartup()
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
            key?.DeleteValue(_appName, throwOnMissingValue: false);
            return true;
        }

        private bool CreateOrUpdateRegistryStartup(DesiredStartupConfiguration desired)
        {
            if (string.IsNullOrWhiteSpace(_exePath) || !File.Exists(_exePath))
            {
                _logger.Error($"Startup Registry write rejected: current executable path is invalid ('{_exePath}').");
                return false;
            }
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true)
                ?? Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key == null) return false;
            string expected = $"{Quote(_exePath)} {desired.Arguments}".Trim();
            key.SetValue(_appName, expected, RegistryValueKind.String);
            string? readBack = key.GetValue(_appName) as string;
            bool verified = string.Equals(readBack, expected, StringComparison.Ordinal);
            if (!verified) _logger.Warn($"Startup Registry write verification failed. Expected='{expected}'; Actual='{readBack ?? "<null>"}'.");
            return verified;
        }

        private StartupOperationResult ExecuteTaskRemoval()
        {
            if (RemoveScheduledTask()) return new(true);
            return ExecuteTaskOperation(new StartupHelperCommand(StartupHelperAction.RemoveTask, StartupWindowMode.Normal));
        }

        private StartupOperationResult ExecuteTaskOperation(StartupHelperCommand command)
        {
            if (IsRunAsAdmin())
            {
                bool success = command.Action == StartupHelperAction.RemoveTask ? RemoveScheduledTask() : CreateAdvancedScheduledTask(new AppSettings { StartupWindowMode = command.Mode });
                return new(success, ElevationRequired: false, Error: success ? null : "Scheduled Task operation failed.");
            }
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = _exePath,
                    Arguments = command.ToArguments(),
                    UseShellExecute = true,
                    Verb = "runas"
                });
                if (process == null || !process.WaitForExit(30000)) return new(false, true, false, "Elevated startup helper did not complete.");
                return new(process.ExitCode == 0, true, false, process.ExitCode == 0 ? null : "Elevated startup helper failed.");
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return new(false, true, true, "Administrator approval was cancelled.");
            }
            catch (Exception ex) { return new(false, true, false, ex.Message); }
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
            return new ScheduledTaskStartupReader(_taskSchedulerQuery, _exePath).Read(desired);
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
            var desired = new DesiredStartupConfiguration(settings.StartWithWindows, settings.StartupWindowMode, settings.StartupRunElevated);
            var result = new StartupConfigurationExecutor(this).ApplyAsync(desired).GetAwaiter().GetResult();
            return new((WindowsStartupState)result.FinalEvaluation.State, result.Error ?? result.FinalEvaluation.State.ToString());
        }

        private static AppSettings SanitizeSettings(AppSettings settings)
        {
            // Monitoring is deliberately session-only and always starts disabled.
            settings.HardwareMonitorEnabled = false;
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


        private static string? NormalizePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try { return Path.GetFullPath(path.Trim()); } catch { return path.Trim(); }
        }

        private static bool PathsEqual(string? left, string? right) => left != null && right != null && string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

        private static string GetCurrentExecutablePath()
        {
            string? path = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            return NormalizePath(path) ?? string.Empty;
        }

        private bool IsExpectedCommand(string command) => command.StartsWith(Quote(_exePath), StringComparison.OrdinalIgnoreCase);
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
