using System.Security.Cryptography;
using System.Text;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Benchmarking;

namespace FrameHub.BenchmarkHarness;

public enum HarnessIdentityConfidence
{
    ExactPath,
    ExplicitGameId,
    AdHocExactProcess,
    PathUnavailableUniqueName,
    PathUnavailableExplicitGameId,
    PathUnavailableAdHoc
}

public sealed class HarnessTargetResolution
{
    public BenchmarkTarget Target { get; init; } = new();
    public HarnessIdentityConfidence Confidence { get; init; }
}

public sealed class HarnessTargetResolver
{
    private readonly BenchmarkGameResolver _gameResolver = new();

    public HarnessTargetResolution Resolve(
        BenchmarkProcessIdentity process,
        IEnumerable<LibraryItem> libraryItems,
        string? requestedGameId = null)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(libraryItems);
        List<LibraryItem> games = libraryItems.Where(item => item.Type == LibraryItemType.Game).ToList();

        if (!string.IsNullOrWhiteSpace(requestedGameId))
        {
            List<LibraryItem> requested = games
                .Where(item => string.Equals(item.Id, requestedGameId.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (requested.Count == 0) throw new BenchmarkTargetException("library_game_not_found", $"No library game has ID '{requestedGameId}'.");
            if (requested.Count > 1) throw new BenchmarkTargetException("ambiguous_library_identity", $"Multiple library games have ID '{requestedGameId}'.");

            BenchmarkTarget target = _gameResolver.CreateTarget(requested[0]);
            BenchmarkGameResolver.ValidateConfiguredPath(target, process);
            return new HarnessTargetResolution
            {
                Target = target,
                Confidence = string.IsNullOrWhiteSpace(process.ExecutablePath)
                    ? HarnessIdentityConfidence.PathUnavailableExplicitGameId
                    : string.IsNullOrWhiteSpace(target.ConfiguredExecutablePath)
                        ? HarnessIdentityConfidence.ExplicitGameId
                        : HarnessIdentityConfidence.ExactPath
            };
        }

        string? runningPath = ProfileService.NormalizeExecutablePath(process.ExecutablePath);
        List<LibraryItem> exactPathMatches = games
            .Where(item => !string.IsNullOrWhiteSpace(item.ExecutablePath))
            .Where(item => string.Equals(ProfileService.NormalizeExecutablePath(item.ExecutablePath), runningPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (exactPathMatches.Count > 1)
        {
            throw new BenchmarkTargetException(
                "ambiguous_library_match",
                $"{exactPathMatches.Count} library games use executable '{runningPath}'. Specify --game-id to resolve the ambiguity.");
        }
        if (exactPathMatches.Count == 1)
        {
            return new HarnessTargetResolution
            {
                Target = _gameResolver.CreateTarget(exactPathMatches[0]),
                Confidence = HarnessIdentityConfidence.ExactPath
            };
        }

        if (runningPath is null)
        {
            string runningName = ProfileService.NormalizeProcessName(process.ProcessName);
            List<LibraryItem> nameMatches = games
                .Where(item => LibraryProcessName(item).Equals(runningName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (nameMatches.Count > 1)
            {
                throw new BenchmarkTargetException(
                    "ambiguous_name_match",
                    $"The process path is unavailable and {nameMatches.Count} library games match process name '{runningName}'. Specify --game-id; FrameHub will not guess.");
            }
            if (nameMatches.Count == 1)
            {
                return new HarnessTargetResolution
                {
                    Target = _gameResolver.CreateTarget(nameMatches[0]),
                    Confidence = HarnessIdentityConfidence.PathUnavailableUniqueName
                };
            }

            string pathlessIdentity = $"{runningName.ToUpperInvariant()}|{process.ProcessId}|{process.StartTimeUtc.Ticks}";
            string pathlessHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pathlessIdentity)))[..16].ToLowerInvariant();
            return new HarnessTargetResolution
            {
                Target = new BenchmarkTarget
                {
                    LibraryItemId = $"adhoc-pathless-{pathlessHash}",
                    DisplayName = string.IsNullOrWhiteSpace(runningName) ? $"PID {process.ProcessId}" : runningName,
                    LibrarySource = "AdHocPathUnavailable"
                },
                Confidence = HarnessIdentityConfidence.PathUnavailableAdHoc
            };
        }

        string stablePathIdentity = runningPath.ToUpperInvariant();
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stablePathIdentity)))[..16].ToLowerInvariant();
        return new HarnessTargetResolution
        {
            Target = new BenchmarkTarget
            {
                LibraryItemId = $"adhoc-{hash}",
                DisplayName = Path.GetFileNameWithoutExtension(runningPath),
                LibrarySource = "AdHoc",
                ConfiguredExecutablePath = runningPath
            },
            Confidence = HarnessIdentityConfidence.AdHocExactProcess
        };
    }

    private static string LibraryProcessName(LibraryItem item)
    {
        return string.IsNullOrWhiteSpace(item.ExecutablePath)
            ? string.Empty
            : ProfileService.NormalizeProcessName(Path.GetFileName(item.ExecutablePath));
    }
}
