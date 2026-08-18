using System.Diagnostics;
using System.IO;
using FrameHub.Core.Logging;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Services.Library;

namespace FrameHub.App.Services;

public sealed record LibraryLaunchResult
{
    public bool Success { get; init; }
    public string ErrorCode { get; init; } = string.Empty;

    public static LibraryLaunchResult Ok() => new() { Success = true, ErrorCode = "launched" };
    public static LibraryLaunchResult Fail(string errorCode) => new() { Success = false, ErrorCode = errorCode };
}

public interface IAppLibraryLaunchService
{
    LibraryLaunchResult Launch(LibraryItem? item);
}

public sealed class AppLibraryLaunchService : IAppLibraryLaunchService
{
    private readonly Func<ProcessStartInfo, bool> _processStarter;
    private readonly ILogger _logger;

    public AppLibraryLaunchService(Func<ProcessStartInfo, bool>? processStarter = null, ILogger? logger = null)
    {
        _processStarter = processStarter ?? DefaultProcessStarter;
        _logger = logger ?? LoggerService.Instance;
    }

    private static bool DefaultProcessStarter(ProcessStartInfo psi)
    {
        var process = Process.Start(psi);
        return process != null;
    }

    public LibraryLaunchResult Launch(LibraryItem? item)
    {
        if (item == null || !item.IsEnabled)
        {
            return LibraryLaunchResult.Fail("not_launchable");
        }

        if (!LibraryItemFilter.IsSupportedLibraryItem(item))
        {
            return LibraryLaunchResult.Fail("not_launchable");
        }

        bool isExistingLaunchType = item.Type == LibraryItemType.Game || item.Type == LibraryItemType.App;
        bool isOptedInBackgroundApp = item.Type == LibraryItemType.BackgroundApp && item.AllowRemoteControl;
        if (!isExistingLaunchType && !isOptedInBackgroundApp)
        {
            return LibraryLaunchResult.Fail("not_launchable");
        }

        if (string.IsNullOrWhiteSpace(item.ExecutablePath) && string.IsNullOrWhiteSpace(item.LaunchPath) && item.Source != LibrarySource.Steam)
        {
            return LibraryLaunchResult.Fail("not_launchable");
        }

        // Steam-sourced games must be launched through the official Steam protocol (steam://run/<AppID>)
        // so the Steam client initializes the game inside its secure VAC/overlay launcher context.
        if (item.Source == LibrarySource.Steam)
        {
            if (string.IsNullOrWhiteSpace(item.AppId) || !uint.TryParse(item.AppId.Trim(), out _))
            {
                _logger.Warn($"Steam library item '{item.DisplayName}' lacks a valid numeric Steam AppId ('{item.AppId}'). Direct executable launch is refused to preserve Steam launch context.");
                return LibraryLaunchResult.Fail("steam_appid_missing");
            }

            string steamUri = $"steam://run/{item.AppId.Trim()}";
            var steamStartInfo = new ProcessStartInfo
            {
                FileName = steamUri,
                UseShellExecute = true
            };

            try
            {
                bool steamStarted = _processStarter(steamStartInfo);
                if (!steamStarted)
                {
                    _logger.Warn($"Process launcher returned false for Steam item '{item.DisplayName}' ({steamUri}).");
                    return LibraryLaunchResult.Fail("launch_failed");
                }

                _logger.Info($"Successfully launched Steam library item '{item.DisplayName}' through Steam protocol '{steamUri}'.");
                return LibraryLaunchResult.Ok();
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to launch Steam library item '{item.DisplayName}' through Steam protocol '{steamUri}': {ex.Message}", ex);
                return LibraryLaunchResult.Fail("launch_failed");
            }
        }

        // Trusted shell launch entry (e.g. an official Riot-created Start Menu shortcut). The shortcut
        // itself is executed via the shell with no FrameHub-supplied arguments, equivalent to the user
        // double-clicking it; the stored target identity (e.g. ExecutablePath) stays observation-only.
        if (!string.IsNullOrWhiteSpace(item.LaunchPath))
        {
            string shortcutPath;
            try
            {
                shortcutPath = Path.GetFullPath(item.LaunchPath.Trim());
            }
            catch
            {
                return LibraryLaunchResult.Fail("not_launchable");
            }

            if (!string.Equals(Path.GetExtension(shortcutPath), ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                return LibraryLaunchResult.Fail("not_launchable");
            }

            if (!File.Exists(shortcutPath))
            {
                return LibraryLaunchResult.Fail("launch_target_missing");
            }

            var shortcutStartInfo = new ProcessStartInfo
            {
                FileName = shortcutPath,
                UseShellExecute = true
            };

            try
            {
                bool shortcutStarted = _processStarter(shortcutStartInfo);
                if (!shortcutStarted)
                {
                    _logger.Warn($"Process launcher returned false for shortcut item '{item.DisplayName}' ({shortcutPath}).");
                    return LibraryLaunchResult.Fail("launch_failed");
                }

                _logger.Info($"Successfully launched library item '{item.DisplayName}' through trusted shortcut '{shortcutPath}'.");
                return LibraryLaunchResult.Ok();
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to launch library item '{item.DisplayName}' through shortcut '{shortcutPath}': {ex.Message}", ex);
                return LibraryLaunchResult.Fail("launch_failed");
            }
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(item.ExecutablePath!.Trim());
        }
        catch
        {
            return LibraryLaunchResult.Fail("not_launchable");
        }

        if (!File.Exists(fullPath))
        {
            return LibraryLaunchResult.Fail("executable_missing");
        }

        string? workingDir;
        try
        {
            workingDir = Path.GetDirectoryName(fullPath);
        }
        catch
        {
            workingDir = null;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = fullPath,
            WorkingDirectory = workingDir ?? string.Empty,
            UseShellExecute = true
        };

        try
        {
            bool started = _processStarter(startInfo);
            if (!started)
            {
                _logger.Warn($"Process launcher returned false for item '{item.DisplayName}' ({fullPath}).");
                return LibraryLaunchResult.Fail("launch_failed");
            }

            _logger.Info($"Successfully launched library item '{item.DisplayName}' (PID/Target: {fullPath}).");
            return LibraryLaunchResult.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to launch library item '{item.DisplayName}' from path '{fullPath}': {ex.Message}", ex);
            return LibraryLaunchResult.Fail("launch_failed");
        }
    }
}
