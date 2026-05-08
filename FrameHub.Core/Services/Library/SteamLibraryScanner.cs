using FrameHub.Core.Models.Library;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FrameHub.Core.Services.Library;

public sealed class SteamLibraryScanner
{
    private static readonly Regex KeyValueRegex = new("\"(?<key>[^\"]+)\"\\s+\"(?<value>[^\"]*)\"", RegexOptions.Compiled);

    public LibraryScanResult Scan()
    {
        var result = new LibraryScanResult();
        var libraries = FindSteamLibraries().Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (libraries.Count == 0)
        {
            result.Warnings.Add("Steam libraryfolders.vdf was not found.");
            return result;
        }

        foreach (string library in libraries)
        {
            string steamApps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamApps)) continue;

            foreach (string manifest in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var values = ParseValveKeyValues(File.ReadAllText(manifest));
                    values.TryGetValue("appid", out string? appId);
                    values.TryGetValue("name", out string? name);
                    values.TryGetValue("installdir", out string? installDir);

                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installDir)) continue;

                    string installPath = Path.Combine(steamApps, "common", installDir);
                    string? exe = ExecutableResolver.FindBestExecutable(installPath, name);

                    result.Items.Add(new LibraryItem
                    {
                        DisplayName = name,
                        Source = LibrarySource.Steam,
                        Type = LibraryItemType.Game,
                        AppId = appId,
                        InstallPath = Directory.Exists(installPath) ? installPath : null,
                        ExecutablePath = exe,
                        ProcessName = ExecutableResolver.ProcessNameFromExecutable(exe),
                        IconPath = exe,
                        IsEnabled = true,
                        WatchProcess = true
                    });
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Steam manifest skipped: {Path.GetFileName(manifest)} ({ex.Message})");
                }
            }
        }

        return result;
    }

    private static IEnumerable<string> FindSteamLibraries()
    {
        var steamRoots = new List<string>();

        TryAddRegistryValue(steamRoots, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
        TryAddRegistryValue(steamRoots, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        TryAddRegistryValue(steamRoots, Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");

        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86)) steamRoots.Add(Path.Combine(programFilesX86, "Steam"));

        foreach (string root in steamRoots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return root;

            string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;

            foreach (string library in ParseLibraryFolders(File.ReadAllText(vdf)))
            {
                if (Directory.Exists(library)) yield return library;
            }
        }
    }

    private static IEnumerable<string> ParseLibraryFolders(string content)
    {
        foreach (Match match in KeyValueRegex.Matches(content))
        {
            string key = match.Groups["key"].Value;
            string value = match.Groups["value"].Value.Replace("\\\\", "\\");
            if (key.Equals("path", StringComparison.OrdinalIgnoreCase) && Directory.Exists(value))
            {
                yield return value;
            }
        }
    }

    private static Dictionary<string, string> ParseValveKeyValues(string content)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in KeyValueRegex.Matches(content))
        {
            values[match.Groups["key"].Value] = match.Groups["value"].Value;
        }
        return values;
    }

    private static void TryAddRegistryValue(List<string> list, RegistryKey root, string path, string valueName)
    {
        try
        {
            using var key = root.OpenSubKey(path);
            string? value = key?.GetValue(valueName)?.ToString();
            if (!string.IsNullOrWhiteSpace(value)) list.Add(value);
        }
        catch
        {
            // Registry access can fail on restricted systems.
        }
    }
}
