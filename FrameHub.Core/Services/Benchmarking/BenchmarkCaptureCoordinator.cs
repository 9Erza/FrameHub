using FrameHub.Core.Logging;
using FrameHub.Core.Models.Benchmarking;

namespace FrameHub.Core.Services.Benchmarking;

public sealed class BenchmarkCaptureCoordinator : IBenchmarkCaptureCoordinator, IBenchmarkOperationArbiter
{
    private readonly BenchmarkStorageService _storage;
    private readonly Func<IBenchmarkCaptureBackend> _backendFactory;
    private readonly IBenchmarkProcessIdentityProvider _identityProvider;
    private readonly IBenchmarkEnvironmentProvider? _environmentProvider;
    private readonly ILogger _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayProvider;
    private ILivePresentMonPreemption? _livePresentMonPreemption;
    private TimeSpan _livePreemptionTimeout = TimeSpan.FromSeconds(2);

    private readonly object _lock = new();
    private readonly Queue<StateNotification> _pendingStateNotifications = new();
    private Task<BenchmarkCaptureOutcome>? _activeTask;
    private CancellationTokenSource? _activeCts;
    private AcceptedCaptureReservation? _activeReservation;
    private bool _notificationDrainActive;
    private bool _disposed;
    private int _externalMutationOwners;

    private CoordinatorState _state = CoordinatorState.Idle;
    private int _remainingCountdownSeconds;
    private string? _targetDisplayName;
    private DateTimeOffset? _captureStartedAtUtc;
    private string? _lastErrorCode;

    public event EventHandler<BenchmarkCaptureStateSnapshot>? StateChanged;

    public BenchmarkCaptureStateSnapshot CurrentState
    {
        get
        {
            lock (_lock)
            {
                return new BenchmarkCaptureStateSnapshot
                {
                    State = _state,
                    IsActive = IsStateActive(_state),
                    RemainingCountdownSeconds = _remainingCountdownSeconds,
                    TargetDisplayName = _targetDisplayName,
                    CaptureStartedAtUtc = _captureStartedAtUtc,
                    ErrorCode = _lastErrorCode
                };
            }
        }
    }

    public bool IsActive
    {
        get
        {
            lock (_lock)
            {
                return IsStateActive(_state);
            }
        }
    }

    public static bool IsStateActive(CoordinatorState state) => state switch
    {
        CoordinatorState.Waiting => true,
        CoordinatorState.Capturing => true,
        CoordinatorState.Stopping => true,
        CoordinatorState.Completing => true,
        CoordinatorState.Completed => false,
        CoordinatorState.Cancelled => false,
        CoordinatorState.Failed => false,
        CoordinatorState.Idle => false,
        _ => false
    };

    public BenchmarkCaptureCoordinator(
        BenchmarkStorageService? storage = null,
        Func<IBenchmarkCaptureBackend>? backendFactory = null,
        IBenchmarkProcessIdentityProvider? identityProvider = null,
        ILogger? logger = null,
        Func<TimeSpan, CancellationToken, Task>? delayProvider = null,
        IBenchmarkEnvironmentProvider? environmentProvider = null)
    {
        _storage = storage ?? new BenchmarkStorageService();
        _backendFactory = backendFactory ?? (() => new PresentMonApiCaptureBackend(storage: _storage));
        _identityProvider = identityProvider ?? new BenchmarkProcessIdentityProvider();
        _logger = logger ?? LoggerService.Instance;
        _delayProvider = delayProvider ?? Task.Delay;
        _environmentProvider = environmentProvider;
    }

    public Task<BenchmarkCaptureOutcome> StartCaptureAsync(BenchmarkCaptureRequest request, CancellationToken cancellationToken = default)
    {
        var handle = TryStartCapture(request, cancellationToken);
        if (!handle.Accepted)
        {
            return Task.FromResult(new BenchmarkCaptureOutcome
            {
                Status = CoordinatorStatus.AlreadyRunning,
                ErrorCode = handle.ErrorCode ?? "already_running"
            });
        }
        handle.Start();
        return handle.CompletionTask!;
    }

