using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Services.Benchmarking;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.Core.Models;
using FrameHub.Core.Services;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using FrameHub.App.Views;
using FrameHub.App.ViewModels.Benchmark;

namespace FrameHub.Tests;

[TestClass]
public sealed class BenchmarkGameDetectionTests
{
    [TestMethod]
    public void NoProcesses_ReturnsNoGames() => Assert.AreEqual(0, Detect([], Game("one", @"C:\Games\one.exe")).Count);

    [TestMethod]
    public void ExactPath_SelectsOnlyConfiguredGame()
    {
        IReadOnlyList<BenchmarkRunningGame> found = Detect([new(42, "one", @"C:\Games\one.exe", DateTime.UtcNow)], Game("one", @"C:\Games\one.exe"), Game("two", @"C:\Games\two.exe"));
        Assert.AreEqual("one", found.Single().LibraryItem.Id);
        Assert.AreEqual(42, found.Single().Process.ProcessId);
    }

    [TestMethod]
    public void PathlessUniqueName_IsAccepted()
    {
        IReadOnlyList<BenchmarkRunningGame> found = Detect([new(42, "one", null, DateTime.UtcNow)], Game("one", @"C:\Games\one.exe"));
        Assert.AreEqual(BenchmarkProcessPathResolution.Unavailable, found.Single().Process.ExecutablePathResolution);
    }

    [TestMethod]
    public void PathlessAmbiguousName_IsNeverGuessed()
    {
        IReadOnlyList<BenchmarkRunningGame> found = Detect([new(42, "game", null, DateTime.UtcNow)], Game("a", @"C:\A\game.exe"), Game("b", @"D:\B\game.exe"));
        Assert.AreEqual(0, found.Count);
    }

    private static IReadOnlyList<BenchmarkRunningGame> Detect(IReadOnlyList<BenchmarkProcessSnapshot> processes, params LibraryItem[] games) =>
        new BenchmarkGameDetectionService(new FakeProcesses(processes)).Detect(games);
    private static LibraryItem Game(string id, string path) => new() { Id = id, DisplayName = id, ExecutablePath = path, ProcessName = Path.GetFileNameWithoutExtension(path), Type = LibraryItemType.Game, IsEnabled = true };
    private sealed class FakeProcesses(IReadOnlyList<BenchmarkProcessSnapshot> processes) : IBenchmarkProcessSnapshotProvider { public IReadOnlyList<BenchmarkProcessSnapshot> GetProcesses() => processes; }
}

[TestClass]
public sealed class BenchmarkChartDataTests
{
    [TestMethod]
    public void PresentedSeries_UsesCumulativePositiveIntervalsForExactTarget()
    {
        IReadOnlyList<BenchmarkChartPoint> points = BenchmarkChartData.BuildPresentedSeries([
            Frame(7, "0x1", 10), Frame(8, "0x1", 99), Frame(7, "0x2", 50), Frame(7, "0x1", -1), Frame(7, "0x1", 20)], 7, "0x1");
        CollectionAssert.AreEqual(new[] { 0.01, 0.03 }, points.Select(p => p.ElapsedSeconds).ToArray());
    }

    [TestMethod]
    public void Downsampling_PreservesLargeSpikeAndChronology()
    {
        var input = Enumerable.Range(0, 1000).Select(i => new BenchmarkChartPoint(i / 100.0, i == 455 ? 250 : 10 + i % 3)).ToList();
        IReadOnlyList<BenchmarkChartPoint> result = BenchmarkChartData.DownsampleMinMax(input, 50);
        Assert.IsTrue(result.Any(point => point.FrameTimeMs == 250));
        Assert.IsTrue(result.Zip(result.Skip(1)).All(pair => pair.First.ElapsedSeconds <= pair.Second.ElapsedSeconds));
    }

    [TestMethod]
    public void Downsampling_RejectsInvalidBucketCount() => Assert.ThrowsException<ArgumentOutOfRangeException>(() => BenchmarkChartData.DownsampleMinMax([], 0));
    private static BenchmarkFrameSample Frame(int pid, string chain, double interval) => new() { ProcessId = pid, SwapChainAddress = chain, MsBetweenPresents = interval };
}

[TestClass]
public sealed class BenchmarkComparisonTests
{
    [TestMethod]
    public void PercentageDelta_UsesDocumentedFormula() => Assert.AreEqual(25, BenchmarkComparisonService.CalculatePercentageDelta(80, 100));
    [TestMethod]
    public void PercentageDelta_RejectsZeroNullAndNonFinite()
    {
        Assert.IsNull(BenchmarkComparisonService.CalculatePercentageDelta(0, 1));
        Assert.IsNull(BenchmarkComparisonService.CalculatePercentageDelta(null, 1));
        Assert.IsNull(BenchmarkComparisonService.CalculatePercentageDelta(double.NaN, 1));
    }
    [TestMethod]
    public void Compare_ProvidesFpsAndFrametimeDirections()
    {
        IReadOnlyList<BenchmarkComparisonMetric> values = BenchmarkComparisonService.Compare(Entry("game", 100, 10), Entry("game", 110, 9));
        Assert.AreEqual(BenchmarkMetricDirection.HigherIsBetter, values.Single(v => v.Key == "average_fps").Direction);
        Assert.AreEqual(BenchmarkMetricDirection.LowerIsBetter, values.Single(v => v.Key == "p99_frame_time").Direction);
    }
    [TestMethod]
    public void Compare_RejectsDifferentGames() => Assert.ThrowsException<BenchmarkException>(() => BenchmarkComparisonService.Compare(Entry("a", 1, 1), Entry("b", 1, 1)));

    private static BenchmarkHistoryEntry Entry(string id, double average, double p99) => new()
    {
        SessionDirectory = id,
        Metadata = new BenchmarkSessionMetadata { SessionId = Guid.NewGuid(), Game = new BenchmarkTarget { LibraryItemId = id, DisplayName = id, LibrarySource = "Manual" }, Status = BenchmarkSessionStatus.Completed },
        Summary = new BenchmarkSummary { PrimaryPresentedMetrics = new BenchmarkMetricSet { AverageFps = average, P99FrameTimeMs = p99 } }
    };
}

