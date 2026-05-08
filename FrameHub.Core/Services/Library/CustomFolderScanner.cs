using FrameHub.Core.Models.Library;
using System;
using System.IO;

namespace FrameHub.Core.Services.Library;

public sealed class CustomFolderScanner
{
    public LibraryScanResult Scan(IEnumerable<string> folders)
    {
        var result = new LibraryScanResult();

        foreach (string folder in folders.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(folder))
            {
                result.Warnings.Add($"Custom folder not found: {folder}");
                continue;
            }

            foreach (string exe in ExecutableResolver.FindExecutableCandidates(folder, maxDepth: 4, limit: 200))
            {
                try
                {
                    result.Items.Add(new LibraryItem
                    {
                        DisplayName = Path.GetFileNameWithoutExtension(exe),
                        Source = LibrarySource.CustomFolder,
                        Type = LibraryItemType.Game,
                        InstallPath = Path.GetDirectoryName(exe),
                        ExecutablePath = exe,
                        ProcessName = ExecutableResolver.ProcessNameFromExecutable(exe),
                        IconPath = exe,
                        IsEnabled = true,
                        WatchProcess = true
                    });
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Executable skipped: {exe} ({ex.Message})");
                }
            }
        }

        return result;
    }
}
