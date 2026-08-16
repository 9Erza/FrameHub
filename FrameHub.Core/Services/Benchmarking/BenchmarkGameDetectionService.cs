using System.Diagnostics;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Services.Library;

namespace FrameHub.Core.Services.Benchmarking;

public sealed record BenchmarkProcessSnapshot(int ProcessId, string ProcessName, string? ExecutablePath, DateTime StartTimeUtc);

public sealed class BenchmarkRunningGame
{
    public required LibraryItem LibraryItem { get; init; }
    public required BenchmarkTarget Target { get; init; }
    public required BenchmarkProcessIdentity Process { get; init; }
}

public interface IBenchmarkProcessSnapshotProvider
{
    IReadOnlyList<BenchmarkProcessSnapshot> GetProcesses();
}

public sealed class SystemBenchmarkProcessSnapshotProvider : IBenchmarkProcessSnapshotProvider
{
    private readonly IProcessObservationSnapshotProvider _observationProvider;

    public SystemBenchmarkProcessSnapshotProvider(IProcessObservationSnapshotProvider? observationProvider = null)
    {
        _observationProvider = observationProvider ?? new ProcessObservationSnapshotProvider();
    }

    public IReadOnlyList<BenchmarkProcessSnapshot> GetProcesses()
    {
        ProcessObservationSnapshot snapshot = _observationProvider.GetSnapshotAsync().GetAwaiter().GetResult();
        return snapshot.Processes
            .Select(process => new BenchmarkProcessSnapshot(
                process.ProcessId,
                process.ProcessName,
                process.ExecutablePath,
                process.StartTimeUtc))
            .ToList();
    }
}

public sealed class BenchmarkGameDetectionService
{
    private readonly IBenchmarkProcessSnapshotProvider _processProvider;
    private readonly BenchmarkGameResolver _resolver;

    public BenchmarkGameDetectionService(IBenchmarkProcessSnapshotProvider? processProvider = null, BenchmarkGameResolver? resolver = null)
    {
        _processProvider = processProvider ?? new SystemBenchmarkProcessSnapshotProvider();
        _resolver = resolver ?? new BenchmarkGameResolver();
    }

    /// <summary>
    /// Benchmark/live-PresentMon eligible running games. A game is excluded when benchmark
    /// capture is disabled or when its identity is a protected Riot process, even if a
    /// malformed/manual/legacy item kept AllowBenchmark == true.
    /// </summary>
    public IReadOnlyList<BenchmarkRunningGame> Detect(IEnumerable<LibraryItem> libraryItems)
        => DetectRunningGames(libraryItems, requireBenchmarkEligibility: true);

    /// <summary>
    /// Active-game running identity only. A game is NOT excluded merely because benchmark
    /// capture is disabled (e.g. Riot titles are visible as the running/active game while
    /// remaining ineligible for benchmark capture and live PresentMon telemetry).
    /// </summary>
    public IReadOnlyList<BenchmarkRunningGame> DetectActiveGames(IEnumerable<LibraryItem> libraryItems)
        => DetectRunningGames(libraryItems, requireBenchmarkEligibility: false);

    private IReadOnlyList<BenchmarkRunningGame> DetectRunningGames(IEnumerable<LibraryItem> libraryItems, bool requireBenchmarkEligibility)
    {
        ArgumentNullException.ThrowIfNull(libraryItems);
        List<LibraryItem> games = libraryItems
            .Where(item => item.IsEnabled && item.Type == LibraryItemType.Game && !string.IsNullOrWhiteSpace(item.Id))
            .Where(item => !requireBenchmarkEligibility || IsBenchmarkEligible(item))
            .ToList();
        IReadOnlyList<BenchmarkProcessSnapshot> processes = _processProvider.GetProcesses();
        var detected = new List<BenchmarkRunningGame>();

        foreach (BenchmarkProcessSnapshot process in processes)
        {
            string processName = ProfileService.NormalizeProcessName(process.ProcessName);
            string? processPath = ProfileService.NormalizeExecutablePath(process.ExecutablePath);
            List<LibraryItem> matches;
            if (processPath is not null)
            {
                matches = games.Where(item => string.Equals(ProfileService.NormalizeExecutablePath(item.ExecutablePath), processPath, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count == 0)
                {
                    matches = games.Where(item => string.IsNullOrWhiteSpace(item.ExecutablePath)
                        && string.Equals(ProfileService.NormalizeProcessName(item.ProcessName), processName, StringComparison.OrdinalIgnoreCase)).ToList();
                }
            }
            else
            {
                matches = games.Where(item => CredibleExecutableName(item).Equals(processName, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (matches.Count != 1) continue;
            LibraryItem item = matches[0];
            BenchmarkTarget target = _resolver.CreateTarget(item);
            detected.Add(new BenchmarkRunningGame
            {
                LibraryItem = item,
                Target = target,
                Process = new BenchmarkProcessIdentity
                {
                    ProcessId = process.ProcessId,
                    ProcessName = processName,
                    ExecutablePath = processPath,
                    ExecutablePathResolution = processPath is null ? BenchmarkProcessPathResolution.Unavailable : BenchmarkProcessPathResolution.Managed,
                    StartTimeUtc = process.StartTimeUtc
                }
            });
        }

        return detected
            .GroupBy(game => (game.Process.ProcessId, game.Process.StartTimeUtc))
            .Select(group => group.First())
            .OrderBy(game => game.LibraryItem.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(game => game.Process.ProcessId)
            .ToList();
    }

    /// <summary>
    /// Eligibility boundary for benchmark capture and live PresentMon. AllowBenchmark is the
    /// user-facing flag; the curated protected Riot process list is defense-in-depth so a
    /// manual/custom/legacy item pointing at a protected Riot game executable can never become
    /// benchmark or PresentMon eligible, while remaining observable as an active game.
    /// </summary>
    private static bool IsBenchmarkEligible(LibraryItem item)
    {
        if (!item.AllowBenchmark) return false;
        if (RiotGameProcesses.IsProtectedProcessName(item.ProcessName)) return false;
        if (!string.IsNullOrWhiteSpace(item.ExecutablePath)
            && RiotGameProcesses.IsProtectedProcessName(Path.GetFileNameWithoutExtension(item.ExecutablePath)))
        {
            return false;
        }
        return true;
    }

    private static string CredibleExecutableName(LibraryItem item)
    {
        string? executableName = string.IsNullOrWhiteSpace(item.ExecutablePath) ? null : Path.GetFileNameWithoutExtension(item.ExecutablePath);
        return ProfileService.NormalizeProcessName(executableName ?? item.ProcessName);
    }
}
