using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using FrameHub.Core.Helpers;
using FrameHub.Core.Logging;
using FrameHub.Core.Models;

namespace FrameHub.Core.Services
{
    /// <summary>
    /// Handles CPU topology discovery and lightweight per-core telemetry.
    /// </summary>
    public class HardwareService : IDisposable
    {
        private const int MaxAffinityMaskLogicalProcessors = 64;
        private readonly ILogger _logger;
        private readonly List<PerformanceCounter> _cpuCounters = new();
        private bool _countersInitialized;
        private bool _disposed;

        public HardwareService()
        {
            _logger = LoggerService.Instance;
        }

        public List<CoreInfo> GetCoreTopology()
        {
            try
            {
                var cpuSets = GetSystemCpuSetInfos()
                    .Where(x => x.Group == 0)
                    .OrderBy(x => x.LogicalProcessorIndex)
                    .Take(MaxAffinityMaskLogicalProcessors)
                    .ToList();

                if (cpuSets.Count > 0)
                {
                    var cores = BuildTopologyFromCpuSets(cpuSets);
                    _logger.Info($"CPU topology from CPU Sets: logical={cores.Count}, P={cores.Count(c => !c.IsECore && !c.IsThread)}, T={cores.Count(c => c.IsThread)}, E={cores.Count(c => c.IsECore)}");

                    int totalCpuSets = GetSystemCpuSetInfos().Count;
                    if (totalCpuSets > cores.Count)
                    {
                        _logger.Warn("Only processor group 0 / first 64 logical processors are exposed because classic affinity masks are limited in this UI.");
                    }

                    return cores;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"CPU Sets topology detection failed, falling back to WMI heuristic: {ex.Message}");
            }

            return GetFallbackTopology();
        }

        public List<double> GetCurrentLoads()
        {
            EnsureCountersInitialized();
            var loads = new List<double>();

            foreach (var counter in _cpuCounters)
            {
                try
                {
                    loads.Add(Math.Round(counter.NextValue(), 1));
                }
                catch (Exception ex)
                {
                    _logger.Debug($"Failed to sample CPU load: {ex.Message}");
                    loads.Add(0);
                }
            }

            if (loads.Count > 0)
            {
                _logger.Debug($"CPU Load: Avg={loads.Average():F1}%, Max={loads.Max():F1}%, Min={loads.Min():F1}%");
            }

            return loads;
        }

        public Dictionary<int, uint> GetLogicalCoreToCpuSetIdMap()
        {
            try
            {
                var map = GetSystemCpuSetInfos()
                    .Where(x => x.Group == 0)
                    .GroupBy(x => x.LogicalProcessorIndex)
                    .ToDictionary(g => (int)g.Key, g => g.First().Id);

                _logger.Info($"CPU Set Map: {map.Count} logical processors mapped");
                return map;
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to build CPU Set map", ex);
                return new Dictionary<int, uint>();
            }
        }

        public string GetCpuVendor()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("Select Manufacturer from Win32_Processor");
                foreach (var item in searcher.Get())
                {
                    var vendor = item["Manufacturer"]?.ToString();
                    if (!string.IsNullOrEmpty(vendor))
                    {
                        if (vendor.Contains("AMD", StringComparison.OrdinalIgnoreCase)) return "AMD";
                        if (vendor.Contains("Intel", StringComparison.OrdinalIgnoreCase)) return "Intel";
                        return vendor;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Failed to get CPU vendor via WMI: {ex.Message}");
            }

            return "Unknown";
        }

        private List<CoreInfo> BuildTopologyFromCpuSets(List<CpuSetInfo> cpuSets)
        {
            byte maxEfficiency = cpuSets.Max(x => x.EfficiencyClass);
            byte minEfficiency = cpuSets.Min(x => x.EfficiencyClass);
            bool isHeterogeneous = maxEfficiency > minEfficiency;

            var result = new List<CoreInfo>();

            var coreGroups = cpuSets
                .GroupBy(x => new { x.Group, x.CoreIndex })
                .OrderBy(g => g.Min(x => x.LogicalProcessorIndex));

            foreach (var coreGroup in coreGroups)
            {
                var logicalProcessors = coreGroup
                    .OrderBy(x => x.LogicalProcessorIndex)
                    .ToList();

                bool isECore = isHeterogeneous && logicalProcessors.Max(x => x.EfficiencyClass) < maxEfficiency;

                for (int i = 0; i < logicalProcessors.Count; i++)
                {
                    var cpu = logicalProcessors[i];
                    bool isThread = !isECore && logicalProcessors.Count > 1 && i > 0;

                    result.Add(new CoreInfo
                    {
                        Index = cpu.LogicalProcessorIndex,
                        Group = cpu.Group,
                        CoreIndex = cpu.CoreIndex,
                        EfficiencyClass = cpu.EfficiencyClass,
                        IsECore = isECore,
                        IsThread = isThread,
                        TypeTag = isECore ? "[E]" : isThread ? "[T]" : "[P]"
                    });
                }
            }

            return result
                .OrderBy(c => c.Index)
                .Take(MaxAffinityMaskLogicalProcessors)
                .ToList();
        }

