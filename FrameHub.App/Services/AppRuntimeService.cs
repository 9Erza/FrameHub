using FrameHub.App.ViewModels;
using FrameHub.Core.Logging;
using FrameHub.Core.Models;
using FrameHub.Core.Services;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace FrameHub.App.Services;

/// <summary>
/// Application-level runtime coordinator. It keeps FrameHub independent from PCO while reusing the migrated core logic.
/// Heavy UI scans and hardware telemetry are opt-in, but the profile watcher stays active in the background.
/// </summary>
public sealed class AppRuntimeService : IDisposable
{
    private readonly DispatcherTimer _profileWatcherTimer;
    private readonly Dictionary<string, DateTime> _failureLogThrottle = new(StringComparer.OrdinalIgnoreCase);
    private bool _watcherBusy;
    private bool _disposed;

    public SettingsService SettingsService { get; } = new();
    public AppSettings Settings { get; private set; }
    public ProfileService ProfileService { get; } = new();
    public ProcessService ProcessService { get; } = new();
    public HardwareService HardwareTopologyService { get; } = new();
    public ProcessScannerService ProcessScanner { get; }
    public OptimizationService Optimization { get; }
    public Dictionary<int, uint> CpuSetMap { get; }
    public IReadOnlyList<CoreInfo> Cores { get; }
    public List<ProcessProfile> Profiles { get; private set; }
    public ObservableCollection<ActivityItemViewModel> Activity { get; } = new();

    public event EventHandler? ProfilesChanged;
    public event EventHandler? WatcherStateChanged;
    public event EventHandler? RuntimeStateChanged;

    public bool IsProfileWatcherActive => _profileWatcherTimer.IsEnabled;
    public string LastAppliedProfile { get; private set; } = string.Empty;
    public int OptimizedProcessCount { get; private set; }

