using FrameHub.Companion.Models;

namespace FrameHub.Companion.Providers;

public interface ICompanionSessionOptimizationProvider
{
    Task<CompanionSessionOptimizationStateDto> GetStateAsync(CancellationToken cancellationToken = default);
    Task<CompanionOptimizationResultDto> ApplyOptimizationAsync(CancellationToken cancellationToken = default);
    Task<CompanionOptimizationResultDto> RestoreSessionAsync(CancellationToken cancellationToken = default);
}

public sealed class NullCompanionSessionOptimizationProvider : ICompanionSessionOptimizationProvider
{
    public Task<CompanionSessionOptimizationStateDto> GetStateAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CompanionSessionOptimizationStateDto());
    }

    public Task<CompanionOptimizationResultDto> ApplyOptimizationAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CompanionOptimizationResultDto
        {
            Success = false,
            ErrorCode = "optimization_provider_unavailable"
        });
    }

    public Task<CompanionOptimizationResultDto> RestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CompanionOptimizationResultDto
        {
            Success = false,
            ErrorCode = "optimization_provider_unavailable"
        });
    }
}
