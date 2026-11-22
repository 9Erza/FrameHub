using FrameHub.Core.Logging;
using FrameHub.Core.Models;
using FrameHub.Core.Services.Library;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace FrameHub.Core.Services
{
    /// <summary>
    /// Central optimization engine for profiles, temporary process tuning and future FrameHub sessions.
    /// </summary>
    public sealed class OptimizationService
    {
        private readonly ProcessService _processService;
        private readonly Func<Dictionary<int, uint>> _cpuSetMapProvider;
        private readonly ILogger _logger;
        private readonly Dictionary<ProcessInstanceKey, string> _appliedProfileSignatures = new();
        // The profile watcher and manual "apply now" can run concurrently; guard the cache.
        private readonly object _cacheSync = new();

        public OptimizationService(ProcessService processService, Func<Dictionary<int, uint>> cpuSetMapProvider)
        {
            _processService = processService;
            _cpuSetMapProvider = cpuSetMapProvider;
            _logger = LoggerService.Instance;
        }

        public OptimizationBatchResult ApplyProfileToRunningProcesses(ProcessProfile profile, bool allowRealtimePriority, bool force)
        {
            var batch = new OptimizationBatchResult();
            Process[] processes = Array.Empty<Process>();

            try
            {
                string processName = ProfileService.NormalizeProcessName(profile.ProcessName);
                processes = Process.GetProcessesByName(processName);
                foreach (var process in processes)
                {
                    if (!ProfileService.MatchesIdentity(profile, process.ProcessName, TryGetProcessPath(process))) continue;
                    var result = ApplyProfileToProcess(process, profile, allowRealtimePriority, force);
                    batch.Results.Add(result);
                }
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }

            batch.Total = batch.Results.Count;
            batch.Successful = batch.Results.Count(r => r.Success);
            batch.Failed = batch.Total - batch.Successful;
            return batch;
        }

        private static string? TryGetProcessPath(Process process)
        {
            try { return process.MainModule?.FileName; }
            catch { return null; }
        }

        public OptimizationBatchResult ApplyProfilesForSnapshots(IEnumerable<ProcessProfile> profiles, IEnumerable<ProcessGroupSnapshot> snapshots, bool allowRealtimePriority, bool force)
        {
            var byName = profiles
                .Where(p => p.IsEnabled)
                .GroupBy(p => ProfileService.NormalizeProcessName(p.ProcessName), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(p => !string.IsNullOrWhiteSpace(p.ExecutablePath)).ThenByDescending(p => p.UpdatedAt).ToList(),
                    StringComparer.OrdinalIgnoreCase);

            var batch = new OptimizationBatchResult();

            foreach (var snapshot in snapshots)
            {
                if (!byName.TryGetValue(snapshot.Name, out var matchingProfiles)) continue;

                foreach (var instance in snapshot.Instances)
                {
                    foreach (var profile in matchingProfiles)
                    {
                        var result = ApplyProfileToPid(instance, snapshot.Name, profile, allowRealtimePriority, force);
                        if (result.Message == "SKIPPED_IDENTITY_MISMATCH") continue;
                        batch.Results.Add(result);
                        break;
                    }
                }
            }

            batch.Total = batch.Results.Count;
            batch.Successful = batch.Results.Count(r => r.Success);
            batch.Failed = batch.Total - batch.Successful;
            return batch;
        }

        private OptimizationResult ApplyProfileToProcess(Process process, ProcessProfile profile, bool allowRealtimePriority, bool force)
        {
            return ApplyProfileToPid(ProcessScannerService.CreateInstanceKey(process), process.ProcessName, profile, allowRealtimePriority, force);
        }

    private OptimizationResult ApplyProfileToPid(ProcessInstanceKey key, string processName, ProcessProfile profile, bool allowRealtimePriority, bool force)
    {
        // Riot games, client, launcher, and anti-cheat processes are never mutated by FrameHub (Riot compliance).
        if (RiotGameProcesses.IsProtectedProcessName(processName))
        {
            return new OptimizationResult { Success = false, ProcessId = key.ProcessId, ProcessName = processName, Message = "SKIPPED_PROTECTED_RIOT" };
        }

            string priority = PriorityService.Normalize(profile.Priority, allowRealtimePriority);
            var mode = NormalizeOptimizationMode(profile.OptimizationMode);
            string signature = BuildProfileSignature(profile, priority, mode, allowRealtimePriority);

            // The cache key is PID + start time, which already pinned identity when the profile
            // was applied, so the costly identity re-check (process open + module walk) is only
            // needed when something will actually be written. A cached entry is re-applied when
            // the process changed its own affinity, CPU Sets or priority afterwards (many games
            // and launchers reset them during startup), so the profile sticks.
            bool cached;
            lock (_cacheSync)
            {
                cached = _appliedProfileSignatures.TryGetValue(key, out string? existingSignature) && existingSignature == signature;
            }

            bool drifted = !force && cached && HasDrifted(key.ProcessId, profile, priority, allowRealtimePriority);
            if (drifted)
            {
                _logger.Info($"Profile '{profile.DisplayName}' drifted on PID {key.ProcessId} ({processName}); re-applying.");
            }

            if ((force || !cached || drifted) && !MatchesRunningProcessIdentity(key, processName, profile))
            {
                return new OptimizationResult { Success = false, ProcessId = key.ProcessId, ProcessName = processName, Message = "SKIPPED_IDENTITY_MISMATCH" };
            }

            if (!force && cached && !drifted)
            {
                return new OptimizationResult
                {
                    Success = false,
                    ProcessId = key.ProcessId,
                    ProcessName = processName,
                    Mode = mode,
                    Priority = priority,
                    Message = "SKIPPED_ALREADY_APPLIED"
                };
            }

            bool coreApplied = false;
            bool priorityApplied = false;
            string coreMessage = "SKIPPED_CORE";
            string priorityMessage = "SKIPPED_PRIORITY";
            bool requiresAdmin = false;
            int? win32Error = null;

            if (profile.ApplyCoreOptimization)
            {
                coreMessage = _processService.ApplyCoreOptimization(key.ProcessId, profile.AffinityMask, mode, _cpuSetMapProvider());
                coreApplied = coreMessage.StartsWith("OK", StringComparison.OrdinalIgnoreCase);
                requiresAdmin = coreMessage.Contains("_5", StringComparison.OrdinalIgnoreCase) || coreMessage.Contains("ACCESS", StringComparison.OrdinalIgnoreCase);
                win32Error = ExtractWin32Error(coreMessage);
            }

            if (profile.ApplyPriority && PriorityService.TryParse(priority, allowRealtimePriority, out ProcessPriorityClass parsedPriority))
            {
                priorityApplied = _processService.SetPriority(key.ProcessId, parsedPriority);
                priorityMessage = priorityApplied ? "OK_PRIORITY" : "ERR_PRIORITY";
            }

            bool success = coreApplied || priorityApplied;
            bool allRequestedOperationsApplied = (!profile.ApplyCoreOptimization || coreApplied)
                && (!profile.ApplyPriority || priorityApplied);
            lock (_cacheSync)
            {
                if (success && allRequestedOperationsApplied)
                {
                    _appliedProfileSignatures[key] = signature;
                }
                else
                {
                    _appliedProfileSignatures.Remove(key);
                }
            }

            if (!success)
            {
                _logger.Debug($"Optimization failed for PID {key.ProcessId}: core={coreMessage}, priority={priorityMessage}");
            }

            return new OptimizationResult
            {
                Success = success,
                ProcessId = key.ProcessId,
                ProcessName = processName,
                Mode = mode,
                Priority = priority,
                Message = $"{coreMessage}; {priorityMessage}",
                CoreApplied = coreApplied,
                PriorityApplied = priorityApplied,
                RequiresAdmin = requiresAdmin,
                Win32Error = win32Error
            };
        }

        public void ClearProfileCacheForProcess(string processName)
        {
            Process[] processes = Array.Empty<Process>();
            try
            {
                processName = ProfileService.NormalizeProcessName(processName);
                processes = Process.GetProcessesByName(processName);
                foreach (var process in processes)
                {
                    var key = ProcessScannerService.CreateInstanceKey(process);
                    lock (_cacheSync) _appliedProfileSignatures.Remove(key);
                }
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }

        private static bool MatchesRunningProcessIdentity(ProcessInstanceKey key, string processName, ProcessProfile profile)
        {
            if (key.ProcessId <= 0 || key.StartTimeUtc == default)
            {
                return false;
            }

            try
            {
                using var process = Process.GetProcessById(key.ProcessId);
                if (process.HasExited || process.StartTime.ToUniversalTime() != key.StartTimeUtc)
                {
                    return false;
                }

                string? path;
                try { path = process.MainModule?.FileName; } catch { path = null; }
                return ProfileService.MatchesIdentity(profile, process.ProcessName, path);
            }
            catch { return false; }
        }

        public void CleanupStaleCache(HashSet<ProcessInstanceKey> activeInstances)
        {
            lock (_cacheSync)
            {
                foreach (var staleKey in _appliedProfileSignatures.Keys.Where(k => !activeInstances.Contains(k)).ToList())
                {
                    _appliedProfileSignatures.Remove(staleKey);
                }
            }
        }

        /// <summary>
        /// Cheap read-only check whether an already applied profile no longer matches the live
        /// process. Unreadable state never counts as drift, so an inaccessible process cannot
        /// cause an apply loop.
        /// </summary>
        internal bool HasDrifted(int pid, ProcessProfile profile, string priority, bool allowRealtimePriority)
        {
            try
            {
                if (profile.ApplyCoreOptimization)
                {
                    Dictionary<int, uint> map = _cpuSetMapProvider();
                    long expected = ExpectedEffectiveMask(profile.AffinityMask, map);
                    if (expected != 0)
                    {
                        var current = _processService.GetCurrentCoreSelection(pid, map);
                        if (current.Success && current.Mask != expected) return true;
                    }
                }

                if (profile.ApplyPriority && PriorityService.TryParse(priority, allowRealtimePriority, out ProcessPriorityClass desired))
                {
                    ProcessPriorityClass? current = _processService.GetPriority(pid);
                    if (current.HasValue && current.Value != desired) return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Profile drift check skipped for PID {pid}: {ex.Message}");
            }

            return false;
        }

        /// <summary>The profile mask limited to logical processors the topology actually exposes.</summary>
        internal static long ExpectedEffectiveMask(long profileMask, IReadOnlyDictionary<int, uint> cpuSetMap)
        {
            long expected = 0;
            foreach (int index in cpuSetMap.Keys)
            {
                if (index is >= 0 and < 64 && (profileMask & (1L << index)) != 0) expected |= 1L << index;
            }

            return expected;
        }

        private static string BuildProfileSignature(ProcessProfile profile, string priority, OptimizationMode mode, bool allowRealtimePriority)
        {
            return $"{ProfileService.NormalizeProcessName(profile.ProcessName)}|{profile.AffinityMask:X}|{priority}|{mode}|{profile.IsEnabled}|{profile.ApplyPriority}|{profile.ApplyCoreOptimization}|RT:{allowRealtimePriority}";
        }

        private static OptimizationMode NormalizeOptimizationMode(OptimizationMode mode)
        {
#pragma warning disable CS0618
            return (int)mode == 2 ? OptimizationMode.Affinity : mode;
#pragma warning restore CS0618
        }

        private static int? ExtractWin32Error(string message)
        {
            const string openPrefix = "ERR_OPEN_PROCESS_";
            const string cpuSetPrefix = "ERR_CPUSETS_";

            if (message.StartsWith(openPrefix, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(message[openPrefix.Length..], out int openError))
            {
                return openError;
            }

            if (message.StartsWith(cpuSetPrefix, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(message[cpuSetPrefix.Length..], out int cpuSetError))
            {
                return cpuSetError;
            }

            return null;
        }
    }
}
