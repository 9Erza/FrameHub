using System.ComponentModel;
using System.Diagnostics;
using FrameHub.Core.Models.Benchmarking;

namespace FrameHub.Core.Services.Benchmarking;

/// <summary>
/// Uses only the normal managed Process path. Inaccessible paths are respected and
/// returned as unavailable; benchmarking never probes the process with native APIs.
/// </summary>
public sealed class ProcessExecutablePathResolver
{
    public ProcessExecutablePathResult Resolve(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return Resolve(() => process.MainModule?.FileName);
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
