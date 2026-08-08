using FrameHub.App.Helpers;
using FrameHub.App.Services;
using FrameHub.Core.Models;
using FrameHub.Core.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace FrameHub.App.ViewModels;

public sealed class ShellViewModel : ViewModelBase, IDisposable
{
    private readonly LocalizationService _localization;
    private readonly SettingsService _settingsService;
    private readonly AppRuntimeService _runtime;
    private object _currentViewModel;
    private string _currentTitle = string.Empty;
    private string _currentSubtitle = string.Empty;
    private string _currentKey = "Dashboard";
    private bool _disposed;

    private readonly DashboardViewModel _dashboardViewModel;
    private readonly LibraryViewModel _libraryViewModel;
    private readonly SessionOptimizationViewModel _sessionOptimizationViewModel;
    private readonly ProcessesViewModel _processesViewModel;
    private readonly ProfilesViewModel _profilesViewModel;
    private readonly HardwareViewModel _hardwareViewModel;
    private readonly LogsViewModel _logsViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly Dictionary<string, object> _views;

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }
    public ICommand NavigateCommand { get; }

    public AppRuntimeService Runtime => _runtime;

    public string AppName { get; } = "FrameHub";
    public string AppVersion { get; } = new AppInfo().Version;
    public string CoreFoundationStatus => _localization.T("Status.CoreMigrated");
    public string WatcherStatus => _runtime.IsProfileWatcherActive ? _localization.T("Status.WatcherActive") : _localization.T("Status.WatcherInactive");
    public string MinimizeTooltip => _localization.T("Window.Minimize");
    public string MaximizeRestoreTooltip => _localization.T("Window.MaximizeRestore");
    public string CloseTooltip => _localization.T("Window.Close");

    public object CurrentViewModel
    {
        get => _currentViewModel;
        private set => SetProperty(ref _currentViewModel, value);
    }

    public string CurrentTitle
    {
        get => _currentTitle;
        private set => SetProperty(ref _currentTitle, value);
    }

    public string CurrentSubtitle
    {
        get => _currentSubtitle;
        private set => SetProperty(ref _currentSubtitle, value);
    }

    public ShellViewModel()
    {
        _runtime = new AppRuntimeService();
        _settingsService = _runtime.SettingsService;
        _localization = new LocalizationService(_settingsService);
        _dashboardViewModel = new DashboardViewModel(_localization, _runtime);
        _libraryViewModel = new LibraryViewModel(_localization, _runtime);
        _sessionOptimizationViewModel = new SessionOptimizationViewModel(_localization, _runtime);
        _processesViewModel = new ProcessesViewModel(_localization, _runtime);
        _profilesViewModel = new ProfilesViewModel(_localization, _runtime);
        _hardwareViewModel = new HardwareViewModel(_localization, _runtime);
        _logsViewModel = new LogsViewModel(_localization, _runtime);
        _settingsViewModel = new SettingsViewModel(_localization, _runtime);

        _localization.LanguageChanged += (_, _) =>
        {
            _runtime.ReloadSettings();
            _settingsViewModel.ReloadFromRuntime();
            RefreshTexts();
        };
        _runtime.WatcherStateChanged += (_, _) => OnPropertyChanged(nameof(WatcherStatus));
        _runtime.ProfilesChanged += (_, _) =>
        {
            _dashboardViewModel.RefreshTexts();
            _libraryViewModel.RefreshTexts();
        };

        NavigationItems = new ObservableCollection<NavigationItemViewModel>
        {
            new(_localization) { Key = "Dashboard", TitleKey = "Nav.Dashboard", Icon = "\uE80F" },
            new(_localization) { Key = "Library", TitleKey = "Nav.Library", Icon = "\uE7FC" },
            new(_localization) { Key = "Session", TitleKey = "Nav.Session", Icon = "\uEC4A" },
            new(_localization) { Key = "Processes", TitleKey = "Nav.CoreControl", Icon = "\uE950" },
            new(_localization) { Key = "Profiles", TitleKey = "Nav.ProfilesRules", Icon = "\uE734" },
            new(_localization) { Key = "Hardware", TitleKey = "Nav.Hardware", Icon = "\uE9D9" },
            new(_localization) { Key = "Logs", TitleKey = "Nav.Logs", Icon = "\uE8A5" },
            new(_localization) { Key = "Settings", TitleKey = "Nav.Settings", Icon = "\uE713" }
        };

        _views = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["Dashboard"] = _dashboardViewModel,
            ["Library"] = _libraryViewModel,
            ["Session"] = _sessionOptimizationViewModel,
            ["Processes"] = _processesViewModel,
            ["Profiles"] = _profilesViewModel,
            ["Hardware"] = _hardwareViewModel,
            ["Settings"] = _settingsViewModel,
            ["Logs"] = _logsViewModel
        };

        NavigateCommand = new RelayCommand(parameter => Navigate(parameter?.ToString() ?? "Dashboard"));

        _currentViewModel = _dashboardViewModel;
        Navigate("Dashboard");
    }

    private void RefreshTexts()
    {
        foreach (var item in NavigationItems) item.RefreshTexts();

        _dashboardViewModel.RefreshTexts();
        _libraryViewModel.RefreshTexts();
        _sessionOptimizationViewModel.RefreshTexts();
        _processesViewModel.RefreshTexts();
        _profilesViewModel.RefreshTexts();
        _hardwareViewModel.RefreshTexts();
        _logsViewModel.RefreshTexts();
        _settingsViewModel.RefreshTexts();

        Navigate(_currentKey);

        OnPropertyChanged(nameof(CoreFoundationStatus));
        OnPropertyChanged(nameof(WatcherStatus));
        OnPropertyChanged(nameof(MinimizeTooltip));
        OnPropertyChanged(nameof(MaximizeRestoreTooltip));
        OnPropertyChanged(nameof(CloseTooltip));
    }


    private void Navigate(string key)
    {
        if (!_views.TryGetValue(key, out var viewModel))
        {
            key = "Dashboard";
            viewModel = _dashboardViewModel;
        }

        if (!_currentKey.Equals("Library", StringComparison.OrdinalIgnoreCase) && key.Equals("Library", StringComparison.OrdinalIgnoreCase))
        {
            _libraryViewModel.SelectedItem = null;
        }

        if (!_currentKey.Equals("Processes", StringComparison.OrdinalIgnoreCase) && key.Equals("Processes", StringComparison.OrdinalIgnoreCase))
        {
            _processesViewModel.Start();
        }
        else if (_currentKey.Equals("Processes", StringComparison.OrdinalIgnoreCase) && !key.Equals("Processes", StringComparison.OrdinalIgnoreCase))
        {
            _processesViewModel.Stop();
        }

        _currentKey = key;

        foreach (var item in NavigationItems)
        {
            item.IsSelected = item.Key.Equals(key, StringComparison.OrdinalIgnoreCase);
        }

        CurrentViewModel = viewModel;
        CurrentTitle = viewModel switch
        {
            DashboardViewModel dashboard => dashboard.Title,
            LibraryViewModel library => library.Title,
            SessionOptimizationViewModel session => session.Title,
            ProcessesViewModel processes => processes.Title,
            ProfilesViewModel profiles => profiles.Title,
            HardwareViewModel hardware => hardware.Title,
            SettingsViewModel settings => settings.Title,
            LogsViewModel logs => logs.Title,
            _ => key
        };
        CurrentSubtitle = viewModel switch
        {
            DashboardViewModel dashboard => dashboard.Subtitle,
            LibraryViewModel library => library.Subtitle,
            SessionOptimizationViewModel session => session.Subtitle,
            ProcessesViewModel processes => processes.Subtitle,
            ProfilesViewModel profiles => profiles.Subtitle,
            HardwareViewModel hardware => hardware.Subtitle,
            SettingsViewModel settings => settings.Subtitle,
            LogsViewModel logs => logs.Subtitle,
            _ => string.Empty
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sessionOptimizationViewModel.Dispose();
        _processesViewModel.Dispose();
        _hardwareViewModel.Dispose();
        _runtime.Dispose();
    }
}
