using FrameHub.App.Helpers;
using FrameHub.App.Services;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Models.SessionOptimization;
using FrameHub.Core.Services.SessionOptimization;
using FrameHub.Core.Services.Library;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Services.Benchmarking;

namespace FrameHub.App.ViewModels;

public sealed class DashboardViewModel : ViewModelBase
{
    private readonly LocalizationService _localization;
    private readonly AppRuntimeService _runtime;
    private readonly SessionOptimizationSettingsService _sessionSettingsService = new();
    private readonly SessionStateService _sessionStateService = new();
    private readonly LibraryService _libraryService = new();
    private readonly BenchmarkStorageService _benchmarkStorage = new();
    private bool _isGamingBusy;
    private bool _isRestoreBusy;
    private LibraryItem? _selectedGamingGame;
    private bool _isSelectedGamingGameRunning;
    private string _gamingStatusMessage = string.Empty;
    private ActiveSessionState? _gamingSession;
    public Action? BenchmarkNavigationRequested { get; set; }
    public ICommand OpenBenchmarksCommand { get; }
    public ICommand StartGamingModeCommand { get; }
    public ICommand RestoreGamingModeCommand { get; }

    public string Title => _localization.T("Dashboard.Title");
    public string Subtitle => _localization.T("Dashboard.Subtitle");
    public string FoundationPlanTitle => _localization.T("Dashboard.FoundationPlan.Title");
    public string FoundationPlanDescription => _localization.T("Dashboard.FoundationPlan.Description");
    public string RecentActivityTitle => _localization.T("Dashboard.RecentActivity.Title");
    public string RecentActivityLiveText => _localization.T("Status.Live");

    public string MainStatusTitle => _localization.T("Dashboard.MainStatus.Title");
    public string MainStatusDescription => _localization.T("Dashboard.MainStatus.Description");
    public string ActiveModulesTitle => _localization.T("Dashboard.ActiveModules.Title");
    public string ActiveModulesDescription => _localization.T("Dashboard.ActiveModules.Description");
    public string SessionStateTitle => _localization.T("Dashboard.SessionState.Title");
    public string SessionStateValue { get; private set; } = string.Empty;
    public string SessionStateDetail { get; private set; } = string.Empty;
    public string SessionAutomationText { get; private set; } = string.Empty;
    public string ProfileMonitorText { get; private set; } = string.Empty;
    public string LastProfileText { get; private set; } = string.Empty;
    public string LoggingText { get; private set; } = string.Empty;
    public string ProfileMonitorTitle => _localization.T("Dashboard.ProfileMonitor.Title");
    public string SessionOptimizationTitle => _localization.T("Dashboard.SessionOptimization.Title");
    public string LastProfileTitle => _localization.T("Dashboard.LastProfile.Title");
    public string LoggingTitle => _localization.T("Dashboard.Logging.Title");
    public string LatestBenchmarkTitle => _localization.T("Dashboard.Benchmark.Title");
    public string LatestBenchmarkGame { get; private set; } = string.Empty;
    public string LatestBenchmarkResult { get; private set; } = string.Empty;
    public string OpenBenchmarksText => _localization.T("Dashboard.Benchmark.Open");

    public ObservableCollection<MetricCardViewModel> Metrics { get; } = new();
    public ObservableCollection<ActivityItemViewModel> Activity => _runtime.Activity;

    public IReadOnlyList<LibraryItem> GamingGames { get; private set; } = Array.Empty<LibraryItem>();
    public bool HasGamingGames => GamingGames.Count > 0;
    public bool HasNoGamingGames => !HasGamingGames;
    public bool ShowGamingSelector => !IsGamingModeActive && HasGamingGames;

    public LibraryItem? SelectedGamingGame
    {
        get => _selectedGamingGame;
        set
        {
            if (SetProperty(ref _selectedGamingGame, value))
            {
                _ = RefreshSelectedGameRunningStateAsync();
            }
        }
    }

