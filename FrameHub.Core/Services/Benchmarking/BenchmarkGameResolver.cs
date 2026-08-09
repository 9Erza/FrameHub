using System.Diagnostics;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Models.Library;

namespace FrameHub.Core.Services.Benchmarking;

/// <summary>
/// Binds a library item to one exact Windows process instance. PID plus process start
/// time protects against reuse even when the normal managed image path is inaccessible.
/// </summary>
public sealed class BenchmarkGameResolver
{
    private readonly ProcessExecutablePathResolver _pathResolver;

    public BenchmarkGameResolver(ProcessExecutablePathResolver? pathResolver = null) =>
        _pathResolver = pathResolver ?? new ProcessExecutablePathResolver();

    public BenchmarkTarget CreateTarget(LibraryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(item.Id))
        {
            throw new BenchmarkTargetException("missing_library_identity", "The game has no stable FrameHub library item ID.");
        }

        return new BenchmarkTarget
        {
            LibraryItemId = item.Id.Trim(),
            DisplayName = item.DisplayName.Trim(),
            LibrarySource = item.Source.ToString(),
            SourceId = NullIfWhiteSpace(item.AppId),
            ConfiguredExecutablePath = ProfileService.NormalizeExecutablePath(item.ExecutablePath)
        };
    }

    public BenchmarkProcessIdentity ResolveProcessIdentity(Process process, BenchmarkTarget target)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(target);

        try
        {
            if (process.HasExited)
            {
                throw new BenchmarkTargetException("target_not_running", "The selected game process has already exited.");
            }

            ProcessExecutablePathResult pathResult = _pathResolver.Resolve(process);

            var identity = new BenchmarkProcessIdentity
            {
                ProcessId = process.Id,
                ProcessName = ProfileService.NormalizeProcessName(process.ProcessName),
                ExecutablePath = pathResult.ExecutablePath,
                ExecutablePathResolution = pathResult.Resolution,
                StartTimeUtc = process.StartTime.ToUniversalTime()
            };

            ValidateConfiguredPath(target, identity);
            return identity;
        }
        catch (BenchmarkException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            throw new BenchmarkTargetException("target_identity_unavailable", "FrameHub could not establish the selected process identity.", ex);
        }
    }

    public BenchmarkProcessIdentity ResolveCurrentIdentity(int processId, BenchmarkTarget target)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return ResolveProcessIdentity(process, target);
        }
        catch (ArgumentException ex)
        {
            throw new BenchmarkTargetException("target_disappeared", "The benchmark target process is no longer running.", ex);
        }
    }

    public static void ValidateConfiguredPath(BenchmarkTarget target, BenchmarkProcessIdentity identity)
    {
        string? configuredPath = ProfileService.NormalizeExecutablePath(target.ConfiguredExecutablePath);
        string? runningPath = ProfileService.NormalizeExecutablePath(identity.ExecutablePath);
        if (configuredPath is not null
            && runningPath is not null
            && !string.Equals(configuredPath, runningPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new BenchmarkTargetException(
                "target_path_mismatch",
                $"The selected PID is '{runningPath}', but the library item is configured for '{configuredPath}'.");
        }
    }

    public static void ValidateSameProcessInstance(BenchmarkProcessIdentity expected, BenchmarkProcessIdentity current)
    {
        if (!expected.IsSameInstance(current))
        {
            throw new BenchmarkTargetException("target_identity_changed", "The target PID now refers to a different process instance.");
        }
    }

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
