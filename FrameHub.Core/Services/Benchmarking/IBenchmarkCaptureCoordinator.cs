using FrameHub.Core.Models.Benchmarking;

namespace FrameHub.Core.Services.Benchmarking;

public interface IBenchmarkCaptureCoordinator : IAsyncDisposable, IDisposable
{
    event EventHandler<BenchmarkCaptureStateSnapshot>? StateChanged;
    BenchmarkCaptureStateSnapshot CurrentState { get; }
    bool IsActive { get; }

    BenchmarkCaptureStartHandle TryStartCapture(BenchmarkCaptureRequest request, CancellationToken cancellationToken = default);
    Task<BenchmarkCaptureOutcome> StartCaptureAsync(BenchmarkCaptureRequest request, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
