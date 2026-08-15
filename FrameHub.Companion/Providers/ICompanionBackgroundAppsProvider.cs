using FrameHub.Companion.Models;

namespace FrameHub.Companion.Providers;

public interface ICompanionBackgroundAppsProvider
{
    Task<IReadOnlyList<CompanionBackgroundAppDto>> GetBackgroundAppsAsync(CancellationToken cancellationToken = default);
    Task<CompanionBackgroundAppOperationDto> StartBackgroundAppAsync(string id, CancellationToken cancellationToken = default);
    Task<CompanionBackgroundAppOperationDto> StopBackgroundAppAsync(string id, CancellationToken cancellationToken = default);
}

public sealed class NullCompanionBackgroundAppsProvider : ICompanionBackgroundAppsProvider
{
    public Task<IReadOnlyList<CompanionBackgroundAppDto>> GetBackgroundAppsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CompanionBackgroundAppDto>>(Array.Empty<CompanionBackgroundAppDto>());

    public Task<CompanionBackgroundAppOperationDto> StartBackgroundAppAsync(string id, CancellationToken cancellationToken = default) =>
        Unavailable();

    public Task<CompanionBackgroundAppOperationDto> StopBackgroundAppAsync(string id, CancellationToken cancellationToken = default) =>
        Unavailable();

    private static Task<CompanionBackgroundAppOperationDto> Unavailable() =>
        Task.FromResult(new CompanionBackgroundAppOperationDto { Success = false, ErrorCode = "background_apps_provider_unavailable" });
}
