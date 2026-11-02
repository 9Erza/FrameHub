using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FrameHub.App.Helpers;

/// <summary>
/// Bounded shared decode cache for per-game icons (audit 2026-11).
/// The Library/Session/Benchmark <c>IconSource</c> getters previously ran
/// <c>ExtractAssociatedIcon</c> + <c>CreateBitmapSourceFromHIcon</c> on every
/// binding evaluation, churning GDI handles and thread-affine
/// <c>BitmapSource</c> instances while scrolling virtualized lists.
/// Pure presentation helper: no domain logic, no timers, no I/O beyond one
/// icon read per distinct path. Capacity 96 entries with LRU eviction.
/// Cached images are frozen so they can be shared across view-models and
/// threads. Negative results (missing file) are NOT cached because
/// executable presence can change after install or repair.
/// </summary>
internal static class GameIconCache
{
    private const int Capacity = 96;
    private static readonly object _gate = new();
    private static readonly Dictionary<string, ImageSource> _entries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> _lru = new();

    internal static ImageSource? GetOrLoad(string? path, int size)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        string key = path + "|" + size;

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out ImageSource? cached))
            {
                _lru.Remove(key);
                _lru.AddFirst(key);
                return cached;
            }
        }

        ImageSource? loaded = Load(path, size);
        if (loaded is null) return null;

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out ImageSource? raced))
            {
                _lru.Remove(key);
                _lru.AddFirst(key);
                return raced;
            }

            _entries[key] = loaded;
            _lru.AddFirst(key);
            while (_entries.Count > Capacity && _lru.Last is not null)
            {
                string oldest = _lru.Last.Value;
                _lru.RemoveLast();
                _entries.Remove(oldest);
            }

            return loaded;
        }
    }

    private static ImageSource? Load(string path, int size)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(size, size));
            if (source.CanFreeze) source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }
}
