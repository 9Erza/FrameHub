using FrameHub.Core.Models.Benchmarking;

namespace FrameHub.Core.Services.Benchmarking;

public interface IBenchmarkCaptureBackend
{
    Task<BenchmarkCaptureResult> CaptureAsync(BenchmarkSession session, CancellationToken cancellationToken = default);
}

public interface IBenchmarkProcessIdentityProvider
{
    BenchmarkProcessIdentity GetCurrentIdentity(int processId, BenchmarkTarget target);
}

public sealed class BenchmarkProcessIdentityProvider : IBenchmarkProcessIdentityProvider
{
    private readonly BenchmarkGameResolver _resolver = new();
    public BenchmarkProcessIdentity GetCurrentIdentity(int processId, BenchmarkTarget target) => _resolver.ResolveCurrentIdentity(processId, target);
}
