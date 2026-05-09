using FrameHub.App.Helpers;
using FrameHub.App.Services;
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
    private LanguageOptionViewModel? _selectedLanguage;
    private bool _disposed;

    private readonly DashboardViewModel _dashboardViewModel;
    private readonly LibraryViewModel _libraryViewModel;
    private readonly ProcessesViewModel _processesViewModel;
    private readonly ProfilesViewModel _profilesViewModel;
    private readonly HardwareViewModel _hardwareViewModel;
    private readonly LogsViewModel _logsViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly Dictionary<string, object> _views;

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }
    public ObservableCollection<LanguageOptionViewModel> LanguageOptions { get; }

    public ICommand NavigateCommand { get; }
    public ICommand SetLanguageCommand { get; }

    public AppRuntimeService Runtime => _runtime;

    public string AppName { get; } = "FrameHub";
    public string AppVersion { get; } = "0.3.1";
    public string AppTagline => _localization.T("App.Tagline");
    public string LanguageLabel => _localization.T("Language.Label");
    public string CoreFoundationStatus => _localization.T("Status.CoreMigrated");
    public string WatcherStatus => _runtime.IsProfileWatcherActive ? _localization.T("Status.WatcherActive") : _localization.T("Status.WatcherInactive");
    public string MinimizeTooltip => _localization.T("Window.Minimize");
    public string MaximizeRestoreTooltip => _localization.T("Window.MaximizeRestore");
    public string CloseTooltip => _localization.T("Window.Close");

    public LanguageOptionViewModel? SelectedLanguage
    {
        get => _selectedLanguage;
        private set => SetProperty(ref _selectedLanguage, value);
    }

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

        LanguageOptions = new ObservableCollection<LanguageOptionViewModel>
        {
            new("en", "Language.English", "pack://application:,,,/Assets/us_flag.png", _localization),
            new("pl", "Language.Polish", "pack://application:,,,/Assets/pl_flag.png", _localization)
        };

        NavigationItems = new ObservableCollection<NavigationItemViewModel>
        {
            new(_localization) { Key = "Dashboard", TitleKey = "Nav.Dashboard", Icon = "\uE80F" },
            new(_localization) { Key = "Library", TitleKey = "Nav.Library", Icon = "\uE7FC", BadgeKey = "Badge.Ready" },
            new(_localization) { Key = "Session", TitleKey = "Nav.Session", Icon = "\uEC4A", BadgeKey = "Badge.Planned" },
            new(_localization) { Key = "Processes", TitleKey = "Nav.CoreControl", Icon = "\uE950", BadgeKey = "Badge.Ready" },
            new(_localization) { Key = "Profiles", TitleKey = "Nav.ProfilesRules", Icon = "\uE734", BadgeKey = "Badge.Ready" },
            new(_localization) { Key = "Background", TitleKey = "Nav.Background", Icon = "\uE8EF", BadgeKey = "Badge.Planned" },
            new(_localization) { Key = "Toolkit", TitleKey = "Nav.Toolkit", Icon = "\uE90F", BadgeKey = "Badge.Planned" },
            new(_localization) { Key = "Hardware", TitleKey = "Nav.Hardware", Icon = "\uE9D9", BadgeKey = "Badge.Ready" },
            new(_localization) { Key = "Benchmark", TitleKey = "Nav.Benchmark", Icon = "\uE9D2", BadgeKey = "Badge.Planned" },
            new(_localization) { Key = "WindowsTuning", TitleKey = "Nav.WindowsTuning", Icon = "\uE75C", BadgeKey = "Badge.Preview", IsPlanned = true },
            new(_localization) { Key = "Settings", TitleKey = "Nav.Settings", Icon = "\uE713" },
            new(_localization) { Key = "Logs", TitleKey = "Nav.Logs", Icon = "\uE8A5" }
        };

        _views = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["Dashboard"] = _dashboardViewModel,
            ["Library"] = _libraryViewModel,
            ["Session"] = BuildPlaceholder("Module.Session", new[] { "Module.Session.Item1", "Module.Session.Item2", "Module.Session.Item3", "Module.Session.Item4", "Module.Session.Item5" }),
            ["Processes"] = _processesViewModel,
            ["Profiles"] = _profilesViewModel,
            ["Background"] = BuildPlaceholder("Module.Background", new[] { "Module.Background.Item1", "Module.Background.Item2", "Module.Background.Item3", "Module.Background.Item4", "Module.Background.Item5" }),
            ["Toolkit"] = BuildPlaceholder("Module.Toolkit", new[] { "Module.Toolkit.Item1", "Module.Toolkit.Item2", "Module.Toolkit.Item3", "Module.Toolkit.Item4", "Module.Toolkit.Item5", "Module.Toolkit.Item6" }),
            ["Hardware"] = _hardwareViewModel,
            ["Benchmark"] = BuildPlaceholder("Module.Benchmark", new[] { "Module.Benchmark.Item1", "Module.Benchmark.Item2", "Module.Benchmark.Item3", "Module.Benchmark.Item4" }),
            ["WindowsTuning"] = BuildPlaceholder("Module.WindowsTuning", new[] { "Module.WindowsTuning.Item1", "Module.WindowsTuning.Item2", "Module.WindowsTuning.Item3", "Module.WindowsTuning.Item4", "Module.WindowsTuning.Item5" }, true),
            ["Settings"] = _settingsViewModel,
            ["Logs"] = _logsViewModel
        };

        NavigateCommand = new RelayCommand(parameter => Navigate(parameter?.ToString() ?? "Dashboard"));
        SetLanguageCommand = new RelayCommand(parameter => SetLanguage(parameter?.ToString() ?? "en"));

        _currentViewModel = _dashboardViewModel;
        SelectCurrentLanguageOption();
        Navigate("Dashboard");
    }

    private PlaceholderViewModel BuildPlaceholder(string baseKey, IReadOnlyList<string> plannedItemKeys, bool isPlanned = false)
    {
        var placeholder = new PlaceholderViewModel(_localization, plannedItemKeys)
        {
            TitleKey = $"{baseKey}.Title",
            SubtitleKey = $"{baseKey}.Subtitle",
            StatusKey = $"{baseKey}.Status",
            DescriptionKey = $"{baseKey}.Description",
            IsPlanned = isPlanned
        };

        placeholder.RefreshTexts();
        return placeholder;
    }

    private void SetLanguage(string code) => _localization.SetLanguage(code);

    private void RefreshTexts()
    {
        foreach (var item in NavigationItems) item.RefreshTexts();
        foreach (var item in LanguageOptions) item.RefreshTexts();

        _dashboardViewModel.RefreshTexts();
        _libraryViewModel.RefreshTexts();
        _processesViewModel.RefreshTexts();
        _profilesViewModel.RefreshTexts();
        _hardwareViewModel.RefreshTexts();
        _logsViewModel.RefreshTexts();
        _settingsViewModel.RefreshTexts();

        foreach (var placeholder in _views.Values.OfType<PlaceholderViewModel>()) placeholder.RefreshTexts();

        SelectCurrentLanguageOption();
        Navigate(_currentKey);

        OnPropertyChanged(nameof(AppTagline));
        OnPropertyChanged(nameof(LanguageLabel));
        OnPropertyChanged(nameof(CoreFoundationStatus));
        OnPropertyChanged(nameof(WatcherStatus));
        OnPropertyChanged(nameof(MinimizeTooltip));
        OnPropertyChanged(nameof(MaximizeRestoreTooltip));
        OnPropertyChanged(nameof(CloseTooltip));
    }

    private void SelectCurrentLanguageOption()
    {
        foreach (var option in LanguageOptions)
        {
            option.IsSelected = option.Code.Equals(_localization.CurrentLanguage, StringComparison.OrdinalIgnoreCase);
            if (option.IsSelected) SelectedLanguage = option;
        }
    }

    private void Navigate(string key)
    {
        if (!_views.TryGetValue(key, out var viewModel))
        {
            key = "Dashboard";
            viewModel = _dashboardViewModel;
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
            ProcessesViewModel processes => processes.Title,
            ProfilesViewModel profiles => profiles.Title,
            HardwareViewModel hardware => hardware.Title,
            SettingsViewModel settings => settings.Title,
            LogsViewModel logs => logs.Title,
            PlaceholderViewModel placeholder => placeholder.Title,
            _ => key
        };
        CurrentSubtitle = viewModel switch
        {
            DashboardViewModel dashboard => dashboard.Subtitle,
            LibraryViewModel library => library.Subtitle,
            ProcessesViewModel processes => processes.Subtitle,
            ProfilesViewModel profiles => profiles.Subtitle,
            HardwareViewModel hardware => hardware.Subtitle,
            SettingsViewModel settings => settings.Subtitle,
            LogsViewModel logs => logs.Subtitle,
            PlaceholderViewModel placeholder => placeholder.Subtitle,
            _ => AppTagline
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _processesViewModel.Dispose();
        _hardwareViewModel.Dispose();
        _runtime.Dispose();
    }
}