    public void ConfigureLivePresentMonPreemption(ILivePresentMonPreemption preemption, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(preemption);
        lock (_lock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(BenchmarkCaptureCoordinator));
            if (_activeTask != null) throw new InvalidOperationException("Live PresentMon preemption cannot be changed during capture.");
            _livePresentMonPreemption = preemption;
            _livePreemptionTimeout = timeout is { } configured && configured > TimeSpan.Zero
                ? configured
                : TimeSpan.FromSeconds(2);
        }
    }

    public BenchmarkCaptureStartHandle TryStartCapture(BenchmarkCaptureRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        AcceptedCaptureReservation reservation;
        StateNotification waitingNotification;
        bool shouldDrainNotifications;

        lock (_lock)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(BenchmarkCaptureCoordinator));
            }

            if (_activeTask != null)
            {
                return new BenchmarkCaptureStartHandle
                {
                    Accepted = false,
                    ErrorCode = "already_running",
                    CompletionTask = null
                };
            }

            if (_externalMutationOwners > 0)
            {
                return new BenchmarkCaptureStartHandle
                {
                    Accepted = false,
                    ErrorCode = "operation_in_progress",
                    CompletionTask = null
                };
            }

            _activeCts = new CancellationTokenSource();
            CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _activeCts.Token);
            reservation = new AcceptedCaptureReservation(linkedCts);
            _activeReservation = reservation;
            _activeTask = reservation.CompletionTask;

            _targetDisplayName = request.Target.DisplayName;
            _lastErrorCode = null;
            _captureStartedAtUtc = null;
            _state = CoordinatorState.Waiting;
            _remainingCountdownSeconds = request.CountdownSeconds;
            waitingNotification = new StateNotification(CreateStateSnapshotLocked(), waitForDelivery: true);
            shouldDrainNotifications = EnqueueStateNotificationLocked(waitingNotification);
        }

        if (shouldDrainNotifications) ScheduleStateNotificationDrain();
        waitingNotification.WaitForDelivery();

        lock (_lock)
        {
            if (!ReferenceEquals(_activeReservation, reservation) || !IsStateActive(_state))
            {
                return new BenchmarkCaptureStartHandle
                {
                    Accepted = false,
                    ErrorCode = _lastErrorCode ?? "capture_cancelled",
                    CompletionTask = null
                };
            }

            return new BenchmarkCaptureStartHandle
            {
                Accepted = true,
                ErrorCode = null,
                CompletionTask = reservation.CompletionTask,
                StartReservation = () => StartAcceptedCapture(reservation, request)
            };
        }
    }

    private bool StartAcceptedCapture(AcceptedCaptureReservation reservation, BenchmarkCaptureRequest request)
    {
        lock (_lock)
        {
            if (_disposed || !ReferenceEquals(_activeReservation, reservation) || !reservation.TryMarkStarted())
            {
                return false;
            }
        }

        _ = Task.Run(async () =>
        {
            BenchmarkCaptureOutcome outcome = await ExecuteCapturePipelineAsync(
                reservation,
                request,
                reservation.LinkedCts.Token).ConfigureAwait(false);
            reservation.TrySetOutcome(outcome);
        });
        return true;
    }

    /// <summary>
    /// Captures the one-shot benchmark environment snapshot. Best-effort only:
    /// any provider failure is logged and never aborts benchmark capture.
    /// The provider is invoked exactly once per accepted capture and never polled.
    /// </summary>
    private BenchmarkEnvironmentSnapshot? CaptureEnvironmentSnapshot()
    {
        IBenchmarkEnvironmentProvider? provider = _environmentProvider;
        if (provider is null) return null;
        try
        {
            return provider.Capture();
        }
        catch (Exception ex)
        {
            _logger.Warn($"Benchmark environment snapshot unavailable; capture continues without it: {ex.Message}");
            return null;
        }
    }

    public bool TryAcquireExternalMutation(out IDisposable? lease)
    {
        lock (_lock)
        {
            if (_disposed || _activeTask != null || IsStateActive(_state))
            {
                lease = null;
                return false;
            }

            _externalMutationOwners++;
            lease = new ExternalMutationLease(this);
            return true;
        }
    }

    private void ReleaseExternalMutation()
    {
        lock (_lock)
        {
            if (_externalMutationOwners > 0) _externalMutationOwners--;
        }
    }

    private sealed class ExternalMutationLease(BenchmarkCaptureCoordinator owner) : IDisposable
    {
        private BenchmarkCaptureCoordinator? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ReleaseExternalMutation();
    }

    private sealed class AcceptedCaptureReservation(CancellationTokenSource linkedCts)
    {
        private readonly TaskCompletionSource<BenchmarkCaptureOutcome> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _started;

        public CancellationTokenSource LinkedCts { get; } = linkedCts;
        public Task<BenchmarkCaptureOutcome> CompletionTask => _completion.Task;
        public bool IsStarted => Volatile.Read(ref _started) != 0;

        public bool TryMarkStarted() => Interlocked.CompareExchange(ref _started, 1, 0) == 0;
        public void TrySetOutcome(BenchmarkCaptureOutcome outcome) => _completion.TrySetResult(outcome);
    }

    private sealed class StateNotification(BenchmarkCaptureStateSnapshot snapshot, bool waitForDelivery = false)
    {
        private readonly TaskCompletionSource? _delivered = waitForDelivery
            ? new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
            : null;

        public BenchmarkCaptureStateSnapshot Snapshot { get; } = snapshot;
        public void MarkDelivered() => _delivered?.TrySetResult();
        public void WaitForDelivery() => _delivered?.Task.GetAwaiter().GetResult();
    }


    private async Task<BenchmarkCaptureOutcome> ExecuteCapturePipelineAsync(
        AcceptedCaptureReservation reservation,
        BenchmarkCaptureRequest request,
        CancellationToken cancellationToken)
    {
        ILivePresentMonPreemption? livePreemption = _livePresentMonPreemption;
        bool preemptionRequested = false;
        try
        {
            if (livePreemption != null)
            {
                preemptionRequested = true;
                using var preemptionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                preemptionCts.CancelAfter(_livePreemptionTimeout);

                bool released;
                try
                {
                    released = await livePreemption.RequestPresentMonReleaseAsync(preemptionCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new BenchmarkException("live_telemetry_preemption_timeout", "Live telemetry did not release PresentMon before the benchmark timeout.");
                }

                if (!released)
                {
                    throw new BenchmarkException("live_telemetry_preemption_failed", "Live telemetry could not confirm PresentMon teardown.");
                }
            }

            // 1. Countdown Phase
            if (request.CountdownSeconds > 0)
            {
                SetState(CoordinatorState.Waiting, remainingCountdown: request.CountdownSeconds);

                for (int remaining = request.CountdownSeconds; remaining > 0; remaining--)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    BenchmarkProcessIdentity currentIdentity = _identityProvider.GetCurrentIdentity(request.Process.ProcessId, request.Target);
                    BenchmarkGameResolver.ValidateSameProcessInstance(request.Process, currentIdentity);

                    await _delayProvider(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);

                    SetState(CoordinatorState.Waiting, remainingCountdown: remaining - 1);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 2. Session Creation (Exactly once)
            DateTime startUtc = DateTime.UtcNow;
            BenchmarkEnvironmentSnapshot? environment = CaptureEnvironmentSnapshot();
            BenchmarkSession session = _storage.CreateSession(
                request.Target,
                request.Process,
                request.AppVersion,
                startUtc,
                request.ProfileId,
                request.ProfileName,
                request.SessionOptimizationActive,
                request.DurationSeconds,
                environment);

            // 3. Instantiate Backend (Exactly once per accepted capture)
            IBenchmarkCaptureBackend backend = _backendFactory();

            SetState(CoordinatorState.Capturing, captureStartedAtUtc: startUtc);

            // 4. Execute Backend Capture
            BenchmarkCaptureResult result = await backend.CaptureAsync(session, cancellationToken).ConfigureAwait(false);

            SetState(CoordinatorState.Completing);

            BenchmarkCaptureOutcome outcome = new BenchmarkCaptureOutcome
            {
                Status = CoordinatorStatus.Completed,
                Result = result
            };

            SetState(CoordinatorState.Completed);
            return outcome;
        }
        catch (OperationCanceledException)
        {
            BenchmarkCaptureOutcome outcome = new BenchmarkCaptureOutcome
            {
                Status = CoordinatorStatus.Cancelled,
                ErrorCode = "capture_cancelled"
            };

            SetState(CoordinatorState.Cancelled, errorCode: outcome.ErrorCode);
            return outcome;
        }
        catch (BenchmarkException ex)
        {
            _logger.Error($"Benchmark capture failed [{ex.Code}]: {ex.Message}", ex);

            BenchmarkCaptureOutcome outcome = new BenchmarkCaptureOutcome
            {
                Status = CoordinatorStatus.Failed,
                ErrorCode = ex.Code,
                TechnicalDetail = ex.ToString()
            };

            SetState(CoordinatorState.Failed, errorCode: ex.Code);
            return outcome;
        }
        catch (Exception ex)
        {
            _logger.Error($"Benchmark capture unexpected exception: {ex.Message}", ex);

            BenchmarkCaptureOutcome outcome = new BenchmarkCaptureOutcome
            {
                Status = CoordinatorStatus.Failed,
                ErrorCode = "capture_failed",
                TechnicalDetail = ex.ToString()
            };

            SetState(CoordinatorState.Failed, errorCode: outcome.ErrorCode);
            return outcome;
        }
        finally
        {
            if (preemptionRequested)
            {
                try { livePreemption!.ReleasePresentMonPreemption(); }
                catch (Exception ex) { _logger.Warn($"Failed to release live telemetry preemption: {ex.Message}"); }
            }
            reservation.LinkedCts.Dispose();
            lock (_lock)
            {
                if (ReferenceEquals(_activeReservation, reservation))
                {
                    _activeCts?.Dispose();
                    _activeCts = null;
                    _activeTask = null;
                    _activeReservation = null;
                }
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task<BenchmarkCaptureOutcome>? taskToWait = null;
        AcceptedCaptureReservation? cancelledReservation = null;
        bool shouldDrainNotifications = false;

        lock (_lock)
        {
            if (_activeTask == null || _activeTask.IsCompleted)
            {
                return Task.CompletedTask;
            }

            if (_activeCts != null && !_activeCts.IsCancellationRequested)
            {
                try
                {
                    _activeCts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Ignore cancellation dispose races
                }
                catch (AggregateException)
                {
                    // Ignore cancellation exception races
                }
                _state = CoordinatorState.Stopping;
                _remainingCountdownSeconds = 0;
                shouldDrainNotifications = EnqueueStateNotificationLocked(new StateNotification(CreateStateSnapshotLocked()));
            }

            taskToWait = _activeTask;
            if (_activeReservation is { IsStarted: false } reservation)
            {
                cancelledReservation = reservation;
                _state = CoordinatorState.Cancelled;
                _lastErrorCode = "capture_cancelled";
                shouldDrainNotifications |= EnqueueStateNotificationLocked(new StateNotification(CreateStateSnapshotLocked()));
                _activeCts?.Dispose();
                _activeCts = null;
                _activeTask = null;
                _activeReservation = null;
            }
        }

        if (cancelledReservation != null)
        {
            cancelledReservation.LinkedCts.Dispose();
            cancelledReservation.TrySetOutcome(CancelledOutcome());
        }
        if (shouldDrainNotifications) ScheduleStateNotificationDrain();
        return AwaitActiveTaskAsync(taskToWait, cancellationToken);
    }

    private async Task AwaitActiveTaskAsync(Task<BenchmarkCaptureOutcome> taskToWait, CancellationToken cancellationToken)
    {
        try
        {
            await taskToWait.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected cancellation condition during StopAsync wait
        }
        catch (TimeoutException)
        {
            // Expected timeout condition if cancellationToken times out
        }
        catch (Exception ex)
        {
            _logger.Error($"Unexpected exception while awaiting active capture task in StopAsync: {ex.Message}", ex);
        }
    }

    private void SetState(
        CoordinatorState newState,
        int remainingCountdown = 0,
        DateTimeOffset? captureStartedAtUtc = null,
        string? errorCode = null)
    {
        bool shouldDrainNotifications;

        lock (_lock)
        {
            _state = newState;
            _remainingCountdownSeconds = remainingCountdown;
            if (captureStartedAtUtc.HasValue) _captureStartedAtUtc = captureStartedAtUtc;
            if (errorCode != null) _lastErrorCode = errorCode;

            shouldDrainNotifications = EnqueueStateNotificationLocked(new StateNotification(CreateStateSnapshotLocked()));
        }

        if (shouldDrainNotifications) ScheduleStateNotificationDrain();
    }

    private BenchmarkCaptureStateSnapshot CreateStateSnapshotLocked() => new()
    {
        State = _state,
        IsActive = IsStateActive(_state),
        RemainingCountdownSeconds = _remainingCountdownSeconds,
        TargetDisplayName = _targetDisplayName,
        CaptureStartedAtUtc = _captureStartedAtUtc,
        ErrorCode = _lastErrorCode
    };

    private bool EnqueueStateNotificationLocked(StateNotification notification)
    {
        _pendingStateNotifications.Enqueue(notification);
        if (_notificationDrainActive)
        {
            return false;
        }

        _notificationDrainActive = true;
        return true;
    }

    private void ScheduleStateNotificationDrain()
    {
        ThreadPool.QueueUserWorkItem(
            static state => ((BenchmarkCaptureCoordinator)state!).DrainStateNotifications(),
            this,
            preferLocal: false);
    }

    private void DrainStateNotifications()
    {
        while (true)
        {
            StateNotification notification;
            lock (_lock)
            {
                if (_pendingStateNotifications.Count == 0)
                {
                    _notificationDrainActive = false;
                    return;
                }
                notification = _pendingStateNotifications.Dequeue();
            }

            try
            {
                PublishStateChanged(notification.Snapshot);
            }
            finally
            {
                notification.MarkDelivered();
            }
        }
    }

    private void PublishStateChanged(BenchmarkCaptureStateSnapshot snapshot)
    {
        var handlers = StateChanged;
        if (handlers == null)
        {
            return;
        }

        foreach (EventHandler<BenchmarkCaptureStateSnapshot> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, snapshot);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Benchmark state subscriber failed during '{snapshot.State}': {ex.Message}");
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        Task<BenchmarkCaptureOutcome>? taskToWait = null;
        AcceptedCaptureReservation? cancelledReservation = null;
        bool shouldDrainNotifications = false;

        lock (_lock)
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;

            if (_activeCts != null && !_activeCts.IsCancellationRequested)
            {
                try { _activeCts.Cancel(); } catch { }
            }
            else if (_activeTask == null)
            {
                _activeCts?.Dispose();
                _activeCts = null;
            }

            taskToWait = _activeTask;
            if (_activeReservation is { IsStarted: false } reservation)
            {
                cancelledReservation = reservation;
                _state = CoordinatorState.Cancelled;
                _lastErrorCode = "capture_cancelled";
                shouldDrainNotifications = EnqueueStateNotificationLocked(new StateNotification(CreateStateSnapshotLocked()));
                _activeCts?.Dispose();
                _activeCts = null;
                _activeTask = null;
                _activeReservation = null;
            }
        }

        if (cancelledReservation != null)
        {
            cancelledReservation.LinkedCts.Dispose();
            cancelledReservation.TrySetOutcome(CancelledOutcome());
        }
        if (shouldDrainNotifications) ScheduleStateNotificationDrain();

        if (taskToWait != null)
        {
            return new ValueTask(AwaitActiveTaskWithTimeoutAsync(taskToWait));
        }

        return ValueTask.CompletedTask;
    }

    private static BenchmarkCaptureOutcome CancelledOutcome() => new()
    {
        Status = CoordinatorStatus.Cancelled,
        ErrorCode = "capture_cancelled"
    };

    private async Task AwaitActiveTaskWithTimeoutAsync(Task<BenchmarkCaptureOutcome> taskToWait)
    {
        try
        {
            await taskToWait.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected cancellation condition on shutdown
        }
        catch (TimeoutException)
        {
            _logger.Warn("Timeout (5s) reached while waiting for active capture task during coordinator disposal.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Unexpected exception while awaiting active capture task during disposal: {ex.Message}", ex);
        }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
