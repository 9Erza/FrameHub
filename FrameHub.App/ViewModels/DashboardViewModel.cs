using FrameHub.App.Helpers;
using FrameHub.App.Services;
using FrameHub.Core.Models.SessionOptimization;
using FrameHub.Core.Services.SessionOptimization;
using System.Collections.ObjectModel;

namespace FrameHub.App.ViewModels;

public sealed class DashboardViewModel : ViewModelBase
{
    private readonly LocalizationService _localization;
    private readonly AppRuntimeService _runtime;
    private readonly SessionOptimizationSettingsService _sessionSettingsService = new();
    private readonly SessionStateService _sessionStateService = new();

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

    public ObservableCollection<MetricCardViewModel> Metrics { get; } = new();
    public ObservableCollection<ActivityItemViewModel> Activity => _runtime.Activity;

    public DashboardViewModel(LocalizationService localization, AppRuntimeService runtime)
    {
        _localization = localization;
        _runtime = runtime;
        _runtime.WatcherStateChanged += (_, _) => RefreshTexts();
        _runtime.RuntimeStateChanged += (_, _) => RefreshTexts();
        _runtime.ProfilesChanged += (_, _) => RefreshTexts();
        RefreshTexts();
    }

    public void RefreshTexts()
    {
        SessionOptimizationSettings sessionSettings = _sessionSettingsService.Load();
        ActiveSessionState? activeSession = _sessionStateService.Load();
        bool isSessionActive = activeSession?.IsActive == true;
        int enabledProfiles = _runtime.Profiles.Count(p => p.IsEnabled);
        int totalProfiles = _runtime.Profiles.Count;
        int autoGames = sessionSettings.AutoEnabledGameIds.Distinct(StringComparer.OrdinalIgnoreCase).Count();

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
            Title = _localization.T("Metric.Logging.Title"),
            Value = _runtime.Settings.LogEnabled ? _localization.T("Metric.Logging.ValueOn") : _localization.T("Metric.Logging.ValueOff"),
            Detail = _runtime.Activity.Count == 1
                ? _localization.T("Metric.Logging.DetailOne")
                : string.Format(_localization.T("Metric.Logging.DetailMany"), _runtime.Activity.Count),
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
    }
}
