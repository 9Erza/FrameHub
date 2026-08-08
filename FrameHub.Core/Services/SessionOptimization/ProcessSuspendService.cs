using FrameHub.Core.Logging;
using FrameHub.Core.Models.SessionOptimization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace FrameHub.Core.Services.SessionOptimization;

public sealed class ProcessSuspendService
{
    private const uint PROCESS_SUSPEND_RESUME = 0x0800;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    private readonly ILogger _logger = LoggerService.Instance;

    private static readonly HashSet<string> CriticalProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass",
        "svchost", "fontdrvhost", "dwm", "audiodg", "spoolsv", "wudfhost", "wmiprvse", "taskhostw",
        "sihost", "searchhost", "startmenuexperiencehost", "runtimebroker", "applicationframehost",
        "securityhealthservice", "taskmgr", "conhost", "dllhost", "ctfmon", "shellexperiencehost"
    };

    private static readonly HashSet<string> AntiCheatAndProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "faceit", "faceitclient", "faceitservice", "faceitclientservice", "faceitoverlay", "faceitservice64",
        "vgc", "vgtray", "vgk", "riotclientcrashhandler", "riotclientservices",
        "easyanticheat", "easyanticheat_eos", "eaclauncher", "eosoverlayrenderer",
        "beservice", "beservice_x64", "battleye", "belauncher",
        "punkbuster", "pnkbstra", "pnkbstrb",
        "framehub", "framehub.app",
        // Steam and Steam overlay/web helper processes are never touched by Session Optimization.
        // Suspending these can hurt CS2/Steam games badly instead of improving FPS.
        "steam", "steamservice", "steamclientservice", "steamwebhelper", "steamerrorreporter",
        "steamerrorreporter64", "gameoverlayui"
    };

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(
        IntPtr hProcess,
        out System.Runtime.InteropServices.ComTypes.FILETIME creationTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME exitTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME kernelTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME userTime);

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(IntPtr processHandle);

    private enum IdentityValidationResult
    {
        Match,
        ProcessNotFound,
        DifferentProcess,
        CannotVerify
    }

    private enum ResumeAttemptResult
    {
        Resumed,
        ProcessNotFound,
        DifferentProcess,
        Failed
    }

    private sealed class ProcessIdentity
    {
        public string ProcessName { get; init; } = string.Empty;
        public DateTime ProcessStartTimeUtc { get; init; }
        public string? ExecutablePath { get; init; }
    }

    public Task<SessionProcessSnapshot> CaptureProcessSnapshotAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => CaptureProcessSnapshot(cancellationToken), cancellationToken);
    }

    public IReadOnlyList<RunningProcessGroup> GetRunningProcessGroups(
        SessionProcessSnapshot snapshot,
        IEnumerable<string> protectedProcessNames)
    {
        var protectedNames = BuildProtectedNameSet(protectedProcessNames);
        return snapshot.Processes
            .Where(process => !string.IsNullOrWhiteSpace(process.NormalizedProcessName))
            .Where(process => !IsProtectedProcessName(process.NormalizedProcessName, protectedNames, allowExplorer: true))
            .Where(process => !IsSteamRelatedProcess(process.NormalizedProcessName, process.ExecutablePath))
            .GroupBy(process => process.NormalizedProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new RunningProcessGroup
            {
                NormalizedProcessName = group.Key,
                ProcessName = AddExeSuffix(group.First().ProcessName),
                InstanceCount = group.Count(),
                ExamplePath = group.Select(process => process.ExecutablePath).FirstOrDefault(path => !string.IsNullOrWhiteSpace(path))
            })
            .OrderBy(group => group.ProcessName)
            .ToList();
    }

    public IReadOnlyList<SuspendCandidate> BuildCandidates(
        SessionProcessSnapshot snapshot,
        IEnumerable<BackgroundProcessRule> enabledRules,
        IEnumerable<string> customProcessNames,
        IEnumerable<string> protectedProcessNames)
    {
        var rules = enabledRules.Where(rule => rule.IsEnabled && rule.ProcessNames.Count > 0).ToList();
        var customNames = new HashSet<string>(customProcessNames.Select(NormalizeProcessName).Where(name => !string.IsNullOrWhiteSpace(name)), StringComparer.OrdinalIgnoreCase);
        if (rules.Count == 0 && customNames.Count == 0) return Array.Empty<SuspendCandidate>();

        var protectedNames = BuildProtectedNameSet(protectedProcessNames);
        var candidates = new List<SuspendCandidate>();
        foreach (var process in snapshot.Processes)
        {
            if (process.ProcessStartTimeUtc == default) continue;

            bool isExplorer = process.NormalizedProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase);
            if (IsProtectedProcessName(process.NormalizedProcessName, protectedNames, allowExplorer: isExplorer)
                || IsSteamRelatedProcess(process.NormalizedProcessName, process.ExecutablePath)) continue;

            var rule = rules.FirstOrDefault(rule => MatchesRule(rule, process.NormalizedProcessName, process.ExecutablePath)
                && (!isExplorer || rule.Id.Equals("explorer", StringComparison.OrdinalIgnoreCase)));
            if (rule != null)
            {
                candidates.Add(new SuspendCandidate
                {
                    RuleId = rule.Id,
                    RuleName = rule.DisplayName,
                    ProcessId = process.ProcessId,
                    ProcessName = AddExeSuffix(process.ProcessName),
                    ExecutablePath = process.ExecutablePath,
                    ProcessStartTimeUtc = process.ProcessStartTimeUtc,
                    IsExplorer = isExplorer
                });
            }
            else if (customNames.Contains(process.NormalizedProcessName))
            {
                candidates.Add(new SuspendCandidate
                {
                    RuleId = $"custom:{process.NormalizedProcessName}",
                    RuleName = "Manual selection",
                    ProcessId = process.ProcessId,
                    ProcessName = AddExeSuffix(process.ProcessName),
                    ExecutablePath = process.ExecutablePath,
                    ProcessStartTimeUtc = process.ProcessStartTimeUtc,
                    IsExplorer = isExplorer
                });
            }
        }

        return candidates.GroupBy(candidate => candidate.ProcessId).Select(group => group.First())
            .OrderBy(candidate => candidate.RuleName).ThenBy(candidate => candidate.ProcessName).ThenBy(candidate => candidate.ProcessId).ToList();
    }

    public IReadOnlyList<RunningProcessGroup> GetRunningProcessGroups(IEnumerable<string> protectedProcessNames)
    {
        var protectedNames = BuildProtectedNameSet(protectedProcessNames);
        var groups = new Dictionary<string, RunningProcessGroup>(StringComparer.OrdinalIgnoreCase);
        int currentPid = Environment.ProcessId;

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id <= 4 || process.Id == currentPid || process.HasExited)
                {
                    continue;
                }

                string normalized = NormalizeProcessName(process.ProcessName);
                string? path = TryGetProcessPath(process);
                if (string.IsNullOrWhiteSpace(normalized)
                    || IsProtectedProcessName(normalized, protectedNames, allowExplorer: true)
                    || IsSteamRelatedProcess(normalized, path))
                {
                    continue;
                }

                if (!groups.TryGetValue(normalized, out var group))
                {
                    group = new RunningProcessGroup
                    {
                        NormalizedProcessName = normalized,
                        ProcessName = AddExeSuffix(process.ProcessName),
                        InstanceCount = 0,
                        ExamplePath = path
                    };
                    groups[normalized] = group;
                }

                group.InstanceCount++;
                group.ExamplePath ??= path;
            }
            catch (Exception ex)
            {
                _logger.Debug($"Session process list skipped a process: {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }

        return groups.Values
            .OrderBy(x => x.ProcessName)
            .ToList();
    }

    public IReadOnlyList<SuspendCandidate> BuildCandidates(
        IEnumerable<BackgroundProcessRule> enabledRules,
        IEnumerable<string> customProcessNames,
        IEnumerable<string> protectedProcessNames)
    {
        var rules = enabledRules
            .Where(r => r.IsEnabled && r.ProcessNames.Count > 0)
            .ToList();

        var customNames = new HashSet<string>(
            customProcessNames.Select(NormalizeProcessName).Where(x => !string.IsNullOrWhiteSpace(x)),
            StringComparer.OrdinalIgnoreCase);

        if (rules.Count == 0 && customNames.Count == 0)
        {
            return Array.Empty<SuspendCandidate>();
        }

        var protectedNames = BuildProtectedNameSet(protectedProcessNames);
        var candidates = new List<SuspendCandidate>();
        int currentPid = Environment.ProcessId;

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id <= 4 || process.Id == currentPid || process.HasExited)
                {
                    continue;
                }

                string normalizedProcessName = NormalizeProcessName(process.ProcessName);
                if (string.IsNullOrWhiteSpace(normalizedProcessName))
                {
                    continue;
                }

                bool isExplorer = normalizedProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase);
                string? path = TryGetProcessPath(process);
                if (!TryGetProcessStartTimeUtc(process, out DateTime processStartTimeUtc))
                {
                    continue;
                }

                if (IsProtectedProcessName(normalizedProcessName, protectedNames, allowExplorer: isExplorer)
                    || IsSteamRelatedProcess(normalizedProcessName, path))
                {
                    continue;
                }

                SuspendCandidate? candidate = null;

                foreach (var rule in rules)
                {
                    if (!MatchesRule(rule, normalizedProcessName, path))
                    {
                        continue;
                    }

                    if (isExplorer && !rule.Id.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    candidate = new SuspendCandidate
                    {
                        RuleId = rule.Id,
                        RuleName = rule.DisplayName,
                        ProcessId = process.Id,
                        ProcessName = AddExeSuffix(process.ProcessName),
                        ExecutablePath = path,
                        ProcessStartTimeUtc = processStartTimeUtc,
                        IsExplorer = isExplorer
                    };
                    break;
                }

                if (candidate == null && customNames.Contains(normalizedProcessName))
                {
                    candidate = new SuspendCandidate
                    {
                        RuleId = $"custom:{normalizedProcessName}",
                        RuleName = "Manual selection",
                        ProcessId = process.Id,
                        ProcessName = AddExeSuffix(process.ProcessName),
                        ExecutablePath = path,
                        ProcessStartTimeUtc = processStartTimeUtc,
                        IsExplorer = isExplorer
                    };
                }

                if (candidate != null)
                {
                    candidates.Add(candidate);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Session candidate scan skipped a process: {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }

        return candidates
            .GroupBy(x => x.ProcessId)
            .Select(g => g.First())
            .OrderBy(x => x.RuleName)
            .ThenBy(x => x.ProcessName)
            .ThenBy(x => x.ProcessId)
            .ToList();
    }

    public SessionActionResult SuspendProcesses(IEnumerable<SuspendCandidate> candidates)
    {
        var result = new SessionActionResult();

        foreach (var candidate in candidates.GroupBy(x => x.ProcessId).Select(g => g.First()))
        {
            if (TrySuspend(candidate, out string message))
            {
                result.SuccessCount++;
                result.Records.Add(new SuspendedProcessRecord
                {
                    ProcessId = candidate.ProcessId,
                    ProcessName = candidate.ProcessName,
                    ProcessStartTimeUtc = candidate.ProcessStartTimeUtc,
                    RuleId = candidate.RuleId,
                    RuleName = candidate.RuleName,
                    ExecutablePath = candidate.ExecutablePath,
                    SuspendedAtUtc = DateTime.UtcNow
                });
            }
            else
            {
                result.FailedCount++;
                result.Messages.Add($"{candidate.ProcessName} PID {candidate.ProcessId}: {message}");
            }
        }

        return result;
    }

    public SessionActionResult ResumeProcesses(IEnumerable<SuspendedProcessRecord> records)
    {
        var result = new SessionActionResult();

        foreach (var record in records.GroupBy(x => x.ProcessId).Select(g => g.First()))
        {
            IdentityValidationResult validation = ValidateProcessIdentity(record, ReadProcessIdentity(record.ProcessId));
            if (validation == IdentityValidationResult.ProcessNotFound)
            {
                result.ResolvedCount++;
                result.Records.Add(record);
                result.Messages.Add($"{record.ProcessName} PID {record.ProcessId}: process no longer exists; recovery record resolved.");
                continue;
            }

            if (validation == IdentityValidationResult.DifferentProcess)
            {
                result.ResolvedCount++;
                result.StaleProcessCount++;
                result.Records.Add(record);
                result.Messages.Add($"{record.ProcessName} PID {record.ProcessId}: PID belongs to a different process instance; no resume attempted.");
                continue;
            }

            if (validation == IdentityValidationResult.CannotVerify)
            {
                result.FailedCount++;
                result.Messages.Add($"{record.ProcessName} PID {record.ProcessId}: process identity cannot be verified safely.");
                continue;
            }

            ResumeAttemptResult attempt = TryResume(record, out string message);
            if (attempt == ResumeAttemptResult.Resumed)
            {
                result.SuccessCount++;
                result.Records.Add(record);
            }
            else if (attempt == ResumeAttemptResult.ProcessNotFound)
            {
                result.ResolvedCount++;
                result.Records.Add(record);
                result.Messages.Add($"{record.ProcessName} PID {record.ProcessId}: process exited before resume; recovery record resolved.");
            }
            else if (attempt == ResumeAttemptResult.DifferentProcess)
            {
                result.ResolvedCount++;
                result.StaleProcessCount++;
                result.Records.Add(record);
                result.Messages.Add($"{record.ProcessName} PID {record.ProcessId}: PID changed before resume; no resume attempted.");
            }
            else
            {
                result.FailedCount++;
                result.Messages.Add($"{record.ProcessName} PID {record.ProcessId}: {message}");
            }
        }

        return result;
    }

    private static HashSet<string> BuildProtectedNameSet(IEnumerable<string> protectedProcessNames)
    {
        var protectedNames = new HashSet<string>(AntiCheatAndProtectedNames, StringComparer.OrdinalIgnoreCase);
        foreach (string name in protectedProcessNames.Select(NormalizeProcessName).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            protectedNames.Add(name);
        }

        protectedNames.Add(NormalizeProcessName(Process.GetCurrentProcess().ProcessName));
        return protectedNames;
    }

    private static bool IsProtectedProcessName(string normalizedProcessName, HashSet<string> protectedNames, bool allowExplorer)
    {
        if (!allowExplorer && normalizedProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!normalizedProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase) && CriticalProcessNames.Contains(normalizedProcessName))
        {
            return true;
        }

        return protectedNames.Contains(normalizedProcessName);
    }

    private static bool IsSteamRelatedProcess(string normalizedProcessName, string? path)
    {
        if (string.IsNullOrWhiteSpace(normalizedProcessName))
        {
            return false;
        }

        if (normalizedProcessName.Contains("steam", StringComparison.OrdinalIgnoreCase)
            || normalizedProcessName.Equals("gameoverlayui", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(path))
        {
            string normalizedPath = path.Replace('/', '\\');
            if (normalizedPath.Contains("\\Steam\\", StringComparison.OrdinalIgnoreCase)
                || normalizedPath.Contains("\\steamapps\\", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesRule(BackgroundProcessRule rule, string normalizedProcessName, string? path)
    {
        bool nameMatches = rule.ProcessNames
            .Select(NormalizeProcessName)
            .Any(x => x.Equals(normalizedProcessName, StringComparison.OrdinalIgnoreCase));

        if (!nameMatches)
        {
            return false;
        }

        if (rule.PathContains.Count == 0 || string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        return rule.PathContains.Any(hint => path.Contains(hint, StringComparison.OrdinalIgnoreCase));
    }

    private bool TrySuspend(SuspendCandidate candidate, out string message)
    {
        message = string.Empty;
        IntPtr handle = OpenProcess(PROCESS_SUSPEND_RESUME | PROCESS_QUERY_LIMITED_INFORMATION, false, candidate.ProcessId);
        if (handle == IntPtr.Zero)
        {
            message = $"OpenProcess failed: {Marshal.GetLastWin32Error()}";
            return false;
        }

        try
        {
            if (!TryGetProcessStartTimeUtc(handle, out DateTime actualStartTimeUtc)
                || actualStartTimeUtc != candidate.ProcessStartTimeUtc)
            {
                message = "Process identity changed before suspend.";
                return false;
            }

            int status = NtSuspendProcess(handle);
            if (status == 0)
            {
                return true;
            }

            message = $"NtSuspendProcess returned 0x{status:X8}";
            return false;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private ResumeAttemptResult TryResume(SuspendedProcessRecord record, out string message)
    {
        message = string.Empty;
        IntPtr handle = OpenProcess(PROCESS_SUSPEND_RESUME | PROCESS_QUERY_LIMITED_INFORMATION, false, record.ProcessId);
        if (handle == IntPtr.Zero)
        {
            message = $"OpenProcess failed: {Marshal.GetLastWin32Error()}";
            return ReadProcessIdentity(record.ProcessId) == null
                ? ResumeAttemptResult.ProcessNotFound
                : ResumeAttemptResult.Failed;
        }

        try
        {
            if (!TryGetProcessStartTimeUtc(handle, out DateTime actualStartTimeUtc)
                || actualStartTimeUtc != record.ProcessStartTimeUtc)
            {
                message = "Process identity changed before resume.";
                return ResumeAttemptResult.DifferentProcess;
            }

            int status = NtResumeProcess(handle);
            if (status == 0)
            {
                return ResumeAttemptResult.Resumed;
            }

            message = $"NtResumeProcess returned 0x{status:X8}";
            return ResumeAttemptResult.Failed;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return ResumeAttemptResult.Failed;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static IdentityValidationResult ValidateProcessIdentity(SuspendedProcessRecord record, ProcessIdentity? current)
    {
        if (current == null)
        {
            return IdentityValidationResult.ProcessNotFound;
        }

        if (record.ProcessStartTimeUtc == default || current.ProcessStartTimeUtc == default)
        {
            return IdentityValidationResult.CannotVerify;
        }

        if (record.ProcessStartTimeUtc != current.ProcessStartTimeUtc
            || !NormalizeProcessName(record.ProcessName).Equals(NormalizeProcessName(current.ProcessName), StringComparison.OrdinalIgnoreCase))
        {
            return IdentityValidationResult.DifferentProcess;
        }

        if (!string.IsNullOrWhiteSpace(record.ExecutablePath)
            && !string.IsNullOrWhiteSpace(current.ExecutablePath)
            && !NormalizePath(record.ExecutablePath).Equals(NormalizePath(current.ExecutablePath), StringComparison.OrdinalIgnoreCase))
        {
            return IdentityValidationResult.DifferentProcess;
        }

        return IdentityValidationResult.Match;
    }

    private static SessionProcessSnapshot CaptureProcessSnapshot(CancellationToken cancellationToken)
    {
        var items = new List<SessionProcessSnapshotItem>();
        int currentProcessId = Environment.ProcessId;

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (process.Id <= 4 || process.Id == currentProcessId || process.HasExited) continue;

                string processName = process.ProcessName;
                string normalizedProcessName = NormalizeProcessName(processName);
                if (string.IsNullOrWhiteSpace(normalizedProcessName)) continue;

                items.Add(new SessionProcessSnapshotItem
                {
                    ProcessId = process.Id,
                    ProcessName = processName,
                    NormalizedProcessName = normalizedProcessName,
                    ExecutablePath = TryGetProcessPath(process),
                    ProcessStartTimeUtc = TryGetProcessStartTimeUtc(process, out DateTime startTimeUtc) ? startTimeUtc : default
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Individual process metadata can be unavailable; retain the rest of the snapshot.
            }
            finally
            {
                process.Dispose();
            }
        }

        return new SessionProcessSnapshot { Processes = items };
    }

    private static ProcessIdentity? ReadProcessIdentity(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return null;
            }

            return new ProcessIdentity
            {
                ProcessName = process.ProcessName,
                ProcessStartTimeUtc = TryGetProcessStartTimeUtc(process, out DateTime startTimeUtc) ? startTimeUtc : default,
                ExecutablePath = TryGetProcessPath(process)
            };
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch
        {
            // An existing process whose identity cannot be read must remain pending rather than be resumed by PID alone.
            return new ProcessIdentity();
        }
    }

    private static bool TryGetProcessStartTimeUtc(Process process, out DateTime startTimeUtc)
    {
        try
        {
            startTimeUtc = process.StartTime.ToUniversalTime();
            return true;
        }
        catch
        {
            startTimeUtc = default;
            return false;
        }
    }

    private static bool TryGetProcessStartTimeUtc(IntPtr processHandle, out DateTime startTimeUtc)
    {
        startTimeUtc = default;
        if (!GetProcessTimes(processHandle, out var creationTime, out _, out _, out _))
        {
            return false;
        }

        try
        {
            long fileTime = ((long)(uint)creationTime.dwHighDateTime << 32) | (uint)creationTime.dwLowDateTime;
            startTimeUtc = DateTime.FromFileTimeUtc(fileTime);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch
        {
            return path.Trim();
        }
    }

    private static string? TryGetProcessPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    public static string NormalizeProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return string.Empty;
        }

        string name = processName.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = Path.GetFileNameWithoutExtension(name);
        }

        return name;
    }

    private static string AddExeSuffix(string processName)
    {
        return processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName
            : processName + ".exe";
    }
}
