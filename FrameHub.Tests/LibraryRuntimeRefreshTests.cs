using System.IO;
using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.App.ViewModels.Library;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

/// <summary>
/// CP4 coverage: Library runtime-status refresh cadence (view activation + one post-launch pulse).
/// All process state is synthetic; no real games or Riot processes are started.
/// </summary>
[TestClass]
public sealed class LibraryRuntimeRefreshTests
{
    private string _tempDirectory = null!;
    private List<ProcessObservation> _processes = null!;
    private long _observationClockTicks;

    private const string LeaguePath = @"C:\Riot Games\League of Legends\Game\League of Legends.exe";
    private const string Cs2Path = @"C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive\game\bin\win64\cs2.exe";

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.LibraryRefreshTests", Guid.NewGuid().ToString("N"));
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
    public async Task Activate_AfterLeagueProcessStarts_MarksLeagueRunning()
    {
        using var host = CreateViewModelHost();
        var league = host.ViewModel.Items.First(item => item.Id == "riot-lol");
        var cs2 = host.ViewModel.Items.First(item => item.Id == "cs2");
        Assert.IsFalse(league.IsRunning, "Initially no synthetic process is running.");

        _processes.Add(new ProcessObservation(4242, "League of Legends", LeaguePath, DateTime.UtcNow));
        host.ViewModel.Activate();

        await WaitUntilAsync(() => league.IsRunning);
        Assert.IsFalse(cs2.IsRunning, "A running Riot game must not mark unrelated games as running.");
    }

    [TestMethod]
    public async Task Reactivate_AfterProcessExits_PerformsSecondFreshRefresh()
    {
        using var host = CreateViewModelHost();
        var league = host.ViewModel.Items.First(item => item.Id == "riot-lol");
        _processes.Add(new ProcessObservation(4242, "League of Legends", LeaguePath, DateTime.UtcNow));
        host.ViewModel.Activate();
        await WaitUntilAsync(() => league.IsRunning);

        _processes.Clear();
        host.ViewModel.Activate();

        await WaitUntilAsync(() => !league.IsRunning);
    }

    [TestMethod]
    public async Task Activate_DoesNotRescanLibraryOrPersistChanges()
    {
        using var host = CreateViewModelHost();
        string libraryPath = host.LibraryPath;
        string contentBefore = File.ReadAllText(libraryPath);
        int itemCountBefore = host.ViewModel.Items.Count;

        _processes.Add(new ProcessObservation(4242, "League of Legends", LeaguePath, DateTime.UtcNow));
        host.ViewModel.Activate();
        await WaitUntilAsync(() => host.ViewModel.Items.First(item => item.Id == "riot-lol").IsRunning);

        Assert.AreEqual(itemCountBefore, host.ViewModel.Items.Count, "Activation must not add or remove Library items.");
        Assert.AreEqual(contentBefore, File.ReadAllText(libraryPath), "Activation must be runtime-state only and must not rewrite library.json.");
    }

    [TestMethod]
    public async Task LaunchSelected_Success_SchedulesExactlyOneDelayedRefresh()
    {
        using var host = CreateViewModelHost();
        var league = host.ViewModel.Items.First(item => item.Id == "riot-lol");
        host.ViewModel.SelectedItem = league;
        host.Launcher.NextResult = LibraryLaunchResult.Ok();
        int delayCalls = 0;
        host.ViewModel.PostLaunchRefreshDelayProvider = (delay, token) =>
        {
            delayCalls++;
            // The game process appears during the delayed pulse window.
            _processes.Add(new ProcessObservation(4242, "League of Legends", LeaguePath, DateTime.UtcNow));
            return Task.CompletedTask;
        };

        host.ViewModel.LaunchSelectedCommand.Execute(null);

        await WaitUntilAsync(() => league.IsRunning);
        await Task.Delay(100);
        Assert.AreEqual(1, delayCalls, "Exactly one bounded post-launch pulse must run; it must not become a polling loop.");
    }

