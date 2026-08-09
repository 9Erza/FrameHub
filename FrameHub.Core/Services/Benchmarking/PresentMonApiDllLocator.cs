using System.Runtime.InteropServices;
using FrameHub.Core.Models.Benchmarking;

namespace FrameHub.Core.Services.Benchmarking;

public interface IPresentMonServiceConfigReader { string? TryGetBinaryPath(string serviceName); }

public sealed class PresentMonApiDllLocator
{
    public const string ServiceName = "PresentMonSharedService";
    public static readonly string OfficialPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Intel", "PresentMonSharedService", "PresentMonAPI2.dll");
    private readonly string? _explicitPath;
    private readonly IPresentMonServiceConfigReader _serviceReader;
    private readonly Func<string, bool> _fileExists;

    public PresentMonApiDllLocator(string? explicitPath = null, IPresentMonServiceConfigReader? serviceReader = null, Func<string, bool>? fileExists = null)
    { _explicitPath = explicitPath; _serviceReader = serviceReader ?? new WindowsServiceConfigReader(); _fileExists = fileExists ?? File.Exists; }

    public string Locate()
    {
        if (!string.IsNullOrWhiteSpace(_explicitPath))
        {
            if (!Path.IsPathFullyQualified(_explicitPath)) throw new PresentMonUnavailableException("The --presentmon-api-dll override must be an absolute path.");
            string candidate = Path.GetFullPath(_explicitPath);
            if (!_fileExists(candidate)) throw new PresentMonUnavailableException($"The --presentmon-api-dll override does not exist: '{candidate}'.");
            return candidate;
        }
        string? binaryPath = _serviceReader.TryGetBinaryPath(ServiceName);
        if (!string.IsNullOrWhiteSpace(binaryPath))
        {
            string? directory = Path.GetDirectoryName(ExtractExecutablePath(binaryPath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                string candidate = Path.Combine(directory, "PresentMonAPI2.dll");
                if (_fileExists(candidate)) return candidate;
            }
        }
        if (_fileExists(OfficialPath)) return OfficialPath;
        throw new PresentMonUnavailableException($"PresentMon Service/API is unavailable: PresentMonAPI2.dll was not found via service '{ServiceName}' or the official path '{OfficialPath}'.");
    }

    internal static string ExtractExecutablePath(string binaryPath)
    {
        string value = binaryPath.Trim();
        if (value.StartsWith('"')) { int end = value.IndexOf('"', 1); return end > 1 ? value[1..end] : value.Trim('"'); }
        int exe = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe >= 0 ? value[..(exe + 4)] : value;
    }
}

public sealed class WindowsServiceConfigReader : IPresentMonServiceConfigReader
{
    public string? TryGetBinaryPath(string serviceName)
    {
        nint scm = OpenSCManager(null, null, 0x0001); if (scm == 0) return null;
        try
        {
            nint service = OpenService(scm, serviceName, 0x0001); if (service == 0) return null;
            try
            {
                QueryServiceConfig(service, 0, 0, out uint required); if (required == 0) return null;
                nint buffer = Marshal.AllocHGlobal((int)required);
                try { return QueryServiceConfig(service, buffer, required, out _) ? Marshal.PtrToStructure<QueryServiceConfigData>(buffer).BinaryPathName : null; }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(scm); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct QueryServiceConfigData { public uint ServiceType, StartType, ErrorControl; public string BinaryPathName, LoadOrderGroup; public nint TagId, Dependencies; public string ServiceStartName, DisplayName; }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint OpenService(nint manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryServiceConfig(nint service, nint config, uint size, out uint needed);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseServiceHandle(nint handle);
}
