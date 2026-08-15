using System.Diagnostics;
using System.IO;
using FrameHub.Core.Logging;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Services;

namespace FrameHub.App.Services;

public sealed record LibraryControlResult(bool Success, string ErrorCode)
{
    public static LibraryControlResult Ok(string code) => new(true, code);
    public static LibraryControlResult Fail(string code) => new(false, code);
}

public interface ITrustedProcessTerminator
{
    Task<bool> StopAsync(LibraryItem item, LibraryProcessIdentity expectedIdentity, CancellationToken cancellationToken);
}

public interface IAppLibraryControlService
{
    LibraryControlResult Start(LibraryItem item);
    Task<LibraryControlResult> StopAsync(LibraryItem item, CancellationToken cancellationToken = default);
}

public sealed class AppLibraryControlService : IAppLibraryControlService
{
    private readonly ProcessScannerService _processScanner;
    private readonly IAppLibraryLaunchService _launchService;
    private readonly ITrustedProcessTerminator _terminator;

    public AppLibraryControlService(
        ProcessScannerService processScanner,
        IAppLibraryLaunchService launchService,
        ITrustedProcessTerminator? terminator = null)
    {
        _processScanner = processScanner;
        _launchService = launchService;
        _terminator = terminator ?? new SystemTrustedProcessTerminator();
    }

    public LibraryControlResult Start(LibraryItem item)
    {
        LibraryLaunchResult result = _launchService.Launch(item);
        return result.Success
            ? LibraryControlResult.Ok("started")
            : LibraryControlResult.Fail(result.ErrorCode == "not_launchable" ? "not_eligible" : result.ErrorCode);
    }

    public async Task<LibraryControlResult> StopAsync(LibraryItem item, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<LibraryProcessIdentity> identities =
            await _processScanner.FindRunningLibraryItemProcessesAsync(item, cancellationToken).ConfigureAwait(false);
        if (identities.Count == 0) return LibraryControlResult.Fail("not_running");

        foreach (LibraryProcessIdentity identity in identities)
        {
            if (!await _terminator.StopAsync(item, identity, cancellationToken).ConfigureAwait(false))
            {
                return LibraryControlResult.Fail("stop_failed");
            }
        }

        return LibraryControlResult.Ok("stop_succeeded");
    }
}

