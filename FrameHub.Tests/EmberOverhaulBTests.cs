using System.IO;
using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.App.ViewModels.Benchmark;
using FrameHub.App.ViewModels.Library;
using FrameHub.Core.Models;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Benchmarking;
using FrameHub.Core.Services.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

/// <summary>
/// UI overhaul part B (Ember): library search/filter, dashboard previews, benchmark
/// honest engine messaging and send-to-compare. All process state is synthetic;
/// the Top-CPU preview test only observes (never mutates) real processes.
/// </summary>
[TestClass]
public sealed class EmberOverhaulBTests
{
    private string _tempDirectory = null!;
    private List<ProcessObservation> _processes = null!;
    private long _observationClockTicks;

    private const string LeaguePath = @"C:\Riot Games\League of Legends\Game\League of Legends.exe";
    private const string Cs2Path = @"C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive\game\bin\win64\cs2.exe";

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.EmberBTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _processes = new List<ProcessObservation>();
        _observationClockTicks = 0;
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { }
        }
    }

    [TestMethod]
    public void LibraryFilter_Search_NarrowsAndRestores()
    {
        using var host = CreateLibraryHost();
        var vm = host.ViewModel;
        Assert.AreEqual(2, vm.FilteredItems.Count);

        vm.SearchText = "counter";
        Assert.AreEqual(1, vm.FilteredItems.Count);
        Assert.AreEqual("cs2", vm.FilteredItems[0].Id);

        vm.SearchText = string.Empty;
        Assert.AreEqual(2, vm.FilteredItems.Count);
    }

    [TestMethod]
    public void LibraryFilter_MissingExe_SelectsOnlyBrokenEntries()
    {
        using var host = CreateLibraryHost();
        var vm = host.ViewModel;
        vm.SelectedFilterIndex = 4;
        Assert.IsTrue(vm.FilteredItems.Count >= 1, "Synthetic exe paths do not exist on disk, so the missing-file filter must match.");
        Assert.IsTrue(vm.FilteredItems.All(item => item.IsExecutableMissing));
    }

    [TestMethod]
    public void LibraryFilter_Cs2_UsesShortcutIdentity()
    {
        using var host = CreateLibraryHost();
        var vm = host.ViewModel;
        vm.SelectedFilterIndex = 3;
        Assert.IsTrue(vm.FilteredItems.Any(item => item.Id == "cs2"));
        Assert.IsFalse(vm.FilteredItems.Any(item => item.Id == "riot-lol"), "A Riot shortcut entry must not match the CS2 filter.");
    }

    [TestMethod]
    public async Task LibraryFilter_Running_ReflectsRuntimeState()
    {
        using var host = await CreateLibraryHostAsync();
        var vm = host.ViewModel;
        lock (_processes) _processes.Add(new ProcessObservation(4242, "cs2", Cs2Path, DateTime.UtcNow));
        vm.Activate();
        await WaitUntilAsync(() => vm.Items.First(item => item.Id == "cs2").IsRunning);

        vm.SelectedFilterIndex = 2;
        Assert.AreEqual(1, vm.FilteredItems.Count);
        Assert.AreEqual("cs2", vm.FilteredItems[0].Id);
    }

    [TestMethod]
    public void Dashboard_RecentActivity_CappedAtThree()
    {
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        using var runtime = new AppRuntimeService(settingsPath);
        var localization = new LocalizationService(runtime.SettingsService);
        using var vm = new DashboardViewModel(localization, runtime);

        for (int i = 0; i < 5; i++) runtime.AddActivity($"Synthetic activity {i}", "Info");

        Assert.AreEqual(3, vm.RecentActivity.Count);
        StringAssert.Contains(vm.RecentActivity[0].Message, "Synthetic activity 4");
        StringAssert.Contains(vm.RecentActivityCountText, vm.Activity.Count.ToString(System.Globalization.CultureInfo.CurrentCulture));
    }

    [TestMethod]
    public async Task Dashboard_TopProcesses_BoundedToThree()
    {
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        using var runtime = new AppRuntimeService(settingsPath);
        var localization = new LocalizationService(runtime.SettingsService);
        using var vm = new DashboardViewModel(localization, runtime);

        await vm.RefreshTopProcessesAsync();
        PumpDispatcher();
        Assert.IsTrue(vm.TopProcesses.Count <= 3, "Preview shows at most three rows; the full table lives in Processes & CPU.");
    }

    [TestMethod]
    public void Benchmark_SendToCompare_PresetsSelectedAndOpensCompareTab()
    {
        var storage = new BenchmarkStorageService(_tempDirectory);
        using var vm = new BenchmarkViewModel(new LocalizationService(new SettingsService()), new EmberFakeRuntime(), storage, engineProbe: () => (false, null, "synthetic"));
        var first = new BenchmarkHistoryItemViewModel(CompletedEntry("game", "Game", 100), new LocalizationService(new SettingsService()));
        var second = new BenchmarkHistoryItemViewModel(CompletedEntry("game", "Game", 110), new LocalizationService(new SettingsService()));
        vm.History.Add(first);
        vm.History.Add(second);
        vm.SelectedHistory = first;

        Assert.IsTrue(vm.SendToCompareCommand.CanExecute(null));
        vm.SendToCompareCommand.Execute(null);

        Assert.AreSame(first, vm.ComparisonA);
        Assert.AreEqual(2, vm.SelectedTabIndex);
    }

    [TestMethod]
    public void Benchmark_EngineHint_NeverSuggestsReinstall()
    {
        var storage = new BenchmarkStorageService(_tempDirectory);
        using var vm = new BenchmarkViewModel(new LocalizationService(new SettingsService()), new EmberFakeRuntime(), storage, engineProbe: () => (false, null, "synthetic"));

        Assert.IsFalse(vm.EngineReady);
        StringAssert.Contains(vm.EngineStatus.ToLowerInvariant(), "presentmon");
        Assert.IsFalse(vm.EngineStatus.Contains("reinstall", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(vm.EngineStatus.Contains("napraw", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(vm.EngineHint.Contains("reinstall", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(vm.EngineHint, "PresentMon");
    }

    [TestMethod]
    public void EmberNewKeys_HaveEnglishAndPolish()
    {
        string[] keys =
        [
            "Dashboard.Benchmark.EngineDown", "Dashboard.Hero.ScanLibrary", "Dashboard.Hero.HowItWorks",
            "Dashboard.Hero.NoGamesDetail", "Dashboard.TopProcesses.Title", "Dashboard.TopProcesses.Hint",
            "Dashboard.TopProcesses.Empty", "Dashboard.TopProcesses.Open", "Dashboard.TopProcesses.RowDetail",
            "Dashboard.OpenLogs", "Dashboard.RecentActivity.Count", "Dashboard.Monitor.IdleBadge",
            "Shell.Session.Pill", "Shell.Footer.PresentMon", "Shell.Footer.License", "Shell.ToggleNav",
            "Shell.About", "Shell.About.Title", "Shell.About.Message",
            "Library.ScanMenu", "Library.AddManually", "Library.SearchPlaceholder", "Library.FilterLabel",
            "Library.Filter.All", "Library.Filter.Ready", "Library.Filter.Running", "Library.Filter.Cs2",
            "Library.Filter.MissingExe", "Library.EmptyFiltered", "Library.SourcesNote",
            "Session.Steps.Title", "Session.Steps.Configure", "Session.Steps.Preview", "Session.Steps.Start",
            "Session.Steps.Restore", "Session.Footer.RestoreReady", "Session.Footer.RestoreIdle", "Session.Start.NeedsGame",
            "Benchmark.Engine.ReadyHint", "Benchmark.Engine.NoService",
            "Benchmark.Steps.Title", "Benchmark.Steps.Game", "Benchmark.Steps.Engine", "Benchmark.Steps.Config",
            "Benchmark.Steps.Start", "Benchmark.History.SendToCompare", "Benchmark.History.ExportNote"
        ];
        foreach (string key in keys)
        {
            Assert.AreNotEqual(key, LocalizationService.Translate(key, "en"), $"Missing English key: {key}");
            Assert.AreNotEqual(key, LocalizationService.Translate(key, "pl"), $"Missing Polish key: {key}");
        }
    }

    private static BenchmarkHistoryEntry CompletedEntry(string gameId, string name, double average) => new()
    {
        SessionDirectory = gameId,
        Metadata = new BenchmarkSessionMetadata { SessionId = Guid.NewGuid(), StartUtc = DateTime.UtcNow, Game = new BenchmarkTarget { LibraryItemId = gameId, DisplayName = name, LibrarySource = "Manual" }, Status = BenchmarkSessionStatus.Completed },
        Summary = new BenchmarkSummary { PrimaryPresentedMetrics = new BenchmarkMetricSet { AverageFps = average, OnePercentLowFps = average * .8, P99FrameTimeMs = 10 }, Quality = new BenchmarkQualityResult { Level = BenchmarkQualityLevel.Valid } }
    };

    private sealed class EmberFakeRuntime : IBenchmarkRuntimeContext
    {
        public AppSettings Settings { get; } = new();
        public List<ProcessProfile> Profiles { get; } = [];
        public string? LastAppliedProfile => null;
        public IBenchmarkCaptureCoordinator BenchmarkCoordinator { get; } = new BenchmarkCaptureCoordinator();
        public void AddActivity(string message, string level = "Info") { }
    }

    private sealed record LibraryHost(LibraryViewModel ViewModel, EmberFakeLaunchService Launcher) : IDisposable
    {
        public void Dispose() => ViewModel.Dispose();
    }

    private sealed class EmberFakeLaunchService : IAppLibraryLaunchService
    {
        public LibraryLaunchResult NextResult { get; set; } = LibraryLaunchResult.Ok();
        public int LaunchCalls { get; private set; }

        public LibraryLaunchResult Launch(LibraryItem? item)
        {
            LaunchCalls++;
            return NextResult;
        }
    }

    private LibraryHost CreateLibraryHost()
    {
        string libraryPath = Path.Combine(_tempDirectory, $"library-{Guid.NewGuid():N}.json");
        var libraryService = new LibraryService(libraryPath);
        libraryService.SaveItems(new[]
        {
            new LibraryItem
            {
                Id = "riot-lol",
                DisplayName = "League of Legends",
                Source = LibrarySource.Riot,
                Type = LibraryItemType.Game,
                ProcessName = "League of Legends",
                ExecutablePath = LeaguePath,
                LaunchPath = @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Riot Games\League of Legends.lnk",
                AllowBenchmark = false
            },
            new LibraryItem
            {
                Id = "cs2",
                DisplayName = "Counter-Strike 2",
                Type = LibraryItemType.Game,
                ProcessName = "cs2",
                ExecutablePath = Cs2Path
            }
        });

        var observationProvider = new ProcessObservationSnapshotProvider(
            timeToLive: TimeSpan.Zero,
            enumerate: () => { lock (_processes) return _processes.ToList(); },
            clock: () => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(Interlocked.Increment(ref _observationClockTicks)));
        var scanner = new ProcessScannerService(new ProcessService(), observationProvider);
        var launcher = new EmberFakeLaunchService();

        string settingsPath = Path.Combine(_tempDirectory, $"settings-{Guid.NewGuid():N}.json");
        var runtime = new AppRuntimeService(settingsPath);
        var localization = new LocalizationService(runtime.SettingsService);
        var viewModel = new LibraryViewModel(localization, runtime, libraryService, scanner, launcher);
        return new LibraryHost(viewModel, launcher);
    }

    private async Task<LibraryHost> CreateLibraryHostAsync()
    {
        var host = CreateLibraryHost();
        try
        {
            var provider = new ProcessObservationSnapshotProvider(
                timeToLive: TimeSpan.Zero,
                enumerate: () => { lock (_processes) return _processes.ToList(); },
                clock: () => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(Interlocked.Increment(ref _observationClockTicks)));
            await provider.GetSnapshotAsync();
            return host;
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 300 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.IsTrue(condition(), "Expected runtime-state condition was not reached within the test timeout.");
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new System.Windows.Threading.DispatcherOperationCallback(_ => { frame.Continue = false; return null; }),
            null);
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}
