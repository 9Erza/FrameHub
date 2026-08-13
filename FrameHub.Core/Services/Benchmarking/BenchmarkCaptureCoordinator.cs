using FrameHub.Core.Logging;
using FrameHub.Core.Models.Benchmarking;

namespace FrameHub.Core.Services.Benchmarking;

public sealed class BenchmarkCaptureCoordinator : IBenchmarkCaptureCoordinator
{
    private readonly BenchmarkStorageService _storage;
    private readonly Func<IBenchmarkCaptureBackend> _backendFactory;
    private readonly IBenchmarkProcessIdentityProvider _identityProvider;
    private readonly ILogger _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayProvider;

    private readonly object _lock = new();
    private Task<BenchmarkCaptureOutcome>? _activeTask;
    private CancellationTokenSource? _activeCts;
    private bool _disposed;

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
        Func<TimeSpan, CancellationToken, Task>? delayProvider = null)
    {
        _storage = storage ?? new BenchmarkStorageService();
        _backendFactory = backendFactory ?? (() => new PresentMonApiCaptureBackend(storage: _storage));
        _identityProvider = identityProvider ?? new BenchmarkProcessIdentityProvider();
        _logger = logger ?? LoggerService.Instance;
        _delayProvider = delayProvider ?? Task.Delay;
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
        return handle.CompletionTask!;
    }

    public BenchmarkCaptureStartHandle TryStartCapture(BenchmarkCaptureRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        CancellationTokenSource linkedCts;
        Task<BenchmarkCaptureOutcome> captureTask;

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

            _activeCts = new CancellationTokenSource();
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _activeCts.Token);
            var token = linkedCts.Token;

            _targetDisplayName = request.Target.DisplayName;
            _lastErrorCode = null;
            _captureStartedAtUtc = null;

            captureTask = Task.Run(() => ExecuteCapturePipelineAsync(request, linkedCts, token));
            _activeTask = captureTask;

            return new BenchmarkCaptureStartHandle
            {
                Accepted = true,
                ErrorCode = null,
                CompletionTask = captureTask
            };
        }
    }


    private async Task<BenchmarkCaptureOutcome> ExecuteCapturePipelineAsync(
        BenchmarkCaptureRequest request,
        CancellationTokenSource linkedCts,
        CancellationToken cancellationToken)
    {
        try
        {
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
            BenchmarkSession session = _storage.CreateSession(
                request.Target,
                request.Process,
                request.AppVersion,
                startUtc,
                request.ProfileId,
                request.ProfileName,
                request.SessionOptimizationActive,
                request.DurationSeconds);

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
            linkedCts.Dispose();
            lock (_lock)
            {
                _activeCts?.Dispose();
                _activeCts = null;
                _activeTask = null;
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task<BenchmarkCaptureOutcome>? taskToWait = null;

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
                SetState(CoordinatorState.Stopping);
            }

            taskToWait = _activeTask;
        }

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
        BenchmarkCaptureStateSnapshot snapshot;

        lock (_lock)
        {
            _state = newState;
            _remainingCountdownSeconds = remainingCountdown;
            if (captureStartedAtUtc.HasValue) _captureStartedAtUtc = captureStartedAtUtc;
            if (errorCode != null) _lastErrorCode = errorCode;

            snapshot = new BenchmarkCaptureStateSnapshot
            {
                State = _state,
                IsActive = IsStateActive(_state),
                RemainingCountdownSeconds = _remainingCountdownSeconds,
                TargetDisplayName = _targetDisplayName,
                CaptureStartedAtUtc = _captureStartedAtUtc,
                ErrorCode = _lastErrorCode
            };
        }

        StateChanged?.Invoke(this, snapshot);
    }

    public ValueTask DisposeAsync()
    {
        Task<BenchmarkCaptureOutcome>? taskToWait = null;

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
        }

        if (taskToWait != null)
        {
            return new ValueTask(AwaitActiveTaskWithTimeoutAsync(taskToWait));
        }

        return ValueTask.CompletedTask;
    }

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
