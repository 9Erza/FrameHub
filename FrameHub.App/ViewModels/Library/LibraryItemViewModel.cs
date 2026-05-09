using FrameHub.App.Helpers;
using FrameHub.App.Services;
using FrameHub.Core.Models;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Services;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FrameHub.App.ViewModels.Library;

public sealed class LibraryItemViewModel : ViewModelBase
{
    private readonly LocalizationService _localization;
    private readonly Func<IReadOnlyList<ProcessProfile>> _profilesProvider;
    private bool _isRunning;
    private bool _isOptimized;

    public LibraryItem Item { get; }

    public string Id => Item.Id;
    public string DisplayName => Item.DisplayName;
    public string SourceText => Item.Source.ToString();
    public string TypeText => _localization.T($"Library.Type.{Item.Type}");
    public string ProcessName => string.IsNullOrWhiteSpace(Item.ProcessName) ? _localization.T("Library.NotConfigured") : Item.ProcessName!;
    public string ExecutablePath => string.IsNullOrWhiteSpace(Item.ExecutablePath) ? _localization.T("Library.ExeMissing") : Item.ExecutablePath!;
    public string InstallPath => string.IsNullOrWhiteSpace(Item.InstallPath) ? "—" : Item.InstallPath!;
    public string WatchText => Item.WatchProcess ? _localization.T("Library.WatcherOn") : _localization.T("Library.WatcherOff");
    public string ProfileText => HasLinkedProfile ? _localization.T("Library.ProfileLinked") : _localization.T("Library.ProfileMissing");
    public string StatusText => IsOptimized ? _localization.T("Library.Status.Optimized") : IsRunning ? _localization.T("Library.Status.Running") : _localization.T("Library.Status.NotRunning");
    public string SetupText => IsReady ? _localization.T("Library.Status.Ready") : _localization.T("Library.Status.NeedsSetup");

    public bool IsReady => !string.IsNullOrWhiteSpace(Item.ProcessName) || !string.IsNullOrWhiteSpace(Item.ExecutablePath);
    public bool HasLinkedProfile
    {
        get
        {
            var profiles = _profilesProvider();
            if (!string.IsNullOrWhiteSpace(Item.LinkedProfileId) && profiles.Any(p => p.Id.Equals(Item.LinkedProfileId, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (profiles.Any(p => p.LibraryItemId?.Equals(Item.Id, StringComparison.OrdinalIgnoreCase) == true))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(Item.ExecutablePath) && profiles.Any(p => !string.IsNullOrWhiteSpace(p.ExecutablePath) && p.ExecutablePath.Equals(Item.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            string itemProcessName = ProfileService.NormalizeProcessName(Item.ProcessName);
            return !string.IsNullOrWhiteSpace(itemProcessName)
                && profiles.Any(p => ProfileService.NormalizeProcessName(p.ProcessName).Equals(itemProcessName, StringComparison.OrdinalIgnoreCase));
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set => SetProperty(ref _isRunning, value);
    }

    public bool IsOptimized
    {
        get => _isOptimized;
        private set => SetProperty(ref _isOptimized, value);
    }

    public ImageSource? IconSource
    {
        get
        {
            try
            {
                string? path = !string.IsNullOrWhiteSpace(Item.IconPath) ? Item.IconPath : Item.ExecutablePath;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon == null) return null;

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

    public LibraryItemViewModel(LibraryItem item, LocalizationService localization, Func<IReadOnlyList<ProcessProfile>> profilesProvider)
    {
        Item = item;
        _localization = localization;
        _profilesProvider = profilesProvider;
        RefreshRuntimeState();
    }

    public void RefreshRuntimeState()
    {
        bool running = false;
        if (!string.IsNullOrWhiteSpace(Item.ProcessName))
        {
            try
            {
                var processes = Process.GetProcessesByName(ProfileService.NormalizeProcessName(Item.ProcessName));
                running = processes.Length > 0;
                foreach (var process in processes) process.Dispose();
            }
            catch
            {
                running = false;
            }
        }

        IsRunning = running;
        IsOptimized = running && HasLinkedProfile;
        if (running) Item.LastSeenRunningAt = DateTime.UtcNow;

        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(SetupText));
        OnPropertyChanged(nameof(ProfileText));
        OnPropertyChanged(nameof(WatchText));
    }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(TypeText));
        OnPropertyChanged(nameof(ProcessName));
        OnPropertyChanged(nameof(ExecutablePath));
        OnPropertyChanged(nameof(WatchText));
        OnPropertyChanged(nameof(ProfileText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(SetupText));
    }
}