[TestClass]
public sealed class BenchmarkHistoryStorageTests
{
    private string _root = null!;
    [TestInitialize] public void Initialize() => _root = Path.Combine(Path.GetTempPath(), "FrameHubTests", Guid.NewGuid().ToString("N"));
    [TestCleanup] public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [TestMethod]
    public void Enumerate_LoadsCompletedSchemaV1Summary()
    {
        BenchmarkStorageService storage = new(_root);
        BenchmarkSession session = Create(storage, BenchmarkSessionStatus.Completed);
        storage.SaveSummary(session, new BenchmarkSummary { SessionId = session.Metadata.SessionId, PrimaryPresentedMetrics = new BenchmarkMetricSet { AverageFps = 120 } });
        BenchmarkHistoryEntry entry = storage.EnumerateSessions().Sessions.Single();
        Assert.AreEqual(1, entry.Metadata.SchemaVersion);
        Assert.AreEqual(120, entry.Summary!.PrimaryPresentedMetrics!.AverageFps);
    }

    [TestMethod]
    public void Enumerate_IncludesFailedAndCancelledWithoutInventingSummary()
    {
        BenchmarkStorageService storage = new(_root);
        Create(storage, BenchmarkSessionStatus.Failed);
        Create(storage, BenchmarkSessionStatus.Cancelled);
        Assert.IsTrue(storage.EnumerateSessions().Sessions.All(entry => entry.Summary is null));
    }

    [TestMethod]
    public void CorruptSession_DoesNotBreakValidHistory()
    {
        BenchmarkStorageService storage = new(_root);
        Create(storage, BenchmarkSessionStatus.Cancelled);
        string bad = Path.Combine(_root, "bad"); Directory.CreateDirectory(bad); File.WriteAllText(Path.Combine(bad, BenchmarkFormat.SessionFileName), "{");
        BenchmarkHistoryResult result = storage.EnumerateSessions();
        Assert.AreEqual(1, result.Sessions.Count);
        Assert.AreEqual(1, result.Warnings.Count);
    }

    [TestMethod]
    public void MissingCompletedSummary_IsRepresentedAsReadError()
    {
        BenchmarkStorageService storage = new(_root); Create(storage, BenchmarkSessionStatus.Completed);
        Assert.IsNotNull(storage.EnumerateSessions().Sessions.Single().ReadError);
    }

    [TestMethod]
    public void Delete_IsSafeAndRejectsOutOfRoot()
    {
        BenchmarkStorageService storage = new(_root); BenchmarkSession session = Create(storage, BenchmarkSessionStatus.Cancelled);
        storage.DeleteSession(session.SessionDirectory);
        Assert.IsFalse(Directory.Exists(session.SessionDirectory));
        Assert.ThrowsException<InvalidOperationException>(() => storage.DeleteSession(Path.GetTempPath()));
    }

    private static BenchmarkSession Create(BenchmarkStorageService storage, BenchmarkSessionStatus status)
    {
        BenchmarkSession session = storage.CreateSession(new BenchmarkTarget { LibraryItemId = Guid.NewGuid().ToString("N"), DisplayName = "Game", LibrarySource = "Manual" }, new BenchmarkProcessIdentity { ProcessId = 1, ProcessName = "game", StartTimeUtc = DateTime.UtcNow }, "0.6.0", DateTime.UtcNow);
        session.Metadata.Status = status; storage.SaveSession(session); return session;
    }
}

[TestClass]
public sealed class BenchmarkViewModelWorkflowTests
{
    private string _root = null!;
    [TestInitialize] public void Initialize() => _root = Path.Combine(Path.GetTempPath(), "FrameHubVmTests", Guid.NewGuid().ToString("N"));
    [TestCleanup] public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [TestMethod]
    public async Task InitialRefresh_AutoSelectsExactlyOneRunningGameAndEnablesStart()
    {
        using BenchmarkViewModel vm = Create(BackendMode.Success, out _);
        Assert.AreEqual(FrameHub.App.ViewModels.Benchmark.BenchmarkUiState.Idle, vm.State);
        await vm.RefreshGamesAsync();
        Assert.AreEqual("Game", vm.SelectedGame!.DisplayName);
        Assert.IsTrue(vm.CanStart);
    }

    [TestMethod]
    public async Task SuccessfulCapture_DisplaysResultRefreshesHistoryAndPreventsDuplicateStart()
    {
        using BenchmarkViewModel vm = Create(BackendMode.Success, out FakeBackend backend);
        await vm.RefreshGamesAsync(); vm.CountdownSeconds = 0;
        Task first = vm.StartAsync(); Task second = vm.StartAsync();
        await Task.WhenAll(first, second);
        Assert.AreEqual(1, backend.CallCount);
        Assert.AreEqual(FrameHub.App.ViewModels.Benchmark.BenchmarkUiState.Completed, vm.State);
        Assert.IsNotNull(vm.CurrentResult);
        Assert.AreEqual(1, vm.History.Count);
        Assert.IsFalse(vm.HasNoHistory);
    }

    [TestMethod]
    public async Task EmptyHistory_ReportsEmptyStateOnlyWhenListIsEmpty()
    {
        using BenchmarkViewModel vm = Create(BackendMode.Success, out _);
        await vm.RefreshHistoryAsync();
        Assert.IsTrue(vm.HasNoHistory);
        Assert.IsFalse(vm.HasHistory);
    }

    [TestMethod]
    public async Task HistoryRefresh_AutoSelectsNewestCompletedSession()
    {
        var storage = new BenchmarkStorageService(_root);
        BenchmarkSession older = SaveCompleted(storage, "game", DateTime.UtcNow.AddMinutes(-5), 100);
        BenchmarkSession newer = SaveCompleted(storage, "game", DateTime.UtcNow, 120);
        using BenchmarkViewModel vm = Create(storage, BackendMode.Success, out _);

        await vm.RefreshHistoryAsync();

        Assert.AreEqual(2, vm.History.Count);
        Assert.AreEqual(newer.Metadata.SessionId, vm.SelectedHistory?.Entry.Metadata.SessionId);
        Assert.AreNotEqual(older.Metadata.SessionId, vm.SelectedHistory?.Entry.Metadata.SessionId);
    }

