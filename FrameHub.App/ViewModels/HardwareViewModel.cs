using FrameHub.App.Helpers;
using FrameHub.App.Services;
using FrameHub.Core.Services;
using System.Windows.Threading;

namespace FrameHub.App.ViewModels;

public sealed class HardwareViewModel : ViewModelBase, IDisposable
{
    private readonly LocalizationService _localization;
    private readonly AppRuntimeService _runtime;
    private readonly DispatcherTimer _timer;
    private HardwareMonitorService? _hardwareMonitor;
    private bool _isMonitorEnabled;
    private bool _disposed;
    private bool _pollInProgress;
    private CancellationTokenSource? _pollCancellation;
    private bool _closeSensorsAfterPoll;
    private double _cpuTemp;
    private double _gpuTemp;
    private double _gpuLoad;
    private double _ramUsage;
    private double _vramUsage;

    public string Title => _localization.T("Hardware.Title");
    public string Subtitle => _localization.T("Hardware.Subtitle");
    public string TelemetryTitle => _localization.T("Hardware.Telemetry.Title");
    public string TelemetryDescription => _localization.T("Hardware.Telemetry.Description");
    public string MonitorTitle => _localization.T("Hardware.Monitor.Title");
    public string EnableMonitoringText => _localization.T("Hardware.Monitor.Enable");
    public string CpuLabel => _localization.T("Hardware.Cpu");
    public string GpuLabel => _localization.T("Hardware.Gpu");
    public string GpuLoadLabel => _localization.T("Hardware.GpuLoad");
    public string RamLabel => _localization.T("Hardware.Ram");
    public string VramLabel => _localization.T("Hardware.Vram");
    public string BackendStatusText => IsMonitorEnabled ? _localization.T("Hardware.BackendActive") : _localization.T("Hardware.BackendInactive");
    public string BackgroundBehaviorTitle => _localization.T("Hardware.BackgroundBehavior.Title");
    public string ProfileWatcherTitle => _localization.T("Hardware.ProfileWatcher.Title");
    public string ProfileWatcherStatus => _runtime.IsProfileWatcherActive ? _localization.T("Hardware.ProfileWatcher.StatusActive") : _localization.T("Hardware.ProfileWatcher.StatusInactive");
    public string StorageSensorsTitle => _localization.T("Hardware.StorageSensors.Title");
    public string StorageSensorsDescription => _localization.T("Hardware.StorageSensors.Description");
    public string RefreshModesTitle => _localization.T("Hardware.RefreshModes.Title");
    public string RefreshModesDescription => _localization.T("Hardware.RefreshModes.Description");

    public bool IsMonitorEnabled
    {
        get => _isMonitorEnabled;
        set
        {
            if (!SetProperty(ref _isMonitorEnabled, value)) return;
            if (value) StartMonitor();
            else StopMonitor(closeSensors: true);
            OnPropertyChanged(nameof(MonitorStatus));
            OnPropertyChanged(nameof(MonitorDescription));
            OnPropertyChanged(nameof(BackendStatusText));
        }
    }

    public string MonitorStatus => IsMonitorEnabled ? _localization.T("Hardware.Monitor.On") : _localization.T("Hardware.Monitor.Off");
    public string MonitorDescription => IsMonitorEnabled
        ? _localization.T("Hardware.Monitor.DescriptionOn")
        : _localization.T("Hardware.Monitor.DescriptionOff");

    public string CpuTempText => IsMonitorEnabled ? $"{CpuTemp:N1} °C" : "-- °C";
    public string GpuTempText => IsMonitorEnabled ? $"{GpuTemp:N1} °C" : "-- °C";
    public string GpuLoadText => IsMonitorEnabled ? $"{GpuLoad:N1}%" : "--%";
    public string RamUsageText => IsMonitorEnabled ? $"{RamUsage:N1}%" : "--%";
    public string VramUsageText => IsMonitorEnabled ? $"{VramUsage:N1}%" : "--%";

    public double CpuTemp { get => _cpuTemp; set { if (SetProperty(ref _cpuTemp, value)) OnPropertyChanged(nameof(CpuTempText)); } }
    public double GpuTemp { get => _gpuTemp; set { if (SetProperty(ref _gpuTemp, value)) OnPropertyChanged(nameof(GpuTempText)); } }
    public double GpuLoad { get => _gpuLoad; set { if (SetProperty(ref _gpuLoad, value)) OnPropertyChanged(nameof(GpuLoadText)); } }
    public double RamUsage { get => _ramUsage; set { if (SetProperty(ref _ramUsage, value)) OnPropertyChanged(nameof(RamUsageText)); } }
    public double VramUsage { get => _vramUsage; set { if (SetProperty(ref _vramUsage, value)) OnPropertyChanged(nameof(VramUsageText)); } }