    [TestMethod]
    public async Task LaunchSelected_Failure_SchedulesNoRefresh()
    {
        using var host = CreateViewModelHost();
        var league = host.ViewModel.Items.First(item => item.Id == "riot-lol");
        host.ViewModel.SelectedItem = league;
        host.Launcher.NextResult = LibraryLaunchResult.Fail("executable_missing");
        int delayCalls = 0;
        host.ViewModel.PostLaunchRefreshDelayProvider = (delay, token) => { delayCalls++; return Task.CompletedTask; };

        host.ViewModel.LaunchSelectedCommand.Execute(null);
        await Task.Delay(150);

        Assert.AreEqual(0, delayCalls, "A failed launch must not schedule a post-success refresh.");
        Assert.IsFalse(league.IsRunning);
        Assert.AreEqual(1, host.Launcher.LaunchCalls);
    }

    [TestMethod]
    public async Task RiotClientOrLeagueClientProcesses_Alone_NeverMarkLeagueRunning()
    {
        using var host = CreateViewModelHost();
        var league = host.ViewModel.Items.First(item => item.Id == "riot-lol");
        _processes.AddRange(new[]
        {
            new ProcessObservation(900, "RiotClientServices", @"C:\Riot Games\Riot Client\RiotClientServices.exe", DateTime.UtcNow),
            new ProcessObservation(901, "LeagueClient", @"C:\Riot Games\League of Legends\LeagueClient.exe", DateTime.UtcNow),
            new ProcessObservation(902, "LeagueClientUx", @"C:\Riot Games\League of Legends\LeagueClientUx.exe", DateTime.UtcNow)
        });

        host.ViewModel.Activate();
        host.ViewModel.PostLaunchRefreshDelayProvider = (delay, token) => Task.CompletedTask;
        host.ViewModel.SelectedItem = league;
        host.Launcher.NextResult = LibraryLaunchResult.Ok();
        host.ViewModel.LaunchSelectedCommand.Execute(null);

        await Task.Delay(200);
        Assert.IsFalse(league.IsRunning, "Launcher/client processes alone must never count as the game running.");
    }

    [TestMethod]
    public void ShellNavigation_ActivatesLibraryRefreshOncePerTransition_NoNewTimers()
    {
        string? repoRoot = FindRepoRoot();
        if (repoRoot == null)
        {
            Assert.Inconclusive("Repository source root was not discoverable from the test assembly location.");
        }

        string shellSource = File.ReadAllText(Path.Combine(repoRoot, "FrameHub.App", "ViewModels", "ShellViewModel.cs"));
        int transitionStart = shellSource.IndexOf("!_currentKey.Equals(\"Library\"", StringComparison.Ordinal);
        Assert.IsTrue(transitionStart >= 0, "ShellViewModel.Navigate must contain the Library transition block.");
        string transitionBlock = shellSource.Substring(transitionStart, Math.Min(500, shellSource.Length - transitionStart));
        Assert.IsTrue(transitionBlock.Contains("_libraryViewModel.Activate()"), "Navigating to Library must invoke the bounded activation refresh.");
        Assert.IsTrue(transitionBlock.Contains("_libraryViewModel.SelectedItem = null"), "Existing Library navigation behavior must be preserved.");

        string librarySource = File.ReadAllText(Path.Combine(repoRoot, "FrameHub.App", "ViewModels", "LibraryViewModel.cs"));
        Assert.AreEqual(1, CountOccurrences(librarySource, "DispatcherTimer"), "Only the pre-existing CS2 timer may exist in LibraryViewModel.");
        Assert.IsFalse(librarySource.Contains("PeriodicTimer"), "No periodic timer may be added.");
        Assert.IsFalse(librarySource.Contains("System.Threading.Timer"), "No thread-pool timer may be added.");

        int activateStart = librarySource.IndexOf("public void Activate()", StringComparison.Ordinal);
        Assert.IsTrue(activateStart >= 0, "LibraryViewModel must expose the activation refresh.");
        string activateBody = librarySource.Substring(activateStart, Math.Min(200, librarySource.Length - activateStart));
        Assert.IsFalse(activateBody.Contains("Scan"), "Activation must not run library discovery scans.");
        Assert.IsFalse(activateBody.Contains("MergeAndSave"), "Activation must not rescan or persist library changes.");
        Assert.IsTrue(activateBody.Contains("RefreshRuntimeState"), "Activation must perform the runtime-state refresh.");
    }

