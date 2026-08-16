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
/// CP4.5 coverage: honest Riot Library action presentation. Wording/availability only;
/// launch, benchmark, and optimization backend semantics are untouched.
/// </summary>
[TestClass]
public sealed class RiotLibraryUxTests
{
    private string _tempDirectory = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.RiotUxTests", Guid.NewGuid().ToString("N"));
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
    public void RiotItemSelected_LaunchTextUsesRiotClientWording()
    {
        using var host = CreateViewModelHost();
        var league = host.Items.First(item => item.Id == "riot-lol");
        host.ViewModel.SelectedItem = league;

        Assert.AreEqual("Open in Riot Client", host.ViewModel.LaunchText, "Riot items must use the honest Riot Client wording.");
    }

    [TestMethod]
    public void NonRiotItemSelected_LaunchTextStaysGeneric()
    {
        using var host = CreateViewModelHost();
        var cs2 = host.Items.First(item => item.Id == "cs2");
        host.ViewModel.SelectedItem = cs2;

        Assert.AreEqual("Launch", host.ViewModel.LaunchText, "Non-Riot items must keep the generic launch label.");
    }

    [TestMethod]
    public void SwitchingSelection_UpdatesLaunchTextWithoutRecreatingViewModel()
    {
        using var host = CreateViewModelHost();
        var league = host.Items.First(item => item.Id == "riot-lol");
        var cs2 = host.Items.First(item => item.Id == "cs2");

        host.ViewModel.SelectedItem = league;
        Assert.AreEqual("Open in Riot Client", host.ViewModel.LaunchText);

        host.ViewModel.SelectedItem = cs2;
        Assert.AreEqual("Launch", host.ViewModel.LaunchText);

        host.ViewModel.SelectedItem = league;
        Assert.AreEqual("Open in Riot Client", host.ViewModel.LaunchText);
    }

    [TestMethod]
    public void BenchmarkCommand_AllowBenchmarkFalse_CannotExecute()
    {
        using var host = CreateViewModelHost();
        var custom = host.Items.First(item => item.Id == "custom-ineligible");
        Assert.IsFalse(custom.Item.AllowBenchmark, "Test precondition: the custom item must be benchmark-ineligible.");

        host.ViewModel.SelectedItem = custom;

        Assert.IsFalse(host.ViewModel.BenchmarkSelectedCommand.CanExecute(null),
            "Benchmark must not appear actionable for AllowBenchmark == false items.");
        Assert.AreEqual("Benchmarking is unavailable for this game.", host.ViewModel.BenchmarkButtonTooltip);
    }

    [TestMethod]
    public void BenchmarkCommand_EligibleGame_CanExecute()
    {
        using var host = CreateViewModelHost();
        var cs2 = host.Items.First(item => item.Id == "cs2");
        var league = host.Items.First(item => item.Id == "riot-lol");
        Assert.IsTrue(cs2.Item.AllowBenchmark, "Test precondition: the normal game must be benchmark-eligible.");
        Assert.IsTrue(league.Item.AllowBenchmark, "Trusted Riot game is automatically benchmark-eligible on load.");

        host.ViewModel.SelectedItem = cs2;
        Assert.IsTrue(host.ViewModel.BenchmarkSelectedCommand.CanExecute(null), "Eligible games must keep today's behavior.");
        Assert.IsNull(host.ViewModel.BenchmarkButtonTooltip, "No eligibility tooltip is needed for eligible games.");

        host.ViewModel.SelectedItem = league;
        Assert.IsTrue(host.ViewModel.BenchmarkSelectedCommand.CanExecute(null), "Trusted Riot game benchmark button must be actionable.");
        Assert.IsNull(host.ViewModel.BenchmarkButtonTooltip);
    }

    [TestMethod]
    public void OptimizeCommand_RiotStaysAvailable_WithHonestTooltip()
    {
        using var host = CreateViewModelHost();
        var league = host.Items.First(item => item.Id == "riot-lol");
        var cs2 = host.Items.First(item => item.Id == "cs2");

        host.ViewModel.SelectedItem = league;
        Assert.IsTrue(host.ViewModel.ApplyLinkedProfileCommand.CanExecute(null),
            "Profile management remains available for Riot; native process guards stay authoritative in the backend.");
        StringAssert.Contains(host.ViewModel.ApplyProfileButtonTooltip ?? string.Empty, "protected Riot game processes are not modified");

        host.ViewModel.SelectedItem = cs2;
        Assert.IsNull(host.ViewModel.ApplyProfileButtonTooltip, "The Riot note must not appear for normal games.");
    }

