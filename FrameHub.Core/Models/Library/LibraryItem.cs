using System;

namespace FrameHub.Core.Models.Library;

public sealed class LibraryItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = string.Empty;
    public LibrarySource Source { get; set; } = LibrarySource.Manual;
    public LibraryItemType Type { get; set; } = LibraryItemType.Game;

    public string? AppId { get; set; }
    public string? InstallPath { get; set; }
    public string? ExecutablePath { get; set; }
    public string? ProcessName { get; set; }
    public string? IconPath { get; set; }

    public bool IsEnabled { get; set; } = true;
    public bool WatchProcess { get; set; } = true;
    public string? LinkedProfileId { get; set; }

    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSeenRunningAt { get; set; }
}
