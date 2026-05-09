using FrameHub.App.ViewModels;
using FrameHub.Core.Logging;
using FrameHub.Core.Models;
using FrameHub.Core.Services;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace FrameHub.App.Services;

/// <summary>
/// Application-level runtime coordinator for profiles, process scanning and hardware topology.
/// Heavy UI scans and hardware telemetry are opt-in, while profile monitoring stays active in the background.
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
    public string CpuName { get; }
    public string CpuVendor { get; }
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
        CpuName = HardwareTopologyService.GetCpuName();
        CpuVendor = HardwareTopologyService.GetCpuVendor();
        CpuSetMap = HardwareTopologyService.GetLogicalCoreToCpuSetIdMap();
        Cores = HardwareTopologyService.GetCoreTopology();

        ProcessScanner = new ProcessScannerService(ProcessService);
        Optimization = new OptimizationService(ProcessService, () => CpuSetMap);

        _profileWatcherTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Clamp(Settings.ProfileWatcherSeconds, 1, 30))
        };
        _profileWatcherTimer.Tick += async (_, _) => await RunProfileWatcherOnceAsync();

        AddActivity("Działanie FrameHub uruchomione.");
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
        AddActivity($"Zapisano profile: {Profiles.Count}.");
    }

    public void UpsertProfile(ProcessProfile profile, bool replaceSameProcessName = true)
    {
        var profiles = Profiles
            .Where(p => !p.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (replaceSameProcessName)
        {
            profiles = profiles
                .Where(p => !p.ProcessName.Equals(profile.ProcessName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        profiles.Add(profile);
        SaveProfiles(profiles);
    }

    public void DeleteProfile(ProcessProfile profile)
    {
        SaveProfiles(Profiles.Where(p => !p.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase)).ToList());
        Optimization.ClearProfileCacheForProcess(profile.ProcessName);
        AddActivity($"Usunięto profil '{profile.ProcessName}'.");
    }

    public OptimizationBatchResult ApplyProfileNow(ProcessProfile profile, bool force = true)
    {
        var result = Optimization.ApplyProfileToRunningProcesses(profile, Settings.AllowRealtimePriority, force);
        LogBatchResult(profile.ProcessName, result, "zastosowanie ręczne");
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
                    AddActivity($"Zastosowano profil '{result.ProcessName}' przez monitor w tle: PID={result.ProcessId}, tryb={result.Mode}, priorytet={result.Priority}.");
                }
                else
                {
                    LogApplyFailure(result);
                }
            }
        }
        catch (Exception ex)
        {
            AddActivity($"Monitor profili w tle zgłosił błąd: {ex.Message}", "Warn");
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
            AddActivity($"Zastosowano profil '{processName}' ({source}): {batch.Successful}/{batch.Total} instancji.");
        }
        else if (batch.Total > 0)
        {
            AddActivity($"Profil '{processName}' nie został zastosowany do żadnej uruchomionej instancji ({source}).", "Warn");
        }
        else
        {
            AddActivity($"Nie znaleziono uruchomionego procesu dla profilu '{processName}'.", "Warn");
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
        string adminHint = result.RequiresAdmin ? " Może to wymagać uprawnień administratora albo proces jest chroniony." : string.Empty;
        AddActivity($"Nie udało się zastosować profilu '{result.ProcessName}': PID={result.ProcessId}, powód={result.Message}.{adminHint}", "Warn");
    }

    private string GetWatcherStartupText()
    {
        var enabled = Profiles.Where(p => p.IsEnabled).Select(p => p.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return enabled.Count == 0
            ? "Monitor profili w tle aktywny. Brak włączonych profili."
            : $"Monitor profili w tle aktywny dla {enabled.Count} włączonych profili: {string.Join(", ", enabled)}.";
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