    private static int CountOccurrences(string source, string fragment)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += fragment.Length;
        }
        return count;
    }

    [TestMethod]
    public void DashboardView_GamingGamesComboBox_HasExplicitDisplayNameItemTemplate()
    {
        string? repoRoot = FindRepoRoot();
        if (repoRoot == null)
        {
            Assert.Inconclusive("Repository source root was not discoverable from the test assembly location.");
        }

        string xaml = File.ReadAllText(Path.Combine(repoRoot!, "FrameHub.App", "Views", "DashboardView.xaml"));
        int comboStart = xaml.IndexOf("ItemsSource=\"{Binding GamingGames}\"", StringComparison.Ordinal);
        Assert.IsTrue(comboStart >= 0, "DashboardView must contain the GamingGames ComboBox.");

        int comboEnd = xaml.IndexOf("</ComboBox>", comboStart, StringComparison.Ordinal);
        Assert.IsTrue(comboEnd > comboStart, "GamingGames ComboBox closing tag must exist.");
        string comboXaml = xaml.Substring(comboStart, comboEnd - comboStart);

        StringAssert.Contains(comboXaml, "Text=\"{Binding DisplayName}\"",
            "Dashboard ComboBox must explicitly bind DisplayName in its ItemTemplate and not rely on domain model ToString().");

        // Verify LibraryItem has no custom presentation ToString override
        var item = new LibraryItem();
        Assert.AreEqual(typeof(LibraryItem).ToString(), item.ToString(),
            "LibraryItem domain entity must not override ToString() with presentation logic.");
    }

    [TestMethod]
    public void LibraryService_Sanitize_StripsDeletedTempTestArtifacts()
    {
        string libraryPath = Path.Combine(_tempDirectory, $"library-sanitize-{Guid.NewGuid():N}.json");
        var libraryService = new LibraryService(libraryPath);

        string nonExistentTemp = Path.Combine(Path.GetTempPath(), "FrameHub.LibraryRefreshTests", "temp_sub", "desktop_test_game.exe");
        var testArtifact = new LibraryItem
        {
            Id = "desktop-game-1",
            DisplayName = "Desktop Game",
            ExecutablePath = nonExistentTemp,
            Type = LibraryItemType.Game
        };

        var realGame = new LibraryItem
        {
            Id = "real-game-1",
            DisplayName = "Real Game",
            ExecutablePath = @"C:\Program Files (x86)\Steam\steamapps\common\RealGame\game.exe",
            Type = LibraryItemType.Game
        };

        libraryService.SaveItems(new[] { testArtifact, realGame });
        var loaded = libraryService.LoadItems();

        Assert.AreEqual(1, loaded.Count);
        Assert.AreEqual("real-game-1", loaded[0].Id);
    }

    [TestMethod]
    public void LibraryService_Sanitize_PreservesLegitimateMissingAndCustomItems()
    {
        string libraryPath = Path.Combine(_tempDirectory, $"library-preserve-{Guid.NewGuid():N}.json");
        var libraryService = new LibraryService(libraryPath);

        // 1. Legitimate manual item named "Desktop Game" in standard games folder (missing exe)
        var manualDesktopGame = new LibraryItem
        {
            Id = "manual-desktop-game",
            DisplayName = "Desktop Game",
            ExecutablePath = @"C:\Games\Desktop Game\game.exe",
            Type = LibraryItemType.Game
        };

        // 2. Legitimate missing executable in a custom user folder named "Temp" (not OS temp)
        var customTempGame = new LibraryItem
        {
            Id = "custom-temp-game",
            DisplayName = "Custom Temp Game",
            ExecutablePath = @"C:\Temp\MyOldGame\game.exe",
            Type = LibraryItemType.Game
        };

        // 3. Legitimate missing game in Steam folder
        var missingSteamGame = new LibraryItem
        {
            Id = "missing-steam-game",
            DisplayName = "Missing Steam Game",
            ExecutablePath = @"D:\SteamLibrary\steamapps\common\UninstalledGame\game.exe",
            Type = LibraryItemType.Game
        };

        // 4. Valid existing executable created in isolated test temp folder
        string validExePath = Path.Combine(_tempDirectory, "existing_game.exe");
        File.WriteAllText(validExePath, "dummy");
        var existingGame = new LibraryItem
        {
            Id = "existing-game",
            DisplayName = "Existing Game",
            ExecutablePath = validExePath,
            Type = LibraryItemType.Game
        };

        libraryService.SaveItems(new[] { manualDesktopGame, customTempGame, missingSteamGame, existingGame });
        var loaded = libraryService.LoadItems();

        Assert.AreEqual(4, loaded.Count, "All 4 legitimate items must survive sanitization.");
        Assert.IsTrue(loaded.Any(x => x.Id == "manual-desktop-game"));
        Assert.IsTrue(loaded.Any(x => x.Id == "custom-temp-game"));
        Assert.IsTrue(loaded.Any(x => x.Id == "missing-steam-game"));
        Assert.IsTrue(loaded.Any(x => x.Id == "existing-game"));
    }

    [TestMethod]
    public void LibraryItemViewModel_MissingExecutable_ReportsMissingStatus()
    {
        string settingsPath = Path.Combine(_tempDirectory, $"settings-vm-{Guid.NewGuid():N}.json");
        using var runtime = new AppRuntimeService(settingsPath);
        var loc = new LocalizationService(runtime.SettingsService);

        var missingItem = new LibraryItem
        {
            Id = "missing-1",
            DisplayName = "Missing Game",
            ExecutablePath = @"C:\NonExistentDrive\Games\missing.exe",
            Type = LibraryItemType.Game
        };

        var vm = new LibraryItemViewModel(missingItem, loc, () => Array.Empty<Core.Models.ProcessProfile>());
        Assert.IsTrue(vm.IsExecutableMissing);
        Assert.AreEqual(loc.T("Library.ExeMissingBadge"), vm.ExeMissingText);
        Assert.AreEqual(loc.T("Library.ExeMissingStatus"), vm.StatusText);
    }

    private static string? FindRepoRoot()
    {
        string? directory = Path.GetDirectoryName(typeof(LibraryRuntimeRefreshTests).Assembly.Location);
        while (directory != null && !Directory.Exists(Path.Combine(directory, "FrameHub.App")))
        {
            directory = Path.GetDirectoryName(directory);
        }
        return directory;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 300 && !condition(); i++)
        {
            await Task.Delay(10);
        }
        Assert.IsTrue(condition(), "Expected runtime-state condition was not reached within the test timeout.");
    }

    private ViewModelHost CreateViewModelHost()
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

        // Monotonic clock so every snapshot request bypasses the observation TTL cache.
        var observationProvider = new ProcessObservationSnapshotProvider(
            timeToLive: TimeSpan.Zero,
            enumerate: () => _processes.ToList(),
            clock: () => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(Interlocked.Increment(ref _observationClockTicks)));
        var scanner = new ProcessScannerService(new ProcessService(), observationProvider);
        var launcher = new FakeLaunchService();

        string settingsPath = Path.Combine(_tempDirectory, $"settings-{Guid.NewGuid():N}.json");
        var runtime = new AppRuntimeService(settingsPath);
        var localization = new LocalizationService(runtime.SettingsService);
        var viewModel = new LibraryViewModel(localization, runtime, libraryService, scanner, launcher);
        return new ViewModelHost(viewModel, runtime, launcher, libraryPath);
    }

    private sealed record ViewModelHost(
        LibraryViewModel ViewModel,
        AppRuntimeService Runtime,
        FakeLaunchService Launcher,
        string LibraryPath) : IDisposable
    {
        public void Dispose()
        {
            ViewModel.Dispose();
            Runtime.Dispose();
        }
    }

    private sealed class FakeLaunchService : IAppLibraryLaunchService
    {
        public LibraryLaunchResult NextResult { get; set; } = LibraryLaunchResult.Ok();
        public int LaunchCalls { get; private set; }

        public LibraryLaunchResult Launch(LibraryItem? item)
        {
            LaunchCalls++;
            return NextResult;
        }
    }
}
