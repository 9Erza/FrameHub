using FrameHub.App.ViewModels;
using FrameHub.Companion;
using FrameHub.Companion.Providers;
using FrameHub.Core.Logging;
using FrameHub.Core.Models;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Benchmarking;
using FrameHub.Core.Services.Library;
using FrameHub.Core.Services.SessionOptimization;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
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
    private readonly Dispatcher _uiDispatcher;
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
    IProcessObservationSnapshotProvider IBenchmarkRuntimeContext.ProcessObservationProvider => ProcessScanner.ObservationProvider;

    public AppRuntimeService(string? customSettingsFilePath = null, IHardwareMonitorBackend? hardwareMonitor = null)
        : this(new SettingsService(customSettingsFilePath), hardwareMonitor)
    {
    }

    public AppRuntimeService(SettingsService settingsService, IHardwareMonitorBackend? hardwareMonitor = null)
    {
        SettingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _uiDispatcher = Dispatcher.CurrentDispatcher;
        _hardwareMonitor = hardwareMonitor ?? new HardwareMonitorService();
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

        BenchmarkCoordinator = new BenchmarkCaptureCoordinator(environmentProvider: new BenchmarkEnvironmentProvider());
        var gameDetector = new BenchmarkGameDetectionService(
            new SystemBenchmarkProcessSnapshotProvider(ProcessScanner.ObservationProvider));
        ActiveGameMonitor = new ActiveGameMonitor(gameDetector);
        LiveTelemetryService = new LivePerformanceTelemetryService(ActiveGameMonitor, BenchmarkCoordinator);
        BenchmarkCoordinator.ConfigureLivePresentMonPreemption(LiveTelemetryService);
        TelemetryProvider = new AppTelemetrySnapshotProvider(this, ActiveGameMonitor, LiveTelemetryService);
        CompanionServer.ConfigureTelemetryProvider(TelemetryProvider, AcquireHardwareLease);
        BenchmarkProvider = new AppBenchmarkProvider(this);
        CompanionServer.ConfigureBenchmarkProvider(BenchmarkProvider);
        CompanionServer.ConfigurePresentationPreferencesProvider(new AppPresentationPreferencesProvider(this));
        LaunchService = new AppLibraryLaunchService();
        LaunchReservations = new LibraryLaunchReservationService();
        LibraryProvider = new AppLibraryProvider(this, LaunchService, LaunchReservations);
        BackgroundAppProvider = new AppBackgroundAppProvider(
            ProcessScanner,
            BenchmarkCoordinator,
            LaunchService,
            launchReservations: LaunchReservations);
        CompanionServer.ConfigureLibraryProvider(LibraryProvider);
        CompanionServer.ConfigureBackgroundAppsProvider(BackgroundAppProvider);
        SessionCpuControlBackend sessionCpuBackend = new(() => Cores, () => CpuSetMap);
        SessionOptimizationCoordinator = new SessionOptimizationCoordinator(ProcessScanner, benchmarkArbiter: BenchmarkCoordinator, activeGameMonitor: ActiveGameMonitor, sessionCpuBackend: sessionCpuBackend);
        SessionOptimizationProvider = new AppSessionOptimizationProvider(this, SessionOptimizationCoordinator, ActiveGameMonitor, BenchmarkCoordinator, gameProfileResolver: FindActiveGameCpuProfile);
        CompanionServer.ConfigureSessionOptimizationProvider(SessionOptimizationProvider);
        HardwareMonitoringProvider = new AppCompanionHardwareMonitoringProvider(this);
        CompanionServer.ConfigureHardwareMonitoringProvider(HardwareMonitoringProvider);
        GamingQuickActions = new GamingQuickActionService(
            ProcessScanner,
            BenchmarkCoordinator,
            LaunchService,
            LaunchReservations,
            SessionOptimizationCoordinator);

        AddLocalizedActivity("Activity.Startup");
        AddWatcherStartupActivity();
        StartProfileWatcher();
        _ = SyncCompanionServerStateAsync();
    }

    public AppBenchmarkProvider BenchmarkProvider { get; }
    public IAppLibraryLaunchService LaunchService { get; }
    public LibraryLaunchReservationService LaunchReservations { get; }
    public AppLibraryProvider LibraryProvider { get; }
    public AppBackgroundAppProvider BackgroundAppProvider { get; }
    public SessionOptimizationCoordinator SessionOptimizationCoordinator { get; }
    public AppSessionOptimizationProvider SessionOptimizationProvider { get; }
    public AppCompanionHardwareMonitoringProvider HardwareMonitoringProvider { get; }
    public IGamingQuickActionService GamingQuickActions { get; }

    public void SaveSettings(AppSettings settings)
    {
        Settings = settings;
        SettingsService.SaveSettings(Settings);
        ApplyRuntimeSettings();
        RuntimeStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Presentation-only source resolution for session CPU control: finds the enabled
    /// core-optimization profile associated with a library game (by LibraryItemId or
    /// process identity, mirroring the benchmark profile match). Never mutates anything.
    /// </summary>
    private ProcessProfile? FindActiveGameCpuProfile(string libraryItemId)
    {
        try
        {
            LibraryItem? item = new LibraryService().LoadItems()
                .FirstOrDefault(candidate => candidate.Type == LibraryItemType.Game
                    && candidate.Id.Equals(libraryItemId, StringComparison.OrdinalIgnoreCase));
            if (item is null)
            {
                return null;
            }

            return Profiles.FirstOrDefault(profile => profile.IsEnabled
                && profile.ApplyCoreOptimization
                && (profile.LibraryItemId?.Equals(item.Id, StringComparison.OrdinalIgnoreCase) == true
                    || ProfileService.MatchesIdentity(profile, item.ProcessName, item.ExecutablePath)));
        }
        catch
        {
            return null;
        }
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
        ReconcileHardwareMonitoring();
        _ = SyncCompanionServerStateAsync();
    }

    private readonly SemaphoreSlim _companionSyncGate = new(1, 1);

    /// <summary>
    /// Bounded Companion start attempts. The window covers the predecessor's
    /// graceful-shutdown budget (<see cref="DefaultShutdownBudget"/>): on a
    /// cooperative restart the successor starts while the old instance still
    /// holds the port, so the first bind fails and only a retry lets the
    /// successor's Companion come up without manual toggling (QA R1 P2).
    /// </summary>
    public const int CompanionStartMaxAttempts = 6;

    /// <summary>Delay between Companion start attempts (6 attempts ≈ 10 s window).</summary>
    public static readonly TimeSpan CompanionStartRetryDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Bounded retry driver for Companion startup. Pure orchestration over an
    /// injected starter — unit-tested with fakes, no sockets required. Returns
    /// the outcome and the number of attempts used. Exceptions from the starter
    /// propagate (real <c>CompanionServer.StartAsync</c> already reports failures
    /// as <c>false</c> with status details).
    /// </summary>
    public static async Task<(bool Started, int Attempts)> StartCompanionWithRetryAsync(
        Func<Task<bool>> starter,
        int maxAttempts,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default)
    {
        if (starter == null) throw new ArgumentNullException(nameof(starter));
        if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        if (retryDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retryDelay));

        int attempts = 0;
        while (true)
        {
            attempts++;
            if (await starter().ConfigureAwait(false))
            {
                return (true, attempts);
            }

            if (attempts >= maxAttempts || cancellationToken.IsCancellationRequested)
            {
                return (false, attempts);
            }

            await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task SyncCompanionServerStateAsync()
    {
        await _companionSyncGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            var options = new CompanionOptions
            {
                Enabled = Settings.CompanionEnabled,
                LanEnabled = Settings.CompanionLanEnabled,
                LanAddress = Settings.CompanionLanAddress,
                Port = Settings.CompanionPort > 0 ? Settings.CompanionPort : 47821
            };

            if (options.Enabled)
            {
                // QA R1 P2: bounded retry so a restart successor whose first bind
                // collides with the still-exiting predecessor comes up on its own
                // (port released during the predecessor's shutdown budget).
                // No new timer/owner: a plain awaited delay inside the existing
                // sync gate; failures keep the previous Failed-status reporting.
                var (started, attempts) = await StartCompanionWithRetryAsync(
                    async () => _disposed
                        ? false
                        : await CompanionServer.StartAsync(options).ConfigureAwait(false),
                    CompanionStartMaxAttempts,
                    CompanionStartRetryDelay).ConfigureAwait(false);
                if (attempts > 1)
                {
                    LoggerService.Instance.Warn(
                        $"Companion server start needed {attempts} attempt(s) (port collision likely).");
                }
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

    private void StartProfileWatcher()
    {
        if (!_profileWatcherTimer.IsEnabled)
        {
            _profileWatcherTimer.Start();
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
        AddLocalizedActivity("Activity.ProfilesSaved", "Info", Profiles.Count);
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
        AddLocalizedActivity("Activity.ProfileDeleted", "Info", profile.ProcessName);
    }

    public OptimizationBatchResult ApplyProfileNow(ProcessProfile profile, bool force = true)
    {
        if (!TryAcquireProfileMutation(out IDisposable? benchmarkLease))
        {
            AddLocalizedActivity("Activity.MutationBlockedBenchmark", "Warn");
            return CreateBenchmarkBlockedResult(profile.ProcessName);
        }

        using (benchmarkLease)
        {
        var result = Optimization.ApplyProfileToRunningProcesses(profile, Settings.AllowRealtimePriority, force);
        LogBatchResult(profile.ProcessName, result, "zastosowanie ręczne");
        return result;
        }
    }

    /// <summary>
    /// Localizes a runtime activity message against the current settings language.
    /// Keyed entries retain their original arguments so presentation can refresh on
    /// language changes; unkeyed and technical messages remain untouched.
    /// </summary>
    private string ActivityText(string key) => LocalizationService.Translate(key, Settings.Language);

    public void AddActivity(string message, string level = "Info") => AddActivityCore(message, level, null, []);

    private void AddLocalizedActivity(string key, string level = "Info", params object[] arguments)
    {
        string message = string.Format(LocalizationService.GetUiCulture(Settings.Language), ActivityText(key), arguments);
        AddActivityCore(message, level, key, arguments);
    }

    public void RefreshActivityLanguage()
    {
        DispatchActivityIfRequired(() =>
        {
            foreach (ActivityItemViewModel item in Activity) item.Relocalize(Settings.Language);
        });
    }

    private void AddActivityCore(string message, string level, string? localizationKey, object[] arguments)
    {
        // Activity is a WPF-bound ObservableCollection. Most callers run on the UI
        // thread, but timer/task continuations and future Companion paths may call
        // from the ThreadPool (see REA F12). Marshal the mutation to the creating
        // thread's dispatcher; logging below stays synchronous and thread-safe.
        DispatchActivityIfRequired(() =>
        {
            var activity = new ActivityItemViewModel
            {
                Time = DateTime.Now.ToString("HH:mm"),
                Message = message,
                Level = level,
                LocalizationKey = localizationKey,
                LocalizationArguments = arguments
            };
            activity.Relocalize(Settings.Language);
            Activity.Insert(0, activity);

            while (Activity.Count > 200)
            {
                Activity.RemoveAt(Activity.Count - 1);
            }
        });

        switch (level)
        {
            case "Warn": LoggerService.Instance.Warn(message); break;
            case "Error": LoggerService.Instance.Error(message); break;
            default: LoggerService.Instance.Info(message); break;
        }
    }

    private void DispatchActivityIfRequired(Action action)
    {
        Dispatcher? target = null;
        if (_uiDispatcher.Thread.IsAlive && !_uiDispatcher.HasShutdownStarted && !_uiDispatcher.HasShutdownFinished)
        {
            target = _uiDispatcher;
        }
        else if (System.Windows.Application.Current?.Dispatcher is { } appDispatcher)
        {
            target = appDispatcher;
        }

        if (target is null || target.CheckAccess())
        {
            action();
            return;
        }

        if (target.Thread.IsAlive && !target.HasShutdownStarted && !target.HasShutdownFinished)
        {
            try
            {
                target.BeginInvoke(action);
                return;
            }
            catch (InvalidOperationException)
            {
                // Dispatcher was shut down or unavailable during invoke.
            }
        }

        action();
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
            if (!TryAcquireProfileMutation(out IDisposable? benchmarkLease))
            {
                LogBenchmarkWatcherSkip();
                return;
            }

            OptimizationBatchResult batch;
            using (benchmarkLease)
            {
                batch = Optimization.ApplyProfilesForSnapshots(enabledProfiles, scan.Groups, Settings.AllowRealtimePriority, force: false);
                Optimization.CleanupStaleCache(scan.ActiveInstances);
            }

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
                    AddLocalizedActivity("Activity.ProfileAppliedMonitor", "Info", result.ProcessName, result.ProcessId, result.Mode, result.Priority);
                }
                else
                {
                    LogApplyFailure(result);
                }
            }
        }
        catch (Exception ex)
        {
            AddLocalizedActivity("Activity.MonitorError", "Warn", ex.Message);
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
            AddLocalizedActivity("Activity.ProfileAppliedSource", "Info", processName, source, batch.Successful, batch.Total);
        }
        else if (batch.Total > 0)
        {
            AddLocalizedActivity("Activity.ProfileNoInstance", "Warn", processName, source);
        }
        else
        {
            AddLocalizedActivity("Activity.ProfileNoProcess", "Warn", processName);
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
        string adminHint = result.RequiresAdmin ? ActivityText("Activity.ProfileApplyFailedAdminHint") : string.Empty;
        AddActivity(string.Format(ActivityText("Activity.ProfileApplyFailed"), result.ProcessName, result.ProcessId, result.Message, adminHint), "Warn");
    }

    private void LogBenchmarkWatcherSkip()
    {
        const string key = "profile-watcher|benchmark-active";
        if (_failureLogThrottle.TryGetValue(key, out DateTime last)
            && DateTime.UtcNow - last < TimeSpan.FromSeconds(60))
        {
            return;
        }

        _failureLogThrottle[key] = DateTime.UtcNow;
        AddLocalizedActivity("Activity.AutoMutationSkippedBenchmark", "Warn");
    }

    private static OptimizationBatchResult CreateBenchmarkBlockedResult(string processName) => new()
    {
        Total = 1,
        Failed = 1,
        Results =
        [
            new OptimizationResult
            {
                Success = false,
                ProcessName = processName,
                Message = "SKIPPED_BENCHMARK_ACTIVE"
            }
        ]
    };

    internal bool TryAcquireProfileMutation(out IDisposable? lease) =>
        BenchmarkCoordinator.TryAcquireExternalMutation(out lease);

    private void AddWatcherStartupActivity()
    {
        var enabled = Profiles.Where(p => p.IsEnabled).Select(p => p.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (enabled.Count == 0) AddLocalizedActivity("Activity.WatcherIdle");
        else AddLocalizedActivity("Activity.WatcherActive", "Info", enabled.Count, string.Join(", ", enabled));
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

    private readonly IHardwareMonitorBackend _hardwareMonitor;
    private static readonly TimeSpan HardwareSnapshotTimeToLive = TimeSpan.FromMilliseconds(200);
    private int _hardwareConsumerCount;
    private readonly object _hardwareLock = new();
    private HardwareMetrics? _hardwareSnapshot;
    private DateTimeOffset _hardwareSnapshotAt;

    public bool IsHardwareMonitoringActive
    {
        get
        {
            lock (_hardwareLock)
            {
                return !_disposed
                    && Settings.HardwareMonitorEnabled
                    && _hardwareConsumerCount > 0
                    && _hardwareMonitor.IsInitialized;
            }
        }
    }

    internal int HardwareConsumerCountForTesting
    {
        get
        {
            lock (_hardwareLock)
            {
                return _hardwareConsumerCount;
            }
        }
    }

    /// <summary>
    /// Registers a hardware consumer. The sensor backend opens only when the persisted
    /// <see cref="AppSettings.HardwareMonitorEnabled"/> setting is true and at least one consumer exists.
    /// </summary>
    public IHardwareMonitorLease AcquireHardwareLease()
    {
        lock (_hardwareLock)
        {
            _hardwareConsumerCount++;
            ReconcileHardwareMonitoringLocked();
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
            }
            ReconcileHardwareMonitoringLocked();
        }
    }

    /// <summary>
    /// Persists the global hardware monitoring choice and reconciles the sensor backend.
    /// </summary>
    public void SetHardwareMonitorEnabled(bool enabled)
    {
        if (Settings.HardwareMonitorEnabled == enabled) return;
        Settings.HardwareMonitorEnabled = enabled;
        SaveSettings(Settings);
    }

    private void ReconcileHardwareMonitoring()
    {
        lock (_hardwareLock)
        {
            ReconcileHardwareMonitoringLocked();
        }
    }

    private void ReconcileHardwareMonitoringLocked()
    {
        if (_disposed) return;

        bool shouldBeActive = Settings.HardwareMonitorEnabled && _hardwareConsumerCount > 0;
        if (shouldBeActive)
        {
            _hardwareMonitor.Configure(Settings.EnableStorageSensors);
            if (!_hardwareMonitor.IsInitialized)
            {
                _hardwareSnapshot = null;
                _hardwareMonitor.Start();
            }
        }
        else if (_hardwareMonitor.IsInitialized)
        {
            _hardwareSnapshot = null;
            _hardwareMonitor.Stop(closeSensors: true);
        }
    }

    public HardwareMetrics GetHardwareMetrics()
    {
        lock (_hardwareLock)
        {
            if (_hardwareConsumerCount <= 0 || !Settings.HardwareMonitorEnabled)
            {
                return new HardwareMetrics();
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (_hardwareSnapshot != null && now - _hardwareSnapshotAt <= HardwareSnapshotTimeToLive)
            {
                return _hardwareSnapshot;
            }

            _hardwareSnapshot = _hardwareMonitor.GetAllMetrics();
            _hardwareSnapshotAt = now;
            return _hardwareSnapshot;
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
        // Synchronous wrapper over the budgeted async core. Runs on a pool thread
        // so no UI-thread SynchronizationContext can deadlock the inner awaits
        // (same sync-over-async pattern the coordinator shutdown already used).
        try
        {
            Task.Run(() => ShutdownCoreAsync(DefaultShutdownBudget, CancellationToken.None)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            LoggerService.Instance.Warn($"Application shutdown encountered an error: {ex.Message}");
        }
    }

    /// <summary>
    /// Total wall-clock envelope shared with the window shutdown deadline
    /// (<c>MainWindow.PerformGracefulShutdownAsync</c>, 8 s). The window closes
    /// when the deadline expires even if background disposal is still draining —
    /// fail-safe (WAL/recovery protected), never a blind kill.
    /// </summary>
    public static readonly TimeSpan DefaultShutdownBudget = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Bounded cooperative shutdown. Every phase observes <paramref name="budget"/>:
    /// the session coordinator drain (the only phase able to hang indefinitely on
    /// a stuck native operation) receives the remaining budget explicitly instead
    /// of its 10 s default; companion phases keep their own small internal bounds.
    /// Returns <c>true</c> when the coordinator drained cleanly within budget.
    /// Cancellation is observed between phases, never by aborting a disposal.
    /// </summary>
    public async Task<bool> ShutdownAsync(TimeSpan budget, CancellationToken cancellationToken = default)
    {
        if (_disposed) return true;
        _disposed = true;
        return await ShutdownCoreAsync(budget, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> ShutdownCoreAsync(TimeSpan budget, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        TimeSpan Remaining() => budget - stopwatch.Elapsed;

        _profileWatcherTimer.Stop();

        bool gateAcquired = false;
        try
        {
            try
            {
                TimeSpan gateWait = Remaining();
                if (gateWait > TimeSpan.Zero)
                {
                    gateAcquired = await _companionSyncGate
                        .WaitAsync(gateWait > TimeSpan.FromSeconds(3) ? TimeSpan.FromSeconds(3) : gateWait, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch { }

            try
            {
                // Stop external consumers before disposing the providers and state authorities they call.
                await CompanionServer.DisposeAsync().ConfigureAwait(false);
                TelemetryProvider.Dispose();
                LiveTelemetryService.Dispose();
                ActiveGameMonitor.Dispose();
                BenchmarkCoordinator.Dispose();

                TimeSpan coordinatorBudget = Remaining();
                if (coordinatorBudget < TimeSpan.FromSeconds(1))
                {
                    coordinatorBudget = TimeSpan.FromSeconds(1);
                }

                bool sessionCoordinatorStopped = await SessionOptimizationCoordinator
                    .ShutdownAsync(coordinatorBudget)
                    .ConfigureAwait(false);
                if (!sessionCoordinatorStopped)
                {
                    LoggerService.Instance.Warn("Session Optimization shutdown timed out; synchronization resources will be released when the active operation exits.");
                }

                using var stopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                TimeSpan stopRemaining = Remaining();
                stopCts.CancelAfter(stopRemaining > TimeSpan.Zero ? stopRemaining : TimeSpan.Zero);
                try
                {
                    await CompanionServer.StopAsync(stopCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }

                lock (_hardwareLock)
                {
                    _hardwareConsumerCount = 0;
                    _hardwareSnapshot = null;
                    _hardwareMonitor.Dispose();
                }
                SettingsService.Dispose();
                return sessionCoordinatorStopped;
            }
            finally
            {
                if (gateAcquired)
                {
                    _companionSyncGate.Release();
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LoggerService.Instance.Warn($"Application shutdown encountered an error: {ex.Message}");
            return false;
        }
    }
}
