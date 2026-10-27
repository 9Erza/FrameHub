using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

/// <summary>
/// FrameHub UI 3.0 bounded refinement — focused regressions for the structural
/// changes: Profiles starter-vs-filtered empty states, embedded Library refresh,
/// Processes inspector placeholder, Hardware ON-span/OFF-content, centered
/// Settings column, single Session game picker and the collapsed-footer discovery
/// row. View-models run on STA with synthetic data; XAML assertions pin the
/// replacement (not overlay) structure.
/// </summary>
[TestClass]
public sealed class Ui30BoundedRefinementTests
{
    private string _tempDirectory = null!;
    private string? _previousDataDir;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.UI30", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _previousDataDir = Environment.GetEnvironmentVariable("FRAMEHUB_DATA_DIR");
        Environment.SetEnvironmentVariable("FRAMEHUB_DATA_DIR", _tempDirectory);
    }

    [TestCleanup]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("FRAMEHUB_DATA_DIR", _previousDataDir);
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { }
        }
    }

    [TestMethod]
    public void HasStoredProfiles_ZeroDataVsPopulated()
    {
        RunOnSta(() =>
        {
            using var runtime = CreateRuntime();
            var localization = new LocalizationService(runtime.SettingsService);
            var vm = new ProfilesViewModel(localization, runtime);

            Assert.IsFalse(vm.HasStoredProfiles, "Fresh isolated store must report zero stored profiles.");
            Assert.IsFalse(vm.IsSearchActive, "Fresh search must be inactive.");

            runtime.UpsertProfile(NewProfile("p1", "cs2", isEnabled: true));

            Assert.IsTrue(vm.HasStoredProfiles, "Stored profile must flip the zero-data flag.");
        });
    }

    [TestMethod]
    public void SearchText_NoMatch_KeepsStoredFlagAndClears()
    {
        RunOnSta(() =>
        {
            using var runtime = CreateRuntime();
            var localization = new LocalizationService(runtime.SettingsService);
            var vm = new ProfilesViewModel(localization, runtime);
            runtime.UpsertProfile(NewProfile("p1", "cs2", isEnabled: true));
            runtime.UpsertProfile(NewProfile("p2", "notepad", isEnabled: true));

            vm.SearchText = "zzz-no-such-process";

            Assert.IsTrue(vm.HasStoredProfiles, "Filtered-to-zero must NOT look like zero data.");
            Assert.IsTrue(vm.IsSearchActive, "Non-empty filter must report active search.");
            Assert.IsTrue(vm.ProfilesView.IsEmpty, "Unmatched filter must empty the bound view.");

            vm.ClearSearchCommand.Execute(null);

            Assert.AreEqual(string.Empty, vm.SearchText, "Clear search must reset the filter.");
            Assert.IsFalse(vm.IsSearchActive, "Cleared filter must report inactive search.");
            Assert.IsFalse(vm.ProfilesView.IsEmpty, "Cleared filter must restore rows.");
        });
    }

    [TestMethod]
    public void NewKeys_HaveEnAndPlValues()
    {
        using var runtime = CreateRuntime();
        var localization = new LocalizationService(runtime.SettingsService);
        foreach (string key in new[] { "Profiles.NoResults", "Profiles.ClearSearch" })
        {
            localization.SetLanguage("en");
            string en = localization.T(key);
            localization.SetLanguage("pl");
            string pl = localization.T(key);
            Assert.IsFalse(string.IsNullOrWhiteSpace(en), key);
            Assert.IsFalse(string.IsNullOrWhiteSpace(pl), key);
            Assert.AreNotEqual(key, en, key);
            Assert.AreNotEqual(key, pl, key);
            Assert.AreNotEqual(en, pl, key + " must differ between EN and PL.");
        }
    }

    [TestMethod]
    public void ProfilesXaml_StarterReplacesGridInsteadOfOverlay()
    {
        string xaml = ReadView("ProfilesView.xaml");
        Assert.IsFalse(xaml.Contains("Panel.ZIndex", StringComparison.Ordinal),
            "ProfilesView must not overlay the empty state above the grid (focus-obscuring overlay).");
        StringAssert.Contains(xaml, "RelativeSource Self", "Grid must collapse on its own HasItems (replacement, not underlay).");
        StringAssert.Contains(xaml, nameof(ProfilesViewModel.ClearSearchCommand), "Filtered-empty card must wire the clear-filter command.");
        StringAssert.Contains(xaml, nameof(ProfilesViewModel.NoResultsText), "Filtered-empty card must show the no-results text.");
        StringAssert.Contains(xaml, nameof(ProfilesViewModel.HasStoredProfiles), "Empty states must key off stored profiles, not the filtered view.");
    }

    [TestMethod]
    public void LibraryXaml_RefreshStatusEmbeddedInPopulatedCatalog()
    {
        string xaml = ReadView("LibraryView.xaml");
        AssertGatedBy(xaml, "Content=\"{Binding RefreshStatusText}\"", "HasLibraryItems",
            "RefreshStatus must live with the populated catalog actions, not in a detached empty-state row.");
    }

    [TestMethod]
    public void ProcessesXaml_InspectorCollapsesWithoutSelection()
    {
        // UI 3.0 note: the inspector column intentionally collapses when nothing
        // is selected (code-behind gives the table full width for column
        // reachability — QA E4). The placeholder variant was rejected because the
        // responsive contract assigns that column zero width exactly when it
        // would show. This pins the intended collapse contract.
        string xaml = ReadView("ProcessesView.xaml");
        StringAssert.Contains(xaml, "x:Name=\"ProcessInspector\"", "Inspector must keep its code-behind identity.");
        AssertGatedBy(xaml, "x:Name=\"ProcessInspector\"", "{Binding SelectedProcess}\" Value=\"{x:Null}\"",
            "Null selection must collapse the inspector so the table owns the width.");
    }

    [TestMethod]
    public void HardwareXaml_BehaviorSpansWhenOn_OffStaysHonest()
    {
        string xaml = ReadView("HardwareView.xaml");
        StringAssert.Contains(xaml, "Grid.ColumnSpan", "Behavior panel must span the void column when sensors are ON.");
        StringAssert.Contains(xaml, "{Binding StorageInfoText}", "OFF card must reuse the honest storage-unavailability text.");
    }

    [TestMethod]
    public void SettingsXaml_ProductColumnLeftAnchored()
    {
        // QA t_ca0f6d2d P3-3: the 960-DIP readable column must anchor LEFT so its
        // H1/page gutter matches the other screens — centered MaxWidth pushed the
        // Settings H1 157 DIP inward at 1540 wide.
        string xaml = ReadView("SettingsView.xaml");
        StringAssert.Contains(xaml, "HorizontalAlignment=\"Left\" MaxWidth=\"960\"",
            "Constrained Settings column must be left-anchored for a consistent page gutter, not centered.");
    }

    [TestMethod]
    public void MainWindowXaml_CollapsedFooterKeepsDiscoveryRow()
    {
        string? root = FindRepoRoot();
        Assert.IsNotNull(root, "Repo root not found.");
        string xaml = File.ReadAllText(Path.Combine(root, "FrameHub.App", "MainWindow.xaml"));
        StringAssert.Contains(xaml, "FooterExpander", "Footer Expander must be addressable for the discovery row gate.");
        StringAssert.Contains(xaml, "AutomationProperties.Name=\"{Binding SupportTooltip}\" Width=\"32\"",
            "Collapsed footer must keep a slim support shortcut with the real command and tooltip.");
        StringAssert.Contains(xaml, "AutomationProperties.Name=\"{Binding RepositoryTooltip}\" Width=\"32\"",
            "Collapsed footer must keep a slim repository shortcut with the real command and tooltip.");
    }

    [TestMethod]
    public void MainWindowXaml_RailFooterKeepsDiscoveryIcons()
    {
        // QA t_ca0f6d2d P2: the narrow 64-DIP rail must ALSO keep GitHub + BMC
        // subtly visible without expanding — stacked 32px icons with the same
        // commands, tooltips, UIA names and focusability as the wide row.
        string? root = FindRepoRoot();
        Assert.IsNotNull(root, "Repo root not found.");
        string xaml = File.ReadAllText(Path.Combine(root, "FrameHub.App", "MainWindow.xaml"));
        int rail = xaml.IndexOf("x:Name=\"DiscoveryRowRail\"", StringComparison.Ordinal);
        Assert.IsTrue(rail >= 0, "Narrow rail must keep its own discovery row (QA P2).");
        string tail = xaml.Substring(rail, Math.Min(1600, xaml.Length - rail));
        StringAssert.Contains(tail, "Orientation=\"Vertical\"",
            "Rail discovery icons must stack vertically to fit the 64-DIP lane.");
        StringAssert.Contains(tail, "Command=\"{Binding OpenRepositoryCommand}\"",
            "Rail row must reuse the real repository command.");
        StringAssert.Contains(tail, "Command=\"{Binding OpenSupportCommand}\"",
            "Rail row must reuse the real support command.");
        StringAssert.Contains(tail, "AutomationProperties.Name=\"{Binding RepositoryTooltip}\"",
            "Rail repository icon must keep the localized UIA name.");
        StringAssert.Contains(tail, "AutomationProperties.Name=\"{Binding SupportTooltip}\"",
            "Rail support icon must keep the localized UIA name.");
        StringAssert.Contains(tail,
            "Binding IsNarrowWindow, RelativeSource={RelativeSource AncestorType=Window}}\" Value=\"True\"",
            "Rail row must show exactly in narrow mode (the drawer keeps the full labeled links).");
    }

    [TestMethod]
    public void MainWindowXaml_FooterExpanderKeepsThemeAndBottomRhythm()
    {
        // Same P1 class as ProfilesGrid: the local Expander style must extend the
        // implicit theme, and the expanded panel must keep the 12px bottom rhythm (P3-1).
        string? root = FindRepoRoot();
        Assert.IsNotNull(root, "Repo root not found.");
        string xaml = File.ReadAllText(Path.Combine(root, "FrameHub.App", "MainWindow.xaml"));
        StringAssert.Contains(xaml, "BasedOn=\"{StaticResource {x:Type Expander}}\"",
            "Footer Expander local style must extend the implicit Expander theme, not replace it.");
        StringAssert.Contains(xaml, "Setter Property=\"Margin\" Value=\"12,4,12,12\"",
            "Expanded footer must keep the pre-3.0 12px bottom margin (P3-1).");
    }

    /// <summary>
    /// QA t_ca0f6d2d P1/P3-2: a REAL ProfilesView on an STA thread with loaded
    /// application theme resources and a populated ItemsSource must resolve the
    /// dark DataGrid theme. Removing BasedOn turns the body light, shrinks rows
    /// to the default compact height and restores grid lines — every assertion
    /// below fails on that regression. StringAssert on XAML text cannot do this.
    /// </summary>
    [TestMethod]
    public void ProfilesGrid_Populated_InheritsApplicationDataGridTheme()
    {
        RunProfilesScenario(seedProfiles: true, scenario: (view, vm) =>
        {
            var grid = (System.Windows.Controls.DataGrid)view.FindName("ProfilesGrid");
            Assert.IsNotNull(grid, "ProfilesGrid must exist on the live instance.");
            Assert.IsTrue(grid.HasItems, "Fixture must populate the grid (3 profiles).");

            Assert.IsNotNull(grid.Style?.BasedOn,
                "ProfilesGrid style must BasedOn the implicit DataGrid theme; without it the dark theme is replaced (QA P1).");

            var theme = new System.Windows.ResourceDictionary
            {
                Source = new Uri("/FrameHub.App;component/Themes/Colors.xaml", UriKind.Relative)
            };
            var expectedBackground = ((System.Windows.Media.SolidColorBrush)theme["BrushBackgroundAlt"]!).Color;
            var expectedForeground = ((System.Windows.Media.SolidColorBrush)theme["BrushTextPrimary"]!).Color;

            var actualBackground = grid.Background as System.Windows.Media.SolidColorBrush;
            Assert.IsNotNull(actualBackground, "Populated grid body must be a flat theme brush.");
            Assert.AreEqual(expectedBackground, actualBackground.Color,
                "Grid body must resolve the dark theme background, not a default light fill (P1 contrast 1.03:1).");
            var actualForeground = grid.Foreground as System.Windows.Media.SolidColorBrush;
            Assert.IsNotNull(actualForeground, "Populated grid text must be a flat theme brush.");
            Assert.AreEqual(expectedForeground, actualForeground.Color,
                "Grid text must resolve the theme primary brush for readability on the dark body.");
            Assert.AreEqual(40.0, grid.RowHeight,
                "RowHeight must be the themed 40 DIP, not the default compact height (P1 measured 22px).");
            Assert.AreEqual(System.Windows.Controls.DataGridGridLinesVisibility.None, grid.GridLinesVisibility,
                "GridLinesVisibility must be the themed None, not default lines (P1).");
            Assert.AreEqual(System.Windows.Visibility.Visible, grid.Visibility,
                "Populated grid must stay visible.");

            vm.SelectedProfile = vm.Profiles[0];
            view.UpdateLayout();
            Assert.AreEqual(vm.Profiles[0], grid.SelectedItem, "Selection must round-trip to the grid.");
            var row = grid.ItemContainerGenerator.ContainerFromIndex(0) as System.Windows.Controls.DataGridRow;
            Assert.IsNotNull(row, "Populated grid must realize row containers.");
            Assert.AreEqual(40.0, row.ActualHeight, 0.5, "Realized rows must keep the themed 40 DIP height.");
        });
    }

    [TestMethod]
    public void ProfilesGrid_EmptyAndFiltered_CollapseOnLiveInstance()
    {
        RunProfilesScenario(seedProfiles: false, scenario: (view, vm) =>
        {
            var grid = (System.Windows.Controls.DataGrid)view.FindName("ProfilesGrid");
            Assert.IsNotNull(grid, "ProfilesGrid must exist on the live instance.");
            Assert.AreEqual(System.Windows.Visibility.Collapsed, grid.Visibility,
                "Empty store must collapse the grid so the onboarding card owns the cell.");

            SeedProfiles(vm);
            view.UpdateLayout();
            Assert.AreEqual(System.Windows.Visibility.Visible, grid.Visibility,
                "Populated grid must become visible.");

            vm.SearchText = "zzz-no-such-process";
            view.UpdateLayout();
            Assert.AreEqual(System.Windows.Visibility.Collapsed, grid.Visibility,
                "Zero filter matches must collapse the grid so the no-results card owns the cell.");
            Assert.IsTrue(vm.HasStoredProfiles, "Filtered-to-zero must NOT look like zero data.");

            vm.ClearSearchCommand.Execute(null);
            view.UpdateLayout();
            Assert.AreEqual(System.Windows.Visibility.Visible, grid.Visibility,
                "Clear search must restore the populated grid.");
            Assert.IsTrue(grid.HasItems, "Cleared filter must restore rows.");
        });
    }

    [TestMethod]
    public void SessionXaml_SingleGamePicker_NoThinSeparators()
    {
        string xaml = ReadView("SessionOptimizationView.xaml");
        Assert.AreEqual(1, CountOccurrences(xaml, "SelectedItem=\"{Binding SelectedGame, Mode=TwoWay}\""),
            "Session must keep a single game picker (Manual card); automation reuses the same selection.");
        Assert.AreEqual(0, CountOccurrences(xaml, "Height=\"1\" Background=\"{StaticResource BrushBorderSubtle}\" Margin=\"0,12\""),
            "Advanced options must group by spacing, not 1px hairlines.");
        StringAssert.Contains(xaml, "ReloadLibraryButtonText", "Reload action must survive the picker dedupe.");
    }

    private static ProcessProfile NewProfile(string id, string processName, bool isEnabled)
    {
        return NewProfile(id, processName, isEnabled, OptimizationMode.CpuSets, "Normal");
    }

    private static ProcessProfile NewProfile(
        string id, string processName, bool isEnabled, OptimizationMode mode, string priority)
    {
        return new ProcessProfile
        {
            Id = id,
            ProcessName = processName,
            DisplayName = processName,
            IsEnabled = isEnabled,
            OptimizationMode = mode,
            AffinityMask = 0xFF,
            Priority = priority,
            ApplyCoreOptimization = true,
            ApplyPriority = true
        };
    }

    /// <summary>
    /// Three valid fixtures (Affinity + CpuSets modes, canonical raw priorities):
    /// proves the populated grid renders real rows, not an empty shell.
    /// </summary>
    private void SeedProfiles(ProfilesViewModel vm)
    {
        var runtime = GetRuntime(vm);
        runtime.UpsertProfile(NewProfile("p1", "notepad", isEnabled: true, OptimizationMode.Affinity, "Normal"));
        runtime.UpsertProfile(NewProfile("p2", "mspaint", isEnabled: true, OptimizationMode.CpuSets, "High"));
        runtime.UpsertProfile(NewProfile("p3", "calc", isEnabled: false, OptimizationMode.Affinity, "AboveNormal"));
    }

    private static AppRuntimeService GetRuntime(ProfilesViewModel vm)
    {
        var field = typeof(ProfilesViewModel).GetField("_runtime",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.IsNotNull(field, "ProfilesViewModel must keep its runtime for fixture seeding.");
        return (AppRuntimeService)field.GetValue(vm)!;
    }

    /// <summary>
    /// Builds a REAL ProfilesView on one STA thread with loaded application theme
    /// resources (same pattern as OverhaulCScreenshots): DataContext, measure and
    /// arrange so bindings, the HasItems collapse trigger and row virtualization
    /// actually run. The whole scenario — including assertions — stays on that
    /// thread; only plain fields cross back.
    /// </summary>
    private void RunProfilesScenario(bool seedProfiles, Action<FrameHub.App.Views.ProfilesView, ProfilesViewModel> scenario)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current == null)
                {
                    var application = new FrameHub.App.App();
                    application.InitializeComponent();
                }

                using var runtime = CreateRuntime();
                var localization = new LocalizationService(runtime.SettingsService);
                var vm = new ProfilesViewModel(localization, runtime);
                if (seedProfiles)
                {
                    SeedProfiles(vm);
                }

                var view = new FrameHub.App.Views.ProfilesView { DataContext = vm };
                view.Measure(new System.Windows.Size(1200, 800));
                view.Arrange(new System.Windows.Rect(0, 0, 1200, 800));
                view.UpdateLayout();

                scenario(view, vm);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(60)), "ProfilesView STA scenario timed out.");
        if (failure is not null) Assert.Fail(failure.ToString());
    }

    private AppRuntimeService CreateRuntime()
    {
        string settingsPath = Path.Combine(_tempDirectory, $"settings-{Guid.NewGuid():N}.json");
        return new AppRuntimeService(settingsPath);
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "STA test host timed out.");
        if (failure is not null) Assert.Fail(failure.ToString());
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static void AssertGatedBy(string xaml, string binding, string gate, string message)
    {
        int index = xaml.IndexOf(binding, StringComparison.Ordinal);
        Assert.IsTrue(index >= 0, "Expected binding " + binding + " in view. " + message);
        int window = Math.Min(index + 500, xaml.Length) - index;
        StringAssert.Contains(xaml.Substring(index, window), gate, message);
    }

    private static string ReadView(string fileName)
    {
        string? root = FindRepoRoot();
        Assert.IsNotNull(root, "Repo root not found.");
        return File.ReadAllText(Path.Combine(root, "FrameHub.App", "Views", fileName));
    }

    private static string? FindRepoRoot()
    {
        string? directory = Path.GetDirectoryName(typeof(Ui30BoundedRefinementTests).Assembly.Location);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory, "FrameHub.slnx")) ||
                File.Exists(Path.Combine(directory, "FrameHub.sln")))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }
}
