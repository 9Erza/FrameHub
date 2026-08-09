using System.Diagnostics;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Models.Library;

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
    private readonly ProcessExecutablePathResolver _pathResolver = new();

    public IReadOnlyList<BenchmarkProcessSnapshot> GetProcesses()
    {
        var snapshots = new List<BenchmarkProcessSnapshot>();
        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                if (process.HasExited) continue;
                ProcessExecutablePathResult path = _pathResolver.Resolve(process);
                snapshots.Add(new BenchmarkProcessSnapshot(process.Id, process.ProcessName, path.ExecutablePath, process.StartTime.ToUniversalTime()));
            }
            catch
            {
                // Protected or exiting processes are simply unavailable for this lightweight scan.
            }
            finally
            {
                process.Dispose();
            }
        }

        return snapshots;
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

    public IReadOnlyList<BenchmarkRunningGame> Detect(IEnumerable<LibraryItem> libraryItems)
    {
        ArgumentNullException.ThrowIfNull(libraryItems);
        List<LibraryItem> games = libraryItems
            .Where(item => item.IsEnabled && item.Type == LibraryItemType.Game && !string.IsNullOrWhiteSpace(item.Id))
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

    private static string CredibleExecutableName(LibraryItem item)
    {
        string? executableName = string.IsNullOrWhiteSpace(item.ExecutablePath) ? null : Path.GetFileNameWithoutExtension(item.ExecutablePath);
        return ProfileService.NormalizeProcessName(executableName ?? item.ProcessName);
    }
}
