using FrameHub.Core.Logging;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Services.Library;

namespace FrameHub.Core.Services.Benchmarking;

public interface ILivePerformanceTelemetryService : IDisposable
{
    LivePerformanceSnapshot? CurrentSnapshot { get; }
    void Start();
    Task StopAsync();
}

public sealed class LivePerformanceTelemetryService : ILivePerformanceTelemetryService, ILivePresentMonPreemption
{
    private const uint FrameCapacity = 256;
    private static readonly PmMetric[] RequestedMetrics = [PmMetric.SwapChainAddress, PmMetric.BetweenPresents];

    private readonly IActiveGameMonitor _activeGameMonitor;
    private readonly IBenchmarkCaptureCoordinator _benchmarkCoordinator;
    private readonly Func<IPresentMonApi> _apiFactory;
    private readonly ILogger _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayProvider;
    private readonly object _lock = new();
    private readonly object _preemptionLock = new();

    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private bool _disposed;
    private volatile LivePerformanceSnapshot? _currentSnapshot;

    private volatile bool _preempted;
    private bool _ownsNativeSession;
    private bool _preemptionReleaseRequested;
    private NativeOwnershipState _nativeOwnershipState = NativeOwnershipState.Released;
    private long _nativeGeneration;
    private long _activeNativeGeneration;
    private TaskCompletionSource<bool>? _nativeSessionReleased;

    public LivePerformanceSnapshot? CurrentSnapshot => _currentSnapshot;

    public LivePerformanceTelemetryService(
        IActiveGameMonitor activeGameMonitor,
        IBenchmarkCaptureCoordinator benchmarkCoordinator,
        Func<IPresentMonApi>? apiFactory = null,
        ILogger? logger = null,
        Func<TimeSpan, CancellationToken, Task>? delayProvider = null)
    {
        _activeGameMonitor = activeGameMonitor ?? throw new ArgumentNullException(nameof(activeGameMonitor));
        _benchmarkCoordinator = benchmarkCoordinator ?? throw new ArgumentNullException(nameof(benchmarkCoordinator));
        _apiFactory = apiFactory ?? (() => new PresentMonApi());
        _logger = logger ?? LoggerService.Instance;
        _delayProvider = delayProvider ?? Task.Delay;

        _benchmarkCoordinator.StateChanged += OnBenchmarkStateChanged;
    }

    private void OnBenchmarkStateChanged(object? sender, BenchmarkCaptureStateSnapshot e)
    {
        if (e.IsActive)
        {
            lock (_preemptionLock)
            {
                _preemptionReleaseRequested = false;
                _preempted = true;
            }
            _currentSnapshot = null;
        }
        else
        {
            ReleasePresentMonPreemption();
        }
    }

    public async Task<bool> RequestPresentMonReleaseAsync(CancellationToken cancellationToken)
    {
        _currentSnapshot = null;

        Task<bool>? releaseTask;
        bool releaseConfirmed;
        lock (_preemptionLock)
        {
            _preemptionReleaseRequested = false;
            _preempted = true;
            releaseTask = _ownsNativeSession ? _nativeSessionReleased?.Task : null;
            releaseConfirmed = _nativeOwnershipState == NativeOwnershipState.Released;
        }

        return releaseTask != null
            ? await releaseTask.WaitAsync(cancellationToken).ConfigureAwait(false)
            : releaseConfirmed;
    }