    [TestMethod]
    public async Task HistoryRefresh_PreservesExistingCompletedSelection()
    {
        var storage = new BenchmarkStorageService(_root);
        BenchmarkSession older = SaveCompleted(storage, "game", DateTime.UtcNow.AddMinutes(-5), 100);
        SaveCompleted(storage, "game", DateTime.UtcNow, 120);
        using BenchmarkViewModel vm = Create(storage, BackendMode.Success, out _);
        await vm.RefreshHistoryAsync();
        vm.SelectedHistory = vm.History.Single(item => item.Entry.Metadata.SessionId == older.Metadata.SessionId);

        await vm.RefreshHistoryAsync();

        Assert.AreEqual(older.Metadata.SessionId, vm.SelectedHistory?.Entry.Metadata.SessionId);
    }

    [TestMethod]
    public async Task CompareState_IsInsufficientWithZeroCompletedSessions()
    {
        using BenchmarkViewModel vm = Create(BackendMode.Success, out _);
        await vm.RefreshHistoryAsync();
        Assert.IsTrue(vm.HasInsufficientComparisonSessions);
        Assert.IsFalse(vm.HasEnoughComparisonSessions);
    }

    [TestMethod]
    public async Task CompareState_IsInsufficientWithOneCompletedSession()
    {
        var storage = new BenchmarkStorageService(_root);
        SaveCompleted(storage, "game", DateTime.UtcNow, 100);
        using BenchmarkViewModel vm = Create(storage, BackendMode.Success, out _);
        await vm.RefreshHistoryAsync();
        Assert.IsTrue(vm.HasInsufficientComparisonSessions);
    }

    [TestMethod]
    public async Task CompareState_IsReadyWithTwoDistinctSameGameSessions()
    {
        var storage = new BenchmarkStorageService(_root);
        SaveCompleted(storage, "game", DateTime.UtcNow.AddMinutes(-5), 100);
        SaveCompleted(storage, "game", DateTime.UtcNow, 110);
        using BenchmarkViewModel vm = Create(storage, BackendMode.Success, out _);
        await vm.RefreshHistoryAsync();
        Assert.IsTrue(vm.HasEnoughComparisonSessions);
        Assert.IsFalse(vm.HasInsufficientComparisonSessions);
    }

    [TestMethod]
    public async Task HotkeyStart_UsesOnlyRunningLibraryTargetAndSkipsConfiguredCountdown()
    {
        using BenchmarkViewModel vm = Create(BackendMode.Success, out FakeBackend backend);
        await vm.RefreshGamesAsync();
        vm.CountdownSeconds = 5;
        await vm.HandleGlobalHotkeyAsync();
        Assert.AreEqual(1, backend.CallCount);
        Assert.AreEqual(5, vm.CountdownSeconds, "The stored UI countdown must not be changed by a hotkey start.");
    }

    [TestMethod]
    public async Task HotkeyStop_CancelsActiveCapture()
    {
        using BenchmarkViewModel vm = Create(BackendMode.WaitForCancellation, out _);
        await vm.RefreshGamesAsync();
        Task capture = vm.HandleGlobalHotkeyAsync();
        await Task.Delay(30);
        await vm.HandleGlobalHotkeyAsync();
        await capture;
        Assert.AreEqual(BenchmarkUiState.Cancelled, vm.State);
    }

    [TestMethod]
    public async Task HotkeyAmbiguousTargets_DoNotStartArbitraryGame()
    {
        var storage = new BenchmarkStorageService(_root);
        var backend = new FakeBackend(storage, BackendMode.Success);
        LibraryItem first = Game("a", "Alpha", @"C:\Games\alpha.exe", "alpha");
        LibraryItem second = Game("b", "Beta", @"C:\Games\beta.exe", "beta");
        var detector = new BenchmarkGameDetectionService(new FixedProcesses([
            new(101, "alpha", first.ExecutablePath, DateTime.UtcNow),
            new(202, "beta", second.ExecutablePath, DateTime.UtcNow)]));
        using var vm = new BenchmarkViewModel(new LocalizationService(new SettingsService()), new FakeRuntime(), storage, detector, () => [first, second], () => backend, () => false, engineProbe: () => (true, "2.5.1", null));
        await vm.RefreshGamesAsync();
        await vm.HandleGlobalHotkeyAsync();
        Assert.AreEqual(0, backend.CallCount);
        Assert.IsNull(vm.SelectedGame);
    }

    [TestMethod]
    public async Task Stop_CancelsCaptureAndReturnsCancelledState()
    {
        using BenchmarkViewModel vm = Create(BackendMode.WaitForCancellation, out _);
        await vm.RefreshGamesAsync(); vm.CountdownSeconds = 0;
        Task capture = vm.StartAsync();
        await Task.Delay(20); vm.Stop(); await capture;
        Assert.AreEqual(FrameHub.App.ViewModels.Benchmark.BenchmarkUiState.Cancelled, vm.State);
    }

