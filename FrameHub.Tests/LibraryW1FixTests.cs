using System.IO;
using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.App.ViewModels.Library;
using FrameHub.Core.Models;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

/// <summary>
/// W1 B (FH-100-002/003/005/007): Library entry behavior, honest scan naming,
/// selection safety, and localization parity. Presentation wording only — scan,
/// launch, profile, and CPU backend semantics are untouched.
/// </summary>
[TestClass]
public sealed class LibraryW1FixTests
{
    private string _tempDirectory = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.LibraryW1", Guid.NewGuid().ToString("N"));
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

    [TestMethod]
    public void ScanAllLabel_IsHonestAboutPlatformScope()
    {
        using var host = CreateHost();
        string en = LocalizationService.Translate("Library.ScanAll", "en");
        string pl = LocalizationService.Translate("Library.ScanAll", "pl");

        Assert.AreNotEqual("Scan all", en, "FH-100-003: ScanAll must not promise custom folders.");
        Assert.AreNotEqual("Skanuj wszystko", pl, "FH-100-003: ScanAll must not promise custom folders.");
        StringAssert.Contains(en, "Steam", "FH-100-003: honest scan label names the platform scope.");
        StringAssert.Contains(en, "Riot", "FH-100-003: honest scan label names the platform scope.");
        StringAssert.Contains(pl, "Steam", "FH-100-003: honest scan label names the platform scope.");
        StringAssert.Contains(pl, "Riot", "FH-100-003: honest scan label names the platform scope.");
        Assert.IsNotNull(host.ViewModel.ScanCustomCommand, "Custom-folder scan must stay a separate action.");
    }

    [TestMethod]
    public void ManageTitle_IsSecondaryManualEntry()
    {
        string en = LocalizationService.Translate("Library.ManageTitle", "en");
        string pl = LocalizationService.Translate("Library.ManageTitle", "pl");

        Assert.AreEqual("Add game or app", en, "FH-100-003: manage section is the secondary manual entry.");
        Assert.AreEqual("Dodaj grę lub aplikację", pl, "FH-100-003: manage section is the secondary manual entry.");
    }

    [TestMethod]
    public void NewHintKeys_ExistInBothLanguages()
    {
        foreach (string key in new[] { "Library.SelectHint", "Library.ConfigureAction", "Library.InspectorTitle" })
        {
            Assert.AreNotEqual(key, LocalizationService.Translate(key, "en"), $"Missing English key: {key}");
            Assert.AreNotEqual(key, LocalizationService.Translate(key, "pl"), $"Missing Polish key: {key}");
        }

        string formatted = string.Format(LocalizationService.Translate("Library.InspectorTitle", "en"), "Doom");
        StringAssert.Contains(formatted, "Doom", "Inspector title must name the selected game.");
    }

    [TestMethod]
    public void ManageForm_StartsCollapsedInPopulatedLibrary()
    {
        using var host = CreateHost();
        Assert.IsTrue(host.ViewModel.HasLibraryItems, "Test precondition: library must be populated.");
        Assert.IsFalse(host.ViewModel.ShowManageLibrary, "FH-100-003: manual form starts collapsed on entry.");
    }

    [TestMethod]
    public void NavigationEntry_CollapsesManageFormWhenPopulated()
    {
        using var host = CreateHost();
        host.ViewModel.ShowManageLibrary = true;

        host.ViewModel.Activate();

        Assert.IsFalse(host.ViewModel.ShowManageLibrary, "FH-100-003: entering a populated library collapses the manual form.");
    }

    [TestMethod]
    public void ManageOpenCommands_NeverToggleClosed()
    {
        using var host = CreateHost();
        host.ViewModel.ShowManageLibrary = true;

        host.ViewModel.ToggleManageLibraryCommand.Execute(null);
        Assert.IsTrue(host.ViewModel.ShowManageLibrary, "FH-100-003: opening must not accidentally close the form.");

        host.ViewModel.ShowManageLibrary = false;
        host.ViewModel.OpenManageLibraryCommand.Execute(null);
        Assert.IsTrue(host.ViewModel.ShowManageLibrary, "FH-100-003: open command reveals the form.");

        host.ViewModel.ToggleManageLibraryCommand.Execute(null);
        Assert.IsTrue(host.ViewModel.ShowManageLibrary, "FH-100-003: repeated open stays open.");
    }

    [TestMethod]
    public void SelectingGame_NeverLaunchesNorApplies()
    {
        using var host = CreateHost();
        var game = host.Items.First(item => item.Id == "doom");

        host.ViewModel.SelectItemCommand.Execute(game);

        Assert.AreSame(game, host.ViewModel.SelectedItem, "Selection must track the chosen row.");
        Assert.AreEqual(0, host.Launcher.LaunchCalls, "FH-100-007: selection must not launch the game.");
        Assert.IsNull(host.ViewModel.SelectedOptimizationProfile, "FH-100-007: selection must not create or apply a profile.");
        StringAssert.Contains(host.ViewModel.InspectorTitle, game.DisplayName, "FH-100-007: inspector headline names the game.");
    }