        private List<CoreInfo> GetFallbackTopology()
        {
            var cores = new List<CoreInfo>();
            int logicalProcs = Math.Min(Environment.ProcessorCount, MaxAffinityMaskLogicalProcessors);
            int physicalCores = 0;

            try
            {
                using var searcher = new ManagementObjectSearcher("Select NumberOfCores from Win32_Processor");
                foreach (var item in searcher.Get())
                {
                    physicalCores += int.Parse(item["NumberOfCores"]?.ToString() ?? "0");
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Failed to query physical core count via WMI: {ex.Message}");
                physicalCores = logicalProcs;
            }

            if (physicalCores <= 0) physicalCores = logicalProcs;

            bool hasSmt = logicalProcs == physicalCores * 2;
            int pCores = hasSmt ? physicalCores : logicalProcs;
            int index = 0;

            for (int i = 0; i < pCores && index < logicalProcs; i++)
            {
                cores.Add(new CoreInfo { Index = index++, CoreIndex = i, TypeTag = "[P]", IsThread = false, IsECore = false });
                if (hasSmt && index < logicalProcs)
                {
                    cores.Add(new CoreInfo { Index = index++, CoreIndex = i, TypeTag = "[T]", IsThread = true, IsECore = false });
                }
            }

            _logger.Warn("Using fallback CPU topology. P/E-core classification may be approximate on hybrid CPUs.");
            return cores;
        }

        private List<CpuSetInfo> GetSystemCpuSetInfos()
        {
            var results = new List<CpuSetInfo>();
            IntPtr currentProcessHandle = Process.GetCurrentProcess().Handle;

            NativeMethods.GetSystemCpuSetInformation(IntPtr.Zero, 0, out uint length, currentProcessHandle, 0);
            if (length == 0)
            {
                return results;
            }

            IntPtr buffer = Marshal.AllocHGlobal((int)length);
            try
            {
                if (!NativeMethods.GetSystemCpuSetInformation(buffer, length, out length, currentProcessHandle, 0))
                {
                    int error = Marshal.GetLastWin32Error();
                    _logger.Warn($"GetSystemCpuSetInformation failed. Win32Error={error}");
                    return results;
                }

                IntPtr currentPtr = buffer;
                IntPtr endPtr = IntPtr.Add(buffer, (int)length);

                while (currentPtr.ToInt64() < endPtr.ToInt64())
                {
                    uint size = (uint)Marshal.ReadInt32(currentPtr, 0);
                    uint type = (uint)Marshal.ReadInt32(currentPtr, 4);

                    if (size == 0) break;

                    if (type == 0 && size >= 20)
                    {
                        results.Add(new CpuSetInfo
                        {
                            Id = (uint)Marshal.ReadInt32(currentPtr, 8),
                            Group = (ushort)Marshal.ReadInt16(currentPtr, 12),
                            LogicalProcessorIndex = Marshal.ReadByte(currentPtr, 14),
                            CoreIndex = Marshal.ReadByte(currentPtr, 15),
                            EfficiencyClass = Marshal.ReadByte(currentPtr, 18)
                        });
                    }

                    currentPtr = IntPtr.Add(currentPtr, (int)size);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            return results;
        }

        private void EnsureCountersInitialized()
        {
            if (_countersInitialized || _disposed) return;
            _countersInitialized = true;

            try
            {
                int coreCount = Math.Min(Environment.ProcessorCount, MaxAffinityMaskLogicalProcessors);
                for (int i = 0; i < coreCount; i++)
                {
                    var counter = new PerformanceCounter("Processor Information", "% Processor Utility", $"0,{i}");
                    counter.NextValue();
                    _cpuCounters.Add(counter);
                }

                _logger.Info($"Initialized {coreCount} CPU performance counters");
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to initialize CPU performance counters: {ex.Message}");
            }
        }

        public void ReleaseCpuLoadCounters()
        {
            foreach (var counter in _cpuCounters)
            {
                counter.Dispose();
            }

            if (_cpuCounters.Count > 0)
            {
                _logger.Debug($"Released {_cpuCounters.Count} CPU performance counters");
            }

            _cpuCounters.Clear();
            _countersInitialized = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ReleaseCpuLoadCounters();
        }

        private sealed class CpuSetInfo
        {
            public uint Id { get; set; }
            public int Group { get; set; }
            public int LogicalProcessorIndex { get; set; }
            public int CoreIndex { get; set; }
            public byte EfficiencyClass { get; set; }
        }
    }
}
