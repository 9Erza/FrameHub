using System.Diagnostics;
using System.IO;
using System.Text.Json;
using FrameHub.App;
using FrameHub.Core.Models;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

/// <summary>
/// Regression tests for autostart priority, CPU Sets / affinity interaction and game
/// detection heuristics. Nothing here mutates a real process or the real Task Scheduler.
/// </summary>
[TestClass]
public sealed class DetectionAndSchedulingFixTests
{
    private string _tempDirectory = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.DetectionFixTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [TestCleanup]
    public void TearDown()
    {
        try { Directory.Delete(_tempDirectory, true); } catch { }
    }

    // ---- Autostart: scheduled task priority ----

    [DataTestMethod]
    [DataRow("4", true)]
    [DataRow("6", true)]
    [DataRow("7", false)]
    [DataRow("10", false)]
    [DataRow(null, false)]
    [DataRow("garbage", false)]
    public void ScheduledTaskPriority_OnlyNormalOrHigherIsHealthy(string? value, bool expected) =>
        Assert.AreEqual(expected, ScheduledTaskStartupReader.HasNormalPriority(value));

    [TestMethod]
    public void ElevatedTaskWithDefaultPriority_IsRepairedByPlanner()
    {
        string exe = CreateFile("FrameHub.exe");
        var desired = new DesiredStartupConfiguration(true, StartupWindowMode.Tray, true);
        var reader = new ScheduledTaskStartupReader(new FakeQuery(new(true, true, TaskXml(exe, "--tray", priority: "7"))), exe);

        var task = reader.Read(desired);
        Assert.IsTrue(task.Exists);
        Assert.IsFalse(task.HasNormalPriority);

        var registry = new RegistryStartupSnapshot(false, null, string.Empty, null, false, false, false);
        var evaluation = StartupConfigurationPlanner.Evaluate(desired, new ActualStartupConfiguration(registry, task));

        Assert.AreEqual(StartupConfigurationState.Broken, evaluation.State);
        CollectionAssert.Contains(evaluation.Reasons.ToList(), StartupConfigurationReason.TaskPriorityBelowNormal);
        CollectionAssert.Contains(evaluation.RequiredOperations.ToList(), StartupOperation.CreateOrUpdateScheduledTask);
    }

    [TestMethod]
    public void ElevatedTaskWithNormalPriority_IsHealthy()
    {
        string exe = CreateFile("FrameHub.exe");
        var desired = new DesiredStartupConfiguration(true, StartupWindowMode.Tray, true);
        var reader = new ScheduledTaskStartupReader(new FakeQuery(new(true, true, TaskXml(exe, "--tray", priority: "4"))), exe);
        var registry = new RegistryStartupSnapshot(false, null, string.Empty, null, false, false, false);

        var evaluation = StartupConfigurationPlanner.Evaluate(desired, new ActualStartupConfiguration(registry, reader.Read(desired)));

        Assert.AreEqual(StartupConfigurationState.ElevatedScheduledTask, evaluation.State);
        Assert.AreEqual(0, evaluation.RequiredOperations.Count);
    }

