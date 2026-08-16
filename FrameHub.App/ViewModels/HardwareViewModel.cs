using FrameHub.App.Helpers;
using FrameHub.App.Services;
using FrameHub.Core.Services;
using System.Windows.Threading;

namespace FrameHub.App.ViewModels;

public sealed class HardwareViewModel : ViewModelBase, IDisposable
{
    private readonly LocalizationService _localization;
    private readonly AppRuntimeService _runtime;
    private readonly Func<bool> _isElevatedProbe;
    private readonly DispatcherTimer _timer;
    private IHardwareMonitorLease? _hardwareLease;
    private bool _pageActive;
    private bool _disposed;
    private bool _pollInProgress;
    private CancellationTokenSource? _pollCancellation;
    private double? _cpuTemp;
    private double _cpuLoad;
    private double _gpuTemp;
    private double _gpuLoad;
    private double _ramUsage;
    private double _ramUsedGb;
    private double _ramTotalGb;
    private double _vramUsage;

    public string Title => _localization.T("Hardware.Title");
    public string Subtitle => _localization.T("Hardware.Subtitle");
    public string TelemetryTitle => _localization.T("Hardware.Telemetry.Title");
    public string TelemetryDescription => _localization.T("Hardware.Telemetry.Description");
    public string MonitorTitle => _localization.T("Hardware.Monitor.Title");
    public string EnableMonitoringText => _localization.T("Hardware.Monitor.Enable");
    public string CpuLabel => _localization.T("Hardware.Cpu");
    public string GpuLabel => _localization.T("Hardware.Gpu");
    public string CpuLoadLabel => _localization.T("Hardware.CpuLoad");
    public string GpuLoadLabel => _localization.T("Hardware.GpuLoad");
    public string RamLabel => _localization.T("Hardware.Ram");
    public string VramLabel => _localization.T("Hardware.Vram");
    public string CpuElevationHintText => _localization.T("Hardware.CpuElevationHint");
    public string BackendStatusText => IsMonitorEnabled ? _localization.T("Hardware.BackendActive") : _localization.T("Hardware.BackendInactive");
    public string BackgroundBehaviorTitle => _localization.T("Hardware.BackgroundBehavior.Title");
    public string ProfileWatcherTitle => _localization.T("Hardware.ProfileWatcher.Title");
    public string ProfileWatcherStatus => _runtime.IsProfileWatcherActive ? _localization.T("Hardware.ProfileWatcher.StatusActive") : _localization.T("Hardware.ProfileWatcher.StatusInactive");
    public string StorageSensorsTitle => _localization.T("Hardware.StorageSensors.Title");
    public string StorageSensorsDescription => _localization.T("Hardware.StorageSensors.Description");
    public string RefreshModesTitle => _localization.T("Hardware.RefreshModes.Title");
    public string RefreshModesDescription => _localization.T("Hardware.RefreshModes.Description");

    public bool IsProcessElevated { get; }

    /// <summary>
    /// Represents the persisted global <see cref="Core.Models.AppSettings.HardwareMonitorEnabled"/> choice.
    /// Toggling persists through the runtime settings path and reconciles the shared sensor backend.
    /// </summary>
    public bool IsMonitorEnabled
    {
        get => _runtime.Settings.HardwareMonitorEnabled;
        set
        {
            if (_runtime.Settings.HardwareMonitorEnabled == value) return;
            _runtime.SetHardwareMonitorEnabled(value);
            OnSettingsChanged();
        }
    }

    public string MonitorStatus => IsMonitorEnabled ? _localization.T("Hardware.Monitor.On") : _localization.T("Hardware.Monitor.Off");
    public string MonitorDescription => IsMonitorEnabled
        ? _localization.T("Hardware.Monitor.DescriptionOn")
        : _localization.T("Hardware.Monitor.DescriptionOff");

    public static string FormatCpuTemp(bool isMonitorEnabled, double? cpuTemp)
        => isMonitorEnabled && cpuTemp.HasValue && cpuTemp.Value > 0 ? $"{cpuTemp.Value:N1} °C" : "-- °C";

    public static string FormatLoadPercent(bool isMonitorEnabled, double load)
        => isMonitorEnabled ? $"{load:N0}%" : "--%";

    public static string FormatUsedTotalGb(bool isMonitorEnabled, double usedGb, double totalGb)
        => isMonitorEnabled && totalGb > 0 ? $"{usedGb:N1} / {totalGb:N1} GB" : "-- / -- GB";

