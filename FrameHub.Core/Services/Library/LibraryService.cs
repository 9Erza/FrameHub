using FrameHub.Core.Logging;
using FrameHub.Core.Models.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FrameHub.Core.Services.Library;

public sealed class LibraryService
{
    private readonly string _filePath;
    private readonly ILogger _logger = LoggerService.Instance;

    public LibraryService(string? filePath = null)
    {
        _filePath = string.IsNullOrWhiteSpace(filePath)
            ? AppPaths.GetUserDataFilePath("library.json")
            : Path.GetFullPath(filePath);
    }

    public List<LibraryItem> LoadItems()
    {
        try
        {
            string? json = AtomicFileService.ReadAllTextWithBackup(_filePath);
            if (string.IsNullOrWhiteSpace(json)) return new List<LibraryItem>();
            var items = JsonSerializer.Deserialize<List<LibraryItem>>(json) ?? new List<LibraryItem>();
            return Sanitize(items);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to load library items: {ex.Message}");
            return new List<LibraryItem>();
        }
    }

    public void SaveItems(IEnumerable<LibraryItem> items)
    {
        try
        {
            var clean = Sanitize(items);
            string json = JsonSerializer.Serialize(clean, new JsonSerializerOptions { WriteIndented = true });
            AtomicFileService.WriteAllTextAtomic(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to save library items", ex);
        }
    }

    public List<LibraryItem> MergeItems(IEnumerable<LibraryItem> existingItems, IEnumerable<LibraryItem> newItems)
    {
        var merged = Sanitize(existingItems);

        foreach (var item in Sanitize(newItems))
        {
            var existing = merged.FirstOrDefault(x => IsSameItem(x, item));
            if (existing == null)
            {
                merged.Add(item);
                continue;
            }

            existing.DisplayName = string.IsNullOrWhiteSpace(existing.DisplayName) ? item.DisplayName : existing.DisplayName;
            existing.Source = existing.Source == LibrarySource.Manual ? existing.Source : item.Source;
            existing.Type = existing.Type;
            existing.AppId = string.IsNullOrWhiteSpace(existing.AppId) ? item.AppId : existing.AppId;
            existing.InstallPath = string.IsNullOrWhiteSpace(existing.InstallPath) ? item.InstallPath : existing.InstallPath;
            RepairStaleExecutable(existing, item);
            existing.ExecutablePath = string.IsNullOrWhiteSpace(existing.ExecutablePath) ? item.ExecutablePath : existing.ExecutablePath;
            existing.LaunchPath = string.IsNullOrWhiteSpace(existing.LaunchPath) ? item.LaunchPath : existing.LaunchPath;
            existing.ProcessName = string.IsNullOrWhiteSpace(existing.ProcessName) ? item.ProcessName : existing.ProcessName;
            existing.IconPath ??= item.IconPath;
            existing.AllowBenchmark = item.AllowBenchmark;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        return Sanitize(merged);
    }

    /// <summary>
    /// A rescan used to keep the stored executable forever, so a game that was moved, switched
    /// to a new binary name or reinstalled on another drive stayed "executable missing" and was
    /// never detected as running again. When the stored file no longer exists and the scan found
    /// one that does, the scanned path (and the process name and icon derived from the old path)
    /// are adopted. A stored path that still exists is never replaced.
    /// </summary>
    internal static void RepairStaleExecutable(LibraryItem existing, LibraryItem scanned)
    {
        if (string.IsNullOrWhiteSpace(existing.ExecutablePath) || string.IsNullOrWhiteSpace(scanned.ExecutablePath)) return;
        if (!File.Exists(scanned.ExecutablePath)) return;
        if (File.Exists(existing.ExecutablePath) && !IsShippingUpgrade(existing, scanned)) return;

        string oldPath = existing.ExecutablePath;
        string oldDerivedName = ProfileService.NormalizeProcessName(ExecutableResolver.ProcessNameFromExecutable(oldPath));
        existing.ExecutablePath = scanned.ExecutablePath;
        if (string.IsNullOrWhiteSpace(existing.ProcessName)
            || string.Equals(ProfileService.NormalizeProcessName(existing.ProcessName), oldDerivedName, StringComparison.OrdinalIgnoreCase))
        {
            existing.ProcessName = scanned.ProcessName;
        }

        if (string.IsNullOrWhiteSpace(existing.IconPath) || string.Equals(existing.IconPath, oldPath, StringComparison.OrdinalIgnoreCase))
        {
            existing.IconPath = scanned.IconPath ?? scanned.ExecutablePath;
        }

        if (!string.IsNullOrWhiteSpace(scanned.InstallPath) && (string.IsNullOrWhiteSpace(existing.InstallPath) || !Directory.Exists(existing.InstallPath)))
        {
            existing.InstallPath = scanned.InstallPath;
        }
    }

    /// <summary>
    /// Store-scanned items (launched through the store protocol) that were recorded with an
    /// Unreal bootstrap executable move to the "-Shipping" binary of the same install, which is
    /// the process that actually runs. Manually added items are never changed.
    /// </summary>
    private static bool IsShippingUpgrade(LibraryItem existing, LibraryItem scanned)
    {
        if (existing.Source is not (LibrarySource.Steam or LibrarySource.Epic) || existing.Source != scanned.Source) return false;
        static bool IsShipping(string path) => Path.GetFileNameWithoutExtension(path).EndsWith("-Shipping", StringComparison.OrdinalIgnoreCase);
        if (IsShipping(existing.ExecutablePath!) || !IsShipping(scanned.ExecutablePath!)) return false;
        string? installPath = TryNormalizePath(scanned.InstallPath);
        string? existingPath = TryNormalizePath(existing.ExecutablePath);
        string? scannedPath = TryNormalizePath(scanned.ExecutablePath);
        return installPath != null && existingPath != null && scannedPath != null
            && existingPath.StartsWith(installPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && scannedPath.StartsWith(installPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameItem(LibraryItem a, LibraryItem b)
    {
        if (!string.IsNullOrWhiteSpace(a.AppId) && !string.IsNullOrWhiteSpace(b.AppId) && a.Source == b.Source)
        {
            return a.AppId.Equals(b.AppId, StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(a.ExecutablePath) && !string.IsNullOrWhiteSpace(b.ExecutablePath))
        {
            string? aPath = TryNormalizePath(a.ExecutablePath);
            string? bPath = TryNormalizePath(b.ExecutablePath);
            if (aPath != null && bPath != null)
            {
                return aPath.Equals(bPath, StringComparison.OrdinalIgnoreCase);
            }
        }

        return a.DisplayName.Equals(b.DisplayName, StringComparison.OrdinalIgnoreCase) && a.Source == b.Source;
    }

    private static List<LibraryItem> Sanitize(IEnumerable<LibraryItem> items)
    {
        var result = new List<LibraryItem>();
        foreach (var item in items)
        {
            if (item == null) continue;
            if (IsTestArtifact(item)) continue;
            if (!LibraryItemFilter.IsSupportedLibraryItem(item)) continue;
            if (string.IsNullOrWhiteSpace(item.DisplayName) && string.IsNullOrWhiteSpace(item.ExecutablePath)) continue;

            item.Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id.Trim();
            item.DisplayName = string.IsNullOrWhiteSpace(item.DisplayName)
                ? Path.GetFileNameWithoutExtension(item.ExecutablePath) ?? "Unknown"
                : item.DisplayName.Trim();
            item.ProcessName = ProfileService.NormalizeProcessName(string.IsNullOrWhiteSpace(item.ProcessName)
                ? ExecutableResolver.ProcessNameFromExecutable(item.ExecutablePath)
                : item.ProcessName);
            item.ExecutablePath = string.IsNullOrWhiteSpace(item.ExecutablePath) ? null : item.ExecutablePath.Trim();
            item.LaunchPath = string.IsNullOrWhiteSpace(item.LaunchPath) ? null : item.LaunchPath.Trim();
            item.InstallPath = string.IsNullOrWhiteSpace(item.InstallPath) ? null : item.InstallPath.Trim();
            item.IconPath = string.IsNullOrWhiteSpace(item.IconPath) ? item.ExecutablePath : item.IconPath.Trim();
            if (item.Source == LibrarySource.Riot && RiotGameProcesses.IsSupportedGameProcessName(item.ProcessName))
            {
                item.AllowBenchmark = true;
            }
            item.UpdatedAt = item.UpdatedAt == default ? DateTime.UtcNow : item.UpdatedAt;
            item.DetectedAt = item.DetectedAt == default ? DateTime.UtcNow : item.DetectedAt;
            result.Add(item);
        }

        return result
            .GroupBy(BuildIdentityKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.DisplayName)
            .ToList();
    }

    private static string BuildIdentityKey(LibraryItem item)
    {
        string? executablePath = TryNormalizePath(item.ExecutablePath);
        return executablePath != null
            ? $"exe:{executablePath}"
            : $"item:{item.Id}:{item.Source}:{item.AppId}:{item.DisplayName}";
    }

    private static bool IsTestArtifact(LibraryItem item)
    {
        if (string.IsNullOrWhiteSpace(item.ExecutablePath))
        {
            return false;
        }

        // 1. Existing executables are never treated as test artifacts
        if (File.Exists(item.ExecutablePath))
        {
            return false;
        }

        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(item.ExecutablePath.Trim());
        }
        catch
        {
            return false;
        }

        string tempRoot;
        try
        {
            tempRoot = Path.GetFullPath(Path.GetTempPath());
            if (!tempRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                && !tempRoot.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                tempRoot += Path.DirectorySeparatorChar;
            }
        }
        catch
        {
            tempRoot = string.Empty;
        }

        bool isUnderOsTemp = !string.IsNullOrEmpty(tempRoot)
            && normalizedPath.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase);

        bool hasTestDirectoryMarker = normalizedPath.Contains(@"\FrameHub.Tests\", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("/FrameHub.Tests/", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains(@"\FrameHub.LibraryRefreshTests\", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("/FrameHub.LibraryRefreshTests/", StringComparison.OrdinalIgnoreCase);

        string fileName = Path.GetFileName(normalizedPath);
        bool hasSyntheticTestExeName = fileName.Equals("desktop_test_game.exe", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("desktop_test_", StringComparison.OrdinalIgnoreCase);

        if (isUnderOsTemp && (hasTestDirectoryMarker || hasSyntheticTestExeName))
        {
            return true;
        }

        if (hasTestDirectoryMarker)
        {
            return true;
        }

        return false;
    }

    private static string? TryNormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return Path.GetFullPath(path.Trim()); }
        catch { return null; }
    }
}
