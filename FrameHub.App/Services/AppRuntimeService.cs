using FrameHub.App.ViewModels;
using FrameHub.Companion;
using FrameHub.Companion.Providers;
using FrameHub.Core.Logging;
using FrameHub.Core.Models;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Benchmarking;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace FrameHub.App.Services;

/// <summary>
/// Application-level runtime coordinator for profiles, process scanning and hardware topology.
/// Heavy UI scans and hardware telemetry are opt-in, while profile monitoring stays active in the background.
/// </summary>
public sealed class AppRuntimeService : IDisposable, IBenchmarkRuntimeContext
{
    private sealed class AppPresentationPreferencesProvider : ICompanionPresentationPreferencesProvider
    {
        private readonly AppRuntimeService _owner;
        public AppPresentationPreferencesProvider(AppRuntimeService owner) => _owner = owner;
        public string DesktopLanguage => _owner.Settings?.Language ?? "en";
    }
    private readonly DispatcherTimer _profileWatcherTimer;
    private readonly Dictionary<string, DateTime> _failureLogThrottle = new(StringComparer.OrdinalIgnoreCase);
    private bool _watcherBusy;
    private bool _disposed;

    public SettingsService SettingsService { get; }
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
    public event EventHandler<ProfileWatcherSnapshotEventArgs>? ProfileWatcherSnapshot;

    public bool IsProfileWatcherActive => _profileWatcherTimer.IsEnabled;
    public string LastAppliedProfile { get; private set; } = string.Empty;
    public int OptimizedProcessCount { get; private set; }

    public CompanionServer CompanionServer { get; } = new();
    public BenchmarkCaptureCoordinator BenchmarkCoordinator { get; }
    public ActiveGameMonitor ActiveGameMonitor { get; }
    public LivePerformanceTelemetryService LiveTelemetryService { get; }
    public AppTelemetrySnapshotProvider TelemetryProvider { get; }
    IBenchmarkCaptureCoordinator IBenchmarkRuntimeContext.BenchmarkCoordinator => BenchmarkCoordinator;

    public AppRuntimeService(string? customSettingsFilePath = null)
        : this(new SettingsService(customSettingsFilePath))
    {
    }

    public AppRuntimeService(SettingsService settingsService)
    {
        SettingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
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

        BenchmarkCoordinator = new BenchmarkCaptureCoordinator();
        ActiveGameMonitor = new ActiveGameMonitor();
        LiveTelemetryService = new LivePerformanceTelemetryService(ActiveGameMonitor, BenchmarkCoordinator);
        TelemetryProvider = new AppTelemetrySnapshotProvider(this, ActiveGameMonitor, LiveTelemetryService);
        CompanionServer.ConfigureTelemetryProvider(TelemetryProvider, AcquireHardwareLease);
        BenchmarkProvider = new AppBenchmarkProvider(this);
        CompanionServer.ConfigureBenchmarkProvider(BenchmarkProvider);
        CompanionServer.ConfigurePresentationPreferencesProvider(new AppPresentationPreferencesProvider(this));
        LaunchService = new AppLibraryLaunchService();
        LibraryProvider = new AppLibraryProvider(this, LaunchService);
        CompanionServer.ConfigureLibraryProvider(LibraryProvider);
        SessionOptimizationCoordinator = new SessionOptimizationCoordinator(ProcessScanner);
        SessionOptimizationProvider = new AppSessionOptimizationProvider(this, SessionOptimizationCoordinator, ActiveGameMonitor, BenchmarkCoordinator);
        CompanionServer.ConfigureSessionOptimizationProvider(SessionOptimizationProvider);

        AddActivity("Działanie FrameHub uruchomione.");
        AddActivity(GetWatcherStartupText());
        StartProfileWatcher();
        _ = SyncCompanionServerStateAsync();
    }

