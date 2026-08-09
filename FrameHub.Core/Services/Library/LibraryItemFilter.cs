using FrameHub.Core.Models.Library;

namespace FrameHub.Core.Services.Library;

/// <summary>Central conservative policy for items which belong in the game library.</summary>
public static class LibraryItemFilter
{
    // Steam's redistributable depot is represented by a normal appmanifest but is not a game.
    public static bool IsSupportedLibraryItem(LibraryItem? item)
    {
        if (item is null) return false;
        return !(item.Source == LibrarySource.Steam && string.Equals(item.AppId, "228980", StringComparison.OrdinalIgnoreCase));
    }
}