    [TestMethod]
    public async Task CaptureFailure_IsFriendlyAndTerminal()
    {
        using BenchmarkViewModel vm = Create(BackendMode.Failure, out _);
        await vm.RefreshGamesAsync(); vm.CountdownSeconds = 0; await vm.StartAsync();
        Assert.AreEqual(FrameHub.App.ViewModels.Benchmark.BenchmarkUiState.Failed, vm.State);
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.StatusMessage));
        StringAssert.Contains(vm.TechnicalError, "capture_failed");
    }

    [TestMethod]
    public void DisposingViewModel_UnsubscribesFromCoordinatorStateChanged()
    {
        var coordinator = new BenchmarkCaptureCoordinator(new BenchmarkStorageService(_root));
        var game = new LibraryItem { Id = "game", DisplayName = "Game", Source = LibrarySource.Manual, ExecutablePath = @"C:\Games\game.exe", ProcessName = "game", IsEnabled = true };
        var detector = new BenchmarkGameDetectionService(new FixedProcesses([new(123, "game", @"C:\Games\game.exe", DateTime.UtcNow)]));
        var vm = new BenchmarkViewModel(new LocalizationService(new SettingsService()), new FakeRuntime(), new BenchmarkStorageService(_root), detector, () => [game], () => new FakeBackend(new BenchmarkStorageService(_root), BackendMode.Success), () => false, engineProbe: () => (true, "2.5.1", null), coordinator: coordinator);

        vm.Dispose();

        Assert.AreEqual(BenchmarkUiState.Idle, vm.State);
    }

    [TestMethod]
    public async Task ExternalStart_NormalCompletion_LeavesVmInactiveAndNotStuckInCompleting()
    {
        var storage = new BenchmarkStorageService(_root);
        var controllableBackend = new ControllableFakeBackend(storage);
        var coordinator = new BenchmarkCaptureCoordinator(storage, () => controllableBackend);
        var game = Game("g1", "Game 1", @"C:\Games\g1.exe", "g1");
        var startTime = DateTime.UtcNow;
        var detector = new BenchmarkGameDetectionService(new FixedProcesses([new(101, "g1", game.ExecutablePath, startTime)]));
        using var vm = new BenchmarkViewModel(new LocalizationService(new SettingsService()), new FakeRuntime(), storage, detector, () => [game], () => controllableBackend, () => false, engineProbe: () => (true, "1.0", null), coordinator: coordinator);

        await vm.RefreshGamesAsync();
        Assert.IsTrue(vm.CanStart);

        var request = new BenchmarkCaptureRequest
        {
            Target = new BenchmarkTarget { LibraryItemId = game.Id, DisplayName = game.DisplayName },
            Process = new BenchmarkProcessIdentity { ProcessId = 101, ProcessName = "g1", ExecutablePath = game.ExecutablePath, StartTimeUtc = startTime },
            AppVersion = "1.0",
            DurationSeconds = 1,
            CountdownSeconds = 0
        };

        var handle = coordinator.TryStartCapture(request);
        Assert.IsTrue(handle.Accepted);

        await controllableBackend.WaitUntilStartedAsync();
        Assert.IsTrue(vm.IsCaptureActive, "VM should observe external capture becoming active.");

        controllableBackend.Release();
        await handle.CompletionTask!;
        await vm.RefreshHistoryAsync();

        Assert.IsFalse(coordinator.IsActive);
        Assert.IsFalse(vm.IsCaptureActive, "VM must not remain active after external completion.");
        Assert.AreNotEqual(BenchmarkUiState.Completing, vm.State);
        Assert.AreEqual(BenchmarkUiState.Completed, vm.State);
        Assert.IsTrue(vm.CanStart, "Desktop Start button must be available again after external completion.");
    }

    [TestMethod]
    public async Task ExternalStart_CountdownAndStop_LeavesVmInactiveAndNotStuckInWaiting()
    {
        var storage = new BenchmarkStorageService(_root);
        var waitingSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var game = Game("g1", "Game 1", @"C:\Games\g1.exe", "g1");
        var startTime = DateTime.UtcNow;
        var coordinator = new BenchmarkCaptureCoordinator(
            storage,
            () => new FakeBackend(storage, BackendMode.Success),
            identityProvider: new SimpleTestIdentityProvider(startTime),
            delayProvider: async (delay, ct) =>
            {
                waitingSignal.TrySetResult();
                await Task.Delay(1000, ct);
            });

        var detector = new BenchmarkGameDetectionService(new FixedProcesses([new(101, "g1", game.ExecutablePath, startTime)]));
        using var vm = new BenchmarkViewModel(new LocalizationService(new SettingsService()), new FakeRuntime(), storage, detector, () => [game], () => new FakeBackend(storage, BackendMode.Success), () => false, engineProbe: () => (true, "1.0", null), coordinator: coordinator);

        await vm.RefreshGamesAsync();

        var request = new BenchmarkCaptureRequest
        {
            Target = new BenchmarkTarget { LibraryItemId = game.Id, DisplayName = game.DisplayName },
            Process = new BenchmarkProcessIdentity { ProcessId = 101, ProcessName = "g1", ExecutablePath = game.ExecutablePath, StartTimeUtc = startTime },
            AppVersion = "1.0",
            DurationSeconds = 10,
            CountdownSeconds = 5
        };

        var handle = coordinator.TryStartCapture(request);
        Assert.IsTrue(handle.Accepted);

        await waitingSignal.Task;
        Assert.AreEqual(BenchmarkUiState.Waiting, vm.State);

        await coordinator.StopAsync();
        await handle.CompletionTask!;
        await vm.RefreshHistoryAsync();

        Assert.IsFalse(coordinator.IsActive);
        Assert.IsFalse(vm.IsCaptureActive, "VM must not remain in Waiting state after external cancellation.");
        Assert.AreEqual(BenchmarkUiState.Cancelled, vm.State);
    }

    [TestMethod]
    public async Task ExternalStart_Failure_LeavesVmInactiveWithFriendlyError()
    {
        var storage = new BenchmarkStorageService(_root);
        var coordinator = new BenchmarkCaptureCoordinator(storage, () => new FakeBackend(storage, BackendMode.Failure));
        var game = Game("g1", "Game 1", @"C:\Games\g1.exe", "g1");
        var startTime = DateTime.UtcNow;
        var detector = new BenchmarkGameDetectionService(new FixedProcesses([new(101, "g1", game.ExecutablePath, startTime)]));
        using var vm = new BenchmarkViewModel(new LocalizationService(new SettingsService()), new FakeRuntime(), storage, detector, () => [game], () => new FakeBackend(storage, BackendMode.Failure), () => false, engineProbe: () => (true, "1.0", null), coordinator: coordinator);

        await vm.RefreshGamesAsync();

        var request = new BenchmarkCaptureRequest
        {
            Target = new BenchmarkTarget { LibraryItemId = game.Id, DisplayName = game.DisplayName },
            Process = new BenchmarkProcessIdentity { ProcessId = 101, ProcessName = "g1", ExecutablePath = game.ExecutablePath, StartTimeUtc = startTime },
            AppVersion = "1.0",
            DurationSeconds = 1,
            CountdownSeconds = 0
        };

        var handle = coordinator.TryStartCapture(request);
        await handle.CompletionTask!;
        await vm.RefreshHistoryAsync();

        Assert.IsFalse(vm.IsCaptureActive);
        Assert.AreEqual(BenchmarkUiState.Failed, vm.State);
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.StatusMessage));
    }

    [TestMethod]
    public async Task LocalStart_DoesNotDuplicateSideEffects()
    {
        var fakeRuntime = new FakeRuntime();
        var storage = new BenchmarkStorageService(_root);
        var createdBackend = new FakeBackend(storage, BackendMode.Success);
        var game = Game("g1", "Game 1", @"C:\Games\g1.exe", "g1");
        var detector = new BenchmarkGameDetectionService(new FixedProcesses([new(101, "g1", game.ExecutablePath, DateTime.UtcNow)]));
        using var vm = new BenchmarkViewModel(new LocalizationService(new SettingsService()), fakeRuntime, storage, detector, () => [game], () => createdBackend, () => false, engineProbe: () => (true, "1.0", null));

        await vm.RefreshGamesAsync();
        vm.CountdownSeconds = 0;

        int initialCount = fakeRuntime.Activity.Count;
        await vm.StartAsync();

        var captureActivities = fakeRuntime.Activity.Skip(initialCount).ToList();
        Assert.AreEqual(2, captureActivities.Count, "StartAsync should record exact start and completion events without duplication.");
    }

    [TestMethod]
    public async Task AfterExternalCaptureCompleted_DesktopStartIsPossible()
    {
        var storage = new BenchmarkStorageService(_root);
        var coordinator = new BenchmarkCaptureCoordinator(storage, () => new FakeBackend(storage, BackendMode.Success));
        var game = Game("g1", "Game 1", @"C:\Games\g1.exe", "g1");
        var detector = new BenchmarkGameDetectionService(new FixedProcesses([new(101, "g1", game.ExecutablePath, DateTime.UtcNow)]));
        using var vm = new BenchmarkViewModel(new LocalizationService(new SettingsService()), new FakeRuntime(), storage, detector, () => [game], () => new FakeBackend(storage, BackendMode.Success), () => false, engineProbe: () => (true, "1.0", null), coordinator: coordinator);

        await vm.RefreshGamesAsync();

        var request = new BenchmarkCaptureRequest
        {
            Target = new BenchmarkTarget { LibraryItemId = game.Id, DisplayName = game.DisplayName },
            Process = new BenchmarkProcessIdentity { ProcessId = 101, ProcessName = "g1", ExecutablePath = game.ExecutablePath, StartTimeUtc = DateTime.UtcNow },
            AppVersion = "1.0",
            DurationSeconds = 1,
            CountdownSeconds = 0
        };

        var handle = coordinator.TryStartCapture(request);
        await handle.CompletionTask!;

        Assert.IsTrue(vm.CanStart, "Start command must be available for desktop after external capture finishes.");

        vm.CountdownSeconds = 0;
        await vm.StartAsync();

        Assert.AreEqual(BenchmarkUiState.Completed, vm.State);
    }

    private BenchmarkViewModel Create(BackendMode mode, out FakeBackend backend)
    {
        var storage = new BenchmarkStorageService(_root);
        return Create(storage, mode, out backend);
    }

    private BenchmarkViewModel Create(BenchmarkStorageService storage, BackendMode mode, out FakeBackend backend)
    {
        var createdBackend = new FakeBackend(storage, mode);
        backend = createdBackend;
        var game = new LibraryItem { Id = "game", DisplayName = "Game", Source = LibrarySource.Manual, ExecutablePath = @"C:\Games\game.exe", ProcessName = "game", IsEnabled = true };
        var detector = new BenchmarkGameDetectionService(new FixedProcesses([new(123, "game", @"C:\Games\game.exe", DateTime.UtcNow)]));
        return new BenchmarkViewModel(new LocalizationService(new SettingsService()), new FakeRuntime(), storage, detector, () => [game], () => createdBackend, () => false, engineProbe: () => (true, "2.5.1", null));
    }

    private static BenchmarkSession SaveCompleted(BenchmarkStorageService storage, string gameId, DateTime startUtc, double averageFps)
    {
        BenchmarkSession session = storage.CreateSession(
            new BenchmarkTarget { LibraryItemId = gameId, DisplayName = "Game", LibrarySource = "Manual" },
            new BenchmarkProcessIdentity { ProcessId = 123, ProcessName = "game", StartTimeUtc = startUtc },
            "0.6.0",
            startUtc);
        session.Metadata.Status = BenchmarkSessionStatus.Completed;
        session.Metadata.CaptureDurationSeconds = 30;
        session.Metadata.AnalyzedDurationSeconds = 30;
        storage.SaveSession(session);
        storage.SaveSummary(session, new BenchmarkSummary
        {
            SessionId = session.Metadata.SessionId,
            CaptureDurationSeconds = 30,
            AnalyzedDurationSeconds = 30,
            SelectedSwapChainAddress = "0x1",
            PrimaryPresentedMetrics = new BenchmarkMetricSet { ValidFrameCount = 1, AverageFps = averageFps, OnePercentLowFps = averageFps * .8, PointOnePercentLowFps = averageFps * .7, P99FrameTimeMs = 10 },
            Quality = new BenchmarkQualityResult { Level = BenchmarkQualityLevel.Valid }
        });
        File.WriteAllText(session.RawDataPath, JsonSerializer.Serialize(new[] { new BenchmarkFrameSample { ProcessId = 123, SwapChainAddress = "0x1", MsBetweenPresents = 10 } }));
        return session;
    }

    private static LibraryItem Game(string id, string name, string path, string processName) => new() { Id = id, DisplayName = name, Source = LibrarySource.Manual, ExecutablePath = path, ProcessName = processName, Type = LibraryItemType.Game, IsEnabled = true };

    private sealed class SimpleTestIdentityProvider(DateTime startTime, string processName = "g1", string executablePath = @"C:\Games\g1.exe") : IBenchmarkProcessIdentityProvider
    {
        public BenchmarkProcessIdentity GetCurrentIdentity(int processId, BenchmarkTarget target)
            => new() { ProcessId = processId, ProcessName = processName, ExecutablePath = executablePath, StartTimeUtc = startTime };
    }

    private enum BackendMode { Success, WaitForCancellation, Failure }
    private sealed class ControllableFakeBackend(BenchmarkStorageService storage) : IBenchmarkCaptureBackend
    {
        private readonly TaskCompletionSource _startedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitUntilStartedAsync() => _startedTcs.Task;
        public void Release() => _releaseTcs.TrySetResult();

        public async Task<BenchmarkCaptureResult> CaptureAsync(BenchmarkSession session, CancellationToken cancellationToken = default)
        {
            _startedTcs.TrySetResult();
            await _releaseTcs.Task;

            session.Metadata.Status = BenchmarkSessionStatus.Completed; session.Metadata.CaptureDurationSeconds = 1; session.Metadata.AnalyzedDurationSeconds = 0.016;
            var summary = new BenchmarkSummary { SessionId = session.Metadata.SessionId, CaptureDurationSeconds = 1, AnalyzedDurationSeconds = 0.016, SelectedSwapChainAddress = "0x1", PrimaryPresentedMetrics = new BenchmarkMetricSet { ValidFrameCount = 1, AverageFps = 62.5, OnePercentLowFps = 62.5, PointOnePercentLowFps = 62.5, P99FrameTimeMs = 16 }, Quality = new BenchmarkQualityResult { Level = BenchmarkQualityLevel.Valid } };
            storage.SaveSession(session); storage.SaveSummary(session, summary);
            File.WriteAllText(session.RawDataPath, JsonSerializer.Serialize(new[] { new BenchmarkFrameSample { ProcessId = 123, SwapChainAddress = "0x1", MsBetweenPresents = 16 } }));
            return new BenchmarkCaptureResult { Session = session, Summary = summary };
        }
    }
    private sealed class FakeBackend(BenchmarkStorageService storage, BackendMode mode) : IBenchmarkCaptureBackend
    {
        public int CallCount { get; private set; }
        public async Task<BenchmarkCaptureResult> CaptureAsync(BenchmarkSession session, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (mode == BackendMode.WaitForCancellation) { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            if (mode == BackendMode.Failure) throw new BenchmarkException("capture_failed", "Synthetic failure");
            session.Metadata.Status = BenchmarkSessionStatus.Completed; session.Metadata.CaptureDurationSeconds = 1; session.Metadata.AnalyzedDurationSeconds = 0.016;
            var summary = new BenchmarkSummary { SessionId = session.Metadata.SessionId, CaptureDurationSeconds = 1, AnalyzedDurationSeconds = 0.016, SelectedSwapChainAddress = "0x1", PrimaryPresentedMetrics = new BenchmarkMetricSet { ValidFrameCount = 1, AverageFps = 62.5, OnePercentLowFps = 62.5, PointOnePercentLowFps = 62.5, P99FrameTimeMs = 16 }, Quality = new BenchmarkQualityResult { Level = BenchmarkQualityLevel.Valid } };
            storage.SaveSession(session); storage.SaveSummary(session, summary);
            File.WriteAllText(session.RawDataPath, JsonSerializer.Serialize(new[] { new BenchmarkFrameSample { ProcessId = 123, SwapChainAddress = "0x1", MsBetweenPresents = 16 } }));
            return new BenchmarkCaptureResult { Session = session, Summary = summary };
        }
    }
    private sealed class FixedProcesses(IReadOnlyList<BenchmarkProcessSnapshot> items) : IBenchmarkProcessSnapshotProvider { public IReadOnlyList<BenchmarkProcessSnapshot> GetProcesses() => items; }
    private sealed class FakeRuntime : IBenchmarkRuntimeContext
    {
        public AppSettings Settings { get; } = new();
        public List<ProcessProfile> Profiles { get; } = [];
        public string? LastAppliedProfile => null;
        public IBenchmarkCaptureCoordinator BenchmarkCoordinator { get; } = new BenchmarkCaptureCoordinator();
        public List<string> Activity { get; } = [];
        public void AddActivity(string message, string level = "Info") => Activity.Add(message);
    }
}

