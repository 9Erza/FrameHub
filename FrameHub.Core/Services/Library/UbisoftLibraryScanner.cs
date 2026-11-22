using FrameHub.Core.Models.Library;
using Microsoft.Win32;

namespace FrameHub.Core.Services.Library;

/// <summary>One installed Ubisoft Connect game: launcher install id, folder and display name.</summary>
public sealed record UbisoftInstallRecord(string InstallId, string? InstallDir, string? DisplayName);

/// <summary>
/// Read-only Ubisoft Connect discovery: install folders from
/// <c>HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs\&lt;id&gt;\InstallDir</c>, names from the
/// matching <c>Uplay Install &lt;id&gt;</c> uninstall entry. Ubisoft games are launched through
/// <c>uplay://launch/&lt;id&gt;/0</c> (see AppLibraryLaunchService), so the stored executable only
/// identifies the running game process.
/// </summary>
public sealed class UbisoftLibraryScanner
{
    private readonly Func<IReadOnlyList<UbisoftInstallRecord>> _readInstalls;

    public UbisoftLibraryScanner(Func<IReadOnlyList<UbisoftInstallRecord>>? readInstalls = null)
    {
        _readInstalls = readInstalls ?? ReadRegistry;
    }

    public LibraryScanResult Scan()
    {
        var result = new LibraryScanResult();
        IReadOnlyList<UbisoftInstallRecord> installs;
        try { installs = _readInstalls(); }
        catch (Exception ex)
        {
            result.Warnings.Add($"Ubisoft Connect registry could not be read ({ex.Message}).");
            return result;
        }

        foreach (UbisoftInstallRecord install in installs)
        {
            if (!uint.TryParse(install.InstallId, out _)) continue;
            if (string.IsNullOrWhiteSpace(install.InstallDir)) continue;
            string folder = install.InstallDir.Replace('/', '\\').TrimEnd('\\');
            if (!Directory.Exists(folder)) continue;
            string name = string.IsNullOrWhiteSpace(install.DisplayName) ? Path.GetFileName(folder) : install.DisplayName.Trim();

            string? exe = ExecutableResolver.FindBestExecutable(folder, name);
            if (exe is null) continue;

            var item = new LibraryItem
            {
                DisplayName = name,
                Source = LibrarySource.Ubisoft,
                Type = LibraryItemType.Game,
                AppId = install.InstallId,
                InstallPath = folder,
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

    private static IReadOnlyList<UbisoftInstallRecord> ReadRegistry()
    {
        var records = new List<UbisoftInstallRecord>();
        using RegistryKey? installs = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs");
        if (installs is null) return records;
        using RegistryKey? uninstall = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");

        foreach (string id in installs.GetSubKeyNames())
        {
            using RegistryKey? install = installs.OpenSubKey(id);
            string? dir = install?.GetValue("InstallDir") as string;
            string? name = null;
            using (RegistryKey? entry = uninstall?.OpenSubKey($"Uplay Install {id}"))
            {
                name = entry?.GetValue("DisplayName") as string;
            }

            records.Add(new UbisoftInstallRecord(id, dir, name));
        }

        return records;
    }
}