    public AppRuntimeService()
    {
        Settings = SettingsService.LoadSettings();
        ConfigureLoggerFromSettings();

        Profiles = ProfileService.LoadProfiles();
        CpuSetMap = HardwareTopologyService.GetLogicalCoreToCpuSetIdMap();
        Cores = HardwareTopologyService.GetCoreTopology();

        ProcessScanner = new ProcessScannerService(ProcessService);
        Optimization = new OptimizationService(ProcessService, () => CpuSetMap);

        _profileWatcherTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Clamp(Settings.ProfileWatcherSeconds, 1, 30))
        };
        _profileWatcherTimer.Tick += async (_, _) => await RunProfileWatcherOnceAsync();

        AddActivity("FrameHub runtime initialized.");
        AddActivity(GetWatcherStartupText());
        StartProfileWatcher();
    }


    public void SaveSettings(AppSettings settings)
    {
        Settings = settings;
        SettingsService.SaveSettings(Settings);
        ApplyRuntimeSettings();
        RuntimeStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ReloadSettings()
    {
        Settings = SettingsService.LoadSettings();
        ApplyRuntimeSettings();
        RuntimeStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyRuntimeSettings()
    {
        ConfigureLoggerFromSettings();
        _profileWatcherTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(Settings.ProfileWatcherSeconds, 1, 30));
    }

    public void StartProfileWatcher()
    {
        if (!_profileWatcherTimer.IsEnabled)
        {
            _profileWatcherTimer.Start();
            WatcherStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void StopProfileWatcher()
    {
        if (_profileWatcherTimer.IsEnabled)
        {
            _profileWatcherTimer.Stop();
            WatcherStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task<ProcessScanResult> ScanFullProcessListAsync()
    {
        return await ProcessScanner.ScanUserProcessesAsync();
    }

    public void SaveProfiles(IEnumerable<ProcessProfile> profiles)
    {
        Profiles = profiles.ToList();
        ProfileService.SaveProfiles(Profiles);
        Profiles = ProfileService.LoadProfiles();
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
        AddActivity($"Profiles saved: {Profiles.Count}.");
    }

    public void UpsertProfile(ProcessProfile profile)
    {
        var profiles = Profiles
            .Where(p => !p.ProcessName.Equals(profile.ProcessName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        profiles.Add(profile);
        SaveProfiles(profiles);
    }

    public void DeleteProfile(ProcessProfile profile)
    {
        SaveProfiles(Profiles.Where(p => !p.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase)).ToList());
        Optimization.ClearProfileCacheForProcess(profile.ProcessName);
        AddActivity($"Deleted profile '{profile.ProcessName}'.");
    }

    public OptimizationBatchResult ApplyProfileNow(ProcessProfile profile, bool force = true)
    {
        var result = Optimization.ApplyProfileToRunningProcesses(profile, Settings.AllowRealtimePriority, force);
        LogBatchResult(profile.ProcessName, result, "manual apply");
        return result;
    }

    public void AddActivity(string message, string level = "Info")
    {
        Activity.Insert(0, new ActivityItemViewModel
        {
            Time = DateTime.Now.ToString("HH:mm"),
            Message = message,
            Level = level
        });

        while (Activity.Count > 200)
        {
            Activity.RemoveAt(Activity.Count - 1);
        }

        switch (level)
        {
            case "Warn": LoggerService.Instance.Warn(message); break;
            case "Error": LoggerService.Instance.Error(message); break;
            default: LoggerService.Instance.Info(message); break;
        }
    }

    private async Task RunProfileWatcherOnceAsync()
    {
        if (_watcherBusy || _disposed) return;
        _watcherBusy = true;

        try
        {
            var enabledProfiles = Profiles.Where(p => p.IsEnabled).ToList();
            if (enabledProfiles.Count == 0)
            {
                return;
            }

            var scan = await ProcessScanner.ScanProfileProcessesAsync(enabledProfiles);
            var batch = Optimization.ApplyProfilesForSnapshots(enabledProfiles, scan.Groups, Settings.AllowRealtimePriority, force: false);
            Optimization.CleanupStaleCache(scan.ActiveInstances);

            if (batch.Results.Count == 0)
            {
                OptimizedProcessCount = 0;
                return;
            }

            foreach (var result in batch.Results)
            {
                if (result.Message == "SKIPPED_ALREADY_APPLIED") continue;

                if (result.Success)
                {
                    LastAppliedProfile = result.ProcessName;
                    OptimizedProcessCount = batch.Successful;
                    RuntimeStateChanged?.Invoke(this, EventArgs.Empty);
                    AddActivity($"Applied profile '{result.ProcessName}' via background watcher: PID={result.ProcessId}, mode={result.Mode}, priority={result.Priority}.");
                }
                else
                {
                    LogApplyFailure(result);
                }
            }
        }
        catch (Exception ex)
        {
            AddActivity($"Background watcher failed: {ex.Message}", "Warn");
        }
        finally
        {
            _watcherBusy = false;
        }
    }

    private void LogBatchResult(string processName, OptimizationBatchResult batch, string source)
    {
        if (batch.Successful > 0)
        {
            LastAppliedProfile = processName;
            OptimizedProcessCount = batch.Successful;
            RuntimeStateChanged?.Invoke(this, EventArgs.Empty);
            AddActivity($"Applied profile '{processName}' ({source}): {batch.Successful}/{batch.Total} instance(s).");
        }
        else if (batch.Total > 0)
        {
            AddActivity($"Profile '{processName}' did not apply to any running instance ({source}).", "Warn");
        }
        else
        {
            AddActivity($"No running process found for profile '{processName}'.", "Warn");
        }
    }

    private void LogApplyFailure(OptimizationResult result)
    {
        string key = $"{result.ProcessName}|{result.Message}";
        if (_failureLogThrottle.TryGetValue(key, out var last) && DateTime.UtcNow - last < TimeSpan.FromSeconds(60))
        {
            return;
        }

        _failureLogThrottle[key] = DateTime.UtcNow;
        string adminHint = result.RequiresAdmin ? " This may require administrator rights or the process is protected." : string.Empty;
        AddActivity($"Failed to apply profile '{result.ProcessName}': PID={result.ProcessId}, reason={result.Message}.{adminHint}", "Warn");
    }

    private string GetWatcherStartupText()
    {
        var enabled = Profiles.Where(p => p.IsEnabled).Select(p => p.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return enabled.Count == 0
            ? "Background profile watcher active. No enabled profiles configured."
            : $"Background profile watcher active for {enabled.Count} enabled profile(s): {string.Join(", ", enabled)}.";
    }

    private void ConfigureLoggerFromSettings()
    {
        LoggerService.Shared.Configure(
            Settings.LogEnabled,
            LogLevel.FromValue(Settings.LogLevelValue),
            Settings.LogFilePath,
            Settings.EnableConsoleOutput,
            Settings.LogSourceName);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _profileWatcherTimer.Stop();
        HardwareTopologyService.ReleaseCpuLoadCounters();
        HardwareTopologyService.Dispose();
        SettingsService.Dispose();
    }
}