[TestClass]
public sealed class BenchmarkPresentationTests
{
    [TestMethod]
    public void HistoryDisplay_IsFriendlyAndNeverUsesClrTypeName()
    {
        var item = new BenchmarkHistoryItemViewModel(CompletedEntry("game", "Game", 120), new LocalizationService(new SettingsService()));
        StringAssert.Contains(item.ComparisonDisplayText, "Game");
        StringAssert.Contains(item.ComparisonDisplayText, "120");
        Assert.IsFalse(item.ComparisonDisplayText.Contains(nameof(BenchmarkHistoryItemViewModel), StringComparison.Ordinal));
    }

    [TestMethod]
    public void FrameTypeWarning_HasEnglishAndPolishPresentationText()
    {
        const string key = "Benchmark.QualityIssue.frame_type_unavailable";
        string english = LocalizationService.Translate(key, "en");
        string polish = LocalizationService.Translate(key, "pl");
        StringAssert.Contains(english, "frame-type");
        StringAssert.Contains(polish, "typu klatki");
        Assert.AreNotEqual(english, polish);
    }

    [TestMethod]
    public void SameSessionComparison_IsRejectedButDistinctSameGameIsAccepted()
    {
        using BenchmarkViewModel vm = CreateComparisonVm();
        var first = new BenchmarkHistoryItemViewModel(CompletedEntry("game", "Game", 100), new LocalizationService(new SettingsService()));
        var second = new BenchmarkHistoryItemViewModel(CompletedEntry("game", "Game", 110), new LocalizationService(new SettingsService()));
        vm.ComparisonA = first;
        vm.ComparisonB = first;
        Assert.AreEqual(0, vm.ComparisonRows.Count);
        Assert.IsTrue(vm.CompareValidationText == LocalizationService.Translate("Benchmark.Compare.SameSession", "en")
            || vm.CompareValidationText == LocalizationService.Translate("Benchmark.Compare.SameSession", "pl"));
        vm.ComparisonB = second;
        Assert.IsTrue(vm.ComparisonRows.Count > 0);
    }

