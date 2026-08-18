using FrameHub.Companion.Models;

namespace FrameHub.Companion.Providers;

public interface ICompanionLibraryProvider
{
    Task<IReadOnlyList<CompanionLibraryItemDto>> GetLibraryItemsAsync(CancellationToken cancellationToken = default);
    Task<CompanionLaunchResultDto> LaunchItemAsync(string id, CancellationToken cancellationToken = default);
    Task<CompanionLibraryIconResult?> GetItemIconAsync(string id, CancellationToken cancellationToken = default);
}

public sealed class NullCompanionLibraryProvider : ICompanionLibraryProvider
{
    public Task<IReadOnlyList<CompanionLibraryItemDto>> GetLibraryItemsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<CompanionLibraryItemDto>>(Array.Empty<CompanionLibraryItemDto>());
    }

    public Task<CompanionLaunchResultDto> LaunchItemAsync(string id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CompanionLaunchResultDto
        {
            Success = false,
            ErrorCode = "library_provider_unavailable"
        });
    }

    public Task<CompanionLibraryIconResult?> GetItemIconAsync(string id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<CompanionLibraryIconResult?>(null);
    }
}
