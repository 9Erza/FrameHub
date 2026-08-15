using System.Diagnostics;
using System.IO;
using FrameHub.App.Services;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Models.SessionOptimization;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Benchmarking;
using FrameHub.Core.Services.Library;
using FrameHub.Core.Services.SessionOptimization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public sealed class GamingQuickActionServiceTests
{
    private string _tempDirectory = null!;
    private string _stateFilePath = null!;
    private ProcessService _processService = null!;
    private ProcessScannerService _processScanner = null!;
    private string _fakeGameExe = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.GamingQuickActionTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _stateFilePath = Path.Combine(_tempDirectory, "active_session.json");
        _fakeGameExe = Path.Combine(_tempDirectory, "game.exe");
        File.WriteAllText(_fakeGameExe, "game binary");

        _processService = new ProcessService();
        _processScanner = new ProcessScannerService(_processService);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { }
        }
    }

    private sealed class FakeBenchmarkCoordinator : IBenchmarkCaptureCoordinator
    {
        public bool IsActive { get; set; }
        public BenchmarkCaptureStateSnapshot CurrentState => new()
        {
            State = IsActive ? CoordinatorState.Capturing : CoordinatorState.Idle,
            IsActive = IsActive
        };
        public event EventHandler<BenchmarkCaptureStateSnapshot>? StateChanged
        {
            add { }
            remove { }
        }

        public BenchmarkCaptureStartHandle TryStartCapture(BenchmarkCaptureRequest request, CancellationToken cancellationToken = default)
        {
            return new BenchmarkCaptureStartHandle
            {
                Accepted = true,
                CompletionTask = Task.FromResult(new BenchmarkCaptureOutcome { Status = CoordinatorStatus.Completed })
            };
        }

        public Task<BenchmarkCaptureOutcome> StartCaptureAsync(BenchmarkCaptureRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new BenchmarkCaptureOutcome { Status = CoordinatorStatus.Completed });
        }

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }

    private sealed class FakeLibraryLaunchService : IAppLibraryLaunchService
    {
        public int LaunchCalls { get; private set; }
        public LibraryItem? LastLaunchedItem { get; private set; }
        public LibraryLaunchResult ResultToReturn { get; set; } = LibraryLaunchResult.Ok();
        public Action? OnLaunch { get; set; }

        public LibraryLaunchResult Launch(LibraryItem? item)
        {
            LaunchCalls++;
            LastLaunchedItem = item;
            OnLaunch?.Invoke();
            return ResultToReturn;
        }
    }

    private sealed class GamingTestProcessSuspendService : ProcessSuspendService
    {
        private readonly SuspendedProcessRecord _record = new()
        {
            ProcessId = 4242,
            ProcessName = "background",
            ProcessStartTimeUtc = DateTime.UtcNow
        };

        public bool ReturnNoCandidates { get; set; }

        public override Task<SessionProcessSnapshot> CaptureProcessSnapshotAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new SessionProcessSnapshot());
        }

        public override IReadOnlyList<SuspendCandidate> BuildCandidates(
            SessionProcessSnapshot snapshot,
            IEnumerable<BackgroundProcessRule> enabledRules,
            IEnumerable<string> customProcessNames,
            IEnumerable<string> protectedProcessNames)
        {
            return ReturnNoCandidates
                ? []
                : [new SuspendCandidate { ProcessId = _record.ProcessId, ProcessName = _record.ProcessName, ProcessStartTimeUtc = _record.ProcessStartTimeUtc }];
        }

        public override SessionActionResult SuspendProcesses(IEnumerable<SuspendCandidate> candidates)
        {
            return new SessionActionResult
            {
                SuccessCount = 1,
                Records = [_record]
            };
        }

        public override SessionActionResult ResumeProcesses(IEnumerable<SuspendedProcessRecord> records)
        {
            var list = records.ToList();
            return new SessionActionResult
            {
                SuccessCount = list.Count,
                Records = list
            };
        }

        public override SessionActionResult ResolveProcessesWithoutResume(IEnumerable<SuspendedProcessRecord> records)
        {
            var list = records.ToList();
            return new SessionActionResult
            {
                ResolvedCount = list.Count,
                Records = list
            };
        }
    }

    private sealed class GamingTestTaskbarVisibilityService : TaskbarVisibilityService
    {
        public override TaskbarVisibilityState? CaptureVisibilityState() => new()
        {
            PrimaryTaskbarFound = true,
            PrimaryTaskbarVisible = true,
            SecondaryTaskbarsVisible = [true]
        };

        public override bool HideTaskbars() => true;
        public override bool ShowTaskbars() => true;
        public override bool RestoreVisibilityState(TaskbarVisibilityState state) => true;
    }

    private SessionOptimizationCoordinator CreateCoordinator(
        GamingTestProcessSuspendService? suspendService = null,
        LibraryService? libraryService = null)
    {
        // The fake suspend service is mandatory: a session started through a real
        // ProcessSuspendService would suspend actual operating-system processes.
        return new SessionOptimizationCoordinator(
            stateService: new SessionStateService(_stateFilePath),
            settingsService: new SessionOptimizationSettingsService(Path.Combine(_tempDirectory, "session_optimization.json")),
            suspendService: suspendService ?? new GamingTestProcessSuspendService(),
            taskbarService: new GamingTestTaskbarVisibilityService(),
            libraryService: libraryService ?? new LibraryService(Path.Combine(_tempDirectory, "library.json")));
    }

    private GamingQuickActionService CreateService(
        SessionOptimizationCoordinator coordinator,
        FakeBenchmarkCoordinator benchmarkCoordinator,
        FakeLibraryLaunchService launchService,
        LibraryLaunchReservationService? reservations = null,
        ProcessScannerService? processScanner = null)
    {
        return new GamingQuickActionService(
            processScanner ?? _processScanner,
            benchmarkCoordinator,
            launchService,
            reservations ?? new LibraryLaunchReservationService(),
            coordinator);
    }

    private static LibraryItem CreateGame(string? executablePath = null) => new()
    {
        Id = "game-1",
        DisplayName = "Game One",
        ProcessName = "game1",
        Type = LibraryItemType.Game,
        IsEnabled = true,
        ExecutablePath = executablePath ?? string.Empty
    };

    [TestMethod]
    public void Constructor_NullRequiredDependencies_ThrowsArgumentNullException()
    {
        using var coordinator = CreateCoordinator();
        var benchmarkCoordinator = new FakeBenchmarkCoordinator();
        var launchService = new FakeLibraryLaunchService();
        var reservations = new LibraryLaunchReservationService();

        Assert.ThrowsException<ArgumentNullException>(() => new GamingQuickActionService(null!, benchmarkCoordinator, launchService, reservations, coordinator));
        Assert.ThrowsException<ArgumentNullException>(() => new GamingQuickActionService(_processScanner, null!, launchService, reservations, coordinator));
        Assert.ThrowsException<ArgumentNullException>(() => new GamingQuickActionService(_processScanner, benchmarkCoordinator, null!, reservations, coordinator));
        Assert.ThrowsException<ArgumentNullException>(() => new GamingQuickActionService(_processScanner, benchmarkCoordinator, launchService, null!, coordinator));
        Assert.ThrowsException<ArgumentNullException>(() => new GamingQuickActionService(_processScanner, benchmarkCoordinator, launchService, reservations, null!));
    }

    [TestMethod]
    public void DashboardViewModel_SelectedGamingGame_IsWritableAndNotifiesChange()
    {
        string settingsPath = Path.Combine(_tempDirectory, "dashboard_test_settings.json");
        var settingsService = new SettingsService(settingsPath);
        var localization = new LocalizationService(settingsService);
        using var runtime = new AppRuntimeService(settingsService);
        var dashboard = new FrameHub.App.ViewModels.DashboardViewModel(localization, runtime);

        var game1 = CreateGame(_fakeGameExe);
        var game2 = new LibraryItem { Id = "game-2", DisplayName = "Game Two", ProcessName = "game2", Type = LibraryItemType.Game, IsEnabled = true };

        bool propertyChangedRaised = false;
        dashboard.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(dashboard.SelectedGamingGame))
            {
                propertyChangedRaised = true;
            }
        };

        dashboard.SelectedGamingGame = game1;
        Assert.AreEqual(game1, dashboard.SelectedGamingGame);
        Assert.IsTrue(propertyChangedRaised);

        propertyChangedRaised = false;
        dashboard.SelectedGamingGame = game2;
        Assert.AreEqual(game2, dashboard.SelectedGamingGame);
        Assert.IsTrue(propertyChangedRaised);
    }

    [TestMethod]
    public void DashboardViewModel_ShowGamingSelector_ReflectsSessionAndGamesState()
    {
        string settingsPath = Path.Combine(_tempDirectory, "dashboard_selector_settings.json");
        var settingsService = new SettingsService(settingsPath);
        var localization = new LocalizationService(settingsService);
        using var runtime = new AppRuntimeService(settingsService);
        var dashboard = new FrameHub.App.ViewModels.DashboardViewModel(localization, runtime);

        Assert.AreEqual(dashboard.GamingGames.Count > 0, dashboard.HasGamingGames);
        Assert.AreEqual(dashboard.GamingGames.Count == 0, dashboard.HasNoGamingGames);
        Assert.AreEqual(!dashboard.IsGamingModeActive && dashboard.HasGamingGames, dashboard.ShowGamingSelector);
    }

    [TestMethod]
    public async Task StartGamingModeAsync_InvalidItem_ReturnsNotLaunchableWithoutSideEffects()
    {
        using var coordinator = CreateCoordinator();
        var benchmarkCoordinator = new FakeBenchmarkCoordinator();
        var launchService = new FakeLibraryLaunchService();
        var service = CreateService(coordinator, benchmarkCoordinator, launchService);

        var nullResult = await service.StartGamingModeAsync(null);
        var disabledResult = await service.StartGamingModeAsync(new LibraryItem { Id = "g", Type = LibraryItemType.Game, IsEnabled = false });
        var backgroundAppResult = await service.StartGamingModeAsync(new LibraryItem { Id = "g", Type = LibraryItemType.BackgroundApp, IsEnabled = true });

        Assert.AreEqual("not_launchable", nullResult.ErrorCode);
        Assert.AreEqual("Launch", nullResult.Stage);
        Assert.AreEqual("not_launchable", disabledResult.ErrorCode);
        Assert.AreEqual("not_launchable", backgroundAppResult.ErrorCode);
        Assert.AreEqual(0, launchService.LaunchCalls);
        Assert.IsNull(coordinator.ActiveSession);
    }

    [TestMethod]
    public async Task StartGamingModeAsync_WhenBenchmarkActive_ReturnsBenchmarkActiveWithoutLaunchOrSession()
    {
        using var coordinator = CreateCoordinator();
        var benchmarkCoordinator = new FakeBenchmarkCoordinator { IsActive = true };
        var launchService = new FakeLibraryLaunchService();
        var service = CreateService(coordinator, benchmarkCoordinator, launchService);

        var result = await service.StartGamingModeAsync(CreateGame(_fakeGameExe));

        Assert.IsFalse(result.Success);
        Assert.AreEqual("benchmark_active", result.ErrorCode);
        Assert.IsFalse(result.Launched);
        Assert.AreEqual(0, launchService.LaunchCalls);
        Assert.IsNull(coordinator.ActiveSession);
    }

    [TestMethod]
    public async Task StartGamingModeAsync_AlreadyRunning_SkipsLaunchAndStartsSessionForSameGame()
    {
        // Use the current test process as a trusted already-running library target.
        var currentProcess = Process.GetCurrentProcess();
        string currentExe = currentProcess.MainModule?.FileName ?? _fakeGameExe;
        var game = new LibraryItem
        {
            Id = "game-running",
            DisplayName = "Running Game",
            ProcessName = currentProcess.ProcessName,
            Type = LibraryItemType.Game,
            IsEnabled = true,
            ExecutablePath = currentExe
        };

        using var coordinator = CreateCoordinator();
        var benchmarkCoordinator = new FakeBenchmarkCoordinator();
        var launchService = new FakeLibraryLaunchService();
        var service = CreateService(coordinator, benchmarkCoordinator, launchService);

        var result = await service.StartGamingModeAsync(game);

        Assert.IsTrue(result.Success, $"Session start expected, got '{result.ErrorCode}'.");
        Assert.IsTrue(result.AlreadyRunning);
        Assert.IsFalse(result.Launched);
        Assert.AreEqual(0, launchService.LaunchCalls, "A second copy must not be launched when the game is already running.");
        Assert.IsNotNull(coordinator.ActiveSession);
        Assert.IsTrue(coordinator.ActiveSession.IsActive);
        Assert.AreEqual("Manual", coordinator.ActiveSession.Trigger);
        Assert.AreEqual(game.Id, coordinator.ActiveSession.GameId);
    }

    [TestMethod]
    public async Task StartGamingModeAsync_NotRunning_LaunchesRecordsCooldownAndStartsSession()
    {
        var game = CreateGame(_fakeGameExe);
        using var coordinator = CreateCoordinator();
        var benchmarkCoordinator = new FakeBenchmarkCoordinator();
        var launchService = new FakeLibraryLaunchService();
        var reservations = new LibraryLaunchReservationService();
        var service = CreateService(coordinator, benchmarkCoordinator, launchService, reservations);

        var result = await service.StartGamingModeAsync(game);

        Assert.IsTrue(result.Success, $"Session start expected, got '{result.ErrorCode}'.");
        Assert.IsTrue(result.Launched);
        Assert.AreEqual(1, launchService.LaunchCalls);
        Assert.AreEqual(game.Id, launchService.LastLaunchedItem?.Id);
        Assert.IsNotNull(coordinator.ActiveSession);
        Assert.AreEqual(1, coordinator.ActiveSession.SuspendedProcesses.Count);

        // Second action inside the shared cooldown window is rejected before launch/session work.
        var second = await service.StartGamingModeAsync(game);
        Assert.IsFalse(second.Success);
        Assert.AreEqual("launch_in_progress", second.ErrorCode);
        Assert.AreEqual("Launch", second.Stage);
        Assert.AreEqual(1, launchService.LaunchCalls);
    }

    [TestMethod]
    public async Task StartGamingModeAsync_LaunchFailure_SkipsSessionAndDoesNotRecordCooldown()
    {
        var game = CreateGame(_fakeGameExe);
        using var coordinator = CreateCoordinator();
        var benchmarkCoordinator = new FakeBenchmarkCoordinator();
        var launchService = new FakeLibraryLaunchService { ResultToReturn = LibraryLaunchResult.Fail("launch_failed") };
        var reservations = new LibraryLaunchReservationService();
        var service = CreateService(coordinator, benchmarkCoordinator, launchService, reservations);

        var result = await service.StartGamingModeAsync(game);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("launch_failed", result.ErrorCode);
        Assert.AreEqual("Launch", result.Stage);
        Assert.IsNull(coordinator.ActiveSession, "Session optimization must not start when the launch failed.");

        // No cooldown was recorded, so a retry reaches the launch service again.
        var retry = await service.StartGamingModeAsync(game);
        Assert.AreEqual("launch_failed", retry.ErrorCode);
        Assert.AreEqual(2, launchService.LaunchCalls);
    }

    [TestMethod]
    public async Task StartGamingModeAsync_SessionNoCandidates_AfterSuccessfulLaunch_ReportsSessionStageHonestly()
    {
        var game = CreateGame(_fakeGameExe);
        using var coordinator = CreateCoordinator(new GamingTestProcessSuspendService { ReturnNoCandidates = true });
        var benchmarkCoordinator = new FakeBenchmarkCoordinator();
        var launchService = new FakeLibraryLaunchService();
        var service = CreateService(coordinator, benchmarkCoordinator, launchService);

        var result = await service.StartGamingModeAsync(game);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("no_candidates", result.ErrorCode);
        Assert.AreEqual("SessionStart", result.Stage);
        Assert.IsTrue(result.Launched, "The launch itself succeeded and must be reported.");
        Assert.IsNull(coordinator.ActiveSession);
    }

    [TestMethod]
    public async Task StartGamingModeAsync_ConcurrentAction_RejectedByNonQueuingGate()
    {
        var game = CreateGame(_fakeGameExe);
        using var coordinator = CreateCoordinator();
        var benchmarkCoordinator = new FakeBenchmarkCoordinator();
        var launchEntered = new ManualResetEventSlim(false);
        var releaseLaunch = new ManualResetEventSlim(false);
        var launchService = new FakeLibraryLaunchService
        {
            OnLaunch = () =>
            {
                launchEntered.Set();
                releaseLaunch.Wait(TimeSpan.FromSeconds(10));
            }
        };
        var service = CreateService(coordinator, benchmarkCoordinator, launchService);

        var first = Task.Run(() => service.StartGamingModeAsync(game));
        Assert.IsTrue(launchEntered.Wait(TimeSpan.FromSeconds(10)), "First action never reached the launch stage.");

        var second = await service.StartGamingModeAsync(game);
        Assert.IsFalse(second.Success);
        Assert.AreEqual("operation_in_progress", second.ErrorCode);
        Assert.AreEqual(1, launchService.LaunchCalls, "The rejected concurrent action must not reach the launch service.");

        releaseLaunch.Set();
        var firstResult = await first;
        Assert.IsTrue(firstResult.Success);
    }

    [TestMethod]
    public async Task StopGamingModeAsync_DelegatesToSessionCoordinator()
    {
        using var coordinator = CreateCoordinator();
        var benchmarkCoordinator = new FakeBenchmarkCoordinator();
        var launchService = new FakeLibraryLaunchService();
        var service = CreateService(coordinator, benchmarkCoordinator, launchService);

        var result = await service.StopGamingModeAsync();

        Assert.IsFalse(result.Success);
        Assert.AreEqual("not_active", result.ErrorCode);
    }
}
