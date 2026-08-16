using FrameHub.Companion.Models;
using FrameHub.Companion.Providers;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Benchmarking;
using FrameHub.Core.Services.Library;
using FrameHub.Core.Services.SessionOptimization;

namespace FrameHub.App.Services;

public sealed class AppTelemetrySnapshotProvider : ITelemetrySnapshotProvider, IDisposable
{
    private readonly AppRuntimeService _runtime;
    private readonly IActiveGameMonitor _activeGameMonitor;
    private readonly ILivePerformanceTelemetryService? _liveTelemetryService;
    private readonly object _lock = new();

    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private bool _disposed;
    private volatile CompanionTelemetrySnapshot _currentSnapshot;

    public CompanionTelemetrySnapshot CurrentSnapshot => _currentSnapshot;
    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _loopTask != null && !_loopTask.IsCompleted;
            }
        }
    }

    public AppTelemetrySnapshotProvider(
        AppRuntimeService runtime,
        IActiveGameMonitor? activeGameMonitor = null,
        ILivePerformanceTelemetryService? liveTelemetryService = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _activeGameMonitor = activeGameMonitor ?? runtime.ActiveGameMonitor ?? new ActiveGameMonitor();
        _liveTelemetryService = liveTelemetryService ?? runtime.LiveTelemetryService;

        _currentSnapshot = new CompanionTelemetrySnapshot(
            CapturedAtUtc: DateTimeOffset.UtcNow,
            Hardware: null,
            CurrentGame: null,
            LivePerformance: null
        );
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_disposed) return;
            if (_loopTask != null && !_loopTask.IsCompleted) return; // Idempotent

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _loopTask = Task.Run(() => RunPublicationLoopAsync(token), token);
        }
    }

    public async Task StopAsync()
    {
        Task? taskToWait = null;
        CancellationTokenSource? ctsToDispose = null;

        lock (_lock)
        {
            if (_loopTask == null) return;

            ctsToDispose = _cts;
            taskToWait = _loopTask;
            _cts = null;
            _loopTask = null;
        }

        if (ctsToDispose != null)
        {
            try { ctsToDispose.Cancel(); } catch { }
        }

        if (taskToWait != null)
        {
            try
            {
                await taskToWait.WaitAsync(TimeSpan.FromMilliseconds(1000)).ConfigureAwait(false);
            }
            catch
            {
                // Ignore task cancellation / timeout exceptions
            }
        }

        if (ctsToDispose != null)
        {
            try { ctsToDispose.Dispose(); } catch { }
        }
    }

    private async Task RunPublicationLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                UpdateSnapshotOnce();
                await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Ignore transient background update errors
            }
        }
    }

    public void UpdateSnapshotOnce()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        HardwareTelemetrySnapshot? hardwareSnapshot = null;

        if (_runtime.IsHardwareMonitoringActive)
        {
            var metrics = _runtime.GetHardwareMetrics();
            hardwareSnapshot = CreateHardwareSnapshot(metrics);
        }

        _currentSnapshot = new CompanionTelemetrySnapshot(
            CapturedAtUtc: now,
            Hardware: hardwareSnapshot,
            CurrentGame: ResolveCurrentGame(),
            LivePerformance: _liveTelemetryService?.CurrentSnapshot
        );
    }

    public static HardwareTelemetrySnapshot? CreateHardwareSnapshot(HardwareMetrics? metrics)
    {
        if (metrics == null) return null;

        double cpuLoad = Math.Clamp(metrics.CpuLoad, 0.0, 100.0);
        double gpuLoad = Math.Clamp(metrics.GpuLoad, 0.0, 100.0);

        long? ramUsedBytes = metrics.RamUsedGB > 0 ? (long)(metrics.RamUsedGB * 1024.0 * 1024.0 * 1024.0) : null;
        long? ramTotalBytes = (metrics.RamUsedGB + metrics.RamAvailableGB) > 0 ? (long)((metrics.RamUsedGB + metrics.RamAvailableGB) * 1024.0 * 1024.0 * 1024.0) : null;

        return new HardwareTelemetrySnapshot(
            CpuUtilizationPercent: cpuLoad,
            CpuTemperatureCelsius: metrics.CpuTemp is > 0 ? metrics.CpuTemp : null,
            GpuUtilizationPercent: gpuLoad,
            GpuTemperatureCelsius: metrics.GpuTemp > 0 ? metrics.GpuTemp : null,
            RamUsedBytes: ramUsedBytes,
            RamTotalBytes: ramTotalBytes,
            VramUsedBytes: metrics.VramUsedBytes,
            VramTotalBytes: metrics.VramTotalBytes
        );
    }

    private CurrentGameSnapshot? ResolveCurrentGame()
    {
        var activeGame = _activeGameMonitor.CurrentSnapshot;
        if (activeGame == null) return null;

        return new CurrentGameSnapshot(
            LibraryItemId: activeGame.LibraryItem.Id,
            DisplayName: activeGame.LibraryItem.DisplayName,
            IsRunning: true,
            ProcessStartTimeUtc: activeGame.Process.StartTimeUtc
        );
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        StopAsync().GetAwaiter().GetResult();
    }
}
