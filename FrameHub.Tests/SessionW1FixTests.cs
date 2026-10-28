using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Library;
using FrameHub.Core.Services.SessionOptimization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows.Data;

namespace FrameHub.Tests;

/// <summary>
/// W1 C (FH-100-015/016): scalable per-game automation list + clear session workflow.
/// Runs on a dedicated STA thread (WPF CollectionView + Dispatcher affinity).
/// Read-only process snapshot may run during VM construction; no suspend/resume
/// of any process is ever invoked here (dedicated test games exist only in an
/// isolated temp library file).
/// </summary>
[TestClass]
public sealed class SessionW1FixTests
{
    private string _tempDir = null!;
    private string? _previousDataDir;
    private string? _previousLocalDataDir;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FrameHub_SessionW1_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _previousDataDir = Environment.GetEnvironmentVariable(AppPaths.DataDirectoryVariable);
        _previousLocalDataDir = Environment.GetEnvironmentVariable(AppPaths.LocalDataDirectoryVariable);
        Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, _tempDir);
        Environment.SetEnvironmentVariable(AppPaths.LocalDataDirectoryVariable, _tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, _previousDataDir);
        Environment.SetEnvironmentVariable(AppPaths.LocalDataDirectoryVariable, _previousLocalDataDir);
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    [TestMethod]
    public void EmptyLibrary_NoGames_ZeroSummary_NoMatchHidden()
    {
        RunOnSta(() =>
        {
            using var vm = CreateVmWithGames(0);
            Assert.IsFalse(vm.HasGames);
            Assert.AreEqual(0, vm.Games.Count);
            Assert.AreEqual("Auto: 0/0", vm.AutoGamesSummary);
            Assert.IsFalse(vm.NoMatchingGamesVisible);
        });
    }

    [TestMethod]
    public void NineGames_AllVisibleWithoutFilter()
    {
        RunOnSta(() =>
        {
            using var vm = CreateVmWithGames(9);
            Assert.IsTrue(vm.HasGames);
            Assert.AreEqual(9, vm.Games.Count);
            Assert.IsNotNull(vm.GamesView);
            Assert.AreEqual(9, CountVisible(vm));
            Assert.AreEqual("Auto: 0/9", vm.AutoGamesSummary);
            Assert.IsFalse(vm.NoMatchingGamesVisible);
        });
    }

    [TestMethod]
    public void Search_FiltersByNameAndProcess_ClearRestores_AllStatesPreserved()
    {
        RunOnSta(() =>
        {
            using var vm = CreateVmWithGames(100);
            vm.Games[7].AutoEnabled = true;
            vm.Games[42].AutoEnabled = true;
            Assert.AreEqual("Auto: 2/100", vm.AutoGamesSummary);

            vm.GameSearchText = "Game 007";
            Assert.AreEqual(1, CountVisible(vm), "Search must narrow to the single matching game.");
            Assert.IsFalse(vm.NoMatchingGamesVisible);

            vm.GameSearchText = "proc42";
            Assert.AreEqual(1, CountVisible(vm), "Search must also match the process name.");
            var only = SingleVisible(vm);
            Assert.AreEqual("proc42", only.ProcessName);

            vm.GameSearchText = "zzz-no-such-game";
            Assert.IsTrue(vm.NoMatchingGamesVisible);
            Assert.AreEqual(0, CountVisible(vm));

            vm.ClearGameSearchCommand.Execute(null);
            Assert.AreEqual(string.Empty, vm.GameSearchText);
            Assert.AreEqual(100, CountVisible(vm));
            Assert.IsFalse(vm.NoMatchingGamesVisible);

            // Filter churn must not touch identity or per-game settings.
            Assert.AreEqual(100, vm.Games.Count);
            Assert.IsTrue(vm.Games[7].AutoEnabled);
            Assert.IsTrue(vm.Games[42].AutoEnabled);
            Assert.AreEqual("Auto: 2/100", vm.AutoGamesSummary);
            Assert.AreEqual("game-007", vm.Games.First(g => g.DisplayName == "Test Game 007").Id);
        });
    }

    [TestMethod]
    public void ReloadLibrary_PreservesAutoEnabledSelectionAndFilter()
    {
        RunOnSta(() =>
        {
            using var vm = CreateVmWithGames(100);
            string id3 = vm.Games[3].Id;
            vm.Games[3].AutoEnabled = true;
            vm.Games[50].AutoEnabled = true;
            string selectedId = vm.Games[50].Id;
            vm.SelectedGame = vm.Games[50];
            vm.GameSearchText = "Game 05";

            int visibleBefore = CountVisible(vm);
            Assert.AreEqual(10, visibleBefore, "Filter 'Game 05' must match Test Game 050-059.");

            vm.ReloadLibraryCommand.Execute(null);

            Assert.AreEqual(100, vm.Games.Count);
            Assert.IsTrue(vm.Games.First(g => g.Id == id3).AutoEnabled);
            Assert.AreEqual(selectedId, vm.SelectedGame?.Id, "Selection must survive refresh by stable Id.");
            Assert.AreEqual("Game 05", vm.GameSearchText, "Filter text must survive refresh.");
            Assert.AreEqual(visibleBefore, CountVisible(vm), "Filter must still apply after refresh.");
            Assert.AreEqual("Auto: 2/100", vm.AutoGamesSummary);
            Assert.IsNotNull(vm.SelectedGame);
            // Per-game settings object identity follows the game Id, not the row.
            Assert.IsTrue(vm.Games.First(g => g.Id == selectedId).AutoEnabled);
        });
    }

    [TestMethod]
    public void LargeLibrary_250Games_FilterAndToggleStayFunctional()
    {
        RunOnSta(() =>
        {
            using var vm = CreateVmWithGames(250);
            Assert.AreEqual(250, CountVisible(vm));
            vm.GameSearchText = "Game 24";
            int visible = CountVisible(vm);
            Assert.IsTrue(visible >= 2 && visible < 250, $"Expected a small filtered subset, got {visible}.");
            foreach (var game in EnumerateVisible(vm))
            {
                game.AutoEnabled = true;
            }

            vm.ClearGameSearchCommand.Execute(null);
            Assert.AreEqual(250, CountVisible(vm));
            Assert.AreEqual($"Auto: {visible}/250", vm.AutoGamesSummary);
        });
    }

    [TestMethod]
    public void SelectionAndControlEdits_NeverStartSession()
    {
        RunOnSta(() =>
        {
            using var vm = CreateVmWithGames(9);
            Assert.IsFalse(vm.IsSessionActive);

            // Churn every selection/edit path a user can reach without pressing Start.
            foreach (var game in vm.Games)
            {
                vm.SelectedGame = game;
                Assert.IsFalse(vm.IsSessionActive);
            }

            foreach (var game in vm.Games)
            {
                game.AutoEnabled = !game.AutoEnabled;
                Assert.IsFalse(vm.IsSessionActive, "Toggling a per-game switch must not suspend anything.");
            }

            vm.GameSearchText = "Game";
            vm.ClearGameSearchCommand.Execute(null);
            vm.AutoModeEnabled = true;
            Assert.IsFalse(vm.IsSessionActive, "Enabling auto-detection arms the trigger; it must not suspend anything by itself.");
            vm.AutoModeEnabled = false;
            Assert.IsFalse(vm.IsSessionActive);

            vm.SelectedGame = vm.Games[0];
            Assert.AreEqual(0, vm.SuspendedProcesses.Count);
        });
    }

    [TestMethod]
    public void WorkflowGuide_PL_DescribesExistingFlow_WithHonestRestoreLimits()
    {
        RunOnSta(() =>
        {
            using var vm = CreateVmWithGames(9);
            Assert.IsFalse(string.IsNullOrWhiteSpace(vm.WorkflowGuideTitle));
            string guide = vm.WorkflowGuideText;
            Assert.IsTrue(guide.Contains("Wybierz", StringComparison.OrdinalIgnoreCase), "Guide must cover game choice.");
            Assert.IsTrue(guide.Contains("Start", StringComparison.OrdinalIgnoreCase), "Guide must cover manual Start.");
            Assert.IsTrue(guide.Contains("automatyz", StringComparison.OrdinalIgnoreCase), "Guide must cover automation.");
            Assert.IsTrue(guide.Contains("podgląd", StringComparison.OrdinalIgnoreCase), "Guide must point at the preview.");
            Assert.IsTrue(guide.Contains("Przywróć", StringComparison.OrdinalIgnoreCase), "Guide must cover restore.");

            string hint = vm.AutoVsManualHint;
            Assert.IsTrue(hint.Contains("Globalny", StringComparison.OrdinalIgnoreCase), "Must distinguish the global switch.");
            Assert.IsTrue(hint.Contains("per-game"), "Must distinguish per-game toggles.");
            Assert.IsTrue(hint.Contains("Ręczny Start"), "Must state manual Start always works.");

            string safety = vm.CandidateSafetyNote;
            Assert.IsTrue(safety.Contains("tymczasowe", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(safety.Contains("nie da się cofnąć", StringComparison.OrdinalIgnoreCase),
                "Restore limits must be stated honestly; no unconditional safe-resume promise.");
            Assert.IsFalse(safety.Contains("gwarant", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(safety.Contains("zawsze", StringComparison.OrdinalIgnoreCase));
        });
    }

    [TestMethod]
    public void WorkflowGuide_EN_MirrorsPL_WithoutUnconditionalPromises()
    {
        RunOnSta(() =>
        {
            var localization = CreateLocalization();
            localization.SetLanguage("en");
            using var vm = CreateVm(localization, gameCount: 9);
            Assert.AreEqual("How a session works", vm.WorkflowGuideTitle);
            StringAssert.Contains(vm.WorkflowGuideText, "Start");
            StringAssert.Contains(vm.WorkflowGuideText, "Restore");
            StringAssert.Contains(vm.WorkflowGuideText, "preview");
            StringAssert.Contains(vm.WorkflowGuideText, "automation");
            StringAssert.Contains(vm.AutoVsManualHint, "Manual Start");
            StringAssert.Contains(vm.CandidateSafetyNote, "cannot be undone");
            Assert.IsFalse(vm.CandidateSafetyNote.Contains("guarantee", StringComparison.OrdinalIgnoreCase));
        });
    }

    [TestMethod]
    public void SessionViewXaml_GameList_IsBoundedSearchableAndVirtualized()
    {
        string xaml = ReadSessionViewXaml();
        StringAssert.Contains(xaml, "ItemsSource=\"{Binding GamesView}\"");
        StringAssert.Contains(xaml, "Text=\"{Binding GameSearchText");
        StringAssert.Contains(xaml, "VirtualizationMode=\"Recycling\"");
        StringAssert.Contains(xaml, "MaxHeight=\"320\"");
        StringAssert.Contains(xaml, "SelectedItem=\"{Binding SelectedGame");
        StringAssert.Contains(xaml, "{Binding AutoGamesSummary}");
        StringAssert.Contains(xaml, "{Binding WorkflowGuideText}");
        StringAssert.Contains(xaml, "{Binding CandidateSafetyNote}");
        Assert.IsFalse(xaml.Contains("<ItemsControl ItemsSource=\"{Binding Games}\""),
            "The unbounded non-virtualized Games ItemsControl must be gone (the manual-start ComboBox legitimately keeps its own Games binding).");
    }

    private SessionOptimizationViewModel CreateVmWithGames(int gameCount)
    {
        var localization = CreateLocalization();
        localization.SetLanguage("pl");
        return CreateVm(localization, gameCount);
    }

    private SessionOptimizationViewModel CreateVm(LocalizationService localization, int gameCount)
    {
        string libraryPath = Path.Combine(_tempDir, $"library_{gameCount}_{Guid.NewGuid():N}.json");
        var library = new LibraryService(libraryPath);
        library.SaveItems(Enumerable.Range(0, gameCount).Select(i => new LibraryItem
        {
            Id = $"game-{i:000}",
            DisplayName = $"Test Game {i:000}",
            ProcessName = $"proc{i}",
            Type = LibraryItemType.Game
        }));

        var coordinator = new SessionOptimizationCoordinator(
            stateService: new SessionStateService(Path.Combine(_tempDir, $"active_{Guid.NewGuid():N}.json")),
            settingsService: new SessionOptimizationSettingsService(Path.Combine(_tempDir, $"session_{Guid.NewGuid():N}.json")),
            libraryService: library);
        var runtime = new AppRuntimeService(Path.Combine(_tempDir, $"runtime_{Guid.NewGuid():N}.json"));
        return new SessionOptimizationViewModel(localization, runtime, coordinator);
    }

    private static LocalizationService CreateLocalization()
        => new(new SettingsService(Path.Combine(Path.GetTempPath(), "FrameHub_SessionW1_" + Guid.NewGuid().ToString("N") + ".json")));

    private static int CountVisible(SessionOptimizationViewModel vm)
    {
        Assert.IsNotNull(vm.GamesView);
        int count = 0;
        foreach (object? _ in vm.GamesView)
        {
            count++;
        }

        return count;
    }

    private static SessionGameOptionViewModel SingleVisible(SessionOptimizationViewModel vm)
    {
        Assert.IsNotNull(vm.GamesView);
        SessionGameOptionViewModel? single = null;
        int count = 0;
        foreach (object item in vm.GamesView)
        {
            count++;
            single = (SessionGameOptionViewModel)item;
        }

        Assert.AreEqual(1, count);
        return single!;
    }

    private static IEnumerable<SessionGameOptionViewModel> EnumerateVisible(SessionOptimizationViewModel vm)
    {
        Assert.IsNotNull(vm.GamesView);
        foreach (object item in vm.GamesView)
        {
            yield return (SessionGameOptionViewModel)item;
        }
    }

    private static string ReadSessionViewXaml()
    {
        string? dir = AppDomain.CurrentDomain.BaseDirectory;
        while (dir != null)
        {
            string candidate = Path.Combine(dir, "FrameHub.App", "Views", "SessionOptimizationView.xaml");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            candidate = Path.Combine(dir, "Views", "SessionOptimizationView.xaml");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        // Fallback: worktree layout places Views next to the test run via relative path.
        string fallback = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "FrameHub.App", "Views", "SessionOptimizationView.xaml"));
        Assert.IsTrue(File.Exists(fallback), "SessionOptimizationView.xaml not found from test run directory.");
        return File.ReadAllText(fallback);
    }

    private static void RunOnSta(Action testBody)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                testBody();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(60)), "STA session W1 case timed out.");
        if (failure is not null)
        {
            Assert.Fail(failure.ToString());
        }
    }
}