    [TestMethod]
    public void DifferentGameComparison_RemainsRejected()
    {
        using BenchmarkViewModel vm = CreateComparisonVm();
        vm.ComparisonA = new BenchmarkHistoryItemViewModel(CompletedEntry("a", "A", 100), new LocalizationService(new SettingsService()));
        vm.ComparisonB = new BenchmarkHistoryItemViewModel(CompletedEntry("b", "B", 100), new LocalizationService(new SettingsService()));
        Assert.AreEqual(0, vm.ComparisonRows.Count);
    }

    private static BenchmarkViewModel CreateComparisonVm() => new(new LocalizationService(new SettingsService()), new ComparisonRuntime(), engineProbe: () => (false, null, null));
    private static BenchmarkHistoryEntry CompletedEntry(string gameId, string name, double average) => new()
    {
        SessionDirectory = gameId,
        Metadata = new BenchmarkSessionMetadata { SessionId = Guid.NewGuid(), StartUtc = DateTime.UtcNow, Game = new BenchmarkTarget { LibraryItemId = gameId, DisplayName = name, LibrarySource = "Manual" }, Status = BenchmarkSessionStatus.Completed },
        Summary = new BenchmarkSummary { PrimaryPresentedMetrics = new BenchmarkMetricSet { AverageFps = average, OnePercentLowFps = average * .8, P99FrameTimeMs = 10 }, Quality = new BenchmarkQualityResult { Level = BenchmarkQualityLevel.Valid } }
    };
    private sealed class ComparisonRuntime : IBenchmarkRuntimeContext { public AppSettings Settings { get; } = new(); public List<ProcessProfile> Profiles { get; } = []; public string? LastAppliedProfile => null; public IBenchmarkCaptureCoordinator BenchmarkCoordinator { get; } = new BenchmarkCaptureCoordinator(); public void AddActivity(string message, string level = "Info") { } }
}

