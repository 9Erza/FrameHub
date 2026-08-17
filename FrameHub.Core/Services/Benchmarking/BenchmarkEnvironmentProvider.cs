using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using FrameHub.Core.Logging;
using FrameHub.Core.Models.Benchmarking;

namespace FrameHub.Core.Services.Benchmarking;

/// <summary>
/// Narrow one-shot benchmark environment snapshot contract.
/// Implementations must be stateless: no timer, no cache, no background work, and no
/// dependency on hardware monitoring. Capture is invoked exactly once per benchmark
/// by <see cref="BenchmarkCaptureCoordinator"/> and must never be polled.
/// </summary>
public interface IBenchmarkEnvironmentProvider
{
    BenchmarkEnvironmentSnapshot Capture();
}

/// <summary>
/// Best-effort Windows environment snapshot using safe, non-invasive one-shot system queries
/// (WMI metadata, GlobalMemoryStatusEx, EnumDisplaySettings). Each field is collected
/// independently; a failed lookup leaves that field unavailable and never fails the benchmark.
/// Does not use HardwareMonitorService, LibreHardwareMonitor, PawnIO, or elevation.
/// </summary>
public sealed class BenchmarkEnvironmentProvider : IBenchmarkEnvironmentProvider
{
    private readonly ILogger _logger;

    public BenchmarkEnvironmentProvider(ILogger? logger = null)
    {
        _logger = logger ?? LoggerService.Instance;
    }

    public BenchmarkEnvironmentSnapshot Capture()
    {
        (int Width, int Height, int RefreshRateHz)? displayMode = TryField("DisplayMode", GetDisplayMode);
        return new BenchmarkEnvironmentSnapshot
        {
            OsDescription = TryField(nameof(BenchmarkEnvironmentSnapshot.OsDescription), () =>
            {
                string description = RuntimeInformation.OSDescription?.Trim() ?? string.Empty;
                return string.IsNullOrWhiteSpace(description) ? null : description;
            }),
            OsBuild = TryField(nameof(BenchmarkEnvironmentSnapshot.OsBuild), () =>
            {
                int build = Environment.OSVersion.Version.Build;
                return build > 0 ? build.ToString(CultureInfo.InvariantCulture) : null;
            }),
            CpuName = TryField(nameof(BenchmarkEnvironmentSnapshot.CpuName), () =>
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
                foreach (ManagementBaseObject item in searcher.Get())
                {
                    string? name = item["Name"]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(name)) return name;
                }
                return null;
            }),
            GpuName = TryField(nameof(BenchmarkEnvironmentSnapshot.GpuName), () =>
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                foreach (ManagementBaseObject item in searcher.Get())
                {
                    string? name = item["Name"]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(name)) return name;
                }
                return null;
            }),
            GpuDriverVersion = TryField(nameof(BenchmarkEnvironmentSnapshot.GpuDriverVersion), () =>
            {
                using var searcher = new ManagementObjectSearcher("SELECT DriverVersion FROM Win32_VideoController");
                foreach (ManagementBaseObject item in searcher.Get())
                {
                    string? version = item["DriverVersion"]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(version)) return version;
                }
                return null;
            }),
            TotalMemoryBytes = TryField(nameof(BenchmarkEnvironmentSnapshot.TotalMemoryBytes), () =>
            {
                var status = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
                return NativeMethods.GlobalMemoryStatusEx(ref status) && status.ullTotalPhys > 0
                    ? status.ullTotalPhys
                    : (ulong?)null;
            }),
            DisplayWidth = displayMode?.Width,
            DisplayHeight = displayMode?.Height,
            DisplayRefreshRateHz = displayMode?.RefreshRateHz
        };
    }

    private (int Width, int Height, int RefreshRateHz)? GetDisplayMode()
    {
        var mode = new NativeMethods.DEVMODE { dmSize = (short)Marshal.SizeOf<NativeMethods.DEVMODE>() };
        if (!NativeMethods.EnumDisplaySettings(null, NativeMethods.ENUM_CURRENT_SETTINGS, ref mode))
        {
            return null;
        }

        int width = mode.dmPelsWidth > 0 ? mode.dmPelsWidth : 0;
        int height = mode.dmPelsHeight > 0 ? mode.dmPelsHeight : 0;
        int refresh = mode.dmDisplayFrequency > 1 ? mode.dmDisplayFrequency : 0;
        return width <= 0 || height <= 0 ? null : (width, height, refresh);
    }

    private T? TryField<T>(string field, Func<T?> query)
    {
        try
        {
            return query();
        }
        catch (Exception ex)
        {
            _logger.Warn($"Benchmark environment field '{field}' is unavailable: {ex.Message}");
            return default;
        }
    }

    private static class NativeMethods
    {
        public const int ENUM_CURRENT_SETTINGS = -1;

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumDisplaySettings(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);
    }
}