    [TestMethod]
    public void SelectingRiotGame_StaysSafe()
    {
        using var host = CreateHost();
        var league = host.Items.First(item => item.Id == "riot-lol");

        host.ViewModel.SelectedItem = league;

        Assert.AreEqual(0, host.Launcher.LaunchCalls, "FH-100-007: selecting a Riot game must not launch anything.");
        Assert.AreEqual("Open in Riot Client", host.ViewModel.LaunchText, "Riot launch wording unchanged.");
        Assert.IsNotNull(host.ViewModel.ApplyProfileButtonTooltip, "Riot protection note must stay visible.");
    }

    [TestMethod]
    public void FilterNoMatch_KeepsPopulatedLibraryState()
    {
        using var host = CreateHost();
        host.ViewModel.SearchText = "zzz-no-such-game-zzz";

        Assert.IsTrue(host.ViewModel.HasLibraryItems, "Filter must not empty the library itself.");
        Assert.IsTrue(host.ViewModel.HasNoFilteredItems, "No-match filter must surface the empty-filter state.");
        Assert.IsFalse(host.ViewModel.HasFilteredItems, "No-match filter must list nothing.");
    }

    [TestMethod]
    public void LibraryViewXaml_KeepsScopedDarkMenuAndHints()
    {
        string? repoRoot = FindRepoRoot();
        if (repoRoot == null)
        {
            Assert.Inconclusive("Repository source root was not discoverable from the test assembly location.");
        }

        string xaml = File.ReadAllText(Path.Combine(repoRoot, "FrameHub.App", "Views", "LibraryView.xaml"));
        StringAssert.Contains(xaml, "LibraryScanContextMenu", "FH-100-002: scoped dark ContextMenu style must exist.");
        StringAssert.Contains(xaml, "LibraryScanMenuItem", "FH-100-002: scoped dark MenuItem style must exist.");
        StringAssert.Contains(xaml, "{Binding SelectHintText}", "FH-100-007: visible selection hint must stay bound.");
        StringAssert.Contains(xaml, "ConfigureActionText", "FH-100-007: discreet row affordance must stay bound.");
        StringAssert.Contains(xaml, "{Binding InspectorTitle}", "FH-100-007: inspector headline must stay bound.");
    }

    private static string? FindRepoRoot()
    {
        string? directory = Path.GetDirectoryName(typeof(LibraryW1FixTests).Assembly.Location);
        while (directory != null && !Directory.Exists(Path.Combine(directory, "FrameHub.App")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        return directory;
    }

    private Host CreateHost()
    {
        string libraryPath = Path.Combine(_tempDirectory, $"library-{Guid.NewGuid():N}.json");
        var libraryService = new LibraryService(libraryPath);
        libraryService.SaveItems(new[]
        {
            new LibraryItem
            {
                Id = "doom",
                DisplayName = "Doom Eternal",
                Source = LibrarySource.Steam,
                Type = LibraryItemType.Game,
                ProcessName = "DOOMx64",
                ExecutablePath = @"C:\Games\Doom\DOOMx64.exe",
                AllowBenchmark = true
            },
            new LibraryItem
            {
                Id = "riot-lol",
                DisplayName = "League of Legends",
                Source = LibrarySource.Riot,
                Type = LibraryItemType.Game,
                ProcessName = "League of Legends",
                ExecutablePath = @"C:\Riot Games\League of Legends\Game\League of Legends.exe",
                LaunchPath = @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Riot Games\League of Legends.lnk",
                AllowBenchmark = false
            }
        });

        var observationProvider = new ProcessObservationSnapshotProvider(
            timeToLive: TimeSpan.Zero,
            enumerate: () => new List<ProcessObservation>(),
            clock: () => DateTimeOffset.UtcNow);
        var scanner = new ProcessScannerService(new ProcessService(), observationProvider);
        var launcher = new CountingLaunchService();

        string settingsPath = Path.Combine(_tempDirectory, $"settings-{Guid.NewGuid():N}.json");
        var runtime = new AppRuntimeService(settingsPath);
        var localization = new LocalizationService(runtime.SettingsService);
        var viewModel = new LibraryViewModel(localization, runtime, libraryService, scanner, launcher);
        return new Host(viewModel, runtime, launcher, viewModel.Items.ToList());
    }

    private sealed record Host(
        LibraryViewModel ViewModel,
        AppRuntimeService Runtime,
        CountingLaunchService Launcher,
        List<LibraryItemViewModel> Items) : IDisposable
    {
        public void Dispose()
        {
            ViewModel.Dispose();
            Runtime.Dispose();
        }
    }

    private sealed class CountingLaunchService : IAppLibraryLaunchService
    {
        public int LaunchCalls { get; private set; }

        public LibraryLaunchResult Launch(LibraryItem? item)
        {
            LaunchCalls++;
            return LibraryLaunchResult.Ok();
        }
    }
}
