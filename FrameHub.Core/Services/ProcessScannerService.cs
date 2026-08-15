using FrameHub.Core.Logging;
using FrameHub.Core.Models;
using FrameHub.Core.Models.Library;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FrameHub.Core.Services
{
    /// <summary>
    /// Provides throttled process snapshots for the UI and lightweight profile watching.
    /// </summary>
    public sealed class ProcessScannerService
    {
        private readonly ProcessService _processService;
        private readonly ILogger _logger;
        private readonly object _cpuSamplingLock = new();
        private readonly Dictionary<ProcessInstanceKey, TimeSpan> _lastCpuTimes = new();
        private DateTime _lastSampleUtc = DateTime.UtcNow;

        public ProcessScannerService(ProcessService processService)
        {
            _processService = processService;
            _logger = LoggerService.Instance;
        }

        public Task<ProcessScanResult> ScanUserProcessesAsync()
        {
            return Task.Run(ScanUserProcesses);
        }

        public Task<ProcessScanResult> ScanProfileProcessesAsync(IEnumerable<ProcessProfile> profiles)
        {
            return Task.Run(() => ScanProfileProcesses(profiles));
        }

        public Task<IReadOnlySet<string>> FindRunningLibraryItemIdsAsync(IEnumerable<LibraryItem> items)
        {
            return Task.Run(() => FindRunningLibraryItemIds(items));
        }

        public Task<IReadOnlyList<LibraryProcessIdentity>> FindRunningLibraryItemProcessesAsync(
            LibraryItem item,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(item);
            return Task.Run(() => FindRunningLibraryItemProcesses(item, cancellationToken), cancellationToken);
        }

        private ProcessScanResult ScanUserProcesses()
        {
            lock (_cpuSamplingLock)
            {
                Process[] processes = Array.Empty<Process>();
                try
                {
                    processes = Process.GetProcesses();
                    DateTime sampleUtc = DateTime.UtcNow;
                    double elapsedSeconds = Math.Max((sampleUtc - _lastSampleUtc).TotalSeconds, 0.1);
                    _lastSampleUtc = sampleUtc;

                    var userProcesses = processes
                        .Where(p => _processService.IsUserProcess(p))
                        .ToList();

                    var result = BuildGroupedSnapshot(userProcesses, elapsedSeconds, includeResources: true);
                    CleanupCpuCache(result.ActiveInstances);
                    return result;
                }
                finally
                {
                    foreach (var process in processes)
                    {
                        process.Dispose();
                    }
                }
            }
        }

        private ProcessScanResult ScanProfileProcesses(IEnumerable<ProcessProfile> profiles)
        {
            var processNames = profiles
                .Where(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.ProcessName))
                .Select(p => ProfileService.NormalizeProcessName(p.ProcessName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var collected = new List<Process>();
            try
            {
                foreach (var processName in processNames)
                {
                    try
                    {
                        collected.AddRange(Process.GetProcessesByName(processName));
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"Profile process scan skipped for '{processName}': {ex.Message}");
                    }
                }

                return BuildGroupedSnapshot(collected, elapsedSeconds: 1, includeResources: false);
            }
            finally
            {
                foreach (var process in collected)
                {
                    process.Dispose();
                }
            }
        }

        private static IReadOnlySet<string> FindRunningLibraryItemIds(IEnumerable<LibraryItem> items)
        {
            var targets = items
                .Where(item => !string.IsNullOrWhiteSpace(item.Id)
                    && (!string.IsNullOrWhiteSpace(item.ProcessName) || !string.IsNullOrWhiteSpace(item.ExecutablePath)))
                .Select(item => new
                {
                    item.Id,
                    ProcessName = ProfileService.NormalizeProcessName(item.ProcessName),
                    ExecutablePath = NormalizeExecutablePath(item.ExecutablePath)
                })
                .ToList();

            var runningItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (process.HasExited) continue;

                    string processName = ProfileService.NormalizeProcessName(process.ProcessName);
                    string? processPath = TryGetProcessPath(process);
                    foreach (var target in targets)
                    {
                        bool isMatch = target.ExecutablePath != null
                            ? processPath != null && target.ExecutablePath.Equals(NormalizeExecutablePath(processPath), StringComparison.OrdinalIgnoreCase)
                            : target.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase);

                        if (isMatch) runningItemIds.Add(target.Id);
                    }
                }
                catch
                {
                    // Access to another process can be denied; it simply cannot match on this scan.
                }
                finally
                {
                    process.Dispose();
                }
            }

            return runningItemIds;
        }

        private static IReadOnlyList<LibraryProcessIdentity> FindRunningLibraryItemProcesses(
            LibraryItem item,
            CancellationToken cancellationToken)
        {
            string trustedName = ProfileService.NormalizeProcessName(item.ProcessName);
            string? trustedPath = NormalizeExecutablePath(item.ExecutablePath);
            if (string.IsNullOrWhiteSpace(item.Id) || (string.IsNullOrWhiteSpace(trustedName) && trustedPath == null))
            {
                return Array.Empty<LibraryProcessIdentity>();
            }

            var matches = new List<LibraryProcessIdentity>();
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (process.HasExited) continue;

                    string processName = ProfileService.NormalizeProcessName(process.ProcessName);
                    string? processPath = TryGetProcessPath(process);
                    string? normalizedPath = NormalizeExecutablePath(processPath);
                    bool nameMatches = string.IsNullOrWhiteSpace(trustedName)
                        || trustedName.Equals(processName, StringComparison.OrdinalIgnoreCase);
                    bool matchesTrustedItem = trustedPath != null
                        ? nameMatches && normalizedPath != null && trustedPath.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase)
                        : nameMatches;
                    if (!matchesTrustedItem) continue;

                    ProcessInstanceKey key = CreateInstanceKey(process);
                    if (key.StartTimeUtc == DateTime.MinValue) continue;

                    matches.Add(new LibraryProcessIdentity
                    {
                        ProcessId = key.ProcessId,
                        StartTimeUtc = key.StartTimeUtc,
                        ProcessName = processName,
                        ExecutablePath = normalizedPath
                    });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Inaccessible processes cannot establish sufficient trusted identity.
                }
                finally
                {
                    process.Dispose();
                }
            }

            return matches;
        }

        private static string? TryGetProcessPath(Process process)
        {
            try { return process.MainModule?.FileName; }
            catch { return null; }
        }

        private static string? NormalizeExecutablePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            try { return Path.GetFullPath(path.Trim()); }
            catch { return path.Trim(); }
        }

        private ProcessScanResult BuildGroupedSnapshot(IEnumerable<Process> processes, double elapsedSeconds, bool includeResources)
        {
            var activeInstances = new HashSet<ProcessInstanceKey>();
            var snapshots = new List<ProcessGroupSnapshot>();

            var groups = processes
                .GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
            {
                long totalMemory = 0;
                double totalCpu = 0;
                ProcessPriorityClass priority = ProcessPriorityClass.Normal;
                var instances = new List<ProcessInstanceKey>();
                var executablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int firstPid = 0;

                foreach (var process in group)
                {
                    try
                    {
                        if (process.HasExited) continue;
                        firstPid = firstPid == 0 ? process.Id : firstPid;

                        var key = CreateInstanceKey(process);
                        instances.Add(key);
                        activeInstances.Add(key);
                        string? executablePath = TryGetProcessPath(process);
                        if (!string.IsNullOrWhiteSpace(executablePath)) executablePaths.Add(NormalizeExecutablePath(executablePath)!);

                        if (includeResources)
                        {
                            totalMemory += process.WorkingSet64;
                            TimeSpan currentCpu = process.TotalProcessorTime;
                            if (_lastCpuTimes.TryGetValue(key, out TimeSpan lastCpu))
                            {
                                double usageMs = Math.Max((currentCpu - lastCpu).TotalMilliseconds, 0);
                                totalCpu += (usageMs / (elapsedSeconds * 1000 * Math.Max(Environment.ProcessorCount, 1))) * 100;
                            }

                            _lastCpuTimes[key] = currentCpu;
                            priority = process.PriorityClass;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"Failed to snapshot process '{group.Key}': {ex.Message}");
                    }
                }

                if (instances.Count == 0) continue;

                snapshots.Add(new ProcessGroupSnapshot
                {
                    Name = group.Key,
                    ExecutablePath = executablePaths.Count == 1 ? executablePaths.Single() : null,
                    FirstProcessId = firstPid,
                    InstanceCount = instances.Count,
                    TotalMemoryBytes = totalMemory,
                    CpuUsagePercent = totalCpu,
                    Priority = priority,
                    Instances = instances
                });
            }

            return new ProcessScanResult
            {
                Groups = snapshots,
                ActiveInstances = activeInstances
            };
        }

        public static ProcessInstanceKey CreateInstanceKey(Process process)
        {
            DateTime startTimeUtc;
            try
            {
                startTimeUtc = process.StartTime.ToUniversalTime();
            }
            catch
            {
                startTimeUtc = DateTime.MinValue;
            }

            return new ProcessInstanceKey(process.Id, startTimeUtc);
        }

        private void CleanupCpuCache(HashSet<ProcessInstanceKey> activeInstances)
        {
            foreach (var staleKey in _lastCpuTimes.Keys.Where(k => !activeInstances.Contains(k)).ToList())
            {
                _lastCpuTimes.Remove(staleKey);
            }
        }
    }
}