internal sealed class SystemTrustedProcessTerminator : ITrustedProcessTerminator
{
    private static readonly TimeSpan GracefulTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ForcedTimeout = TimeSpan.FromSeconds(2);
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass",
        "svchost", "fontdrvhost", "dwm", "audiodg", "spoolsv", "wudfhost", "wmiprvse", "taskhostw",
        "sihost", "searchhost", "startmenuexperiencehost", "runtimebroker", "applicationframehost",
        "securityhealthservice", "taskmgr", "conhost", "dllhost", "ctfmon", "shellexperiencehost",
        "explorer", "framehub", "framehub.app", "framehub.companion"
    };

    public async Task<bool> StopAsync(
        LibraryItem item,
        LibraryProcessIdentity expectedIdentity,
        CancellationToken cancellationToken)
    {
        if (!IsEligibleTrustedItem(item) || expectedIdentity.ProcessId <= 4 || expectedIdentity.ProcessId == Environment.ProcessId)
        {
            return false;
        }

        try
        {
            using Process process = Process.GetProcessById(expectedIdentity.ProcessId);
            if (!TryReadIdentity(process, out LibraryProcessIdentity? actualIdentity)
                || !IdentityMatches(item, expectedIdentity, actualIdentity!))
            {
                return false;
            }

            if (process.CloseMainWindow())
            {
                if (await WaitForExitAsync(process, GracefulTimeout, cancellationToken).ConfigureAwait(false)) return true;
            }

            if (process.HasExited) return true;
            if (!TryReadIdentity(process, out actualIdentity) || !IdentityMatches(item, expectedIdentity, actualIdentity!))
            {
                return false;
            }

            process.Kill(entireProcessTree: false);
            return await WaitForExitAsync(process, ForcedTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LoggerService.Instance.Warn($"Trusted background-app stop failed for '{item.DisplayName}': {ex.Message}");
            return false;
        }
    }

    internal static bool IsEligibleTrustedItem(LibraryItem item)
    {
        if (!item.IsEnabled || item.Type != LibraryItemType.BackgroundApp || !item.AllowRemoteControl) return false;
        if (string.IsNullOrWhiteSpace(item.ExecutablePath) || string.IsNullOrWhiteSpace(item.ProcessName)) return false;

        string normalizedName = ProfileService.NormalizeProcessName(item.ProcessName);
        if (string.IsNullOrWhiteSpace(normalizedName) || ProtectedNames.Contains(normalizedName)) return false;

        try
        {
            string fullPath = Path.GetFullPath(item.ExecutablePath.Trim());
            if (!File.Exists(fullPath) || IsProtectedSystemPath(fullPath)) return false;
            string executableName = ProfileService.NormalizeProcessName(Path.GetFileNameWithoutExtension(fullPath));
            return executableName.Equals(normalizedName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsProtectedSystemPath(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return true;

        string? normalizedPath = NormalizePath(executablePath);
        if (normalizedPath == null) return true;

        string[] protectedRoots =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetEnvironmentVariable("SystemRoot") ?? string.Empty,
            Environment.GetEnvironmentVariable("WINDIR") ?? string.Empty
        };

        return protectedRoots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Any(root => IsPathWithinDirectory(normalizedPath, root));
    }

    internal static bool IsPathWithinDirectory(string candidatePath, string directoryPath)
    {
        string? candidate = NormalizePath(candidatePath);
        string? directory = NormalizePath(directoryPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (candidate == null || string.IsNullOrWhiteSpace(directory)) return false;
        if (candidate.Equals(directory, StringComparison.OrdinalIgnoreCase)) return true;

        string boundary = directory + Path.DirectorySeparatorChar;
        return candidate.StartsWith(boundary, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IdentityMatches(
        LibraryItem item,
        LibraryProcessIdentity expected,
        LibraryProcessIdentity actual)
    {
        if (actual.ProcessId != expected.ProcessId || actual.StartTimeUtc != expected.StartTimeUtc) return false;

        string trustedName = ProfileService.NormalizeProcessName(item.ProcessName);
        string actualName = ProfileService.NormalizeProcessName(actual.ProcessName);
        string expectedName = ProfileService.NormalizeProcessName(expected.ProcessName);
        if (ProtectedNames.Contains(actualName)
            || !actualName.Equals(trustedName, StringComparison.OrdinalIgnoreCase)
            || !actualName.Equals(expectedName, StringComparison.OrdinalIgnoreCase)) return false;

        string? trustedPath = NormalizePath(item.ExecutablePath);
        string? expectedPath = NormalizePath(expected.ExecutablePath);
        string? actualPath = NormalizePath(actual.ExecutablePath);
        return trustedPath != null && expectedPath != null && actualPath != null
            && trustedPath.Equals(expectedPath, StringComparison.OrdinalIgnoreCase)
            && trustedPath.Equals(actualPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadIdentity(Process process, out LibraryProcessIdentity? identity)
    {
        identity = null;
        try
        {
            if (process.HasExited) return false;
            DateTime startTimeUtc = process.StartTime.ToUniversalTime();
            string processName = ProfileService.NormalizeProcessName(process.ProcessName);
            string? path = process.MainModule?.FileName;
            if (startTimeUtc == DateTime.MinValue || string.IsNullOrWhiteSpace(processName) || string.IsNullOrWhiteSpace(path)) return false;
            identity = new LibraryProcessIdentity
            {
                ProcessId = process.Id,
                StartTimeUtc = startTimeUtc,
                ProcessName = processName,
                ExecutablePath = NormalizePath(path)
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (process.HasExited) return true;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return process.HasExited;
        }
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return Path.GetFullPath(path.Trim()); }
        catch { return null; }
    }
}