    public bool IsSelectedGamingGameRunning
    {
        get => _isSelectedGamingGameRunning;
        private set => SetProperty(ref _isSelectedGamingGameRunning, value);
    }

    public bool IsGamingBusy
    {
        get => _isGamingBusy;
        private set => SetProperty(ref _isGamingBusy, value);
    }

    public bool IsRestoreBusy
    {
        get => _isRestoreBusy;
        private set => SetProperty(ref _isRestoreBusy, value);
    }

    public string GamingStatusMessage
    {
        get => _gamingStatusMessage;
        private set => SetProperty(ref _gamingStatusMessage, value);
    }

    public bool IsGamingModeActive => _gamingSession?.IsActive == true;

    public string GamingModeDetail
    {
        get
        {
            if (_gamingSession?.IsActive != true)
            {
                return string.Empty;
            }

            string gameName = string.IsNullOrWhiteSpace(_gamingSession.GameName)
                ? _localization.T("Dashboard.UnknownGame")
                : _gamingSession.GameName;
            return string.Format(
                _localization.T("Dashboard.Gaming.ActiveDetail"),
                gameName,
                _gamingSession.SuspendedProcesses.Count);
        }
    }

    public string GamingTitle => _localization.T("Dashboard.Gaming.Title");
    public string GamingDescription => _localization.T("Dashboard.Gaming.Description");
    public string GamingEmptyText => _localization.T("Dashboard.Gaming.Empty");
    public string StartGamingModeText => _localization.T("Dashboard.Gaming.Start");
    public string GamingRunningText => _localization.T("Dashboard.Gaming.Running");
    public string GamingActiveText => _localization.T("Dashboard.Gaming.Active");
    public string GamingRestoreText => _localization.T("Dashboard.Gaming.Restore");

    public DashboardViewModel(LocalizationService localization, AppRuntimeService runtime)
    {
        _localization = localization;
        _runtime = runtime;
        OpenBenchmarksCommand = new RelayCommand(_ => BenchmarkNavigationRequested?.Invoke());
        StartGamingModeCommand = new RelayCommand(
            async _ => await StartGamingModeAsync(),
            _ => !_isGamingBusy && !_isRestoreBusy && SelectedGamingGame != null && !IsGamingModeActive);
        RestoreGamingModeCommand = new RelayCommand(
            async _ => await RestoreGamingModeAsync(),
            _ => !_isRestoreBusy && !_isGamingBusy && IsGamingModeActive);
        _runtime.WatcherStateChanged += (_, _) => RefreshTexts();
        _runtime.RuntimeStateChanged += (_, _) => RefreshTexts();
        _runtime.ProfilesChanged += (_, _) => RefreshTexts();
        _runtime.SessionOptimizationCoordinator.SessionStateChanged += OnCoordinatorSessionStateChanged;
        _gamingSession = _runtime.SessionOptimizationCoordinator.ActiveSession;
        RefreshTexts();
        _ = RefreshGamingStateAsync();
    }

    public void ActivateGamingSection()
    {
        _ = RefreshGamingStateAsync();
    }

    private void OnCoordinatorSessionStateChanged(object? sender, ActiveSessionState? session)
    {
        _gamingSession = session;
        OnPropertyChanged(nameof(IsGamingModeActive));
        OnPropertyChanged(nameof(ShowGamingSelector));
        OnPropertyChanged(nameof(GamingModeDetail));
    }