[TestClass]
public sealed class GlobalBenchmarkHotkeyTests
{
    [TestMethod]
    public void GestureParsingFormatting_RequiresSafeCombination()
    {
        Assert.IsTrue(BenchmarkHotkeyGesture.TryCreate(Key.B, ModifierKeys.Control | ModifierKeys.Shift, out BenchmarkHotkeyGesture gesture));
        Assert.AreEqual("Ctrl + Shift + B", gesture.ToString());
        Assert.IsFalse(BenchmarkHotkeyGesture.TryCreate(Key.LeftCtrl, ModifierKeys.Control, out _));
        Assert.IsFalse(BenchmarkHotkeyGesture.TryCreate(Key.B, ModifierKeys.None, out _));
        Assert.IsTrue(BenchmarkHotkeyGesture.TryCreate(Key.F9, ModifierKeys.None, out _));
    }

    [TestMethod]
    public void SettingsJson_RetainsHotkeyConfiguration()
    {
        var settings = new AppSettings { BenchmarkHotkeyEnabled = true, BenchmarkHotkeyModifiers = (uint)(GlobalHotkeyModifiers.Control | GlobalHotkeyModifiers.Shift), BenchmarkHotkeyVirtualKey = 0x42 };
        AppSettings restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.IsTrue(restored.BenchmarkHotkeyEnabled);
        Assert.AreEqual(settings.BenchmarkHotkeyModifiers, restored.BenchmarkHotkeyModifiers);
        Assert.AreEqual(0x42, restored.BenchmarkHotkeyVirtualKey);
    }

    [TestMethod]
    public void RegistrationSuccess_ChangesAndDisposeUnregisterSafely()
    {
        var native = new FakeHotkeyNative(true);
        var service = new GlobalHotkeyService(new IntPtr(123), native);
        var first = new BenchmarkHotkeyGesture(GlobalHotkeyModifiers.Control, 0x42);
        var second = new BenchmarkHotkeyGesture(GlobalHotkeyModifiers.Alt, 0x43);
        Assert.IsTrue(service.UpdateRegistration(first));
        Assert.IsTrue(service.UpdateRegistration(second));
        Assert.AreEqual(1, native.UnregisterCalls);
        service.Dispose();
        Assert.AreEqual(2, native.UnregisterCalls);
    }

    [TestMethod]
    public void RegistrationFailure_IsTypedStateAndDoesNotAttemptUnregister()
    {
        var native = new FakeHotkeyNative(false);
        using var service = new GlobalHotkeyService(new IntPtr(123), native);
        Assert.IsFalse(service.UpdateRegistration(new BenchmarkHotkeyGesture(GlobalHotkeyModifiers.Control, 0x42)));
        Assert.IsNull(service.RegisteredGesture);
        Assert.AreEqual(0, native.UnregisterCalls);
    }

    private sealed class FakeHotkeyNative(bool result) : IGlobalHotkeyNative
    {
        public int UnregisterCalls { get; private set; }
        public bool Register(IntPtr windowHandle, int id, uint modifiers, uint virtualKey) => result;
        public bool Unregister(IntPtr windowHandle, int id) { UnregisterCalls++; return true; }
    }
}

[TestClass]
public sealed class BenchmarkShellAndLocalizationTests
{
    [TestMethod]
    public void ImportantBenchmarkLocalizationKeysExistInBothLanguages()
    {
        string[] required = ["Nav.Benchmarks", "Benchmark.Tab.Capture", "Benchmark.Tab.History", "Benchmark.Tab.Compare", "Benchmark.Error.EngineUnavailable", "Settings.BenchmarkEngine.Description", "Settings.BenchmarkHotkey.Title", "Settings.BenchmarkHotkey.Conflict", "Benchmark.Hotkey.Ambiguous", "Benchmark.Compare.SameSession"];
        foreach (string key in required)
        {
            Assert.IsTrue(LocalizationService.EnglishKeys.Contains(key), $"Missing English key {key}");
            Assert.IsTrue(LocalizationService.PolishKeys.Contains(key), $"Missing Polish key {key}");
        }
    }

