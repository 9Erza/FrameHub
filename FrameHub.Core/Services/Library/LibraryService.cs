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
    private readonly string _filePath = AppPaths.GetUserDataFilePath("library.json");
    private readonly ILogger _logger = LoggerService.Instance;

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
            existing.AppId ??= item.AppId;
            existing.InstallPath ??= item.InstallPath;
            existing.ExecutablePath ??= item.ExecutablePath;
            existing.ProcessName = string.IsNullOrWhiteSpace(existing.ProcessName) ? item.ProcessName : existing.ProcessName;
            existing.IconPath ??= item.IconPath;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        return Sanitize(merged);
    }

    private static bool IsSameItem(LibraryItem a, LibraryItem b)
    {
        if (!string.IsNullOrWhiteSpace(a.AppId) && !string.IsNullOrWhiteSpace(b.AppId) && a.Source == b.Source)
        {
            return a.AppId.Equals(b.AppId, StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(a.ExecutablePath) && !string.IsNullOrWhiteSpace(b.ExecutablePath))
        {
            return Path.GetFullPath(a.ExecutablePath).Equals(Path.GetFullPath(b.ExecutablePath), StringComparison.OrdinalIgnoreCase);
        }

        return a.DisplayName.Equals(b.DisplayName, StringComparison.OrdinalIgnoreCase) && a.Source == b.Source;
    }

    private static List<LibraryItem> Sanitize(IEnumerable<LibraryItem> items)
    {
        var result = new List<LibraryItem>();
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.DisplayName) && string.IsNullOrWhiteSpace(item.ExecutablePath)) continue;

            item.Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id.Trim();
            item.DisplayName = string.IsNullOrWhiteSpace(item.DisplayName)
                ? Path.GetFileNameWithoutExtension(item.ExecutablePath) ?? "Unknown"
                : item.DisplayName.Trim();
            item.ProcessName = string.IsNullOrWhiteSpace(item.ProcessName)
                ? ExecutableResolver.ProcessNameFromExecutable(item.ExecutablePath)
                : item.ProcessName.Trim();
            item.ExecutablePath = string.IsNullOrWhiteSpace(item.ExecutablePath) ? null : item.ExecutablePath.Trim();
            item.InstallPath = string.IsNullOrWhiteSpace(item.InstallPath) ? null : item.InstallPath.Trim();
            item.IconPath = string.IsNullOrWhiteSpace(item.IconPath) ? item.ExecutablePath : item.IconPath.Trim();
            item.UpdatedAt = item.UpdatedAt == default ? DateTime.UtcNow : item.UpdatedAt;
            item.DetectedAt = item.DetectedAt == default ? DateTime.UtcNow : item.DetectedAt;
            result.Add(item);
        }

        return result
            .GroupBy(x => !string.IsNullOrWhiteSpace(x.ExecutablePath) ? $"exe:{Path.GetFullPath(x.ExecutablePath)}" : $"{x.Source}:{x.AppId}:{x.DisplayName}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.DisplayName)
            .ToList();
    }
}
