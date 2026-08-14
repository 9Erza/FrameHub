using FrameHub.Core.Logging;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Models.SessionOptimization;
using FrameHub.Core.Services.Library;
using FrameHub.Core.Services.SessionOptimization;

namespace FrameHub.Core.Services.Benchmarking;

public sealed record ActiveGameSnapshot(
    LibraryItem LibraryItem,
    BenchmarkProcessIdentity Process
);

public interface IActiveGameMonitor : IDisposable
{
    ActiveGameSnapshot? CurrentSnapshot { get; }
    void Start();
    Task StopAsync();
}

public sealed class ActiveGameMonitor : IActiveGameMonitor
{
    private readonly BenchmarkGameDetectionService _gameDetector;
    private readonly SessionStateService _sessionStateService;
    private readonly LibraryService _libraryService;
    private readonly ILogger _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayProvider;
    private readonly Func<List<LibraryItem>> _libraryLoader;
    private readonly Func<ActiveSessionState?> _sessionStateLoader;
    private readonly object _lock = new();

    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private long _loopGeneration;
    private bool _disposed;
    private volatile ActiveGameSnapshot? _currentSnapshot;

    public ActiveGameSnapshot? CurrentSnapshot => _currentSnapshot;

    public ActiveGameMonitor(
        BenchmarkGameDetectionService? gameDetector = null,
        SessionStateService? sessionStateService = null,
        LibraryService? libraryService = null,
        ILogger? logger = null,
        Func<TimeSpan, CancellationToken, Task>? delayProvider = null,
        Func<List<LibraryItem>>? libraryLoader = null,
        Func<ActiveSessionState?>? sessionStateLoader = null)
    {
        _gameDetector = gameDetector ?? new BenchmarkGameDetectionService();
        _sessionStateService = sessionStateService ?? new SessionStateService();
        _libraryService = libraryService ?? new LibraryService();
        _logger = logger ?? LoggerService.Instance;
        _delayProvider = delayProvider ?? Task.Delay;
        _libraryLoader = libraryLoader ?? (() => _libraryService.LoadItems());
        _sessionStateLoader = sessionStateLoader ?? (() => _sessionStateService.Load());
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_disposed) return;
            if (_loopTask != null && !_loopTask.IsCompleted) return;

            long generation = ++_loopGeneration;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _loopTask = Task.Run(() => RunScanLoopAsync(generation, token), token);
        }
    }

    public async Task StopAsync()
    {
        Task? taskToWait = null;
        CancellationTokenSource? ctsToDispose = null;

        lock (_lock)
        {
            _loopGeneration++;
            _currentSnapshot = null;
            if (_loopTask == null)
            {
                return;
            }

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
            catch { }
        }

        if (ctsToDispose != null)
        {
            try { ctsToDispose.Dispose(); } catch { }
        }

        _currentSnapshot = null;
    }

    public void UpdateSnapshotOnce()
    {
        _currentSnapshot = ResolveActiveGame();
    }

    private async Task RunScanLoopAsync(long generation, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                ActiveGameSnapshot? candidate = ResolveActiveGame();
                lock (_lock)
                {
                    if (generation != _loopGeneration || cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    _currentSnapshot = candidate;
                }
                await _delayProvider(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Warn($"ActiveGameMonitor scan error: {ex.Message}");
            }
        }
    }

    private ActiveGameSnapshot? ResolveActiveGame()
    {
        List<LibraryItem> libraryItems;
        try
        {
            libraryItems = _libraryLoader();
        }
        catch (Exception ex)
        {
            _logger.Warn($"ActiveGameMonitor failed to load library items: {ex.Message}");
            return null;
        }

        IReadOnlyList<BenchmarkRunningGame> detectedGames;
        try
        {
            detectedGames = _gameDetector.Detect(libraryItems);
        }
        catch (Exception ex)
        {
            _logger.Warn($"ActiveGameMonitor failed to detect running games: {ex.Message}");
            return null;
        }

        if (detectedGames.Count == 0)
        {
            return null;
        }

        if (detectedGames.Count == 1)
        {
            var single = detectedGames[0];
            return new ActiveGameSnapshot(single.LibraryItem, single.Process);
        }

        // Multiple running games detected: use SessionStateService ONLY to disambiguate which detected game is active.
        try
        {
            var activeSession = _sessionStateLoader();
            if (activeSession?.IsActive == true)
            {
                var matches = detectedGames.Where(g =>
                    (!string.IsNullOrWhiteSpace(activeSession.GameId) && string.Equals(g.LibraryItem.Id, activeSession.GameId, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(activeSession.GameName) && string.Equals(g.LibraryItem.DisplayName, activeSession.GameName, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(activeSession.GameProcessName) && string.Equals(g.Process.ProcessName, activeSession.GameProcessName, StringComparison.OrdinalIgnoreCase))
                ).ToList();

                if (matches.Count == 1)
                {
                    var match = matches[0];
                    return new ActiveGameSnapshot(match.LibraryItem, match.Process);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"ActiveGameMonitor failed to disambiguate active session: {ex.Message}");
        }

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
