using System.Diagnostics;
using FrameHub.Core.Helpers;
using FrameHub.Core.Logging;
using FrameHub.Core.Models;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Services.Library;

namespace FrameHub.Core.Services.SessionOptimization;

/// <summary>
/// A CPU scheduling selection using the exact representation FrameHub already persists
/// and applies for profiles: an <see cref="OptimizationMode"/> plus a 64-bit logical
/// processor mask. FrameHub exposes only processor group 0 / the first 64 logical
/// processors, so masks beyond the known topology are invalid rather than truncated.
/// </summary>
public sealed record SessionCpuSelection(OptimizationMode Mode, long Mask);

public sealed record SessionCpuLogicalProcessor(int Index, int CoreIndex, byte EfficiencyClass, bool IsECore, bool IsThread, string TypeTag);

public sealed record SessionCpuTopology(IReadOnlyList<SessionCpuLogicalProcessor> Processors);

/// <summary>
/// Narrow native boundary for validated session CPU scheduling operations.
/// Performs and reads operations only — it never decides session policy, profile
/// sources, restore semantics, or authorization; those belong to
/// SessionOptimizationCoordinator and existing authorities. Also serves as the
/// synthetic test seam so tests never mutate real processes.
/// </summary>
public interface ISessionCpuControlBackend
{
    SessionCpuTopology GetTopology();

    bool IsValidSelection(OptimizationMode mode, long mask);

    /// <summary>
    /// Freshly reacquires the process for the expected identity using the established
    /// PID + start time + name + managed-path checks, and requires the existing
    /// <see cref="BenchmarkProcessIdentity.IsSameInstance"/> match. Returns null on
    /// exit, PID reuse, or any identity change — mutations must fail closed.
    /// </summary>
    BenchmarkProcessIdentity? ResolveFreshIdentity(BenchmarkProcessIdentity expected);

    SessionCpuSelection? GetCurrentSelection(int processId);

    /// <summary>Returns the existing ProcessService result string ("OK…" on success).</summary>
    string ApplySelection(int processId, SessionCpuSelection selection);
}

/// <summary>
/// Production backend delegating to the existing <see cref="ProcessService"/> affinity /
/// CPU Sets implementation, topology, and CPU Set map. No second scheduling implementation.
/// </summary>
public sealed class SessionCpuControlBackend : ISessionCpuControlBackend
{
    private readonly ProcessService _processService;
    private readonly Func<IReadOnlyList<CoreInfo>> _topologyProvider;
    private readonly Func<Dictionary<int, uint>> _cpuSetMapProvider;

    public SessionCpuControlBackend(
        Func<IReadOnlyList<CoreInfo>> topologyProvider,
        Func<Dictionary<int, uint>> cpuSetMapProvider,
        ProcessService? processService = null)
    {
        _topologyProvider = topologyProvider;
        _cpuSetMapProvider = cpuSetMapProvider;
        _processService = processService ?? new ProcessService();
    }

    public SessionCpuTopology GetTopology() => new(
        _topologyProvider()
            .Select(core => new SessionCpuLogicalProcessor(core.Index, core.CoreIndex, core.EfficiencyClass, core.IsECore, core.IsThread, core.TypeTag))
            .ToList());

    public bool IsValidSelection(OptimizationMode mode, long mask)
    {
        if (mask == 0)
        {
            return false;
        }

#pragma warning disable CS0618
        if (mode is not (OptimizationMode.Affinity or OptimizationMode.CpuSets))
        {
            return false;
        }
#pragma warning restore CS0618

        long validMask = 0;
        foreach (CoreInfo core in _topologyProvider())
        {
            if (core.Index is >= 0 and < 64)
            {
                validMask |= 1L << core.Index;
            }
        }

        if ((mask & ~validMask) != 0)
        {
            return false;
        }

        if (mode == OptimizationMode.CpuSets)
        {
            // Mirror the ProcessService rule: at least one selected logical processor
            // must map to a CPU Set, otherwise ApplyCpuSetsMode fails with no valid sets.
            Dictionary<int, uint> cpuSetMap = _cpuSetMapProvider();
            for (int index = 0; index < 64; index++)
            {
                if ((mask & (1L << index)) != 0 && cpuSetMap.ContainsKey(index))
                {
                    return true;
                }
            }
            return false;
        }

        return true;
    }

    public BenchmarkProcessIdentity? ResolveFreshIdentity(BenchmarkProcessIdentity expected)
    {
        try
        {
            using Process process = Process.GetProcessById(expected.ProcessId);
            if (process.HasExited)
            {
                return null;
            }

            string? executablePath;
            try
            {
                executablePath = process.MainModule?.FileName;
            }
            catch
            {
                // Managed-path resolution is best effort, mirroring the established
                // identity policy: a path the OS will not provide stays unset.
                executablePath = null;
            }

            var fresh = new BenchmarkProcessIdentity
            {
                ProcessId = process.Id,
                ProcessName = ProfileService.NormalizeProcessName(process.ProcessName),
                ExecutablePath = executablePath,
                ExecutablePathResolution = executablePath is null
                    ? BenchmarkProcessPathResolution.Unavailable
                    : BenchmarkProcessPathResolution.Managed,
                StartTimeUtc = process.StartTime.ToUniversalTime()
            };

            return expected.IsSameInstance(fresh) ? fresh : null;
        }
        catch
        {
            return null;
        }
    }

    public SessionCpuSelection? GetCurrentSelection(int processId)
    {
        (bool success, long mask, OptimizationMode mode, _, _) = _processService.GetCurrentCoreSelection(processId, _cpuSetMapProvider());
        return success && mask != 0 ? new SessionCpuSelection(mode, mask) : null;
    }

    public string ApplySelection(int processId, SessionCpuSelection selection) =>
        _processService.ApplyCoreOptimization(processId, selection.Mask, selection.Mode, _cpuSetMapProvider());
}