    public static string FormatVramUsedTotalGb(bool isMonitorEnabled, long? usedBytes, long? totalBytes)
        => isMonitorEnabled && usedBytes.HasValue && totalBytes.HasValue && totalBytes.Value > 0
            ? $"{usedBytes.Value / (1024.0 * 1024.0 * 1024.0):N1} / {totalBytes.Value / (1024.0 * 1024.0 * 1024.0):N1} GB"
            : "-- / -- GB";

    public static bool ShouldShowCpuElevationHint(bool isMonitorEnabled, double? cpuTemp, bool isProcessElevated)
        => isMonitorEnabled && (!cpuTemp.HasValue || cpuTemp.Value <= 0) && !isProcessElevated;

    public string CpuTempText => FormatCpuTemp(IsMonitorEnabled, CpuTemp);
    public string CpuLoadText => FormatLoadPercent(IsMonitorEnabled, CpuLoad);
    public string GpuTempText => IsMonitorEnabled && GpuTemp > 0 ? $"{GpuTemp:N1} °C" : "-- °C";
    public string GpuLoadText => FormatLoadPercent(IsMonitorEnabled, GpuLoad);
    public string RamUsageText => FormatLoadPercent(IsMonitorEnabled, RamUsage);
    public string RamUsedTotalText => FormatUsedTotalGb(IsMonitorEnabled, RamUsedGb, RamTotalGb);
    public string VramUsageText => FormatLoadPercent(IsMonitorEnabled, VramUsage);
    public string VramUsedTotalText => FormatVramUsedTotalGb(IsMonitorEnabled, VramUsedBytes, VramTotalBytes);

    public bool IsCpuElevationHintVisible => ShouldShowCpuElevationHint(IsMonitorEnabled, CpuTemp, IsProcessElevated);

    public double? CpuTemp { get => _cpuTemp; set { if (SetProperty(ref _cpuTemp, value)) { OnPropertyChanged(nameof(CpuTempText)); OnPropertyChanged(nameof(IsCpuElevationHintVisible)); } } }
    public double CpuLoad { get => _cpuLoad; set { if (SetProperty(ref _cpuLoad, value)) OnPropertyChanged(nameof(CpuLoadText)); } }
    public double GpuTemp { get => _gpuTemp; set { if (SetProperty(ref _gpuTemp, value)) OnPropertyChanged(nameof(GpuTempText)); } }
    public double GpuLoad { get => _gpuLoad; set { if (SetProperty(ref _gpuLoad, value)) OnPropertyChanged(nameof(GpuLoadText)); } }
    public double RamUsage { get => _ramUsage; set { if (SetProperty(ref _ramUsage, value)) OnPropertyChanged(nameof(RamUsageText)); } }
    public double RamUsedGb { get => _ramUsedGb; set { if (SetProperty(ref _ramUsedGb, value)) OnPropertyChanged(nameof(RamUsedTotalText)); } }
    public double RamTotalGb { get => _ramTotalGb; set { if (SetProperty(ref _ramTotalGb, value)) OnPropertyChanged(nameof(RamUsedTotalText)); } }
    public double VramUsage { get => _vramUsage; set { if (SetProperty(ref _vramUsage, value)) OnPropertyChanged(nameof(VramUsageText)); } }
    public long? VramUsedBytes { get; private set; }
    public long? VramTotalBytes { get; private set; }

    private void SetVramBytes(long? usedBytes, long? totalBytes)
    {
        bool changed = VramUsedBytes != usedBytes || VramTotalBytes != totalBytes;
        VramUsedBytes = usedBytes;
        VramTotalBytes = totalBytes;
        if (changed) OnPropertyChanged(nameof(VramUsedTotalText));
    }

