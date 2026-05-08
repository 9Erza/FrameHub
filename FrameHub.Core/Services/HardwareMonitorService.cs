using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using LibreHardwareMonitor.Hardware;
using FrameHub.Core.Logging;

namespace FrameHub.Core.Services
{
    /// <summary>
    /// Lazy hardware monitor. LibreHardwareMonitor is opened only when metrics are actually requested.
    /// </summary>
    public class HardwareMonitorService : IDisposable
    {
        private Computer? _computer;
        private readonly ILogger _logger;
        private bool _disposed;
        private bool _enableStorageSensors;

        public HardwareMonitorService(bool enableStorageSensors = false)
        {
            _logger = LoggerService.Instance;
            _enableStorageSensors = enableStorageSensors;
        }

        public bool IsInitialized => _computer != null;

        public void Configure(bool enableStorageSensors)
        {
            if (_enableStorageSensors == enableStorageSensors) return;
            _enableStorageSensors = enableStorageSensors;

            if (_computer != null)
            {
                DisposeComputer();
            }
        }

        public void Start()
        {
            EnsureInitialized();
        }

        public void Stop(bool closeSensors = false)
        {
            if (closeSensors)
            {
                DisposeComputer();
            }
        }

        private void EnsureInitialized()
        {
            if (_computer != null || _disposed) return;

            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsStorageEnabled = _enableStorageSensors
            };

            _computer.Open();
            _logger.Info($"HardwareMonitorService initialized. Storage sensors: {_enableStorageSensors}");
        }

        public HardwareMetrics GetAllMetrics()
        {
            EnsureInitialized();
            var metrics = new HardwareMetrics();
            if (_computer == null) return metrics;

            foreach (var hardware in _computer.Hardware)
            {
                hardware.Update();

                if (hardware.HardwareType == HardwareType.Cpu)
                {
                    metrics.CpuLoad = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Contains("Total"))?.Value ?? 0;

                    var pkgTemp = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name.Contains("Package"));
                    metrics.CpuTemp = pkgTemp?.Value ?? hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature)?.Value ?? 0;

                    metrics.CpuPower = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power && s.Name.Contains("Package"))?.Value ?? 0;
                    metrics.CpuClock = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Clock)?.Value ?? 0;

                    metrics.CoreTemps.Clear();
                    var coreSensors = hardware.Sensors.Where(x => x.SensorType == SensorType.Temperature && x.Name.Contains("Core")).ToList();
                    foreach (var s in coreSensors)
                    {
                        metrics.CoreTemps.Add($"{s.Name}: {s.Value:N1}°C");
                    }
                }

                if (hardware.HardwareType == HardwareType.GpuNvidia || hardware.HardwareType == HardwareType.GpuAmd)
                {
                    metrics.GpuLoad = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Contains("Core"))?.Value ?? 0;
                    metrics.GpuTemp = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name == "GPU Core")?.Value ?? 0;
                    metrics.GpuHotSpot = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name.Contains("Hot Spot"))?.Value ?? 0;
                    metrics.GpuVramTemp = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name.Contains("Memory"))?.Value ?? 0;
                    metrics.GpuCoreClock = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Clock && s.Name.Contains("Core"))?.Value ?? 0;
                    metrics.GpuMemClock = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Clock && s.Name.Contains("Memory"))?.Value ?? 0;
                    metrics.GpuPower = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power)?.Value ?? 0;

                    var vramLoad = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Contains("Memory"));
                    metrics.VramUsagePct = vramLoad?.Value ?? 0;
                }

                if (hardware.HardwareType == HardwareType.Memory)
                {
                    metrics.RamUsagePct = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name == "Memory")?.Value ?? 0;
                    metrics.RamUsedGB = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name == "Memory Used")?.Value ?? 0;
                    metrics.RamAvailableGB = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name == "Memory Available")?.Value ?? 0;
                }

                if (_enableStorageSensors && hardware.HardwareType == HardwareType.Storage)
                {
                    var temp = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
                    if (temp != null)
                        metrics.StorageInfo = $"{hardware.Name}: {temp.Value:N0}°C";
                }
            }

            FillRamFallback(metrics);
            return metrics;
        }

        private static void FillRamFallback(HardwareMetrics metrics)
        {
            if (metrics.RamUsagePct != 0 && metrics.RamUsedGB != 0) return;

            try
            {
                using var availableCounter = new System.Diagnostics.PerformanceCounter("Memory", "Available MBytes");
                double availableMB = availableCounter.NextValue();

                using var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
                double totalMB = 0;
                foreach (var item in searcher.Get())
                {
                    totalMB = Convert.ToDouble(item["TotalVisibleMemorySize"]) / 1024;
                }

                if (totalMB > 0)
                {
                    double usedMB = totalMB - availableMB;
                    metrics.RamAvailableGB = Math.Round(availableMB / 1024.0, 1);
                    metrics.RamUsedGB = Math.Round(usedMB / 1024.0, 1);
                    metrics.RamUsagePct = Math.Round((usedMB / totalMB) * 100, 1);
                }
            }
            catch
            {
                // Ignore fallback errors to prevent application crashes.
            }
        }

        private void DisposeComputer()
        {
            try
            {
                _computer?.Close();
                _computer = null;
                _logger.Info("HardwareMonitorService sensors closed");
            }
            catch
            {
                // Ignore cleanup errors.
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DisposeComputer();
        }
    }

    public class HardwareMetrics
    {
        public double CpuLoad { get; set; }
        public double CpuTemp { get; set; }
        public double CpuPower { get; set; }
        public double CpuClock { get; set; }
        public List<string> CoreTemps { get; set; } = new List<string>();

        public double GpuLoad { get; set; }
        public double GpuTemp { get; set; }
        public double GpuHotSpot { get; set; }
        public double GpuVramTemp { get; set; }
        public double GpuCoreClock { get; set; }
        public double GpuMemClock { get; set; }
        public double GpuPower { get; set; }
        public double VramUsagePct { get; set; }

        public double RamUsagePct { get; set; }
        public double RamUsedGB { get; set; }
        public double RamAvailableGB { get; set; }
        public string StorageInfo { get; set; } = "No data";
    }
}