    public AppBenchmarkProvider BenchmarkProvider { get; }
    public IAppLibraryLaunchService LaunchService { get; }
    public AppLibraryProvider LibraryProvider { get; }
    public SessionOptimizationCoordinator SessionOptimizationCoordinator { get; }
    public AppSessionOptimizationProvider SessionOptimizationProvider { get; }




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
        _ = SyncCompanionServerStateAsync();
    }

    private readonly SemaphoreSlim _companionSyncGate = new(1, 1);

    public async Task SyncCompanionServerStateAsync()
    {
        await _companionSyncGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var options = new CompanionOptions
            {
                Enabled = Settings.CompanionEnabled,
                LanEnabled = Settings.CompanionLanEnabled,
                LanAddress = Settings.CompanionLanAddress,
                Port = Settings.CompanionPort > 0 ? Settings.CompanionPort : 47821
            };

            if (options.Enabled)
            {
                bool started = await CompanionServer.StartAsync(options).ConfigureAwait(false);
                if (started)
                {
                    ActiveGameMonitor.Start();
                    LiveTelemetryService.Start();
                    TelemetryProvider.Start();
                }
                else
                {
                    await TelemetryProvider.StopAsync().ConfigureAwait(false);
                    await LiveTelemetryService.StopAsync().ConfigureAwait(false);
                    await ActiveGameMonitor.StopAsync().ConfigureAwait(false);
                }
            }
            else
            {
                await CompanionServer.StopAsync().ConfigureAwait(false);
                await TelemetryProvider.StopAsync().ConfigureAwait(false);
                await LiveTelemetryService.StopAsync().ConfigureAwait(false);
                await ActiveGameMonitor.StopAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            LoggerService.Instance.Warn($"Failed to synchronize Companion server state: {ex.Message}");
        }
        finally
        {
            _companionSyncGate.Release();
        }
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
                .Where(p => !ProfileService.MatchesIdentity(p, profile.ProcessName, profile.ExecutablePath))
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
            ProfileWatcherSnapshot?.Invoke(this, new ProfileWatcherSnapshotEventArgs(scan.Groups.Select(g => g.Name)));
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

    private readonly HardwareMonitorService _hardwareMonitor = new();
    private int _hardwareConsumerCount;
    private readonly object _hardwareLock = new();

    public bool IsHardwareMonitoringActive
    {
        get
        {
            lock (_hardwareLock)
            {
                return _hardwareConsumerCount > 0 && _hardwareMonitor.IsInitialized;
            }
        }
    }

    public IHardwareMonitorLease AcquireHardwareLease()
    {
        lock (_hardwareLock)
        {
            _hardwareConsumerCount++;
            if (_hardwareConsumerCount == 1)
            {
                _hardwareMonitor.Configure(Settings.EnableStorageSensors);
                _hardwareMonitor.Start();
            }
            return new HardwareMonitorLease(this);
        }
    }

    internal void ReleaseHardwareLease()
    {
        lock (_hardwareLock)
        {
            if (_hardwareConsumerCount > 0)
            {
                _hardwareConsumerCount--;
                if (_hardwareConsumerCount == 0)
                {
                    _hardwareMonitor.Stop(closeSensors: false);
                }
            }
        }
    }

    public HardwareMetrics GetHardwareMetrics()
    {
        lock (_hardwareLock)
        {
            if (_hardwareConsumerCount <= 0)
            {
                return new HardwareMetrics();
            }
            return _hardwareMonitor.GetAllMetrics();
        }
    }

    public void ConfigureHardwareStorageSensors(bool enableStorageSensors)
    {
        lock (_hardwareLock)
        {
            _hardwareMonitor.Configure(enableStorageSensors);
        }
    }

    private sealed class HardwareMonitorLease : IHardwareMonitorLease
    {
        private readonly AppRuntimeService _owner;
        private int _disposed;

        public HardwareMonitorLease(AppRuntimeService owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.ReleaseHardwareLease();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        BenchmarkCoordinator.Dispose();
        LiveTelemetryService.Dispose();
        ActiveGameMonitor.Dispose();
        TelemetryProvider.Dispose();
        CompanionServer.Dispose();
        SessionOptimizationCoordinator.Dispose();
        _companionSyncGate.Dispose();
        _profileWatcherTimer.Stop();
        lock (_hardwareLock)
        {
            _hardwareMonitor.Dispose();
        }
        HardwareTopologyService.ReleaseCpuLoadCounters();
        HardwareTopologyService.Dispose();
        SettingsService.Dispose();
    }
}