    public void ReleasePresentMonPreemption()
    {
        lock (_preemptionLock)
        {
            if (_ownsNativeSession)
            {
                _preemptionReleaseRequested = true;
                return;
            }

            if (_nativeOwnershipState == NativeOwnershipState.ReleaseFailedRecoverable)
            {
                _nativeOwnershipState = NativeOwnershipState.RecoveryPending;
            }
            _preemptionReleaseRequested = false;
            _preempted = false;
        }
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_disposed) return;
            if (_loopTask != null && !_loopTask.IsCompleted) return;

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _loopTask = Task.Run(() => RunTelemetryLoopAsync(token), token);
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
            catch { }
        }

        if (ctsToDispose != null)
        {
            try { ctsToDispose.Dispose(); } catch { }
        }
    }

    private async Task RunTelemetryLoopAsync(CancellationToken cancellationToken)
    {
        IPresentMonApi? api = null;
        nint session = 0;
        nint query = 0;
        uint trackingPid = 0;
        string? trackingLibraryItemId = null;
        DateTime trackingProcessStartTimeUtc = DateTime.MinValue;
        PmQueryElement[] elements = Array.Empty<PmQueryElement>();
        uint blobSize = 0;
        byte[] blobs = Array.Empty<byte>();
        IReadOnlyDictionary<PmMetric, PmFrameMetricInfo>? availableMetrics = null;

        var swapChainBuffers = new Dictionary<string, List<(double FrametimeMs, DateTime ReceivedAt)>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_preempted || _benchmarkCoordinator.IsActive)
                {
                    _preempted = true;
                    _currentSnapshot = null;
                    swapChainBuffers.Clear();
                    TeardownOwnedSession(ref api, ref session, ref query, ref trackingPid);

                    await WaitDelayAsync(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                _preempted = false;

                var activeGame = _activeGameMonitor.CurrentSnapshot;
                if (activeGame == null || activeGame.Process.ProcessId <= 0)
                {
                    _currentSnapshot = null;
                    swapChainBuffers.Clear();
                    TeardownOwnedSession(ref api, ref session, ref query, ref trackingPid);

                    await WaitDelayAsync(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // Eligibility gate before any PresentMon session creation: active-game visibility
                // alone never authorizes PresentMon. Benchmark-ineligible games (AllowBenchmark == false)
                // and any protected Riot identity tear down an existing session and stay idle.
                if (!IsLiveTelemetryEligible(activeGame))
                {
                    _currentSnapshot = null;
                    swapChainBuffers.Clear();
                    TeardownOwnedSession(ref api, ref session, ref query, ref trackingPid);

                    await WaitDelayAsync(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                int targetPid = activeGame.Process.ProcessId;
                string? targetLibraryItemId = activeGame.LibraryItem.Id;
                DateTime targetStartTimeUtc = activeGame.Process.StartTimeUtc;

                if (trackingPid != (uint)targetPid ||
                    trackingProcessStartTimeUtc != targetStartTimeUtc ||
                    !string.Equals(trackingLibraryItemId, targetLibraryItemId, StringComparison.OrdinalIgnoreCase))
                {
                    _currentSnapshot = null;
                    swapChainBuffers.Clear();
                    TeardownOwnedSession(ref api, ref session, ref query, ref trackingPid);
                }

                if (session == 0)
                {
                    if (_benchmarkCoordinator.IsActive || _preempted) continue;

                    if (!TryAcquireNativeSessionOwnership())
                    {
                        await WaitDelayAsync(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    try
                    {
                        api = _apiFactory();
                        PmStatus openStatus = api.OpenSession(out session);
                        if (openStatus != PmStatus.Success)
                        {
                            throw new BenchmarkException("presentmon_api_status", $"OpenSession returned {openStatus}");
                        }

                        availableMetrics = api.GetFrameMetricInfo(session);
                        PmMetric[] validMetrics = RequestedMetrics
                            .Where(m => availableMetrics.TryGetValue(m, out var info) &&
                                        info.MetricType is PmMetricType.FrameEvent or PmMetricType.DynamicFrame &&
                                        info.FrameType != PmDataType.Void)
                            .ToArray();

                        if (!validMetrics.Contains(PmMetric.SwapChainAddress) || !validMetrics.Contains(PmMetric.BetweenPresents))
                        {
                            throw new BenchmarkException("presentmon_api_metric_unavailable", "PresentMon API missing required metrics for live telemetry.");
                        }

                        elements = validMetrics.Select(m => new PmQueryElement { Metric = m, Stat = PmStat.None }).ToArray();
                        PmStatus regStatus = api.RegisterFrameQuery(session, elements, out query, out blobSize);
                        if (regStatus != PmStatus.Success || blobSize == 0)
                        {
                            throw new BenchmarkException("presentmon_api_invalid_blob", $"RegisterFrameQuery failed with {regStatus}, blobSize={blobSize}");
                        }
                        blobs = new byte[checked((int)(blobSize * FrameCapacity))];

                        if (_benchmarkCoordinator.IsActive || _preempted)
                        {
                            TeardownOwnedSession(ref api, ref session, ref query, ref trackingPid);
                            continue;
                        }

                        PmStatus trackStatus = api.StartTrackingProcess(session, (uint)targetPid);
                        if (trackStatus != PmStatus.Success)
                        {
                            throw new BenchmarkException("presentmon_api_status", $"StartTrackingProcess failed with {trackStatus}");
                        }

                        trackingPid = (uint)targetPid;
                        trackingLibraryItemId = targetLibraryItemId;
                        trackingProcessStartTimeUtc = targetStartTimeUtc;
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn($"LivePerformanceTelemetryService PresentMon session init failed: {ex.Message}");
                        _currentSnapshot = null;
                        swapChainBuffers.Clear();
                        TeardownOwnedSession(ref api, ref session, ref query, ref trackingPid);

                        await WaitDelayAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                }

                if (_benchmarkCoordinator.IsActive || _preempted)
                {
                    _currentSnapshot = null;
                    swapChainBuffers.Clear();
                    TeardownOwnedSession(ref api, ref session, ref query, ref trackingPid);
                    continue;
                }

                DateTime now = DateTime.UtcNow;
                uint count = FrameCapacity;

                PmStatus consumeStatus;
                try
                {
                    consumeStatus = api!.ConsumeFrames(query, trackingPid, blobs, ref count);
                }
                catch (Exception ex)
                {
                    _logger.Warn($"ConsumeFrames threw exception: {ex.Message}");
                    consumeStatus = PmStatus.Failure;
                }

                if (consumeStatus != PmStatus.Success)
                {
                    _logger.Warn($"Live telemetry consume error: {consumeStatus}");
                    _currentSnapshot = null;
                    swapChainBuffers.Clear();
                    TeardownOwnedSession(ref api, ref session, ref query, ref trackingPid);

                    await WaitDelayAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (count > 0 && availableMetrics != null)
                {
                    for (uint i = 0; i < count; i++)
                    {
                        ReadOnlySpan<byte> blobSpan = blobs.AsSpan(checked((int)(i * blobSize)), checked((int)blobSize));
                        DecodeAndBufferFrame(blobSpan, elements, availableMetrics, now, swapChainBuffers);
                    }
                }

                PruneRollingBuffers(swapChainBuffers, now);

                if (_benchmarkCoordinator.IsActive || _preempted)
                {
                    _currentSnapshot = null;
                    swapChainBuffers.Clear();
                    TeardownOwnedSession(ref api, ref session, ref query, ref trackingPid);
                }
                else
                {
                    _currentSnapshot = CalculateLiveSnapshot(swapChainBuffers, (int)trackingPid, trackingLibraryItemId, now);
                }

                await WaitDelayAsync(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _currentSnapshot = null;
            TeardownOwnedSession(ref api, ref session, ref query, ref trackingPid);
        }
    }

    private async Task WaitDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await _delayProvider(delay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    private static void DecodeAndBufferFrame(
        ReadOnlySpan<byte> blob,
        PmQueryElement[] elements,
        IReadOnlyDictionary<PmMetric, PmFrameMetricInfo> types,
        DateTime receivedAt,
        Dictionary<string, List<(double FrametimeMs, DateTime ReceivedAt)>> swapChainBuffers)
    {
        bool hasSwap = TryFindMetric(elements, PmMetric.SwapChainAddress, out PmQueryElement swap);
        string swapAddress = hasSwap ? $"0x{ReadU64(blob, swap):X}" : "0x0";

        double? betweenPresents = ReadMetricNumber(blob, elements, types, PmMetric.BetweenPresents);
        if (betweenPresents is > 0 and var ft && double.IsFinite(ft))
        {
            if (!swapChainBuffers.TryGetValue(swapAddress, out var buffer))
            {
                buffer = new List<(double FrametimeMs, DateTime ReceivedAt)>();
                swapChainBuffers[swapAddress] = buffer;
            }
            buffer.Add((ft, receivedAt));
        }
    }

    private static void PruneRollingBuffers(
        Dictionary<string, List<(double FrametimeMs, DateTime ReceivedAt)>> swapChainBuffers,
        DateTime now)
    {
        var emptyChains = new List<string>();
        foreach (var (chainAddress, buffer) in swapChainBuffers)
        {
            buffer.RemoveAll(sample => (now - sample.ReceivedAt).TotalSeconds > 3.0);
            if (buffer.Count == 0)
            {
                emptyChains.Add(chainAddress);
            }
        }
        foreach (var empty in emptyChains)
        {
            swapChainBuffers.Remove(empty);
        }
    }

    private static LivePerformanceSnapshot? CalculateLiveSnapshot(
        Dictionary<string, List<(double FrametimeMs, DateTime ReceivedAt)>> swapChainBuffers,
        int pid,
        string? libraryItemId,
        DateTime now)
    {
        if (swapChainBuffers.Count == 0) return null;

        var selectedPair = swapChainBuffers
            .Select(kvp => new
            {
                Address = kvp.Key,
                Buffer = kvp.Value,
                Count = kvp.Value.Count,
                TotalDurationMs = kvp.Value.Sum(s => s.FrametimeMs)
            })
            .OrderByDescending(x => x.Count)
            .ThenByDescending(x => x.TotalDurationMs)
            .ThenBy(x => x.Address, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (selectedPair == null || selectedPair.Count == 0) return null;

        var threeSecondSamples = selectedPair.Buffer.Select(s => s.FrametimeMs).ToList();
        var oneSecondSamples = selectedPair.Buffer
            .Where(s => (now - s.ReceivedAt).TotalSeconds <= 1.0)
            .Select(s => s.FrametimeMs)
            .ToList();

        BenchmarkMetricSet stats3s = BenchmarkStatistics.Calculate(threeSecondSamples, "Live");

        double? currentFps = null;
        double? currentFrametimeMs = null;

        if (oneSecondSamples.Count > 0)
        {
            BenchmarkMetricSet stats1s = BenchmarkStatistics.Calculate(oneSecondSamples, "Live");
            currentFps = stats1s.AverageFps > 0 ? stats1s.AverageFps : null;
            currentFrametimeMs = oneSecondSamples.Average();
        }
        else if (threeSecondSamples.Count > 0)
        {
            currentFps = stats3s.AverageFps > 0 ? stats3s.AverageFps : null;
            currentFrametimeMs = threeSecondSamples.Average();
        }

        return new LivePerformanceSnapshot(
            ProcessId: pid,
            LibraryItemId: libraryItemId,
            SwapChainAddress: selectedPair.Address,
            CurrentFps: currentFps,
            CurrentFrametimeMs: currentFrametimeMs,
            OnePercentLowFps: stats3s.OnePercentLowFps > 0 ? stats3s.OnePercentLowFps : null,
            PointOnePercentLowFps: stats3s.PointOnePercentLowFps > 0 ? stats3s.PointOnePercentLowFps : null,
            SampleCount: selectedPair.Count,
            CapturedAtUtc: DateTimeOffset.UtcNow
        );
    }

    private bool TryAcquireNativeSessionOwnership()
    {
        lock (_preemptionLock)
        {
            if (_preempted || _ownsNativeSession || _nativeOwnershipState is not (NativeOwnershipState.Released or NativeOwnershipState.RecoveryPending))
            {
                return false;
            }

            _ownsNativeSession = true;
            _nativeOwnershipState = NativeOwnershipState.Owned;
            _activeNativeGeneration = ++_nativeGeneration;
            _nativeSessionReleased = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            return true;
        }
    }

    /// <summary>
    /// Live PresentMon eligibility. Delegated to the shared RiotGameProcesses.IsPassivePerformanceEligible authority.
    /// </summary>
    private static bool IsLiveTelemetryEligible(ActiveGameSnapshot activeGame)
    {
        return RiotGameProcesses.IsPassivePerformanceEligible(activeGame.LibraryItem, activeGame.Process.ProcessName);
    }

    private void TeardownOwnedSession(ref IPresentMonApi? api, ref nint session, ref nint query, ref uint trackingPid)
    {
        long generation;
        lock (_preemptionLock)
        {
            generation = _activeNativeGeneration;
        }

        NativeTeardownResult teardown = TeardownSession(ref api, ref session, ref query, ref trackingPid);
        lock (_preemptionLock)
        {
            if (!_ownsNativeSession || generation != _activeNativeGeneration)
            {
                return;
            }

            _ownsNativeSession = false;
            _nativeOwnershipState = teardown.ReleaseConfirmed
                ? NativeOwnershipState.Released
                : teardown.FreshOwnershipSafe
                    ? NativeOwnershipState.ReleaseFailedRecoverable
                    : NativeOwnershipState.ReleaseFailedUncertain;
            _nativeSessionReleased?.TrySetResult(teardown.ReleaseConfirmed);
            _nativeSessionReleased = null;
            if (_preemptionReleaseRequested)
            {
                if (_nativeOwnershipState == NativeOwnershipState.ReleaseFailedRecoverable)
                {
                    _nativeOwnershipState = NativeOwnershipState.RecoveryPending;
                }
                _preemptionReleaseRequested = false;
                _preempted = false;
            }
        }
    }

    private static NativeTeardownResult TeardownSession(ref IPresentMonApi? api, ref nint session, ref nint query, ref uint trackingPid)
    {
        bool succeeded = true;
        bool disposeSucceeded = true;
        bool closeConfirmed = session == 0;
        if (api != null)
        {
            if (trackingPid != 0 && session != 0)
            {
                try { succeeded &= api.StopTrackingProcess(session, trackingPid) == PmStatus.Success; } catch { succeeded = false; }
                trackingPid = 0;
            }
            if (query != 0)
            {
                try { succeeded &= api.FreeFrameQuery(query) == PmStatus.Success; } catch { succeeded = false; }
                query = 0;
            }
            if (session != 0)
            {
                try
                {
                    closeConfirmed = api.CloseSession(session) == PmStatus.Success;
                    succeeded &= closeConfirmed;
                }
                catch
                {
                    succeeded = false;
                    closeConfirmed = false;
                }
                session = 0;
            }
            try { api.Dispose(); }
            catch
            {
                succeeded = false;
                disposeSucceeded = false;
            }
            api = null;
        }
        // Unloading the API DLL does not close or prove closure of a native PresentMon session.
        // Fresh ownership is safe only when the native close was confirmed and wrapper disposal succeeded.
        return new NativeTeardownResult(succeeded, closeConfirmed && disposeSucceeded);
    }

    private enum NativeOwnershipState
    {
        Released,
        Owned,
        ReleaseFailedRecoverable,
        RecoveryPending,
        ReleaseFailedUncertain
    }

    private readonly record struct NativeTeardownResult(bool ReleaseConfirmed, bool FreshOwnershipSafe);

    private static bool TryFindMetric(PmQueryElement[] elements, PmMetric metric, out PmQueryElement result)
    {
        foreach (var element in elements)
        {
            if (element.Metric == metric)
            {
                result = element;
                return true;
            }
        }
        result = default;
        return false;
    }

    private static double? ReadMetricNumber(ReadOnlySpan<byte> blob, PmQueryElement[] elements, IReadOnlyDictionary<PmMetric, PmFrameMetricInfo> types, PmMetric metric) =>
        TryFindMetric(elements, metric, out var el) && types.TryGetValue(metric, out var info) ? ReadNumber(blob, el, info.FrameType) : null;

    private static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> blob, PmQueryElement element) => blob.Slice(checked((int)element.DataOffset), checked((int)element.DataSize));
    private static ulong ReadU64(ReadOnlySpan<byte> blob, PmQueryElement element) => Slice(blob, element).Length >= 8 ? System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(Slice(blob, element)) : 0;
    private static double? ReadNumber(ReadOnlySpan<byte> blob, PmQueryElement element, PmDataType type)
    {
        ReadOnlySpan<byte> value = Slice(blob, element);
        double? result = type switch
        {
            PmDataType.Double when value.Length >= 8 => BitConverter.Int64BitsToDouble((long)System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(value)),
            PmDataType.Int32 when value.Length >= 4 => System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(value),
            PmDataType.UInt32 or PmDataType.Enum when value.Length >= 4 => System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(value),
            PmDataType.UInt64 when value.Length >= 8 => System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(value),
            PmDataType.Bool when value.Length >= 1 => value[0] == 0 ? 0 : 1,
            _ => null
        };
        return result is double number && double.IsNaN(number) ? null : result;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _benchmarkCoordinator.StateChanged -= OnBenchmarkStateChanged;
        StopAsync().GetAwaiter().GetResult();
    }
}