    public HardwareViewModel(LocalizationService localization, AppRuntimeService runtime)
    {
        _localization = localization;
        _runtime = runtime;
        _runtime.WatcherStateChanged += (_, _) => OnPropertyChanged(nameof(ProfileWatcherStatus));
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Clamp(_runtime.Settings.HardwareRefreshSeconds, 1, 10))
        };
        _timer.Tick += async (_, _) => await UpdateMetricsAsync();
    }

    private void StartMonitor()
    {
        _closeSensorsAfterPoll = false;
        _hardwareMonitor ??= new HardwareMonitorService(_runtime.Settings.EnableStorageSensors);
        _hardwareMonitor.Configure(_runtime.Settings.EnableStorageSensors);
        _timer.Start();
        _ = UpdateMetricsAsync();
        _runtime.AddActivity("Hardware monitor enabled.");
    }

    private void StopMonitor(bool closeSensors)
    {
        _timer.Stop();
        _pollCancellation?.Cancel();
        if (closeSensors && _pollInProgress)
        {
            _closeSensorsAfterPoll = true;
        }
        else
        {
            _hardwareMonitor?.Stop(closeSensors);
            if (closeSensors)
            {
                _hardwareMonitor?.Dispose();
                _hardwareMonitor = null;
            }
        }
        _runtime.AddActivity("Hardware monitor disabled.");
    }

    private async Task UpdateMetricsAsync()
    {
        if (_hardwareMonitor == null || !IsMonitorEnabled || _pollInProgress || _disposed) return;
        _pollInProgress = true;
        _pollCancellation?.Cancel();
        _pollCancellation = new CancellationTokenSource();
        var cancellation = _pollCancellation.Token;

        try
        {
            var monitor = _hardwareMonitor;
            var metrics = await Task.Run(monitor.GetAllMetrics, cancellation);
            if (cancellation.IsCancellationRequested || !IsMonitorEnabled || _disposed) return;
            CpuTemp = Math.Round(metrics.CpuTemp, 1);
            GpuTemp = Math.Round(metrics.GpuTemp, 1);
            GpuLoad = Math.Round(metrics.GpuLoad, 1);
            RamUsage = Math.Round(metrics.RamUsagePct, 1);
            VramUsage = Math.Round(metrics.VramUsagePct, 1);
        }
        catch (Exception ex)
        {
            if (!cancellation.IsCancellationRequested) _runtime.AddActivity($"Hardware monitor update failed: {ex.Message}", "Warn");
        }
        finally
        {
            _pollInProgress = false;
            if (_closeSensorsAfterPoll)
            {
                _closeSensorsAfterPoll = false;
                _hardwareMonitor?.Dispose();
                _hardwareMonitor = null;
            }
        }
    }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(TelemetryTitle));
        OnPropertyChanged(nameof(TelemetryDescription));
        OnPropertyChanged(nameof(MonitorTitle));
        OnPropertyChanged(nameof(EnableMonitoringText));
        OnPropertyChanged(nameof(MonitorStatus));
        OnPropertyChanged(nameof(MonitorDescription));
        OnPropertyChanged(nameof(CpuLabel));
        OnPropertyChanged(nameof(GpuLabel));
        OnPropertyChanged(nameof(GpuLoadLabel));
        OnPropertyChanged(nameof(RamLabel));
        OnPropertyChanged(nameof(VramLabel));
        OnPropertyChanged(nameof(BackendStatusText));
        OnPropertyChanged(nameof(BackgroundBehaviorTitle));
        OnPropertyChanged(nameof(ProfileWatcherTitle));
        OnPropertyChanged(nameof(ProfileWatcherStatus));
        OnPropertyChanged(nameof(StorageSensorsTitle));
        OnPropertyChanged(nameof(StorageSensorsDescription));
        OnPropertyChanged(nameof(RefreshModesTitle));
        OnPropertyChanged(nameof(RefreshModesDescription));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pollCancellation?.Cancel();
        _pollCancellation?.Dispose();
        StopMonitor(closeSensors: true);
    }
}
