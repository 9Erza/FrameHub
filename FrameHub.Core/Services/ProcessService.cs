using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FrameHub.Core.Helpers;
using FrameHub.Core.Logging;
using FrameHub.Core.Models;

namespace FrameHub.Core.Services
{
    /// <summary>
    /// Safe wrapper around process priority, affinity and CPU Sets APIs.
    /// </summary>
    public class ProcessService
    {
        private static readonly ILogger _logger = LoggerService.Instance;

        private static readonly HashSet<string> SystemProcessBlacklist = new(StringComparer.OrdinalIgnoreCase)
        {
            "svchost", "taskhostw", "explorer", "sihost", "searchhost",
            "startmenuexperiencehost", "runtimebroker", "applicationframehost",
            "shellhost", "system", "idle", "conhost", "wmiprvse", "ctfmon",
            "fontdrvhost", "dwm", "spoolsv", "lsass", "csrss", "smss", "winlogon",
            "services", "registry", "securityhealthservice", "audiodg", "taskmgr"
        };

        public bool IsUserProcess(Process p)
        {
            try
            {
                if (p.Id <= 4 || p.HasExited) return false;
                if (p.SessionId == 0) return false;
                if (SystemProcessBlacklist.Contains(p.ProcessName)) return false;

                return true;
            }
            catch (Exception ex)
            {
                _logger.Debug($"Could not classify process as user process: {ex.Message}");
                return false;
            }
        }

        public bool SetProcessAffinity(int pid, long mask)
        {
            if (mask == 0) return false;

            try
            {
                using var proc = Process.GetProcessById(pid);
                proc.ProcessorAffinity = (IntPtr)mask;
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to set CPU affinity for PID {pid}: {ex.Message}");
                return false;
            }
        }

        public bool SetPriority(int pid, ProcessPriorityClass priority)
        {
            try
            {
                using var proc = Process.GetProcessById(pid);
                proc.PriorityClass = priority;
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to set priority for PID {pid}: {ex.Message}");
                return false;
            }
        }

        public string GetPriorityString(int pid)
        {
            try
            {
                using var proc = Process.GetProcessById(pid);
                return proc.PriorityClass.ToString();
            }
            catch (Exception ex)
            {
                _logger.Debug($"Failed to read priority for PID {pid}: {ex.Message}");
                return "Normal";
            }
        }

        public (bool Success, long Mask, OptimizationMode Mode, string Source, string Message) GetCurrentCoreSelection(int pid, Dictionary<int, uint> cpuSetMap)
        {
            if (pid <= 0)
            {
                return (false, 0, OptimizationMode.CpuSets, "None", "Invalid PID.");
            }

            if (TryReadProcessDefaultCpuSets(pid, cpuSetMap, out long cpuSetMask, out string cpuSetMessage) && cpuSetMask != 0)
            {
                return (true, cpuSetMask, OptimizationMode.CpuSets, "CpuSets", cpuSetMessage);
            }

            try
            {
                using var proc = Process.GetProcessById(pid);
                long affinityMask = proc.ProcessorAffinity.ToInt64();
                if (affinityMask != 0)
                {
                    return (true, affinityMask, OptimizationMode.Affinity, "Affinity", "Read current processor affinity.");
                }

                return (false, 0, OptimizationMode.CpuSets, "Affinity", "Processor affinity mask is empty.");
            }
            catch (Exception ex)
            {
                _logger.Debug($"Failed to read current CPU selection for PID {pid}: {ex.Message}");
                return (false, 0, OptimizationMode.CpuSets, "None", ex.Message);
            }
        }

        private bool TryReadProcessDefaultCpuSets(int pid, Dictionary<int, uint> cpuSetMap, out long mask, out string message)
        {
            mask = 0;
            message = "No process CPU Sets assigned.";

            if (cpuSetMap.Count == 0)
            {
                message = "CPU Set map is empty.";
                return false;
            }

            IntPtr hProc = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProc == IntPtr.Zero)
            {
                int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                message = $"OpenProcess for CPU Sets query failed: {error}.";
                return false;
            }

