using System.Collections.Generic;

namespace FrameHub.Core.Models.Library;

public sealed class LibraryScanResult
{
    public List<LibraryItem> Items { get; } = new();
    public List<string> Warnings { get; } = new();
}
