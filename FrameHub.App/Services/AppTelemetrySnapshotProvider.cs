using FrameHub.Companion.Models;
using FrameHub.Companion.Providers;
using FrameHub.Core.Services.Benchmarking;
using FrameHub.Core.Services.Library;
using FrameHub.Core.Services.SessionOptimization;

namespace FrameHub.App.Services;

public sealed class AppTelemetrySnapshotProvider : ITelemetrySnapshotProvider, IDisposable
{
    private readonly AppRuntimeService _runtime;
    private readonly BenchmarkGameDetectionService _gameDetector;
    private readonly SessionStateService _sessionStateService;
    private readonly LibraryService _libraryService;
    private readonly object _lock = new();

    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private bool _disposed;
    private volatile CompanionTelemetrySnapshot _currentSnapshot;

    private CurrentGameSnapshot? _cachedGameSnapshot;
    private DateTimeOffset _lastGameDetectionTime = DateTimeOffset.MinValue;

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
        BenchmarkGameDetectionService? gameDetector = null,
        SessionStateService? sessionStateService = null,
        LibraryService? libraryService = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _gameDetector = gameDetector ?? new BenchmarkGameDetectionService();
        _sessionStateService = sessionStateService ?? new SessionStateService();
        _libraryService = libraryService ?? new LibraryService();

        _currentSnapshot = new CompanionTelemetrySnapshot(
            CapturedAtUtc: DateTimeOffset.UtcNow,
            Hardware: null,
            CurrentGame: null
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
            double cpuLoad = Math.Clamp(metrics.CpuLoad, 0.0, 100.0);
            double gpuLoad = Math.Clamp(metrics.GpuLoad, 0.0, 100.0);

            long? ramUsedBytes = metrics.RamUsedGB > 0 ? (long)(metrics.RamUsedGB * 1024.0 * 1024.0 * 1024.0) : null;
            long? ramTotalBytes = (metrics.RamUsedGB + metrics.RamAvailableGB) > 0 ? (long)((metrics.RamUsedGB + metrics.RamAvailableGB) * 1024.0 * 1024.0 * 1024.0) : null;

            hardwareSnapshot = new HardwareTelemetrySnapshot(
                CpuUtilizationPercent: cpuLoad,
                CpuTemperatureCelsius: metrics.CpuTemp > 0 ? metrics.CpuTemp : null,
                GpuUtilizationPercent: gpuLoad,
                GpuTemperatureCelsius: metrics.GpuTemp > 0 ? metrics.GpuTemp : null,
                RamUsedBytes: ramUsedBytes,
                RamTotalBytes: ramTotalBytes,
                VramUsedBytes: metrics.VramUsagePct > 0 && ramTotalBytes.HasValue ? (long?)(metrics.VramUsagePct / 100.0 * 4096.0 * 1024.0 * 1024.0) : null,
                VramTotalBytes: null
            );
        }

        // Resolve game at a slower 2-second cadence to eliminate 2Hz disk/process polling
        if (now - _lastGameDetectionTime >= TimeSpan.FromSeconds(2))
        {
            _cachedGameSnapshot = ResolveCurrentGame();
            _lastGameDetectionTime = now;
        }

        _currentSnapshot = new CompanionTelemetrySnapshot(
            CapturedAtUtc: now,
            Hardware: hardwareSnapshot,
            CurrentGame: _cachedGameSnapshot
        );
    }

    private CurrentGameSnapshot? ResolveCurrentGame()
    {
        var activeSession = _sessionStateService.Load();
        var libraryItems = _libraryService.LoadItems();

        if (activeSession?.IsActive == true && !string.IsNullOrWhiteSpace(activeSession.GameName))
        {
            var matchedItem = libraryItems.FirstOrDefault(i =>
                string.Equals(i.DisplayName, activeSession.GameName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(i.Id, activeSession.GameId, StringComparison.OrdinalIgnoreCase));

            return new CurrentGameSnapshot(
                LibraryItemId: matchedItem?.Id,
                DisplayName: activeSession.GameName,
                IsRunning: true,
                ProcessStartTimeUtc: activeSession.StartedAtUtc
            );
        }

        var detectedGames = _gameDetector.Detect(libraryItems);
        if (detectedGames.Count == 1)
        {
            var running = detectedGames[0];
            return new CurrentGameSnapshot(
                LibraryItemId: running.LibraryItem.Id,
                DisplayName: running.LibraryItem.DisplayName,
                IsRunning: true,
                ProcessStartTimeUtc: running.Process.StartTimeUtc
            );
        }

        // If 0 games or multiple games detected without an active session: null (unambiguous requirement)
        return null;
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