    [TestMethod]
    public void RiotLibraryUx_NewLocalizationKeys_HaveEnPlParity()
    {
        foreach (string key in new[] { "Library.LaunchRiot", "Library.BenchmarkUnavailable", "Library.RiotOptimizeNote" })
        {
            Assert.IsTrue(LocalizationService.EnglishKeys.Contains(key), $"Missing English key {key}");
            Assert.IsTrue(LocalizationService.PolishKeys.Contains(key), $"Missing Polish key {key}");
        }

        Assert.AreEqual("Otwórz w Riot Client", LocalizationService.Translate("Library.LaunchRiot", "pl"));
        StringAssert.Contains(LocalizationService.Translate("Library.BenchmarkUnavailable", "pl"), "Benchmarkowanie");
        StringAssert.Contains(LocalizationService.Translate("Library.RiotOptimizeNote", "pl"), "nie są modyfikowane");
    }

    [TestMethod]
    public void BenchmarkButton_DisabledStateTooltip_IsShownViaShowOnDisabled()
    {
        // The eligibility tooltip only matters while the button is disabled (AllowBenchmark == false);
        // WPF suppresses tooltips on disabled controls unless ShowOnDisabled is enabled on that button.
        string? repoRoot = FindRepoRoot();
        if (repoRoot == null)
        {
            Assert.Inconclusive("Repository source root was not discoverable from the test assembly location.");
        }

        string xaml = File.ReadAllText(Path.Combine(repoRoot, "FrameHub.App", "Views", "LibraryView.xaml"));
        int buttonStart = xaml.IndexOf("Command=\"{Binding BenchmarkSelectedCommand}\"", StringComparison.Ordinal);
        Assert.IsTrue(buttonStart >= 0, "LibraryView must contain the Benchmark button.");
        int lineStart = xaml.LastIndexOf('<', buttonStart);
        int lineEnd = xaml.IndexOf("/>", buttonStart, StringComparison.Ordinal);
        Assert.IsTrue(lineStart >= 0 && lineEnd > buttonStart, "Benchmark button element boundaries must be found.");
        string benchmarkButton = xaml.Substring(lineStart, lineEnd + 2 - lineStart);

        StringAssert.Contains(benchmarkButton, "ToolTip=\"{Binding BenchmarkButtonTooltip}\"", "Benchmark button must bind the eligibility tooltip.");
        StringAssert.Contains(benchmarkButton, "ToolTipService.ShowOnDisabled=\"True\"", "Benchmark button must show its tooltip while disabled.");
    }

    private static string? FindRepoRoot()
    {
        string? directory = Path.GetDirectoryName(typeof(RiotLibraryUxTests).Assembly.Location);
        while (directory != null && !Directory.Exists(Path.Combine(directory, "FrameHub.App")))
        {
            directory = Path.GetDirectoryName(directory);
        }
        return directory;
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
                ExecutablePath = @"C:\Riot Games\League of Legends\Game\League of Legends.exe",
                AllowBenchmark = false
            },
            new LibraryItem
            {
                Id = "custom-ineligible",
                DisplayName = "Custom Ineligible Game",
                Source = LibrarySource.Manual,
                Type = LibraryItemType.Game,
                ProcessName = "customgame",
                ExecutablePath = @"C:\Games\Custom\customgame.exe",
                AllowBenchmark = false
            },
            new LibraryItem
            {
                Id = "cs2",
                DisplayName = "Counter-Strike 2",
                Type = LibraryItemType.Game,
                ProcessName = "cs2",
                ExecutablePath = @"C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive\game\bin\win64\cs2.exe",
                AllowBenchmark = true
            }
        });

        var observationProvider = new ProcessObservationSnapshotProvider(
            timeToLive: TimeSpan.Zero,
            enumerate: () => new List<ProcessObservation>(),
            clock: () => DateTimeOffset.UtcNow);
        var scanner = new ProcessScannerService(new ProcessService(), observationProvider);
        var launcher = new NoopLaunchService();

        string settingsPath = Path.Combine(_tempDirectory, $"settings-{Guid.NewGuid():N}.json");
        var runtime = new AppRuntimeService(settingsPath);
        var localization = new LocalizationService(runtime.SettingsService);
        var viewModel = new LibraryViewModel(localization, runtime, libraryService, scanner, launcher);
        return new ViewModelHost(viewModel, runtime, viewModel.Items.ToList());
    }

    private sealed record ViewModelHost(
        LibraryViewModel ViewModel,
        AppRuntimeService Runtime,
        System.Collections.Generic.List<LibraryItemViewModel> Items) : IDisposable
    {
        public void Dispose()
        {
            ViewModel.Dispose();
            Runtime.Dispose();
        }
    }

    private sealed class NoopLaunchService : IAppLibraryLaunchService
    {
        public LibraryLaunchResult Launch(LibraryItem? item) => LibraryLaunchResult.Fail("not_launchable");
    }
}