            try
            {
                bool initialRead = NativeMethods.GetProcessDefaultCpuSets(hProc, null, 0, out uint requiredCount);
                if (!initialRead && requiredCount == 0)
                {
                    int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                    message = $"GetProcessDefaultCpuSets failed: {error}.";
                    return false;
                }

                if (requiredCount == 0)
                {
                    return false;
                }

                var cpuSetIds = new uint[(int)requiredCount];
                if (!NativeMethods.GetProcessDefaultCpuSets(hProc, cpuSetIds, requiredCount, out requiredCount))
                {
                    int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                    message = $"GetProcessDefaultCpuSets buffer read failed: {error}.";
                    return false;
                }

                var logicalByCpuSetId = cpuSetMap
                    .GroupBy(x => x.Value)
                    .ToDictionary(g => g.Key, g => g.First().Key);

                foreach (uint cpuSetId in cpuSetIds.Distinct())
                {
                    if (logicalByCpuSetId.TryGetValue(cpuSetId, out int logicalIndex) && logicalIndex >= 0 && logicalIndex < 64)
                    {
                        mask |= 1L << logicalIndex;
                    }
                }

                message = mask == 0
                    ? "Process CPU Sets are assigned, but they do not map to visible logical processors."
                    : "Read current process CPU Sets.";

                return mask != 0;
            }
            catch (Exception ex)
            {
                _logger.Debug($"Failed to read CPU Sets for PID {pid}: {ex.Message}");
                message = ex.Message;
                return false;
            }
            finally
            {
                NativeMethods.CloseHandle(hProc);
            }
        }


        public string ApplyCoreOptimization(int pid, long mask, OptimizationMode mode, Dictionary<int, uint> cpuSetMap)
        {
            if (mask == 0) return "ERR_EMPTY_MASK";

#pragma warning disable CS0618
            if ((int)mode == 2)
            {
                _logger.Warn("Legacy Exclusive mode detected. Falling back to Affinity.");
                mode = OptimizationMode.Affinity;
            }
#pragma warning restore CS0618

            try
            {
                return mode switch
                {
                    OptimizationMode.Affinity => ApplyAffinityMode(pid, mask),
                    OptimizationMode.CpuSets => ApplyCpuSetsMode(pid, mask, cpuSetMap),
                    _ => "ERR_UNKNOWN_MODE"
                };
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to apply core optimization to PID {pid}: {ex.Message}");
                return $"ERR_EXC: {ex.Message}";
            }
        }

        private string ApplyAffinityMode(int pid, long mask)
        {
            using var proc = Process.GetProcessById(pid);
            proc.ProcessorAffinity = (IntPtr)mask;
            ClearCpuSets(pid);
            return "OK_AFFINITY";
        }

        private string ApplyCpuSetsMode(int pid, long mask, Dictionary<int, uint> cpuSetMap)
        {
            if (cpuSetMap.Count == 0)
            {
                return "ERR_CPUSET_MAP_EMPTY";
            }

            uint[] selectedCpuSetIds = ConvertMaskToCpuSetIds(mask, cpuSetMap);
            if (selectedCpuSetIds.Length == 0)
            {
                return "ERR_NO_VALID_CPUSETS";
            }

            IntPtr hProc = NativeMethods.OpenProcess(NativeMethods.PROCESS_SET_LIMITED_INFORMATION, false, pid);
            if (hProc == IntPtr.Zero)
            {
                int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                return $"ERR_OPEN_PROCESS_{error}";
            }

            try
            {
                bool ok = NativeMethods.SetProcessDefaultCpuSets(hProc, selectedCpuSetIds, (uint)selectedCpuSetIds.Length);
                if (!ok)
                {
                    int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                    return $"ERR_CPUSETS_{error}";
                }

                return $"OK_CPUSETS_{selectedCpuSetIds.Length}";
            }
            finally
            {
                NativeMethods.CloseHandle(hProc);
            }
        }

        private void ClearCpuSets(int pid)
        {
            IntPtr hProc = NativeMethods.OpenProcess(NativeMethods.PROCESS_SET_LIMITED_INFORMATION, false, pid);
            if (hProc == IntPtr.Zero) return;

            try
            {
                NativeMethods.SetProcessDefaultCpuSets(hProc, Array.Empty<uint>(), 0);
            }
            catch (Exception ex)
            {
                _logger.Debug($"Could not clear CPU Sets for PID {pid}: {ex.Message}");
            }
            finally
            {
                NativeMethods.CloseHandle(hProc);
            }
        }

        private uint[] ConvertMaskToCpuSetIds(long mask, Dictionary<int, uint> cpuSetMap)
        {
            var ids = new List<uint>();
            int maxIndex = Math.Min(cpuSetMap.Keys.DefaultIfEmpty(-1).Max(), 63);

            for (int i = 0; i <= maxIndex; i++)
            {
                if ((mask & (1L << i)) != 0 && cpuSetMap.TryGetValue(i, out uint cpuSetId))
                {
                    ids.Add(cpuSetId);
                }
            }

            return ids.Distinct().ToArray();
        }
    }
}