    public HardwareViewModel(LocalizationService localization, AppRuntimeService runtime, Func<bool>? isElevatedProbe = null)
    {
        _localization = localization;
        _runtime = runtime;
        _isElevatedProbe = isElevatedProbe ?? runtime.SettingsService.IsRunAsAdmin;
        IsProcessElevated = _isElevatedProbe();
        _runtime.WatcherStateChanged += (_, _) => OnPropertyChanged(nameof(ProfileWatcherStatus));
        _runtime.RuntimeStateChanged += (_, _) => OnSettingsChanged();
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Clamp(_runtime.Settings.HardwareRefreshSeconds, 1, 10))
        };
        _timer.Tick += async (_, _) => await UpdateMetricsAsync();
    }

    private void OnSettingsChanged()
    {
        OnPropertyChanged(nameof(IsMonitorEnabled));
        OnPropertyChanged(nameof(MonitorStatus));
        OnPropertyChanged(nameof(MonitorDescription));
        OnPropertyChanged(nameof(BackendStatusText));
        OnPropertyChanged(nameof(IsCpuElevationHintVisible));
        OnPropertyChanged(nameof(CpuTempText));
        OnPropertyChanged(nameof(CpuLoadText));
        OnPropertyChanged(nameof(GpuTempText));
        OnPropertyChanged(nameof(GpuLoadText));
        OnPropertyChanged(nameof(RamUsageText));
        OnPropertyChanged(nameof(RamUsedTotalText));
        OnPropertyChanged(nameof(VramUsageText));
        OnPropertyChanged(nameof(VramUsedTotalText));
        SyncPollingWithState();
    }

    /// <summary>
    /// Registers the Hardware page as an active hardware consumer. Safe to call repeatedly;
    /// each activation holds exactly one lease until <see cref="Deactivate"/> or disposal.
    /// </summary>
    public void Activate()
    {
        if (_disposed) return;
        if (!_pageActive)
        {
            _pageActive = true;
            _hardwareLease ??= _runtime.AcquireHardwareLease();
        }
        SyncPollingWithState();
    }

    /// <summary>
    /// Releases the Hardware page consumer. Navigating away never changes the persisted setting.
    /// </summary>
    public void Deactivate()
    {
        if (!_pageActive) return;
        _pageActive = false;
        StopPolling();
        if (!_pollInProgress)
        {
            ReleaseLeaseIfCurrent(_hardwareLease);
        }
    }

    private void SyncPollingWithState()
    {
        if (_pageActive && IsMonitorEnabled && !_timer.IsEnabled)
        {
            _timer.Start();
            _ = UpdateMetricsAsync();
        }
        else if ((!_pageActive || !IsMonitorEnabled) && _timer.IsEnabled)
        {
            StopPolling();
        }
    }

    private void StopPolling()
    {
        _timer.Stop();
        try
        {
            _pollCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void ReleaseLeaseIfCurrent(IHardwareMonitorLease? lease)
    {
        if (lease != null && ReferenceEquals(_hardwareLease, lease))
        {
            _hardwareLease = null;
            lease.Dispose();
        }
    }

    private async Task UpdateMetricsAsync()
    {
        var lease = _hardwareLease;
        if (lease == null || !_pageActive || !IsMonitorEnabled || _pollInProgress || _disposed) return;
        _pollInProgress = true;
        _pollCancellation?.Cancel();
        _pollCancellation = new CancellationTokenSource();
        var cancellation = _pollCancellation.Token;

        try
        {
            var metrics = await Task.Run(_runtime.GetHardwareMetrics, cancellation);
            if (cancellation.IsCancellationRequested || !_pageActive || !IsMonitorEnabled || _disposed) return;
            CpuTemp = metrics.CpuTemp.HasValue ? Math.Round(metrics.CpuTemp.Value, 1) : null;
            CpuLoad = Math.Round(metrics.CpuLoad, 1);
            GpuTemp = Math.Round(metrics.GpuTemp, 1);
            GpuLoad = Math.Round(metrics.GpuLoad, 1);
            RamUsage = Math.Round(metrics.RamUsagePct, 1);
            RamUsedGb = Math.Round(metrics.RamUsedGB, 1);
            RamTotalGb = Math.Round(metrics.RamUsedGB + metrics.RamAvailableGB, 1);
            VramUsage = Math.Round(metrics.VramUsagePct, 1);
            SetVramBytes(metrics.VramUsedBytes, metrics.VramTotalBytes);
        }
        catch (Exception ex)
        {
            if (!cancellation.IsCancellationRequested) _runtime.AddActivity($"Hardware monitor update failed: {ex.Message}", "Warn");
        }
        finally
        {
            _pollInProgress = false;
            if (!_pageActive)
            {
                ReleaseLeaseIfCurrent(lease);
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
        OnPropertyChanged(nameof(CpuLoadLabel));
        OnPropertyChanged(nameof(GpuLoadLabel));
        OnPropertyChanged(nameof(RamLabel));
        OnPropertyChanged(nameof(VramLabel));
        OnPropertyChanged(nameof(CpuElevationHintText));
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
        _pageActive = false;
        StopPolling();
        _hardwareLease?.Dispose();
        _hardwareLease = null;
        _pollCancellation?.Dispose();
        _pollCancellation = null;
    }
}