    public async Task RefreshGamingStateAsync()
    {
        try
        {
            string? selectedId = _selectedGamingGame?.Id;
            var games = _runtime.SessionOptimizationCoordinator.LoadLibraryGames();
            GamingGames = games;
            OnPropertyChanged(nameof(GamingGames));
            OnPropertyChanged(nameof(HasGamingGames));
            OnPropertyChanged(nameof(HasNoGamingGames));

            var newSelection = games.FirstOrDefault(game => game.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase))
                ?? games.FirstOrDefault();
            if (!ReferenceEquals(_selectedGamingGame, newSelection))
            {
                SelectedGamingGame = newSelection;
            }
            else
            {
                await RefreshSelectedGameRunningStateAsync().ConfigureAwait(false);
            }

            _gamingSession = _runtime.SessionOptimizationCoordinator.ActiveSession;
            OnPropertyChanged(nameof(IsGamingModeActive));
            OnPropertyChanged(nameof(ShowGamingSelector));
            OnPropertyChanged(nameof(GamingModeDetail));
        }
        catch
        {
            // Library load or observation unavailable; keep the last presented state.
        }
    }

    private async Task RefreshSelectedGameRunningStateAsync()
    {
        try
        {
            var game = _selectedGamingGame;
            if (game == null)
            {
                IsSelectedGamingGameRunning = false;
                return;
            }

            // One batch observation request for display only; never a mutation input.
            var runningIds = await _runtime.ProcessScanner.FindRunningLibraryItemIdsAsync(new[] { game }).ConfigureAwait(false);
            if (ReferenceEquals(_selectedGamingGame, game))
            {
                IsSelectedGamingGameRunning = runningIds.Contains(game.Id);
            }
        }
        catch
        {
            IsSelectedGamingGameRunning = false;
        }
    }

    private async Task StartGamingModeAsync()
    {
        if (_isGamingBusy || _isRestoreBusy)
        {
            return;
        }

        var game = _selectedGamingGame;
        if (game == null || IsGamingModeActive)
        {
            return;
        }

        IsGamingBusy = true;
        GamingStatusMessage = string.Empty;
        try
        {
            GamingQuickActionResult result = await _runtime.GamingQuickActions.StartGamingModeAsync(game).ConfigureAwait(true);
            GamingStatusMessage = BuildGamingStatusMessage(result, game);
        }
        catch
        {
            GamingStatusMessage = _localization.T("Dashboard.Gaming.Error.Generic");
        }
        finally
        {
            IsGamingBusy = false;
        }

        await RefreshGamingStateAsync().ConfigureAwait(true);
    }

    private async Task RestoreGamingModeAsync()
    {
        if (_isRestoreBusy || _isGamingBusy)
        {
            return;
        }

        IsRestoreBusy = true;
        try
        {
            var result = await _runtime.GamingQuickActions.StopGamingModeAsync().ConfigureAwait(true);
            GamingStatusMessage = result.Success
                ? string.Format(_localization.T("Dashboard.Gaming.Result.Restored"), result.ResumedCount)
                : MapRestoreError(result.ErrorCode);
        }
        catch
        {
            GamingStatusMessage = _localization.T("Dashboard.Gaming.Error.RestoreFailed");
        }
        finally
        {
            IsRestoreBusy = false;
        }
    }

    private string MapRestoreError(string errorCode)
    {
        return errorCode switch
        {
            "not_active" => _localization.T("Dashboard.Gaming.Error.RestoreNotActive"),
            "benchmark_active" => _localization.T("Dashboard.Gaming.Error.BenchmarkActive"),
            "operation_in_progress" => _localization.T("Dashboard.Gaming.Error.Busy"),
            _ => _localization.T("Dashboard.Gaming.Error.RestoreFailed")
        };
    }

    private string BuildGamingStatusMessage(GamingQuickActionResult result, LibraryItem game)
    {
        string gameName = string.IsNullOrWhiteSpace(game.DisplayName)
            ? _localization.T("Dashboard.UnknownGame")
            : game.DisplayName;

        if (result.Success)
        {
            return result.Launched
                ? string.Format(_localization.T("Dashboard.Gaming.Result.Launched"), gameName, result.SuspendedCount)
                : string.Format(_localization.T("Dashboard.Gaming.Result.OptimizeOnly"), gameName, result.SuspendedCount);
        }

        string launchPart = result.Stage == "SessionStart" && result.Launched
            ? string.Format(_localization.T("Dashboard.Gaming.Result.LaunchedOnly"), gameName) + " "
            : string.Empty;

        string sessionPart = result.ErrorCode switch
        {
            "no_candidates" => _localization.T("Dashboard.Gaming.Result.NoCandidates"),
            "not_launchable" => _localization.T("Dashboard.Gaming.Error.NotLaunchable"),
            "executable_missing" => _localization.T("Library.ExecutableMissing"),
            "launch_failed" => _localization.T("Library.LaunchFailed"),
            "benchmark_active" => _localization.T("Dashboard.Gaming.Error.BenchmarkActive"),
            "launch_in_progress" => _localization.T("Dashboard.Gaming.Error.LaunchInProgress"),
            "operation_in_progress" => _localization.T("Dashboard.Gaming.Error.Busy"),
            "already_active" => _localization.T("Dashboard.Gaming.Error.AlreadyActive"),
            "state_persist_failed" or "apply_failed" or "coordinator_stopping" or "no_game" => _localization.T("Dashboard.Gaming.Error.SessionFailed"),
            _ => _localization.T("Dashboard.Gaming.Error.Generic")
        };

        return launchPart + sessionPart;
    }

    public void RefreshTexts()
    {
        SessionOptimizationSettings sessionSettings = _sessionSettingsService.Load();
        ActiveSessionState? activeSession = _sessionStateService.Load();
        bool isSessionActive = activeSession?.IsActive == true;
        int enabledProfiles = _runtime.Profiles.Count(p => p.IsEnabled);
        int totalProfiles = _runtime.Profiles.Count;
        int autoGames = sessionSettings.AutoEnabledGameIds.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        int libraryGames = _libraryService.LoadItems().Count;
        BenchmarkHistoryEntry? latestBenchmark = _benchmarkStorage.EnumerateSessions().Sessions
            .FirstOrDefault(entry => entry.Metadata.Status == BenchmarkSessionStatus.Completed && entry.Summary?.PrimaryPresentedMetrics is not null);
        LatestBenchmarkGame = latestBenchmark?.Metadata.Game.DisplayName ?? _localization.T("Dashboard.Benchmark.Empty");
        LatestBenchmarkResult = latestBenchmark?.Summary?.PrimaryPresentedMetrics is { } metrics
            ? string.Format(CultureInfo.CurrentCulture, _localization.T("Dashboard.Benchmark.Result"), metrics.AverageFps, metrics.OnePercentLowFps, latestBenchmark.Metadata.StartUtc.ToLocalTime())
            : _localization.T("Dashboard.Benchmark.EmptyDetail");

        SessionStateValue = isSessionActive
            ? _localization.T("Dashboard.SessionState.ValueActive")
            : _localization.T("Dashboard.SessionState.ValueInactive");

        SessionStateDetail = isSessionActive
            ? string.Format(
                _localization.T("Dashboard.SessionState.DetailActive"),
                string.IsNullOrWhiteSpace(activeSession?.GameName) ? _localization.T("Dashboard.UnknownGame") : activeSession!.GameName,
                activeSession?.SuspendedProcesses.Count ?? 0)
            : _localization.T("Dashboard.SessionState.DetailInactive");

        SessionAutomationText = sessionSettings.AutoModeEnabled
            ? string.Format(_localization.T("Dashboard.SessionAutomation.Enabled"), autoGames)
            : _localization.T("Dashboard.SessionAutomation.Disabled");

        ProfileMonitorText = _runtime.IsProfileWatcherActive
            ? string.Format(_localization.T("Dashboard.ProfileMonitor.Active"), enabledProfiles)
            : _localization.T("Dashboard.ProfileMonitor.Disabled");

        LastProfileText = string.IsNullOrWhiteSpace(_runtime.LastAppliedProfile)
            ? _localization.T("Dashboard.LastProfile.None")
            : string.Format(_localization.T("Dashboard.LastProfile.Value"), _runtime.LastAppliedProfile);

        LoggingText = _runtime.Settings.LogEnabled
            ? _localization.T("Dashboard.Logging.Enabled")
            : _localization.T("Dashboard.Logging.Disabled");

        Metrics.Clear();
        Metrics.Add(new MetricCardViewModel
        {
            Title = _localization.T("Metric.BackgroundWatcher.Title"),
            Value = _runtime.IsProfileWatcherActive ? _localization.T("Metric.BackgroundWatcher.ValueActive") : _localization.T("Metric.BackgroundWatcher.ValueOff"),
            Detail = string.Format(_localization.T("Metric.BackgroundWatcher.Detail"), enabledProfiles, totalProfiles),
            Accent = "#22C55E"
        });
        Metrics.Add(new MetricCardViewModel
        {
            Title = _localization.T("Metric.SessionOptimization.Title"),
            Value = isSessionActive ? _localization.T("Metric.SessionOptimization.ValueActive") : _localization.T("Metric.SessionOptimization.ValueReady"),
            Detail = sessionSettings.AutoModeEnabled
                ? string.Format(_localization.T("Metric.SessionOptimization.DetailAuto"), autoGames)
                : _localization.T("Metric.SessionOptimization.DetailManual"),
            Accent = "#06B6D4"
        });
        Metrics.Add(new MetricCardViewModel
        {
            Title = _localization.T("Metric.Profiles.Title"),
            Value = totalProfiles.ToString(),
            Detail = string.IsNullOrWhiteSpace(_runtime.LastAppliedProfile) ? _localization.T("Metric.Profiles.Detail") : _runtime.LastAppliedProfile,
            Accent = "#3B82F6"
        });
        Metrics.Add(new MetricCardViewModel
        {
            Title = _localization.T("Dashboard.LibraryMetric.Title"),
            Value = libraryGames.ToString(),
            Detail = _localization.T("Dashboard.LibraryMetric.Detail"),
            Accent = "#F59E0B"
        });

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(FoundationPlanTitle));
        OnPropertyChanged(nameof(FoundationPlanDescription));
        OnPropertyChanged(nameof(RecentActivityTitle));
        OnPropertyChanged(nameof(RecentActivityLiveText));
        OnPropertyChanged(nameof(MainStatusTitle));
        OnPropertyChanged(nameof(MainStatusDescription));
        OnPropertyChanged(nameof(ActiveModulesTitle));
        OnPropertyChanged(nameof(ActiveModulesDescription));
        OnPropertyChanged(nameof(SessionStateTitle));
        OnPropertyChanged(nameof(SessionStateValue));
        OnPropertyChanged(nameof(SessionStateDetail));
        OnPropertyChanged(nameof(SessionAutomationText));
        OnPropertyChanged(nameof(ProfileMonitorText));
        OnPropertyChanged(nameof(LastProfileText));
        OnPropertyChanged(nameof(LoggingText));
        OnPropertyChanged(nameof(ProfileMonitorTitle));
        OnPropertyChanged(nameof(SessionOptimizationTitle));
        OnPropertyChanged(nameof(LastProfileTitle));
        OnPropertyChanged(nameof(LoggingTitle));
        OnPropertyChanged(nameof(LatestBenchmarkTitle));
        OnPropertyChanged(nameof(LatestBenchmarkGame));
        OnPropertyChanged(nameof(LatestBenchmarkResult));
        OnPropertyChanged(nameof(OpenBenchmarksText));
        OnPropertyChanged(nameof(GamingTitle));
        OnPropertyChanged(nameof(GamingDescription));
        OnPropertyChanged(nameof(GamingEmptyText));
        OnPropertyChanged(nameof(StartGamingModeText));
        OnPropertyChanged(nameof(GamingRunningText));
        OnPropertyChanged(nameof(GamingActiveText));
        OnPropertyChanged(nameof(GamingRestoreText));
        OnPropertyChanged(nameof(GamingModeDetail));
        OnPropertyChanged(nameof(ShowGamingSelector));
    }
}
