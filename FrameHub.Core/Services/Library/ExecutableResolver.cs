using FrameHub.Core.Models.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FrameHub.Core.Services.Library;

public static class ExecutableResolver
{
    private static readonly string[] BadFileNameTokens =
    {
        "unins", "uninstall", "setup", "installer", "install", "crash", "reporter", "redist",
        "vcredist", "dxsetup", "benchmark", "config", "settings", "launcherhelper", "bootstrap"
    };

    private static readonly string[] BadPathTokens =
    {
        "_commonredist", "redist", "redistributable", "directx", "vcredist", "support", "installer",
        "crashreport", "crashreporter", "tools", "extras", "docs"
    };

    public static string? FindBestExecutable(string? installPath, string displayName)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
        {
            return null;
        }

        try
        {
            var candidates = EnumerateExecutablesSafe(installPath, maxDepth: 5)
                .Select(path => new { Path = path, Score = ScoreExecutable(path, installPath, displayName) })
                .Where(x => x.Score > -1000)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Path.Length)
                .Take(20)
                .ToList();

            return candidates.FirstOrDefault()?.Path;
        }
        catch
        {
            return null;
        }
    }

    public static List<string> FindExecutableCandidates(string rootFolder, int maxDepth = 4, int limit = 250)
    {
        if (string.IsNullOrWhiteSpace(rootFolder) || !Directory.Exists(rootFolder))
        {
            return new List<string>();
        }

        return EnumerateExecutablesSafe(rootFolder, maxDepth)
            .Where(path => ScoreExecutable(path, rootFolder, Path.GetFileNameWithoutExtension(path)) > -1000)
            .Take(limit)
            .ToList();
    }

    public static string ProcessNameFromExecutable(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return string.Empty;
        return Path.GetFileNameWithoutExtension(executablePath.Trim()) ?? string.Empty;
    }

    public static LibraryItem CreateManualItemFromExecutable(string executablePath, LibraryItemType type = LibraryItemType.Game)
    {
        string fullPath = Path.GetFullPath(executablePath);
        string displayName = Path.GetFileNameWithoutExtension(fullPath);
        return new LibraryItem
        {
            DisplayName = displayName,
            Source = LibrarySource.Manual,
            Type = type,
            ExecutablePath = fullPath,
            ProcessName = ProcessNameFromExecutable(fullPath),
            InstallPath = Path.GetDirectoryName(fullPath),
            IconPath = fullPath,
            IsEnabled = true,
            WatchProcess = true
        };
    }

    private static IEnumerable<string> EnumerateExecutablesSafe(string rootFolder, int maxDepth)
    {
        var stack = new Stack<(string Path, int Depth)>();
        stack.Push((rootFolder, 0));

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            IEnumerable<string> files = Array.Empty<string>();
            IEnumerable<string> directories = Array.Empty<string>();

            try
            {
                files = Directory.EnumerateFiles(current.Path, "*.exe", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                // Skip folders that cannot be read.
            }

            foreach (var file in files)
            {
                if (!ShouldSkipPath(file)) yield return file;
            }

            if (current.Depth >= maxDepth) continue;

            try
            {
                directories = Directory.EnumerateDirectories(current.Path, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                // Skip folders that cannot be read.
            }

            foreach (var directory in directories)
            {
                if (!ShouldSkipPath(directory)) stack.Push((directory, current.Depth + 1));
            }
        }
    }

    private static int ScoreExecutable(string path, string installPath, string displayName)
    {
        string file = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        string full = path.ToLowerInvariant();
        string normalizedName = Normalize(displayName);
        string normalizedFile = Normalize(file);

        if (BadFileNameTokens.Any(file.Contains)) return -1000;
        if (BadPathTokens.Any(full.Contains)) return -1000;

        int score = 0;
        if (Path.GetDirectoryName(path)?.TrimEnd(Path.DirectorySeparatorChar).Equals(installPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) == true)
        {
            score += 35;
        }

        if (!string.IsNullOrWhiteSpace(normalizedName))
        {
            if (normalizedFile.Equals(normalizedName, StringComparison.OrdinalIgnoreCase)) score += 80;
            else if (normalizedFile.Contains(normalizedName, StringComparison.OrdinalIgnoreCase)) score += 45;
            else if (normalizedName.Contains(normalizedFile, StringComparison.OrdinalIgnoreCase)) score += 20;
        }

        if (full.Contains("win64") || full.Contains("x64") || full.Contains("binaries")) score += 12;
        if (file.Contains("launcher")) score -= 25;
        if (file.Length <= 2) score -= 30;

        return score;
    }

    private static string Normalize(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    private static bool ShouldSkipPath(string path)
    {
        string lower = path.ToLowerInvariant();
        return BadPathTokens.Any(lower.Contains);
    }
}
