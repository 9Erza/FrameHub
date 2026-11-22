using System.ComponentModel;
using System.Diagnostics;
using FrameHub.Core.Models.Benchmarking;

namespace FrameHub.Core.Services.Benchmarking;

/// <summary>
/// Single executable-path reader for observation and identity checks. It asks the kernel for
/// the image name with a query-limited handle (QueryFullProcessImageName): cheaper than walking
/// modules and also available for processes FrameHub cannot open with read access (games and
/// launchers running elevated or protected). It reads no process memory. Riot game, client and
/// Vanguard processes are never opened this way; for them, and when the query fails, the managed
/// MainModule path is used. Unavailable paths stay unavailable.
/// </summary>
public sealed class ProcessExecutablePathResolver
{
    public ProcessExecutablePathResult Resolve(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return Resolve(() => TryGetPath(process));
    }

    /// <summary>Raw (not normalized) executable path, or null. Never throws.</summary>
    public static string? TryGetPath(Process process)
    {
        try
        {
            if (!FrameHub.Core.Services.Library.RiotGameProcesses.IsProtectedProcessName(process.ProcessName)
                && TryQueryImageName(process.Id) is string native)
            {
                return native;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Exited or inaccessible; fall through to the managed path.
        }

        try { return process.MainModule?.FileName; }
        catch { return null; }
    }

    private static string? TryQueryImageName(int processId)
    {
        if (processId <= 4) return null;
        IntPtr handle = FrameHub.Core.Helpers.NativeMethods.OpenProcess(FrameHub.Core.Helpers.NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new char[1024];
            uint size = (uint)buffer.Length;
            return FrameHub.Core.Helpers.NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size) && size > 0
                ? new string(buffer, 0, (int)size)
                : null;
        }
        finally
        {
            FrameHub.Core.Helpers.NativeMethods.CloseHandle(handle);
        }
    }

    public ProcessExecutablePathResult Resolve(Func<string?> managedPathProvider)
    {
        ArgumentNullException.ThrowIfNull(managedPathProvider);
        string? managedPath = TryManaged(managedPathProvider);
        if (Normalize(managedPath) is string normalizedManaged)
        {
            return new ProcessExecutablePathResult(normalizedManaged, BenchmarkProcessPathResolution.Managed);
        }

        return new ProcessExecutablePathResult(null, BenchmarkProcessPathResolution.Unavailable);
    }

    private static string? TryManaged(Func<string?> provider)
    {
        try { return provider(); }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException or UnauthorizedAccessException) { return null; }
    }

    private static string? Normalize(string? path) => string.IsNullOrWhiteSpace(path)
        ? null
        : ProfileService.NormalizeExecutablePath(path);
}

public readonly record struct ProcessExecutablePathResult(string? ExecutablePath, BenchmarkProcessPathResolution Resolution);
