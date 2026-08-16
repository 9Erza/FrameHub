using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using FrameHub.Core.Models;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Models.SessionOptimization;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Benchmarking;
using FrameHub.Core.Services.Library;
using FrameHub.Core.Services.SessionOptimization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public sealed class RiotComplianceTests
{
    private string _tempDirectory = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.RiotTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { }
        }
    }

    private string CreateShortcut(string directory, string fileName, string targetPath, string arguments)
    {
        Directory.CreateDirectory(directory);
        string shortcutPath = Path.Combine(directory, fileName);

        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
        {
            Assert.Inconclusive("WScript.Shell COM is unavailable; shortcut-based discovery cannot be tested.");
        }

        object shell = Activator.CreateInstance(shellType!)!;
        object shortcut = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [shortcutPath])!;
        shortcut.GetType().InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, [targetPath]);
        shortcut.GetType().InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, [arguments]);
        shortcut.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);

        return shortcutPath;
    }

    private (string InstallRoot, string StartMenuPrograms) CreateRiotLayout()
    {
        string installRoot = Path.Combine(_tempDirectory, "RiotRoot");
        Directory.CreateDirectory(Path.Combine(installRoot, "Riot Client"));
        string startMenuPrograms = Path.Combine(_tempDirectory, "StartMenu", "Programs");
        return (installRoot, startMenuPrograms);
    }

    [TestMethod]
    public void Scanner_DiscoversLeagueOfLegends_ThroughOfficialShortcut()
    {
        (string installRoot, string startMenuPrograms) = CreateRiotLayout();
        string gameExe = Path.Combine(installRoot, "League of Legends", "Game", "League of Legends.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(gameExe)!);
        File.WriteAllText(gameExe, "dummy");
        string riotClientExe = Path.Combine(installRoot, "Riot Client", "RiotClientServices.exe");
        string shortcut = CreateShortcut(
            Path.Combine(startMenuPrograms, "Riot Games"),
            "League of Legends.lnk",
            riotClientExe,
            "--launch-product=league_of_legends --launch-patchline=live");

        var scanner = new RiotLibraryScanner(startMenuRoots: [startMenuPrograms]);
        var result = scanner.Scan();

        Assert.AreEqual(1, result.Items.Count, "Exactly one League of Legends item must be discovered.");
        LibraryItem item = result.Items[0];
        Assert.AreEqual("League of Legends", item.DisplayName);
        Assert.AreEqual(LibrarySource.Riot, item.Source);
        Assert.AreEqual(LibraryItemType.Game, item.Type);
        Assert.AreEqual("league_of_legends", item.AppId);
        Assert.AreEqual(shortcut, item.LaunchPath, "Launch entry must be the official Riot-created shortcut.");
        Assert.AreEqual(gameExe, item.ExecutablePath, "ExecutablePath must anchor the actual game process identity.");
        Assert.AreEqual("League of Legends", item.ProcessName);
        Assert.IsFalse(item.AllowBenchmark, "Riot games must not implicitly enable benchmark capture.");
        Assert.IsFalse(item.AllowRemoteControl, "Riot games must not be remote-controllable background apps.");
    }

    [TestMethod]
    public void Scanner_DiscoversValorant_WithActualGameProcessIdentity()
    {
        (string installRoot, string startMenuPrograms) = CreateRiotLayout();
        string gameExe = Path.Combine(installRoot, "VALORANT", "ShooterGame", "Binaries", "Win64", "VALORANT-Win64-Shipping.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(gameExe)!);
        File.WriteAllText(gameExe, "dummy");
        string riotClientExe = Path.Combine(installRoot, "Riot Client", "RiotClientServices.exe");
        CreateShortcut(
            Path.Combine(startMenuPrograms, "Riot Games"),
            "VALORANT.lnk",
            riotClientExe,
            "--launch-product=valorant --launch-patchline=live");

        var scanner = new RiotLibraryScanner(startMenuRoots: [startMenuPrograms]);
        var result = scanner.Scan();

        Assert.AreEqual(1, result.Items.Count);
        LibraryItem item = result.Items[0];
        Assert.AreEqual("VALORANT", item.DisplayName);
        Assert.AreEqual("VALORANT-Win64-Shipping", item.ProcessName, "Process identity must be the actual game executable, not the launcher.");
        Assert.AreEqual(gameExe, item.ExecutablePath);
        Assert.IsFalse(item.AllowBenchmark);
    }

    [TestMethod]
    public void Scanner_IgnoresBareRiotClientAndNonRiotShortcuts()
    {
        (string installRoot, string startMenuPrograms) = CreateRiotLayout();
        string riotClientExe = Path.Combine(installRoot, "Riot Client", "RiotClientServices.exe");
        CreateShortcut(Path.Combine(startMenuPrograms, "Riot Games"), "Riot Client.lnk", riotClientExe, "");
        CreateShortcut(startMenuPrograms, "Notepad.lnk", Path.Combine(_tempDirectory, "notepad.exe"), "");

        var scanner = new RiotLibraryScanner(startMenuRoots: [startMenuPrograms]);
        var result = scanner.Scan();

        Assert.AreEqual(0, result.Items.Count, "The launcher itself and non-Riot shortcuts must not become Library items.");
    }

    [TestMethod]
    public void Scanner_SkipsUnsupportedProductsWithWarning()
    {
        (string installRoot, string startMenuPrograms) = CreateRiotLayout();
        string riotClientExe = Path.Combine(installRoot, "Riot Client", "RiotClientServices.exe");
        CreateShortcut(
            Path.Combine(startMenuPrograms, "Riot Games"),
            "Teamfight Tactics.lnk",
            riotClientExe,
            "--launch-product=teamfighttactics --launch-patchline=live");

        var scanner = new RiotLibraryScanner(startMenuRoots: [startMenuPrograms]);
        var result = scanner.Scan();

        Assert.AreEqual(0, result.Items.Count, "Products with colliding game process identity must be skipped conservatively.");
        Assert.IsTrue(result.Warnings.Any(w => w.Contains("teamfighttactics", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Scanner_MissingGameExecutable_StillAddsItemWithNameIdentityAndWarning()
    {
        (string installRoot, string startMenuPrograms) = CreateRiotLayout();
        string riotClientExe = Path.Combine(installRoot, "Riot Client", "RiotClientServices.exe");
        CreateShortcut(
            Path.Combine(startMenuPrograms, "Riot Games"),
            "League of Legends.lnk",
            riotClientExe,
            "--launch-product=league_of_legends --launch-patchline=live");

        var scanner = new RiotLibraryScanner(startMenuRoots: [startMenuPrograms]);
        var result = scanner.Scan();

        Assert.AreEqual(1, result.Items.Count);
        Assert.IsNull(result.Items[0].ExecutablePath, "Unverifiable install layout must fall back to name-based identity only.");
        Assert.AreEqual("League of Legends", result.Items[0].ProcessName);
        Assert.IsTrue(result.Warnings.Any(w => w.Contains("name-based", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Scanner_DuplicateShortcutsForSameProduct_ProduceSingleItem()
    {
        (string installRoot, string startMenuPrograms) = CreateRiotLayout();
        string riotClientExe = Path.Combine(installRoot, "Riot Client", "RiotClientServices.exe");
        string folder = Path.Combine(startMenuPrograms, "Riot Games");
        CreateShortcut(folder, "League of Legends.lnk", riotClientExe, "--launch-product=league_of_legends --launch-patchline=live");
        CreateShortcut(folder, "Klient League of Legends.lnk", riotClientExe, "--launch-product=league_of_legends --launch-patchline=live");

        var scanner = new RiotLibraryScanner(startMenuRoots: [startMenuPrograms]);
        var result = scanner.Scan();

        Assert.AreEqual(1, result.Items.Count);
    }

    [TestMethod]
    public void LibraryService_PreservesRiotLaunchPathAndStickyBenchmarkExclusion()
    {
        string libraryPath = Path.Combine(_tempDirectory, "library.json");
        var service = new LibraryService(libraryPath);
        var item = new LibraryItem
        {
            DisplayName = "League of Legends",
            Source = LibrarySource.Riot,
            Type = LibraryItemType.Game,
            AppId = "league_of_legends",
            ProcessName = "League of Legends",
            LaunchPath = Path.Combine(_tempDirectory, "League of Legends.lnk"),
            AllowBenchmark = false
        };

        service.SaveItems([item]);

        List<LibraryItem> loaded = service.LoadItems();
        Assert.AreEqual(1, loaded.Count);
        Assert.AreEqual(item.LaunchPath, loaded[0].LaunchPath, "LaunchPath must survive sanitize/persist round-trip.");
        Assert.IsFalse(loaded[0].AllowBenchmark, "AllowBenchmark=false must survive persistence.");

        var rescan = new LibraryItem
        {
            DisplayName = "League of Legends",
            Source = LibrarySource.Riot,
            AppId = "league_of_legends",
            ProcessName = "League of Legends",
            AllowBenchmark = false
        };
        List<LibraryItem> merged = service.MergeItems(loaded, [rescan]);
        Assert.AreEqual(1, merged.Count);
        Assert.AreEqual(item.LaunchPath, merged[0].LaunchPath, "Merge must keep the trusted launch entry.");
        Assert.IsFalse(merged[0].AllowBenchmark, "Merge must keep Riot benchmark exclusion sticky.");
    }

    [TestMethod]
    public void LegacyLibraryJson_WithoutRiotFields_DefaultsToBenchmarkAllowed()
    {
        string libraryPath = Path.Combine(_tempDirectory, "legacy-library.json");
        File.WriteAllText(libraryPath, """
        [
            { "Id": "classic", "DisplayName": "Classic Game", "Source": 0, "Type": 0, "ExecutablePath": "C:\\games\\classic.exe" }
        ]
        """);

        List<LibraryItem> loaded = new LibraryService(libraryPath).LoadItems();

        Assert.AreEqual(1, loaded.Count);
        Assert.IsTrue(loaded[0].AllowBenchmark, "Existing items keep benchmark eligibility after the schema extension.");
        Assert.IsNull(loaded[0].LaunchPath);
    }

    [TestMethod]
    public void BenchmarkDetection_ExcludesRiotItems_EvenWhenRunning()
    {
        string gamePath = Path.GetFullPath(Path.Combine(_tempDirectory, "League of Legends.exe"));
        var provider = new FakeProcessProvider(new BenchmarkProcessSnapshot(4242, "League of Legends", gamePath, DateTime.UtcNow));
        var detector = new BenchmarkGameDetectionService(provider);
        var riotItem = new LibraryItem
        {
            Id = "riot-lol",
            DisplayName = "League of Legends",
            Source = LibrarySource.Riot,
            Type = LibraryItemType.Game,
            ProcessName = "League of Legends",
            ExecutablePath = gamePath,
            AllowBenchmark = false
        };
        var regularItem = new LibraryItem
        {
            Id = "regular",
            DisplayName = "Regular",
            Type = LibraryItemType.Game,
            ProcessName = "othergame",
            ExecutablePath = Path.GetFullPath(Path.Combine(_tempDirectory, "othergame.exe")),
            AllowBenchmark = true
        };

        IReadOnlyList<BenchmarkRunningGame> detected = detector.Detect([riotItem, regularItem]);

        Assert.AreEqual(0, detected.Count, "Riot games must never become benchmark or live-PresentMon targets.");
    }

    [TestMethod]
    public void ActiveGameDetection_IncludesRiotItem_WhileBenchmarkDetectionExcludesIt()
    {
        string leaguePath = Path.GetFullPath(Path.Combine(_tempDirectory, "League of Legends.exe"));
        string cs2Path = Path.GetFullPath(Path.Combine(_tempDirectory, "cs2.exe"));
        var provider = new FakeProcessProvider(
            new BenchmarkProcessSnapshot(4242, "League of Legends", leaguePath, DateTime.UtcNow),
            new BenchmarkProcessSnapshot(5252, "cs2", cs2Path, DateTime.UtcNow));
        var detector = new BenchmarkGameDetectionService(provider);
        var riotItem = new LibraryItem
        {
            Id = "riot-lol",
            DisplayName = "League of Legends",
            Source = LibrarySource.Riot,
            Type = LibraryItemType.Game,
            ProcessName = "League of Legends",
            ExecutablePath = leaguePath,
            AllowBenchmark = false
        };
        var cs2Item = new LibraryItem
        {
            Id = "cs2",
            DisplayName = "Counter-Strike 2",
            Type = LibraryItemType.Game,
            ProcessName = "cs2",
            ExecutablePath = cs2Path,
            AllowBenchmark = true
        };

        IReadOnlyList<BenchmarkRunningGame> active = detector.DetectActiveGames([riotItem, cs2Item]);
        IReadOnlyList<BenchmarkRunningGame> eligible = detector.Detect([riotItem, cs2Item]);

        Assert.AreEqual(2, active.Count, "A running Riot game must remain visible as an active game.");
        Assert.IsTrue(active.Any(g => g.LibraryItem.Id == "riot-lol"));
        Assert.IsTrue(active.Any(g => g.LibraryItem.Id == "cs2"));

        Assert.AreEqual(1, eligible.Count, "Only the benchmark-eligible game may be a benchmark target.");
        Assert.AreEqual("cs2", eligible[0].LibraryItem.Id, "Riot games must never become benchmark or live-PresentMon targets.");
    }

    [TestMethod]
    public void ActiveGameDetection_LauncherProcessesOnly_DoNotReportLeagueRunning()
    {
        var provider = new FakeProcessProvider(
            new BenchmarkProcessSnapshot(900, "RiotClientServices", Path.GetFullPath(Path.Combine(_tempDirectory, "RiotClientServices.exe")), DateTime.UtcNow),
            new BenchmarkProcessSnapshot(901, "LeagueClient", Path.GetFullPath(Path.Combine(_tempDirectory, "LeagueClient.exe")), DateTime.UtcNow),
            new BenchmarkProcessSnapshot(902, "LeagueClientUx", Path.GetFullPath(Path.Combine(_tempDirectory, "LeagueClientUx.exe")), DateTime.UtcNow));
        var detector = new BenchmarkGameDetectionService(provider);
        var riotItem = new LibraryItem
        {
            Id = "riot-lol",
            DisplayName = "League of Legends",
            Source = LibrarySource.Riot,
            Type = LibraryItemType.Game,
            ProcessName = "League of Legends",
            ExecutablePath = Path.GetFullPath(Path.Combine(_tempDirectory, "League of Legends.exe")),
            AllowBenchmark = false
        };

        IReadOnlyList<BenchmarkRunningGame> active = detector.DetectActiveGames([riotItem]);
        IReadOnlyList<BenchmarkRunningGame> eligible = detector.Detect([riotItem]);

        Assert.AreEqual(0, active.Count, "Riot Client or League client processes alone must never count as the game running.");
        Assert.AreEqual(0, eligible.Count);
    }

    [TestMethod]
    public void BenchmarkDetection_ManualItemWithProtectedRiotName_NeverBenchmarkEligible()
    {
        var provider = new FakeProcessProvider(
            new BenchmarkProcessSnapshot(5000, "League of Legends", null, DateTime.UtcNow));
        var detector = new BenchmarkGameDetectionService(provider);
        var manualItem = new LibraryItem
        {
            Id = "manual-lol",
            DisplayName = "My League shortcut",
            Type = LibraryItemType.Game,
            ProcessName = "League of Legends",
            AllowBenchmark = true
        };

        IReadOnlyList<BenchmarkRunningGame> active = detector.DetectActiveGames([manualItem]);
        IReadOnlyList<BenchmarkRunningGame> eligible = detector.Detect([manualItem]);

        Assert.AreEqual(1, active.Count, "A manual item matching a protected Riot game process may stay visible as an active game.");
        Assert.AreEqual("manual-lol", active[0].LibraryItem.Id);
        Assert.AreEqual(0, eligible.Count, "A protected Riot identity must never be benchmark eligible, even with AllowBenchmark == true.");
    }

    [TestMethod]
    public void BenchmarkDetection_ManualItemWithProtectedRiotExecutablePath_NeverBenchmarkEligible()
    {
        string leaguePath = Path.GetFullPath(Path.Combine(_tempDirectory, "League of Legends.exe"));
        var provider = new FakeProcessProvider(
            new BenchmarkProcessSnapshot(5001, "League of Legends", leaguePath, DateTime.UtcNow));
        var detector = new BenchmarkGameDetectionService(provider);
        var manualItem = new LibraryItem
        {
            Id = "manual-lol-path",
            DisplayName = "My League launcher entry",
            Type = LibraryItemType.Game,
            ProcessName = "customlaunchername",
            ExecutablePath = leaguePath,
            AllowBenchmark = true
        };

        IReadOnlyList<BenchmarkRunningGame> active = detector.DetectActiveGames([manualItem]);
        IReadOnlyList<BenchmarkRunningGame> eligible = detector.Detect([manualItem]);

        Assert.AreEqual(1, active.Count, "The path-matched manual item stays visible as an active game.");
        Assert.AreEqual(0, eligible.Count, "A protected Riot executable path must never be benchmark eligible, even with AllowBenchmark == true.");
    }

    [TestMethod]
    public void Optimization_ProtectsRiotProcesses_FromProfileMutation()
    {
        var optimization = new OptimizationService(new ProcessService(), () => new Dictionary<int, uint>());
        var profile = new ProcessProfile { ProcessName = "League of Legends", IsEnabled = true };
        var snapshot = new ProcessGroupSnapshot
        {
            Name = "League of Legends",
            Instances = [new ProcessInstanceKey(4242, DateTime.UtcNow)]
        };

        var result = optimization.ApplyProfilesForSnapshots([profile], [snapshot], allowRealtimePriority: false, force: false);

        Assert.AreEqual(1, result.Results.Count);
        Assert.IsFalse(result.Results[0].Success);
        Assert.AreEqual("SKIPPED_PROTECTED_RIOT", result.Results[0].Message, "Riot processes must be skipped before any native mutation call.");
    }

    [TestMethod]
    public void SuspendCandidates_NeverIncludeRiotProcesses()
    {
        var suspendService = new ProcessSuspendService();
        var start = DateTime.UtcNow;
        var snapshot = new SessionProcessSnapshot
        {
            Processes =
            [
                new SessionProcessSnapshotItem { ProcessId = 100, ProcessName = "League of Legends", NormalizedProcessName = "League of Legends", ProcessStartTimeUtc = start },
                new SessionProcessSnapshotItem { ProcessId = 101, ProcessName = "LeagueClientUx", NormalizedProcessName = "LeagueClientUx", ProcessStartTimeUtc = start },
                new SessionProcessSnapshotItem { ProcessId = 102, ProcessName = "RiotClientServices", NormalizedProcessName = "RiotClientServices", ProcessStartTimeUtc = start },
                new SessionProcessSnapshotItem { ProcessId = 103, ProcessName = "vgtray", NormalizedProcessName = "vgtray", ProcessStartTimeUtc = start },
                new SessionProcessSnapshotItem { ProcessId = 104, ProcessName = "VALORANT-Win64-Shipping", NormalizedProcessName = "VALORANT-Win64-Shipping", ProcessStartTimeUtc = start },
                new SessionProcessSnapshotItem { ProcessId = 105, ProcessName = "somebackgroundapp", NormalizedProcessName = "somebackgroundapp", ProcessStartTimeUtc = start }
            ]
        };

        var candidates = suspendService.BuildCandidates(
            snapshot,
            enabledRules: Array.Empty<BackgroundProcessRule>(),
            customProcessNames: snapshot.Processes.Select(p => p.ProcessName),
            protectedProcessNames: Array.Empty<string>());

        Assert.AreEqual(1, candidates.Count, "Only the non-Riot background app may be a suspend candidate.");
        Assert.AreEqual("somebackgroundapp.exe", candidates[0].ProcessName);
    }

    [TestMethod]
    public async Task RunningDetection_NameOnlyFallback_ForPresentationOnly()
    {
        string trustedPath = Path.GetFullPath(Path.Combine(_tempDirectory, "League of Legends.exe"));
        var provider = new ProcessObservationSnapshotProvider(
            enumerate: () =>
            [
                new ProcessObservation(200, "League of Legends", null, DateTime.UtcNow),
                new ProcessObservation(201, "othergame", Path.GetFullPath(Path.Combine(_tempDirectory, "mismatch.exe")), DateTime.UtcNow)
            ]);
        var scanner = new ProcessScannerService(new ProcessService(), provider);
        var item = new LibraryItem { Id = "riot-lol", ProcessName = "League of Legends", ExecutablePath = trustedPath };
        var other = new LibraryItem { Id = "other", ProcessName = "othergame", ExecutablePath = Path.GetFullPath(Path.Combine(_tempDirectory, "othergame.exe")) };

        IReadOnlySet<string> running = await scanner.FindRunningLibraryItemIdsAsync([item, other]);

        Assert.IsTrue(running.Contains("riot-lol"), "Unreadable process path must still allow passive name-based presentation.");
        Assert.IsFalse(running.Contains("other"), "A known but different executable path must never match by name alone.");
    }

    private sealed class FakeProcessProvider : IBenchmarkProcessSnapshotProvider
    {
        private readonly BenchmarkProcessSnapshot[] _processes;
        public FakeProcessProvider(params BenchmarkProcessSnapshot[] processes) => _processes = processes;
        public IReadOnlyList<BenchmarkProcessSnapshot> GetProcesses() => _processes;
    }
}