    [TestMethod]
    public void CreatedTaskXml_UsesNormalPriority() =>
        Assert.IsTrue(ScheduledTaskStartupReader.HasNormalPriority(SettingsService.ScheduledTaskPriority.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    [TestMethod]
    public void OwnPriority_BelowNormalIsRestoredToNormal()
    {
        var accessor = new FakePriorityAccessor(ProcessPriorityClass.BelowNormal);
        var service = new ProcessService { ProcessPriorityAccessorForTesting = pid => pid == Environment.ProcessId ? accessor : null };

        Assert.IsTrue(App.App.NormalizeOwnProcessPriority(service));
        Assert.AreEqual(ProcessPriorityClass.Normal, accessor.Priority);
    }

    [DataTestMethod]
    [DataRow(ProcessPriorityClass.Normal)]
    [DataRow(ProcessPriorityClass.AboveNormal)]
    [DataRow(ProcessPriorityClass.High)]
    public void OwnPriority_NormalOrHigherIsLeftAlone(ProcessPriorityClass start)
    {
        var accessor = new FakePriorityAccessor(start);
        var service = new ProcessService { ProcessPriorityAccessorForTesting = _ => accessor };

        Assert.IsFalse(App.App.NormalizeOwnProcessPriority(service));
        Assert.AreEqual(start, accessor.Priority);
        Assert.AreEqual(0, accessor.Writes);
    }

    // ---- CPU Sets vs hard affinity ----

    [TestMethod]
    public void AffinityReset_RestrictedMaskIsWidenedToSystemMask() =>
        Assert.AreEqual(0xFFFFUL, ProcessService.ComputeAffinityResetMask(0x00FFUL, 0xFFFFUL));

    [TestMethod]
    public void AffinityReset_FullMaskIsLeftAlone() =>
        Assert.IsNull(ProcessService.ComputeAffinityResetMask(0xFFFFUL, 0xFFFFUL));

    [TestMethod]
    public void AffinityReset_UnavailableMasksAreLeftAlone()
    {
        Assert.IsNull(ProcessService.ComputeAffinityResetMask(0, 0xFFFFUL));
        Assert.IsNull(ProcessService.ComputeAffinityResetMask(0x0FUL, 0));
    }

    // ---- Profile drift ----

    [TestMethod]
    public void AppliedProfile_IsReappliedWhenProcessResetsItsAffinity()
    {
        using Process process = Process.GetCurrentProcess();
        var native = new DriftingProcessService { CurrentMask = 0b01 };
        var map = new Dictionary<int, uint> { [0] = 256, [1] = 257 };
        var optimization = new OptimizationService(native, () => map);
        var profile = new ProcessProfile
        {
            ProcessName = process.ProcessName,
            IsEnabled = true,
            ApplyCoreOptimization = true,
            ApplyPriority = false,
            AffinityMask = 0b01,
            OptimizationMode = OptimizationMode.Affinity
        };
        var snapshot = new ProcessGroupSnapshot
        {
            Name = process.ProcessName,
            Instances = [new ProcessInstanceKey(process.Id, process.StartTime.ToUniversalTime())]
        };

        OptimizationResult Apply() => optimization.ApplyProfilesForSnapshots([profile], [snapshot], false, false).Results.Single();

        Assert.IsTrue(Apply().CoreApplied);
        Assert.AreEqual("SKIPPED_ALREADY_APPLIED", Apply().Message, "No drift: the cached profile is not re-applied.");
        Assert.AreEqual(1, native.CoreCalls);

        native.CurrentMask = 0b11; // the game widened its own affinity after start
        Assert.IsTrue(Apply().CoreApplied, "Drift must trigger a re-apply.");
        Assert.AreEqual(2, native.CoreCalls);

        native.CurrentMask = 0b01;
        Assert.AreEqual("SKIPPED_ALREADY_APPLIED", Apply().Message);
        Assert.AreEqual(2, native.CoreCalls);
    }

    [TestMethod]
    public void UnreadableState_IsNotTreatedAsDrift()
    {
        using Process process = Process.GetCurrentProcess();
        var native = new DriftingProcessService { CurrentMask = null };
        var optimization = new OptimizationService(native, () => new Dictionary<int, uint> { [0] = 256 });
        var profile = new ProcessProfile { ProcessName = process.ProcessName, IsEnabled = true, ApplyCoreOptimization = true, AffinityMask = 1 };

        Assert.IsFalse(optimization.HasDrifted(process.Id, profile, "Normal", false));
    }

    [TestMethod]
    public void ExpectedEffectiveMask_IgnoresProcessorsOutsideTopology() =>
        Assert.AreEqual(0b11L, OptimizationService.ExpectedEffectiveMask(0b1111, new Dictionary<int, uint> { [0] = 1, [1] = 2 }));

    // ---- Steam manifests ----

    [TestMethod]
    public void SteamManifest_TopLevelValuesAreNotOverwrittenByNestedKeys()
    {
        const string manifest = """
            "AppState"
            {
                "appid"     "730"
                "name"      "Counter-Strike 2"
                "StateFlags"    "4"
                "installdir"    "Counter-Strike Global Offensive"
                "UserConfig"
                {
                    "name"      "nested"
                    "installdir"    "wrong"
                }
            }
            """;

        var values = SteamLibraryScanner.ParseValveKeyValues(manifest);

        Assert.AreEqual("Counter-Strike 2", values["name"]);
        Assert.AreEqual("Counter-Strike Global Offensive", values["installdir"]);
    }

    [DataTestMethod]
    [DataRow("4", true)]
    [DataRow("6", true)]
    [DataRow("1026", false)]
    [DataRow("2", false)]
    [DataRow(null, true)]
    public void SteamStateFlags_FullyInstalledBit(string? flags, bool expected) =>
        Assert.AreEqual(expected, SteamLibraryScanner.IsFullyInstalled(flags));

    // ---- Epic manifests ----

    [DataTestMethod]
    [DataRow("""{"AppName":"Fortnite","MainGameAppName":"Fortnite","AppCategories":["public","games","applications"]}""", true)]
    [DataRow("""{"AppName":"UE_5.4","AppCategories":["engines"]}""", false)]
    [DataRow("""{"AppName":"PluginX","AppCategories":["plugins","engine/plugins"]}""", false)]
    [DataRow("""{"AppName":"GameDlc","MainGameAppName":"Game","AppCategories":["addons","games"]}""", false)]
    [DataRow("""{"AppName":"Game","MainGameAppName":"Game","AppCategories":["games"],"bIsIncompleteInstall":true}""", false)]
    [DataRow("""{"AppName":"LegacyManifest"}""", true)]
    public void EpicManifest_OnlyCompleteBaseGamesAreListed(string json, bool expected)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual(expected, EpicLibraryScanner.IsInstalledGameManifest(doc.RootElement));
    }

    // ---- Process path reader ----

    [TestMethod]
    public void ProcessPathReader_MatchesManagedPathForOwnProcess()
    {
        using Process current = Process.GetCurrentProcess();
        string? path = FrameHub.Core.Services.Benchmarking.ProcessExecutablePathResolver.TryGetPath(current);
        Assert.IsNotNull(path);
        Assert.AreEqual(Path.GetFullPath(current.MainModule!.FileName), Path.GetFullPath(path), ignoreCase: true);
    }

    // ---- Executable resolution ----

    [TestMethod]
    public void ExecutableResolver_LibraryRootNamedToolsStillFindsGame()
    {
        string install = Path.Combine(_tempDirectory, "Tools", "SteamLibrary", "steamapps", "common", "Space Game");
        string game = CreateFile(Path.Combine(install, "SpaceGame.exe"));

        Assert.AreEqual(game, ExecutableResolver.FindBestExecutable(install, "Space Game"));
    }

    [TestMethod]
    public void ExecutableResolver_PrefersRealGameOverAntiCheatBootstrapper()
    {
        string install = Path.Combine(_tempDirectory, "Arena");
        CreateFile(Path.Combine(install, "start_protected_game.exe"));
        CreateFile(Path.Combine(install, "EasyAntiCheat", "EasyAntiCheat_EOS_Setup.exe"));
        CreateFile(Path.Combine(install, "UnityCrashHandler64.exe"));
        string game = CreateFile(Path.Combine(install, "Arena.exe"));

        Assert.AreEqual(game, ExecutableResolver.FindBestExecutable(install, "Arena"));
    }

    [TestMethod]
    public void ExecutableResolver_StillSkipsRedistFoldersInsideInstall()
    {
        string install = Path.Combine(_tempDirectory, "Racer");
        CreateFile(Path.Combine(install, "_CommonRedist", "Racer.exe"));
        string game = CreateFile(Path.Combine(install, "bin", "Racer.exe"));

        Assert.AreEqual(game, ExecutableResolver.FindBestExecutable(install, "Racer"));
    }

    [TestMethod]
    public void ExecutableResolver_PrefersUnrealShippingBinaryOverBootstrap()
    {
        string install = Path.Combine(_tempDirectory, "Hollow Peak");
        CreateFile(Path.Combine(install, "HollowPeak.exe"));
        string shipping = CreateFile(Path.Combine(install, "HollowPeak", "Binaries", "Win64", "HollowPeak-Win64-Shipping.exe"));

        Assert.AreEqual(shipping, ExecutableResolver.FindBestExecutable(install, "Hollow Peak"));
    }

    // ---- Library merge repairs ----

    [TestMethod]
    public void LibraryMerge_StaleExecutableIsReplacedByScannedOne()
    {
        string newExe = CreateFile(Path.Combine("D-drive", "Game", "Game.exe"));
        var existing = new FrameHub.Core.Models.Library.LibraryItem
        {
            DisplayName = "Game",
            ExecutablePath = Path.Combine(_tempDirectory, "C-drive", "Game", "Game_old.exe"),
            ProcessName = "Game_old",
            IconPath = Path.Combine(_tempDirectory, "C-drive", "Game", "Game_old.exe")
        };
        var scanned = new FrameHub.Core.Models.Library.LibraryItem { DisplayName = "Game", ExecutablePath = newExe, ProcessName = "Game", IconPath = newExe, InstallPath = Path.GetDirectoryName(newExe) };

        LibraryService.RepairStaleExecutable(existing, scanned);

        Assert.AreEqual(newExe, existing.ExecutablePath);
        Assert.AreEqual("Game", existing.ProcessName);
        Assert.AreEqual(newExe, existing.IconPath);
    }

    [TestMethod]
    public void LibraryMerge_ExistingExecutableIsKept()
    {
        string kept = CreateFile(Path.Combine("Game", "Custom.exe"));
        string other = CreateFile(Path.Combine("Game", "Game.exe"));
        var existing = new FrameHub.Core.Models.Library.LibraryItem { DisplayName = "Game", ExecutablePath = kept, ProcessName = "Custom" };
        var scanned = new FrameHub.Core.Models.Library.LibraryItem { DisplayName = "Game", ExecutablePath = other, ProcessName = "Game" };

        LibraryService.RepairStaleExecutable(existing, scanned);

        Assert.AreEqual(kept, existing.ExecutablePath);
        Assert.AreEqual("Custom", existing.ProcessName);
    }

    [TestMethod]
    public void LibraryMerge_StoreBootstrapUpgradesToShippingBinary_ManualDoesNot()
    {
        string install = Path.Combine(_tempDirectory, "Peak");
        string bootstrap = CreateFile(Path.Combine(install, "Peak.exe"));
        string shipping = CreateFile(Path.Combine(install, "Peak", "Binaries", "Win64", "Peak-Win64-Shipping.exe"));

        var steam = new FrameHub.Core.Models.Library.LibraryItem { DisplayName = "Peak", Source = FrameHub.Core.Models.Library.LibrarySource.Steam, ExecutablePath = bootstrap, ProcessName = "Peak", InstallPath = install };
        var scanned = new FrameHub.Core.Models.Library.LibraryItem { DisplayName = "Peak", Source = FrameHub.Core.Models.Library.LibrarySource.Steam, ExecutablePath = shipping, ProcessName = "Peak-Win64-Shipping", InstallPath = install };
        LibraryService.RepairStaleExecutable(steam, scanned);
        Assert.AreEqual(shipping, steam.ExecutablePath);
        Assert.AreEqual("Peak-Win64-Shipping", steam.ProcessName);

        var manual = new FrameHub.Core.Models.Library.LibraryItem { DisplayName = "Peak", Source = FrameHub.Core.Models.Library.LibrarySource.Manual, ExecutablePath = bootstrap, ProcessName = "Peak", InstallPath = install };
        LibraryService.RepairStaleExecutable(manual, scanned);
        Assert.AreEqual(bootstrap, manual.ExecutablePath);
    }

    // ---- Epic launch ----

    [DataTestMethod]
    [DataRow("Fortnite", "com.epicgames.launcher://apps/Fortnite?action=launch&silent=true")]
    [DataRow("a1b2.c3-d_4", "com.epicgames.launcher://apps/a1b2.c3-d_4?action=launch&silent=true")]
    [DataRow("bad?name", null)]
    [DataRow("../x", null)]
    [DataRow("", null)]
    [DataRow(null, null)]
    public void EpicLaunchUri_OnlyForSafeAppNames(string? appName, string? expected) =>
        Assert.AreEqual(expected, FrameHub.App.Services.AppLibraryLaunchService.BuildEpicLaunchUri(appName));

    [TestMethod]
    public void EpicGame_LaunchesThroughEpicProtocolNotExecutable()
    {
        string exe = CreateFile(Path.Combine("Epic", "Game-Win64-Shipping.exe"));
        ProcessStartInfo? captured = null;
        var service = new FrameHub.App.Services.AppLibraryLaunchService(psi => { captured = psi; return true; });

        var result = service.Launch(new FrameHub.Core.Models.Library.LibraryItem
        {
            Id = "epic-1",
            DisplayName = "Epic Game",
            Source = FrameHub.Core.Models.Library.LibrarySource.Epic,
            Type = FrameHub.Core.Models.Library.LibraryItemType.Game,
            AppId = "GameApp",
            ExecutablePath = exe,
            IsEnabled = true
        });

        Assert.IsTrue(result.Success);
        Assert.AreEqual("com.epicgames.launcher://apps/GameApp?action=launch&silent=true", captured?.FileName);
        Assert.IsTrue(captured!.UseShellExecute);
    }

    // ---- GOG / Ubisoft ----

    [TestMethod]
    public void GogScanner_ListsBaseGamesAndSkipsDlcAndMissingFolders()
    {
        string install = Path.Combine(_tempDirectory, "GOG Games", "Witcher");
        string exe = CreateFile(Path.Combine(install, "bin", "witcher.exe"));
        var scanner = new GogLibraryScanner(() => new[]
        {
            new GogInstallRecord("1207664643", "The Witcher", install, exe, null),
            new GogInstallRecord("1207664700", "The Witcher DLC", install, exe, "1207664643"),
            new GogInstallRecord("1", "Gone", Path.Combine(_tempDirectory, "missing"), null, null)
        });

        var item = scanner.Scan().Items.Single();
        Assert.AreEqual("The Witcher", item.DisplayName);
        Assert.AreEqual(FrameHub.Core.Models.Library.LibrarySource.Gog, item.Source);
        Assert.AreEqual(exe, item.ExecutablePath);
        Assert.AreEqual("witcher", item.ProcessName);
    }

    [TestMethod]
    public void UbisoftScanner_ResolvesGameAndSkipsInvalidIds()
    {
        string install = Path.Combine(_tempDirectory, "Ubisoft", "Far Cry 6");
        string exe = CreateFile(Path.Combine(install, "bin", "FarCry6.exe"));
        var scanner = new UbisoftLibraryScanner(() => new[]
        {
            new UbisoftInstallRecord("5266", install.Replace('\\', '/') + "/", "Far Cry 6"),
            new UbisoftInstallRecord("not-a-number", install, "Broken")
        });

        var item = scanner.Scan().Items.Single();
        Assert.AreEqual("Far Cry 6", item.DisplayName);
        Assert.AreEqual("5266", item.AppId);
        Assert.AreEqual(exe, item.ExecutablePath);
    }

    [TestMethod]
    public void UbisoftGame_LaunchesThroughUbisoftConnectProtocol()
    {
        string exe = CreateFile(Path.Combine("Ubi", "Game.exe"));
        ProcessStartInfo? captured = null;
        var service = new FrameHub.App.Services.AppLibraryLaunchService(psi => { captured = psi; return true; });

        var result = service.Launch(new FrameHub.Core.Models.Library.LibraryItem
        {
            Id = "ubi-1",
            DisplayName = "Ubi Game",
            Source = FrameHub.Core.Models.Library.LibrarySource.Ubisoft,
            Type = FrameHub.Core.Models.Library.LibraryItemType.Game,
            AppId = "5266",
            ExecutablePath = exe,
            IsEnabled = true
        });

        Assert.IsTrue(result.Success);
        Assert.AreEqual("uplay://launch/5266/0", captured?.FileName);
    }

    private string CreateFile(string relativeOrFull)
    {
        string path = Path.IsPathRooted(relativeOrFull) ? relativeOrFull : Path.Combine(_tempDirectory, relativeOrFull);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Array.Empty<byte>());
        return path;
    }

