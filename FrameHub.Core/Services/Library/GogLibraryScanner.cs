using FrameHub.Core.Models.Library;
using Microsoft.Win32;

namespace FrameHub.Core.Services.Library;

/// <summary>One installed GOG game as GOG Galaxy / the GOG installers register it.</summary>
public sealed record GogInstallRecord(string GameId, string? Name, string? InstallPath, string? Executable, string? DependsOn);

/// <summary>
/// Read-only GOG discovery from <c>HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\&lt;id&gt;</c>
/// (<c>gameName</c>, <c>path</c>, <c>exe</c>, <c>dependsOn</c>). DLC entries (which depend on a
/// base game) are skipped. Launch stays a direct executable launch, which is how GOG games run.
/// </summary>
public sealed class GogLibraryScanner
{
    private readonly Func<IReadOnlyList<GogInstallRecord>> _readInstalls;

    public GogLibraryScanner(Func<IReadOnlyList<GogInstallRecord>>? readInstalls = null)
    {
        _readInstalls = readInstalls ?? ReadRegistry;
    }

    public LibraryScanResult Scan()
    {
        var result = new LibraryScanResult();
        IReadOnlyList<GogInstallRecord> installs;
        try { installs = _readInstalls(); }
        catch (Exception ex)
        {
            result.Warnings.Add($"GOG registry could not be read ({ex.Message}).");
            return result;
        }

        foreach (GogInstallRecord install in installs)
        {
            if (!string.IsNullOrWhiteSpace(install.DependsOn)) continue;
            if (string.IsNullOrWhiteSpace(install.InstallPath) || !Directory.Exists(install.InstallPath)) continue;
            string name = string.IsNullOrWhiteSpace(install.Name) ? Path.GetFileName(install.InstallPath.TrimEnd('\\', '/')) : install.Name.Trim();

            string? exe = !string.IsNullOrWhiteSpace(install.Executable) && File.Exists(install.Executable)
                ? install.Executable
                : ExecutableResolver.FindBestExecutable(install.InstallPath, name);
            if (exe is null) continue;

            var item = new LibraryItem
            {
                DisplayName = name,
                Source = LibrarySource.Gog,
                Type = LibraryItemType.Game,
                AppId = install.GameId,
                InstallPath = install.InstallPath,
                ExecutablePath = exe,
                ProcessName = ExecutableResolver.ProcessNameFromExecutable(exe),
                IconPath = exe,
                IsEnabled = true,
                WatchProcess = true
            };
            if (LibraryItemFilter.IsSupportedLibraryItem(item)) result.Items.Add(item);
        }

        return result;
    }

    private static IReadOnlyList<GogInstallRecord> ReadRegistry()
    {
        var records = new List<GogInstallRecord>();
        using RegistryKey? games = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games")
            ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\GOG.com\Games");
        if (games is null) return records;

        foreach (string id in games.GetSubKeyNames())
        {
            using RegistryKey? game = games.OpenSubKey(id);
            if (game is null) continue;
            records.Add(new GogInstallRecord(
                id,
                game.GetValue("gameName") as string,
                game.GetValue("path") as string,
                game.GetValue("exe") as string,
                game.GetValue("dependsOn") as string));
        }

        return records;
    }
}
