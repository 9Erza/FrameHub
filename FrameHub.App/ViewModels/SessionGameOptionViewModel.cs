using FrameHub.App.Helpers;
using FrameHub.Core.Models.Library;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FrameHub.App.ViewModels;

public sealed class SessionGameOptionViewModel : ViewModelBase
{
    private readonly Action _onChanged;
    private bool _autoEnabled;

    public LibraryItem Item { get; }
    public string Id => Item.Id;
    public string DisplayName => Item.DisplayName;
    public string ProcessName => string.IsNullOrWhiteSpace(Item.ProcessName) ? "—" : Item.ProcessName!;
    public bool HasProcessName => !string.IsNullOrWhiteSpace(Item.ProcessName);

    public ImageSource? IconSource
    {
        get
        {
            try
            {
                string? path = !string.IsNullOrWhiteSpace(Item.IconPath) ? Item.IconPath : Item.ExecutablePath;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return null;
                }

                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon == null)
                {
                    return null;
                }

                return System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(28, 28));
            }
            catch
            {
                return null;
            }
        }
    }

    public bool AutoEnabled
    {
        get => _autoEnabled;
        set
        {
            if (SetProperty(ref _autoEnabled, value))
            {
                _onChanged();
            }
        }
    }

    public SessionGameOptionViewModel(LibraryItem item, bool autoEnabled, Action onChanged)
    {
        Item = item;
        _autoEnabled = autoEnabled;
        _onChanged = onChanged;
    }

    public override string ToString() => DisplayName;
}