    private static string TaskXml(string exe, string arguments, string priority) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <Triggers><LogonTrigger><Enabled>true</Enabled></LogonTrigger></Triggers>
          <Principals><Principal id="Author"><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
          <Settings><Enabled>true</Enabled><Priority>{priority}</Priority></Settings>
          <Actions Context="Author"><Exec><Command>{exe}</Command><Arguments>{arguments}</Arguments></Exec></Actions>
        </Task>
        """.Trim();

    private sealed class DriftingProcessService : ProcessService
    {
        public long? CurrentMask { get; set; }
        public int CoreCalls { get; private set; }

        public override string ApplyCoreOptimization(int pid, long mask, OptimizationMode mode, Dictionary<int, uint> cpuSetMap)
        {
            CoreCalls++;
            return "OK_AFFINITY";
        }

        public override (bool Success, long Mask, OptimizationMode Mode, string Source, string Message) GetCurrentCoreSelection(int pid, Dictionary<int, uint> cpuSetMap) =>
            CurrentMask is long mask
                ? (true, mask, OptimizationMode.Affinity, "Affinity", "synthetic")
                : (false, 0, OptimizationMode.CpuSets, "None", "unreadable");
    }

    private sealed class FakeQuery(ScheduledTaskQueryResult result) : ITaskSchedulerQuery
    {
        public ScheduledTaskQueryResult Query(string taskName) => result;
    }

    private sealed class FakePriorityAccessor(ProcessPriorityClass initial) : IProcessPriorityAccessor
    {
        private ProcessPriorityClass? _priority = initial;
        public int Writes { get; private set; }

        public ProcessPriorityClass? Priority
        {
            get => _priority;
            set { _priority = value; Writes++; }
        }

        public bool MatchesIdentity(BenchmarkProcessIdentity expected) => true;
    }
}
