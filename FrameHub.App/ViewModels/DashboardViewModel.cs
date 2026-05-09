using FrameHub.App.Helpers;
using FrameHub.App.Services;
using System.Collections.ObjectModel;

namespace FrameHub.App.ViewModels;

public sealed class DashboardViewModel : ViewModelBase
{
    private readonly LocalizationService _localization;
    private readonly AppRuntimeService _runtime;

    public string Title => _localization.T("Dashboard.Title");
    public string Subtitle => _localization.T("Dashboard.Subtitle");
    public string FoundationPlanTitle => _localization.T("Dashboard.FoundationPlan.Title");
    public string FoundationPlanDescription => _localization.T("Dashboard.FoundationPlan.Description");
    public string TargetTitle => _localization.T("Dashboard.Target.Title");
    public string TargetDescription => _localization.T("Dashboard.Target.Description");
    public string MigrationTitle => _localization.T("Dashboard.Migration.Title");
    public string MigrationDescription => _localization.T("Dashboard.Migration.Description");
    public string RecentActivityTitle => _localization.T("Dashboard.RecentActivity.Title");
    public string ReadyStatus => _localization.T("Status.CoreMigrated");
    public string RecentActivityLiveText => _localization.T("Status.Live");

    public ObservableCollection<MetricCardViewModel> Metrics { get; } = new();
    public ObservableCollection<ActivityItemViewModel> Activity => _runtime.Activity;

    public DashboardViewModel(LocalizationService localization, AppRuntimeService runtime)
    {
        _localization = localization;
        _runtime = runtime;
        _runtime.WatcherStateChanged += (_, _) => RefreshTexts();
        _runtime.RuntimeStateChanged += (_, _) => RefreshTexts();
        RefreshTexts();
    }

    public void RefreshTexts()
    {
        Metrics.Clear();
        Metrics.Add(new MetricCardViewModel
        {
            Title = _localization.T("Metric.BackgroundWatcher.Title"),
            Value = _runtime.IsProfileWatcherActive ? _localization.T("Metric.BackgroundWatcher.ValueActive") : _localization.T("Metric.BackgroundWatcher.ValueOff"),
            Detail = string.Format(_localization.T("Metric.BackgroundWatcher.Detail"), _runtime.Profiles.Count(p => p.IsEnabled)),
            Accent = "#22C55E"
        });
        Metrics.Add(new MetricCardViewModel
        {
            Title = _localization.T("Metric.OptimizedApps.Title"),
            Value = _runtime.OptimizedProcessCount.ToString(),
            Detail = string.IsNullOrWhiteSpace(_runtime.LastAppliedProfile) ? _localization.T("Metric.OptimizedApps.Detail") : _runtime.LastAppliedProfile,
            Accent = "#3B82F6"
        });
        Metrics.Add(new MetricCardViewModel
        {
            Title = _localization.T("Metric.HardwareMonitor.Title"),
            Value = _localization.T("Metric.HardwareMonitor.Value"),
            Detail = _localization.T("Metric.HardwareMonitor.Detail"),
            Accent = "#F59E0B"
        });
        Metrics.Add(new MetricCardViewModel
        {
            Title = _localization.T("Metric.Debloat.Title"),
            Value = _localization.T("Metric.Debloat.Value"),
            Detail = _localization.T("Metric.Debloat.Detail"),
            Accent = "#94A3B8"
        });

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(FoundationPlanTitle));
        OnPropertyChanged(nameof(FoundationPlanDescription));
        OnPropertyChanged(nameof(TargetTitle));
        OnPropertyChanged(nameof(TargetDescription));
        OnPropertyChanged(nameof(MigrationTitle));
        OnPropertyChanged(nameof(MigrationDescription));
        OnPropertyChanged(nameof(RecentActivityTitle));
        OnPropertyChanged(nameof(ReadyStatus));
        OnPropertyChanged(nameof(RecentActivityLiveText));
    }
}