    [TestMethod]
    public void BenchmarksNavigation_IsInGamingGroupAfterSessionOptimization()
    {
        using var shell = new ShellViewModel();
        CollectionAssert.AreEqual(new[] { "Library", "Session", "Benchmarks" }, shell.GamingGroupItems.Select(item => item.Key).ToArray());
        Assert.AreEqual("Logs", shell.SystemGroupItems.Single().Key);
        Assert.AreEqual("Settings", shell.SettingsNavigationItem.Key);
    }

    [TestMethod]
    public void ReadOnlyBenchmarkOutputBindings_AreOneWayAndActivateOnSta()
    {
        Exception? failure = null;
        BindingMode? progressMode = null;
        BindingMode? diagnosticsMode = null;
        IReadOnlyList<string>? activeFontSources = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current == null)
                {
                    var application = new FrameHub.App.App();
                    application.InitializeComponent();
                }
                else
                {
                    var appDictUri = new Uri("pack://application:,,,/FrameHub.App;component/App.xaml", UriKind.Absolute);
                    if (!Application.Current.Resources.MergedDictionaries.Any(d => d.Source == appDictUri))
                    {
                        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = appDictUri });
                    }
                }
                var view = new BenchmarkView { DataContext = new ReadOnlyBenchmarkOutputs() };
                view.Measure(new Size(1200, 800));
                view.Arrange(new Rect(0, 0, 1200, 800));
                view.UpdateLayout();

                var progress = (ProgressBar)view.FindName("CaptureProgressBar");
                var diagnostics = (TextBox)view.FindName("DiagnosticsTextBox");
                progressMode = BindingOperations.GetBinding(progress, System.Windows.Controls.Primitives.RangeBase.ValueProperty)?.Mode;
                diagnosticsMode = BindingOperations.GetBinding(diagnostics, TextBox.TextProperty)?.Mode;

                string[] fontResourceKeys =
                [
                    "FrameHubFontFamily", "FrameHubFontSemiBold", "FrameHubFontBold", "FrameHubFontExtraBold",
                    "FrameHubDisplayFontFamily", "FrameHubMetricFontFamily"
                ];
                activeFontSources = fontResourceKeys
                    .Select(key =>
                    {
                        object? res = Application.Current?.Dispatcher.Invoke(() => Application.Current.TryFindResource(key))
                            ?? view.TryFindResource(key)
                            ?? view.TryFindResource("FontMetric")
                            ?? view.TryFindResource("FrameHubFontFamily");
                        return (res as System.Windows.Media.FontFamily
                            ?? throw new InvalidOperationException($"Missing font resource: {key}")).Source;
                    })
                    .ToArray();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)), "Benchmark view binding activation timed out.");
        if (failure is not null) Assert.Fail(failure.ToString());
        Assert.AreEqual(BindingMode.OneWay, progressMode);
        Assert.AreEqual(BindingMode.OneWay, diagnosticsMode);
        Assert.IsNotNull(activeFontSources);
        Assert.IsTrue(activeFontSources.All(source => source == "Segoe UI"), "All active FrameHub typography resources must use Segoe UI.");
    }

    [TestMethod]
    public void SettingsView_MaterializesWithoutXamlParseException()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current == null)
                {
                    var application = new FrameHub.App.App();
                    application.InitializeComponent();
                }
                var view = new SettingsView();
                view.Measure(new Size(1200, 800));
                view.Arrange(new Rect(0, 0, 1200, 800));
                view.UpdateLayout();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)), "SettingsView materialization test timed out.");
        if (failure is not null) Assert.Fail($"SettingsView materialization failed: {failure}");
    }

    [TestMethod]
    public void LogsView_MaterializesWithoutXamlParseException()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current == null)
                {
                    var application = new FrameHub.App.App();
                    application.InitializeComponent();
                }
                var view = new LogsView
                {
                    DataContext = new DummyLogsViewModel
                    {
                        Activity = [new ActivityItemViewModel { Time = "12:00", Message = "Test log line", Level = "Info" }]
                    }
                };
                view.Measure(new Size(1200, 800));
                view.Arrange(new Rect(0, 0, 1200, 800));
                view.UpdateLayout();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)), "LogsView materialization test timed out.");
        if (failure is not null) Assert.Fail($"LogsView materialization failed: {failure}");
    }

    [TestMethod]
    public void AllNavigationViews_MaterializeWithoutXamlParseException()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current == null)
                {
                    var application = new FrameHub.App.App();
                    application.InitializeComponent();
                }

                UserControl[] views =
                [
                    new DashboardView(),
                    new LibraryView(),
                    new SessionOptimizationView(),
                    new BenchmarkView(),
                    new ProcessesView(),
                    new ProfilesView(),
                    new HardwareView(),
                    new LogsView
                    {
                        DataContext = new DummyLogsViewModel
                        {
                            Activity = [new ActivityItemViewModel { Time = "12:00", Message = "Test log line", Level = "Info" }]
                        }
                    },
                    new SettingsView()
                ];

                foreach (var view in views)
                {
                    view.Measure(new Size(1200, 800));
                    view.Arrange(new Rect(0, 0, 1200, 800));
                    view.UpdateLayout();
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)), "AllNavigationViews materialization test timed out.");
        if (failure is not null) Assert.Fail($"AllNavigationViews materialization failed: {failure}");
    }

    private sealed class DummyLogsViewModel
    {
        public System.Collections.ObjectModel.ObservableCollection<ActivityItemViewModel> Activity { get; init; } = new();
    }

    private sealed class ReadOnlyBenchmarkOutputs
    {
        public double ProgressValue => 42;
        public string TechnicalError => "diagnostic";
    }
}
