using FrameHub.App.Helpers;
using FrameHub.App.Services;
using FrameHub.Core.Logging;
using FrameHub.Core.Models;
using FrameHub.Core.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace FrameHub.App.ViewModels;

public sealed class ShellViewModel : ViewModelBase, IDisposable
{
    public event EventHandler? UiLanguageChanged;
    public event Action<string>? UserNotificationRequested;
    public event Action<UpdateCheckResult>? UpdateAvailableRequested;
    public event Action<string, string>? AboutRequested;
    public ICommand OpenAboutCommand { get; }
    private readonly LocalizationService _localization;
    private readonly SettingsService _settingsService;
    private readonly AppRuntimeService _runtime;
    private object _currentViewModel;
    private string _currentTitle = string.Empty;
    private string _currentSubtitle = string.Empty;
    private string _currentKey = "Dashboard";
    private bool _disposed;
    private readonly UpdateCheckSession _updateCheckSession = new();

    private readonly DashboardViewModel _dashboardViewModel;
    private readonly LibraryViewModel _libraryViewModel;
    private readonly SessionOptimizationViewModel _sessionOptimizationViewModel;
    private readonly BenchmarkViewModel _benchmarkViewModel;
    private readonly ProcessesViewModel _processesViewModel;
    private readonly ProfilesViewModel _profilesViewModel;
    private readonly HardwareViewModel _hardwareViewModel;
    private readonly LogsViewModel _logsViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly Dictionary<string, object> _views;

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }
    public IEnumerable<NavigationItemViewModel> MainGroupItems => NavigationItems.Take(1);
    public IEnumerable<NavigationItemViewModel> GamingGroupItems => NavigationItems.Skip(1).Take(3);
    public IEnumerable<NavigationItemViewModel> PerformanceGroupItems => NavigationItems.Skip(4).Take(3);
    public IEnumerable<NavigationItemViewModel> SystemGroupItems => NavigationItems.Skip(7).Take(1);
    public NavigationItemViewModel SettingsNavigationItem => NavigationItems[^1];
    /// <summary>
    /// Session-chapters presentation projection (Overview→Dashboard, Prepare→Library,
    /// Play→Session, Verify→Benchmarks). Same item instances, same NavigateCommand
    /// ownership — only the visual grouping is new.
    /// </summary>
    public IEnumerable<NavigationItemViewModel> ChapterItems => NavigationItems.Take(4);
    /// <summary>
    /// Backstage utility destinations for the bottom utility bar.
    /// </summary>
    public IEnumerable<NavigationItemViewModel> UtilityItems => NavigationItems.Skip(4);
    /// <summary>
    /// Ember Ledger sidebar groups (Gra → Narzędzia → System): explicit key order per
    /// the v2 contract. Same item instances, same NavigateCommand ownership — only the
    /// visual grouping is new. Chapter/utility projections above stay for compatibility.
    /// </summary>
    public IEnumerable<NavigationItemViewModel> SidebarGamesItems => ItemsFor("Dashboard", "Library");
    public IEnumerable<NavigationItemViewModel> SidebarToolsItems => ItemsFor("Session", "Processes", "Hardware", "Benchmarks");
    public IEnumerable<NavigationItemViewModel> SidebarSystemItems => ItemsFor("Profiles", "Logs", "Settings");
    public string SidebarGamesTitle => _localization.T("Nav.Group.Games");
    public string SidebarToolsTitle => _localization.T("Nav.Group.Tools");
    public string SidebarSystemTitle => _localization.T("Nav.Group.System");
    public string NavMenuTooltip => _localization.T("Shell.Nav.Menu");

    private IEnumerable<NavigationItemViewModel> ItemsFor(params string[] keys)
    {
        var byKey = new Dictionary<string, NavigationItemViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in NavigationItems) byKey[item.Key] = item;
        foreach (string key in keys)
        {
            if (byKey.TryGetValue(key, out var item)) yield return item;
        }
    }

    private bool _isRailMenuOpen;
    /// <summary>
    /// Rail-mode overlay with full navigation names (below 1100 DIP client width).
    /// </summary>
    public bool IsRailMenuOpen
    {
        get => _isRailMenuOpen;
        set => SetProperty(ref _isRailMenuOpen, value);
    }
    public ICommand ToggleRailMenuCommand { get; }
    public ICommand CloseRailMenuCommand { get; }
    public ICommand NavigateFromRailCommand { get; }
    public string CurrentKey => _currentKey;
    public ICommand NavigateCommand { get; }
    public ICommand OpenRepositoryCommand { get; }
    public ICommand OpenAuthorCommand { get; }
    public ICommand OpenWebsiteCommand { get; }
    public ICommand OpenSupportCommand { get; }

    public AppRuntimeService Runtime => _runtime;
    public LocalizationService Localization => _localization;
    public SettingsViewModel Settings => _settingsViewModel;

    public string AppName { get; } = "FrameHub";
    public string AppVersion { get; } = new AppInfo().Version;
    public string CoreFoundationStatus => _localization.T("Status.CoreMigrated");
    public bool IsWatcherActive => _runtime.IsProfileWatcherActive;
    public string WatcherStatus => _runtime.IsProfileWatcherActive ? _localization.T("Status.WatcherActive") : _localization.T("Status.WatcherInactive");
    public bool IsSessionActive => _runtime.SessionOptimizationCoordinator.ActiveSession?.IsActive == true;
    public bool IsRecoveryPending => _runtime.SessionOptimizationCoordinator.ActiveSession?.IsRecoveryPending == true;
    public string SessionStatus => IsRecoveryPending
        ? _localization.T("Session.State.Recovery")
        : IsSessionActive
            ? _localization.T("Session.State.Active")
            : _localization.T("Session.State.Idle");
    public string SessionPillText => string.Format(_localization.T("Shell.Session.Pill"), SessionStatus);
    public string EngineFooterText => string.Format(
        _localization.T("Shell.Footer.PresentMon"),
        _benchmarkViewModel.EngineReady ? _localization.T("Benchmark.Engine.Ready") : _localization.T("Benchmark.Unavailable"));
    public string FooterLicenseText => string.Format(_localization.T("Shell.Footer.License"), AppVersion);
    public string ToggleNavTooltip => _localization.T("Shell.ToggleNav");
    public string AboutTooltip => _localization.T("Shell.About");
    public string MinimizeTooltip => _localization.T("Window.Minimize");
    public string MaximizeRestoreTooltip => _localization.T("Window.MaximizeRestore");
    public string CloseTooltip => _localization.T("Window.Close");
    public string MainGroupTitle => _localization.T("Nav.Group.Main");
    public string GamingGroupTitle => _localization.T("Nav.Group.Gaming");
    public string PerformanceGroupTitle => _localization.T("Nav.Group.Performance");
    public string SystemGroupTitle => _localization.T("Nav.Group.System");
    public string RepositoryTooltip => _localization.T("Footer.Repository");
    public string AuthorTooltip => _localization.T("Footer.Author");
    public string AuthorLabel => _localization.T("Footer.AuthorLabel");
    public string WebsiteTooltip => _localization.T("Footer.Website");
    public string SupportTooltip => _localization.T("Footer.Support");
    public string TrayOpenText => _localization.T("Tray.Open");
    public string TrayGoToText => _localization.T("Tray.GoTo");
    public string TrayDashboardText => _localization.T("Tray.Dashboard");
    public string TrayGamesOptimizationText => _localization.T("Tray.GamesOptimization");
    public string TrayBenchmarksText => _localization.T("Tray.Benchmarks");
    public string TrayHardwareText => _localization.T("Tray.Hardware");
    public string TraySettingsText => _localization.T("Tray.Settings");
    public string TrayRestartText => _localization.T("Tray.Restart");
    public string TrayExitText => _localization.T("Tray.Exit");
    public string ProjectVersionLabel => $"FrameHub {AppVersion}";

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
        _benchmarkViewModel = new BenchmarkViewModel(_localization, _runtime);
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
            UiLanguageChanged?.Invoke(this, EventArgs.Empty);
        };
        _runtime.WatcherStateChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsWatcherActive));
            OnPropertyChanged(nameof(WatcherStatus));
            UpdateProfilesBadge();
        };
        _runtime.ProfilesChanged += (_, _) =>
        {
            _dashboardViewModel.RefreshTexts();
            _libraryViewModel.RefreshTexts();
            UpdateProfilesBadge();
        };
        _runtime.SessionOptimizationCoordinator.SessionStateChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsSessionActive));
            OnPropertyChanged(nameof(IsRecoveryPending));
            OnPropertyChanged(nameof(SessionStatus));
            OnPropertyChanged(nameof(SessionPillText));
        };
        _benchmarkViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(BenchmarkViewModel.EngineReady) or nameof(BenchmarkViewModel.EngineStatus))
            {
                OnPropertyChanged(nameof(EngineFooterText));
                _dashboardViewModel.IsBenchmarkEngineReady = _benchmarkViewModel.EngineReady;
            }
        };
        _libraryViewModel.Items.CollectionChanged += (_, _) => UpdateLibraryBadge();

        NavigationItems = new ObservableCollection<NavigationItemViewModel>
        {
            new(_localization) { Key = "Dashboard", TitleKey = "Nav.Dashboard", Icon = "\uE80F" },
            new(_localization) { Key = "Library", TitleKey = "Nav.Library", Icon = "\uE7FC" },
            new(_localization) { Key = "Session", TitleKey = "Nav.Session", Icon = "\uEC4A" },
            new(_localization) { Key = "Benchmarks", TitleKey = "Nav.Benchmarks", Icon = "\uE9D2" },
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
            ["Benchmarks"] = _benchmarkViewModel,
            ["Processes"] = _processesViewModel,
            ["Profiles"] = _profilesViewModel,
            ["Hardware"] = _hardwareViewModel,
            ["Settings"] = _settingsViewModel,
            ["Logs"] = _logsViewModel
        };

        NavigateCommand = new RelayCommand(parameter => Navigate(parameter?.ToString() ?? "Dashboard"));
        ToggleRailMenuCommand = new RelayCommand(_ => IsRailMenuOpen = !IsRailMenuOpen);
        CloseRailMenuCommand = new RelayCommand(_ => IsRailMenuOpen = false);
        NavigateFromRailCommand = new RelayCommand(parameter =>
        {
            IsRailMenuOpen = false;
            Navigate(parameter?.ToString() ?? "Dashboard");
        });
        OpenRepositoryCommand = new RelayCommand(_ => OpenExternalLink(FrameHubExternalLink.Repository));
        OpenAuthorCommand = new RelayCommand(_ => OpenExternalLink(FrameHubExternalLink.Author));
        OpenWebsiteCommand = new RelayCommand(_ => OpenExternalLink(FrameHubExternalLink.Website));
        OpenSupportCommand = new RelayCommand(_ => OpenExternalLink(FrameHubExternalLink.Support));
        OpenAboutCommand = new RelayCommand(_ => ShowAbout());

        _libraryViewModel.BenchmarkRequested += item =>
        {
            _benchmarkViewModel.Preselect(item);
            Navigate("Benchmarks");
        };
        _profilesViewModel.CreateFirstProfileRequested += () => Navigate("Processes");
        _runtime.RuntimeStateChanged += (_, _) => _benchmarkViewModel.RefreshHotkeyStatus();
        _benchmarkViewModel.UserNotificationRequested += message => UserNotificationRequested?.Invoke(message);
        _dashboardViewModel.BenchmarkNavigationRequested = () => Navigate("Benchmarks");
        _dashboardViewModel.LibraryNavigationRequested = () => Navigate("Library");
        _dashboardViewModel.NavigationRequested = key => Navigate(key);
        _dashboardViewModel.IsBenchmarkEngineReady = _benchmarkViewModel.EngineReady;

        _currentViewModel = _dashboardViewModel;
        UpdateLibraryBadge();
        UpdateProfilesBadge();
        Navigate("Dashboard");
    }

    private void UpdateLibraryBadge()
    {
        var library = NavigationItems.FirstOrDefault(item => item.Key == "Library");
        if (library is null)
        {
            return;
        }

        int count = _libraryViewModel.Items.Count;
        library.CountBadge = count > 0 ? count.ToString(CultureInfo.CurrentCulture) : string.Empty;
    }

    private void UpdateProfilesBadge()
    {
        var profiles = NavigationItems.FirstOrDefault(item => item.Key == "Profiles");
        if (profiles is null)
        {
            return;
        }

        int total = _runtime.Profiles.Count;
        profiles.CountBadge = total > 0 ? total.ToString(CultureInfo.CurrentCulture) : string.Empty;
    }

    private void ShowAbout()
    {
        // Rail-mode links live in the drawer overlay: activating one dismisses it like navigation does.
        IsRailMenuOpen = false;
        string message = string.Format(
            _localization.T("Shell.About.Message"),
            AppVersion,
            _localization.T("Footer.AuthorLabel"),
            _localization.T("Footer.Website"))
            + "\n\n" + _localization.T("Shell.About.SupporterNote");
        AboutRequested?.Invoke(_localization.T("Shell.About.Title"), message);
    }

    private void RefreshTexts()
    {
        foreach (var item in NavigationItems) item.RefreshTexts();

        _dashboardViewModel.RefreshTexts();
        _libraryViewModel.RefreshTexts();
        _sessionOptimizationViewModel.RefreshTexts();
        _benchmarkViewModel.RefreshTexts();
        _processesViewModel.RefreshTexts();
        _profilesViewModel.RefreshTexts();
        _hardwareViewModel.RefreshTexts();
        _logsViewModel.RefreshTexts();
        _settingsViewModel.RefreshTexts();

        Navigate(_currentKey);

        OnPropertyChanged(nameof(CoreFoundationStatus));
        OnPropertyChanged(nameof(IsWatcherActive));
        OnPropertyChanged(nameof(WatcherStatus));
        OnPropertyChanged(nameof(IsSessionActive));
        OnPropertyChanged(nameof(IsRecoveryPending));
        OnPropertyChanged(nameof(SessionStatus));
        OnPropertyChanged(nameof(SessionPillText));
        OnPropertyChanged(nameof(EngineFooterText));
        OnPropertyChanged(nameof(FooterLicenseText));
        OnPropertyChanged(nameof(ToggleNavTooltip));
        OnPropertyChanged(nameof(AboutTooltip));
        UpdateLibraryBadge();
        UpdateProfilesBadge();
        OnPropertyChanged(nameof(MinimizeTooltip));
        OnPropertyChanged(nameof(MaximizeRestoreTooltip));
        OnPropertyChanged(nameof(CloseTooltip));
        OnPropertyChanged(nameof(MainGroupTitle));
        OnPropertyChanged(nameof(GamingGroupTitle));
        OnPropertyChanged(nameof(PerformanceGroupTitle));
        OnPropertyChanged(nameof(SystemGroupTitle));
        OnPropertyChanged(nameof(SidebarSystemTitle));
        OnPropertyChanged(nameof(SidebarGamesTitle));
        OnPropertyChanged(nameof(SidebarToolsTitle));
        OnPropertyChanged(nameof(NavMenuTooltip));
        OnPropertyChanged(nameof(RepositoryTooltip));
        OnPropertyChanged(nameof(AuthorTooltip));
        OnPropertyChanged(nameof(AuthorLabel));
        OnPropertyChanged(nameof(WebsiteTooltip));
        OnPropertyChanged(nameof(SupportTooltip));
        OnPropertyChanged(nameof(TrayOpenText));
        OnPropertyChanged(nameof(TrayGoToText));
        OnPropertyChanged(nameof(TrayDashboardText));
        OnPropertyChanged(nameof(TrayGamesOptimizationText));
        OnPropertyChanged(nameof(TrayBenchmarksText));
        OnPropertyChanged(nameof(TrayHardwareText));
        OnPropertyChanged(nameof(TraySettingsText));
        OnPropertyChanged(nameof(TrayRestartText));
        OnPropertyChanged(nameof(TrayExitText));
    }

    private void OpenExternalLink(FrameHubExternalLink link)
    {
        // Rail-mode links live in the drawer overlay: activating one dismisses it like navigation does.
        IsRailMenuOpen = false;
        if (!ExternalLinkService.TryOpen(link))
        {
            _runtime.AddActivity(_localization.T("Footer.OpenFailed"), "Warn");
        }
    }

    public void NavigateTo(string key) => Navigate(key);

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
            // One bounded runtime-status refresh per Library activation; no rescan, no timer.
            _libraryViewModel.Activate();
        }

        if (!_currentKey.Equals("Processes", StringComparison.OrdinalIgnoreCase) && key.Equals("Processes", StringComparison.OrdinalIgnoreCase))
        {
            _processesViewModel.Start();
        }

        if (!_currentKey.Equals("Benchmarks", StringComparison.OrdinalIgnoreCase) && key.Equals("Benchmarks", StringComparison.OrdinalIgnoreCase))
        {
            _benchmarkViewModel.Activate();
        }
        else if (_currentKey.Equals("Benchmarks", StringComparison.OrdinalIgnoreCase) && !key.Equals("Benchmarks", StringComparison.OrdinalIgnoreCase))
        {
            _benchmarkViewModel.Deactivate();
        }
        else if (_currentKey.Equals("Processes", StringComparison.OrdinalIgnoreCase) && !key.Equals("Processes", StringComparison.OrdinalIgnoreCase))
        {
            _processesViewModel.Stop();
        }

        if (!_currentKey.Equals("Hardware", StringComparison.OrdinalIgnoreCase) && key.Equals("Hardware", StringComparison.OrdinalIgnoreCase))
        {
            _hardwareViewModel.Activate();
        }
        else if (_currentKey.Equals("Hardware", StringComparison.OrdinalIgnoreCase) && !key.Equals("Hardware", StringComparison.OrdinalIgnoreCase))
        {
            _hardwareViewModel.Deactivate();
        }

        if (key.Equals("Dashboard", StringComparison.OrdinalIgnoreCase))
        {
            _dashboardViewModel.ActivateGamingSection();
        }

        _currentKey = key;

        foreach (var item in NavigationItems)
        {
            item.IsSelected = item.Key.Equals(key, StringComparison.OrdinalIgnoreCase);
        }

        CurrentViewModel = viewModel;
        OnPropertyChanged(nameof(CurrentKey));
        CurrentTitle = viewModel switch
        {
            DashboardViewModel dashboard => dashboard.Title,
            LibraryViewModel library => library.Title,
            SessionOptimizationViewModel session => session.Title,
            BenchmarkViewModel benchmark => benchmark.Title,
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
            BenchmarkViewModel benchmark => benchmark.Subtitle,
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
        DisposeViewModels();
        _runtime.Dispose();
    }

    private void DisposeViewModels()
    {
        _dashboardViewModel.Dispose();
        _sessionOptimizationViewModel.Dispose();
        _benchmarkViewModel.Dispose();
        _processesViewModel.Dispose();
        _hardwareViewModel.Dispose();
        _libraryViewModel.Dispose();
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        await ShutdownAsync(cancellationToken, AppRuntimeService.DefaultShutdownBudget).ConfigureAwait(false);
    }

    /// <summary>
    /// Cooperative shutdown under a total wall-clock <paramref name="totalBudget"/>
    /// shared with the window deadline. The benchmark phase observes
    /// <paramref name="cancellationToken"/>; the remaining budget is then handed
    /// to the runtime shutdown so a stuck session drain cannot silently exceed
    /// the deadline (REA F2). The window is closed by the caller when the
    /// deadline expires regardless — fail-safe, never a blind kill.
    /// </summary>
    public async Task ShutdownAsync(CancellationToken cancellationToken, TimeSpan totalBudget)
    {
        LoggerService.Instance.Info("Shell shutdown initiated.");
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _benchmarkViewModel.CancelAndWaitForCleanupAsync(cancellationToken).ConfigureAwait(false);
            LoggerService.Instance.Info("Benchmark shutdown cleanup completed.");
        }
        catch (Exception ex)
        {
            LoggerService.Instance.Warn($"Benchmark shutdown cleanup error: {ex.Message}");
        }

        try
        {
            DisposeViewModels();

            TimeSpan remaining = totalBudget - stopwatch.Elapsed;
            await _runtime.ShutdownAsync(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
            _disposed = true;
            LoggerService.Instance.Info("Shell disposal completed.");
        }
        catch (Exception ex)
        {
            LoggerService.Instance.Warn($"Shell disposal error: {ex.Message}");
        }
    }

    public async Task HandleBenchmarkHotkeyAsync()
    {
        try
        {
            await _benchmarkViewModel.HandleGlobalHotkeyAsync();
        }
        catch (Exception ex)
        {
            _runtime.AddActivity($"{_localization.T("Benchmark.Hotkey.CouldNotStart")}: {ex.Message}", "Error");
            UserNotificationRequested?.Invoke(_localization.T("Benchmark.Hotkey.CouldNotStart"));
        }
    }

    public void ReportBenchmarkHotkeyRegistrationFailure() => _settingsViewModel.ReportBenchmarkHotkeyRegistrationFailure();

    /// <summary>
    /// Runs the once-per-process automatic update check. Silent unless an update is available.
    /// Triggered only when the main window is actually presented (never during hidden/tray/minimized startup).
    /// </summary>
    public async Task RunAutomaticUpdateCheckIfEligibleAsync()
    {
        if (_disposed) return;
        UpdateCheckResult? result = await _updateCheckSession.TryRunAutomaticCheckAsync(_runtime.Settings.CheckForUpdates);
        if (result is not null && UpdateCheckSession.ShouldPresentUpdateDialog(result))
        {
            UpdateAvailableRequested?.Invoke(result);
        }
    }
}
