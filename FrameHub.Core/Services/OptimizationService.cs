using FrameHub.Core.Logging;
using FrameHub.Core.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        public OptimizationBatchResult ApplyProfilesForSnapshots(IEnumerable<ProcessProfile> profiles, IEnumerable<ProcessGroupSnapshot> snapshots, bool allowRealtimePriority, bool force)
        {
            var byName = profiles
                .Where(p => p.IsEnabled)
                .GroupBy(p => ProfileService.NormalizeProcessName(p.ProcessName), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(p => p.UpdatedAt).Last(),
                    StringComparer.OrdinalIgnoreCase);

            var batch = new OptimizationBatchResult();

            foreach (var snapshot in snapshots)
            {
                if (!byName.TryGetValue(snapshot.Name, out var profile)) continue;

                foreach (var instance in snapshot.Instances)
                {
                    var result = ApplyProfileToPid(instance, snapshot.Name, profile, allowRealtimePriority, force);
                    batch.Results.Add(result);
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
            string priority = PriorityService.Normalize(profile.Priority, allowRealtimePriority);
            var mode = NormalizeOptimizationMode(profile.OptimizationMode);
            string signature = BuildProfileSignature(profile, priority, mode, allowRealtimePriority);

            if (!force && _appliedProfileSignatures.TryGetValue(key, out string? existingSignature) && existingSignature == signature)
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
            if (success)
            {
                _appliedProfileSignatures[key] = signature;
            }
            else
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
                    _appliedProfileSignatures.Remove(ProcessScannerService.CreateInstanceKey(process));
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

        public void ClearAllCache() => _appliedProfileSignatures.Clear();

        public void CleanupStaleCache(HashSet<ProcessInstanceKey> activeInstances)
        {
            foreach (var staleKey in _appliedProfileSignatures.Keys.Where(k => !activeInstances.Contains(k)).ToList())
            {
                _appliedProfileSignatures.Remove(staleKey);
            }
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
