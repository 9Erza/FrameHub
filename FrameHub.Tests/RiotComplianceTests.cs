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
        Assert.IsTrue(item.AllowBenchmark, "Supported trusted Riot games must allow benchmark capture.");
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
        Assert.IsTrue(item.AllowBenchmark);
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
            "UnknownProduct.lnk",
            riotClientExe,
            "--launch-product=tft_mobile");

        var scanner = new RiotLibraryScanner(startMenuRoots: [startMenuPrograms]);
        var result = scanner.Scan();

        Assert.AreEqual(0, result.Items.Count);
        Assert.AreEqual(1, result.Warnings.Count);
        StringAssert.Contains(result.Warnings[0], "tft_mobile");
    }

    [TestMethod]
    public void Scanner_ShortcutResolutionThrows_RecordsWarningAndContinues()
    {
        (string _, string startMenuPrograms) = CreateRiotLayout();
        string brokenShortcut = Path.Combine(startMenuPrograms, "Riot Games", "Broken.lnk");
        Directory.CreateDirectory(Path.GetDirectoryName(brokenShortcut)!);
        File.WriteAllText(brokenShortcut, "corrupt");

        var scanner = new RiotLibraryScanner(
            startMenuRoots: [startMenuPrograms],
            shortcutResolver: _ => throw new InvalidOperationException("COM error"));
        var result = scanner.Scan();

        Assert.AreEqual(0, result.Items.Count);
        Assert.AreEqual(1, result.Warnings.Count);
        StringAssert.Contains(result.Warnings[0], "Broken.lnk");
    }

    [TestMethod]
    public void ExtractLaunchProduct_ParsesSupportedPatterns()
    {
        Assert.AreEqual("league_of_legends", RiotLibraryScanner.ExtractLaunchProduct("--launch-product=league_of_legends --launch-patchline=live"));
        Assert.AreEqual("valorant", RiotLibraryScanner.ExtractLaunchProduct("--launch-product=valorant --launch-patchline=live"));
        Assert.AreEqual("custom-product_1", RiotLibraryScanner.ExtractLaunchProduct("--other-flag --launch-product=custom-product_1"));
        Assert.IsNull(RiotLibraryScanner.ExtractLaunchProduct(null));
        Assert.IsNull(RiotLibraryScanner.ExtractLaunchProduct(""));
        Assert.IsNull(RiotLibraryScanner.ExtractLaunchProduct("--no-product-flag"));
    }

    [TestMethod]
    public void SaveAndLoad_RoundTripsLaunchPath_AndProtectsRemoteControl()
    {
        string libraryPath = Path.Combine(_tempDirectory, "library.json");
        var service = new LibraryService(libraryPath);
        var item = new LibraryItem
        {
            Id = "riot-league",
            DisplayName = "League of Legends",
            Source = LibrarySource.Riot,
            Type = LibraryItemType.Game,
            AppId = "league_of_legends",
            ProcessName = "League of Legends",
            LaunchPath = Path.Combine(_tempDirectory, "League of Legends.lnk"),
            AllowBenchmark = true
        };

        service.SaveItems([item]);

        List<LibraryItem> loaded = service.LoadItems();
        Assert.AreEqual(1, loaded.Count);
        Assert.AreEqual(item.LaunchPath, loaded[0].LaunchPath, "LaunchPath must survive sanitize/persist round-trip.");
        Assert.IsTrue(loaded[0].AllowBenchmark, "AllowBenchmark=true must survive persistence.");

        var rescan = new LibraryItem
        {
            DisplayName = "League of Legends",
            Source = LibrarySource.Riot,
            AppId = "league_of_legends",
            ProcessName = "League of Legends",
            AllowBenchmark = true
        };
        List<LibraryItem> merged = service.MergeItems(loaded, [rescan]);
        Assert.AreEqual(1, merged.Count);
        Assert.AreEqual(item.LaunchPath, merged[0].LaunchPath, "Merge must keep the trusted launch entry.");
        Assert.IsTrue(merged[0].AllowBenchmark, "Merge must keep Riot benchmark eligibility.");
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
    public void LegacyRiotItems_LeagueAndValorant_AutoUpgradedToAllowBenchmarkTrue_OnLoad()
    {
        string libraryPath = Path.Combine(_tempDirectory, "legacy-riot-library.json");
        File.WriteAllText(libraryPath, """
        [
            {
                "Id": "legacy-lol",
                "DisplayName": "League of Legends",
                "Source": 3,
                "Type": 0,
                "AppId": "league_of_legends",
                "ExecutablePath": "C:\\Riot Games\\League of Legends\\Game\\League of Legends.exe",
                "ProcessName": "League of Legends",
                "AllowBenchmark": false
            },
            {
                "Id": "legacy-val",
                "DisplayName": "VALORANT",
                "Source": 3,
                "Type": 0,
                "AppId": "valorant",
                "ExecutablePath": "C:\\Riot Games\\VALORANT\\ShooterGame\\Binaries\\Win64\\VALORANT-Win64-Shipping.exe",
                "ProcessName": "VALORANT-Win64-Shipping",
                "AllowBenchmark": false
            }
        ]
        """);

        var service = new LibraryService(libraryPath);
        List<LibraryItem> loaded = service.LoadItems();

        Assert.AreEqual(2, loaded.Count);
        LibraryItem lol = loaded.First(x => x.Id == "legacy-lol");
        LibraryItem val = loaded.First(x => x.Id == "legacy-val");
        Assert.IsTrue(lol.AllowBenchmark, "Legacy trusted League item must automatically gain benchmark eligibility on load.");
        Assert.IsTrue(val.AllowBenchmark, "Legacy trusted VALORANT item must automatically gain benchmark eligibility on load.");
    }

    [TestMethod]
    public void LegacyNonRiotItems_WithRiotProcess_NeverUpgradedToAllowBenchmarkTrue()
    {
        string libraryPath = Path.Combine(_tempDirectory, "legacy-nonriot-library.json");
        File.WriteAllText(libraryPath, """
        [
            {
                "Id": "manual-lol",
                "DisplayName": "Manual League",
                "Source": 0,
                "Type": 0,
                "ExecutablePath": "C:\\Games\\ManualLeague\\League of Legends.exe",
                "ProcessName": "League of Legends",
                "AllowBenchmark": false
            },
            {
                "Id": "steam-lol",
                "DisplayName": "Steam League",
                "Source": 1,
                "Type": 0,
                "ExecutablePath": "C:\\Games\\SteamLeague\\League of Legends.exe",
                "ProcessName": "League of Legends",
                "AllowBenchmark": false
            },
            {
                "Id": "epic-lol",
                "DisplayName": "Epic League",
                "Source": 2,
                "Type": 0,
                "ExecutablePath": "C:\\Games\\EpicLeague\\League of Legends.exe",
                "ProcessName": "League of Legends",
                "AllowBenchmark": false
            }
        ]
        """);

        var service = new LibraryService(libraryPath);
        List<LibraryItem> loaded = service.LoadItems();

        Assert.AreEqual(3, loaded.Count);
        Assert.IsFalse(loaded.First(x => x.Id == "manual-lol").AllowBenchmark, "Manual items must never be upgraded.");
        Assert.IsFalse(loaded.First(x => x.Id == "steam-lol").AllowBenchmark, "Steam items must never be upgraded.");
        Assert.IsFalse(loaded.First(x => x.Id == "epic-lol").AllowBenchmark, "Epic items must never be upgraded.");
    }

    [TestMethod]
    public void LegacyRiotClientAndLauncherItems_NeverUpgradedToAllowBenchmarkTrue()
    {
        string libraryPath = Path.Combine(_tempDirectory, "legacy-launchers-library.json");
        File.WriteAllText(libraryPath, """
        [
            {
                "Id": "riot-launcher",
                "DisplayName": "Riot Client",
                "Source": 3,
                "Type": 0,
                "ExecutablePath": "C:\\Riot Games\\Riot Client\\RiotClientServices.exe",
                "ProcessName": "RiotClientServices",
                "AllowBenchmark": false
            },
            {
                "Id": "riot-client",
                "DisplayName": "League Client",
                "Source": 3,
                "Type": 0,
                "ExecutablePath": "C:\\Riot Games\\League of Legends\\LeagueClient.exe",
                "ProcessName": "LeagueClient",
                "AllowBenchmark": false
            }
        ]
        """);

        var service = new LibraryService(libraryPath);
        List<LibraryItem> loaded = service.LoadItems();

        Assert.AreEqual(2, loaded.Count);
        Assert.IsFalse(loaded.First(x => x.Id == "riot-launcher").AllowBenchmark, "RiotClientServices must never be upgraded to benchmark eligible.");
        Assert.IsFalse(loaded.First(x => x.Id == "riot-client").AllowBenchmark, "LeagueClient must never be upgraded to benchmark eligible.");
    }

    [TestMethod]
    public void LoadItems_DoesNotModifySourceFileOnDisk()
    {
        string libraryPath = Path.Combine(_tempDirectory, "read-only-check.json");
        string originalContent = """
        [
            {
                "Id": "legacy-lol",
                "DisplayName": "League of Legends",
                "Source": 3,
                "Type": 0,
                "AppId": "league_of_legends",
                "ExecutablePath": "C:\\Riot Games\\League of Legends\\Game\\League of Legends.exe",
                "ProcessName": "League of Legends",
                "AllowBenchmark": false
            }
        ]
        """;
        File.WriteAllText(libraryPath, originalContent);
        DateTime originalWriteTime = File.GetLastWriteTimeUtc(libraryPath);

        var service = new LibraryService(libraryPath);

        // Load multiple times (as ActiveGameMonitor does)
        for (int i = 0; i < 5; i++)
        {
            List<LibraryItem> items = service.LoadItems();
            Assert.AreEqual(1, items.Count);
            Assert.IsTrue(items[0].AllowBenchmark, "In-memory item is sanitized.");
        }

        string diskContent = File.ReadAllText(libraryPath);
        DateTime diskWriteTime = File.GetLastWriteTimeUtc(libraryPath);

        Assert.AreEqual(originalContent, diskContent, "LoadItems must remain strictly read-only and never write to disk.");
        Assert.AreEqual(originalWriteTime, diskWriteTime, "File timestamp must not change on LoadItems.");
    }

    [TestMethod]
    public void BenchmarkDetection_TrustedRiotItem_IsBenchmarkEligible_WhenActualGameRunning()
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
            AllowBenchmark = true
        };

        IReadOnlyList<BenchmarkRunningGame> active = detector.DetectActiveGames([riotItem]);
        IReadOnlyList<BenchmarkRunningGame> eligible = detector.Detect([riotItem]);

        Assert.AreEqual(1, active.Count, "Trusted Riot game running must be detected as active game.");
        Assert.AreEqual(1, eligible.Count, "Trusted Riot game running must be detected as benchmark eligible.");
        Assert.AreEqual("riot-lol", eligible[0].LibraryItem.Id);
    }

    [TestMethod]
    public void ActiveGameDetection_DisabledRiotItem_ActiveGameOnlyNotBenchmarkEligible()
    {
        string leaguePath = Path.GetFullPath(Path.Combine(_tempDirectory, "League of Legends.exe"));
        var provider = new FakeProcessProvider(new BenchmarkProcessSnapshot(4242, "League of Legends", leaguePath, DateTime.UtcNow));
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

        IReadOnlyList<BenchmarkRunningGame> active = detector.DetectActiveGames([riotItem]);
        IReadOnlyList<BenchmarkRunningGame> eligible = detector.Detect([riotItem]);

        Assert.AreEqual(1, active.Count, "A running Riot game with AllowBenchmark=false remains visible as an active game.");
        Assert.AreEqual(0, eligible.Count, "AllowBenchmark=false Riot item must be excluded from benchmark detection.");
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
            AllowBenchmark = true
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
        Assert.AreEqual(0, eligible.Count, "A protected Riot identity with Source != Riot must never be benchmark eligible, even with AllowBenchmark == true.");
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
        Assert.AreEqual(0, eligible.Count, "A protected Riot executable path with Source != Riot must never be benchmark eligible, even with AllowBenchmark == true.");
    }

    [TestMethod]
    public void PresentMonApi_MetricAllowlist_DoesNotIncludeInjectionOrInstrumentedMetrics()
    {
        IReadOnlyList<FrameHub.Core.Services.Benchmarking.PmMetric> frameMetrics = FrameHub.Core.Services.Benchmarking.PresentMonApiFrameSource.FrameQueryMetrics;

        // Required basic timing metrics only
        CollectionAssert.Contains((System.Collections.ICollection)frameMetrics, FrameHub.Core.Services.Benchmarking.PmMetric.SwapChainAddress);
        CollectionAssert.Contains((System.Collections.ICollection)frameMetrics, FrameHub.Core.Services.Benchmarking.PmMetric.BetweenPresents);

        // Disallowed injection/instrumentation metrics
        CollectionAssert.DoesNotContain((System.Collections.ICollection)frameMetrics, FrameHub.Core.Services.Benchmarking.PmMetric.ClickToPhotonLatency);
        CollectionAssert.DoesNotContain((System.Collections.ICollection)frameMetrics, FrameHub.Core.Services.Benchmarking.PmMetric.AllInputToPhotonLatency);
        CollectionAssert.DoesNotContain((System.Collections.ICollection)frameMetrics, FrameHub.Core.Services.Benchmarking.PmMetric.InstrumentedLatency);
        CollectionAssert.DoesNotContain((System.Collections.ICollection)frameMetrics, FrameHub.Core.Services.Benchmarking.PmMetric.BetweenSimulationStart);
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
