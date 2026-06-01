using FrameHub.Core.Logging;
using FrameHub.Core.Models.SessionOptimization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

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

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(IntPtr processHandle);

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
            if (TrySuspend(candidate.ProcessId, out string message))
            {
                result.SuccessCount++;
                result.Records.Add(new SuspendedProcessRecord
                {
                    ProcessId = candidate.ProcessId,
                    ProcessName = candidate.ProcessName,
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
            if (TryResume(record.ProcessId, out string message))
            {
                result.SuccessCount++;
                result.Records.Add(record);
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

    private bool TrySuspend(int pid, out string message)
    {
        message = string.Empty;
        IntPtr handle = OpenProcess(PROCESS_SUSPEND_RESUME | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero)
        {
            message = $"OpenProcess failed: {Marshal.GetLastWin32Error()}";
            return false;
        }

        try
        {
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

    private bool TryResume(int pid, out string message)
    {
        message = string.Empty;
        IntPtr handle = OpenProcess(PROCESS_SUSPEND_RESUME | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero)
        {
            message = $"OpenProcess failed: {Marshal.GetLastWin32Error()}";
            return false;
        }

        try
        {
            int status = NtResumeProcess(handle);
            if (status == 0)
            {
                return true;
            }

            message = $"NtResumeProcess returned 0x{status:X8}";
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
