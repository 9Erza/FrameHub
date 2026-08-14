using FrameHub.Core.Models.Benchmarking;

namespace FrameHub.Core.Services.Benchmarking;

public interface ILivePresentMonPreemption
{
    Task<bool> RequestPresentMonReleaseAsync(CancellationToken cancellationToken);
    void ReleasePresentMonPreemption();
}

public interface IBenchmarkOperationArbiter
{
    bool TryAcquireExternalMutation(out IDisposable? lease);
}

public interface IBenchmarkCaptureCoordinator : IAsyncDisposable, IDisposable
{
    event EventHandler<BenchmarkCaptureStateSnapshot>? StateChanged;
    BenchmarkCaptureStateSnapshot CurrentState { get; }
    bool IsActive { get; }

    /// <summary>
    /// Reserves capture ownership and publishes Waiting. Accepted callers must invoke
    /// <see cref="BenchmarkCaptureStartHandle.Start"/> after receiving the handle.
    /// </summary>
    BenchmarkCaptureStartHandle TryStartCapture(BenchmarkCaptureRequest request, CancellationToken cancellationToken = default);
    Task<BenchmarkCaptureOutcome> StartCaptureAsync(BenchmarkCaptureRequest request, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
