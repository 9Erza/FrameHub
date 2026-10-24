using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.App.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

/// <summary>
/// UI overhaul part C (Ember C4): Logs — level filter, search, bounded count,
/// two-step clear and owner preservation (cap 200 stays in AppRuntimeService).
/// Activity rows are synthetic; the on-disk log file is never written by these tests.
/// </summary>
[TestClass]
public sealed class OverhaulCLogsTests
{
    private string _tempDirectory = null!;
    private string? _previousDataDir;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.EmberC4", Guid.NewGuid().ToString("N"));
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
    public void ClassifyLevel_MapsAllKnownLevels()
    {
        Assert.AreEqual(1, LogsViewModel.ClassifyLevel("Info"));
        Assert.AreEqual(2, LogsViewModel.ClassifyLevel("Warn"));
        Assert.AreEqual(3, LogsViewModel.ClassifyLevel("Error"));
        Assert.AreEqual(3, LogsViewModel.ClassifyLevel("Fatal"));
        Assert.AreEqual(0, LogsViewModel.ClassifyLevel("Verbose"));
        Assert.AreEqual(0, LogsViewModel.ClassifyLevel(null));
    }

    [TestMethod]
    public void Filter_NarrowsByLevelAndSearch()
    {
        RunOnSta(() =>
        {
            using var runtime = CreateRuntime();
            var localization = new LocalizationService(runtime.SettingsService);
            Seed(runtime);
            var vm = new LogsViewModel(localization, runtime);
            Assert.AreEqual(4, CountView(vm));

            vm.SelectedLevelFilterIndex = 2;
            Assert.AreEqual(1, CountView(vm));

            vm.SelectedLevelFilterIndex = 0;
            vm.SearchText = "cs2";
            Assert.AreEqual(2, CountView(vm));

            vm.SearchText = string.Empty;
            Assert.AreEqual(4, CountView(vm));
            Assert.IsTrue(vm.CountText.Contains("4", StringComparison.Ordinal));
        });
    }

    [TestMethod]
    public void ClearTwoStep_RequestRaisesEvent_ConfirmClears()
    {
        RunOnSta(() =>
        {
            using var runtime = CreateRuntime();
            var localization = new LocalizationService(runtime.SettingsService);
            Seed(runtime);
            var vm = new LogsViewModel(localization, runtime);

            bool raised = false;
            vm.ClearConfirmationRequested += () => raised = true;
            vm.ClearActivityCommand.Execute(null);
            Assert.IsTrue(raised);
            Assert.AreEqual(4, runtime.Activity.Count);

            vm.ConfirmClearActivity();
            Assert.AreEqual(0, runtime.Activity.Count);
        });
    }

    [TestMethod]
    public void LogsKeys_HaveEnAndPlValues()
    {
        using var runtime = CreateRuntime();
        var localization = new LocalizationService(runtime.SettingsService);
        foreach (string key in new[]
        {
            "Logs.FilterLabel", "Logs.LevelAll", "Logs.LevelInfo", "Logs.LevelWarnings",
            "Logs.LevelErrors", "Logs.SearchPlaceholder", "Logs.Count", "Logs.BoundedNote",
            "Logs.ClearConfirmTitle", "Logs.ClearConfirmMessage", "Logs.ClearConfirm"
        })
        {
            localization.SetLanguage("en");
            string en = localization.T(key);
            localization.SetLanguage("pl");
            string pl = localization.T(key);
            Assert.IsFalse(string.IsNullOrWhiteSpace(en), key);
            Assert.IsFalse(string.IsNullOrWhiteSpace(pl), key);
            Assert.AreNotEqual(key, en, key);
            Assert.AreNotEqual(key, pl, key);
        }
    }

    [TestMethod]
    public void LogsView_MaterializesWithToolbar()
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
                Seed(runtime);
                var vm = new LogsViewModel(localization, runtime);
                var view = new LogsView { DataContext = vm };
                view.Measure(new System.Windows.Size(1400, 900));
                view.Arrange(new System.Windows.Rect(0, 0, 1400, 900));
                view.UpdateLayout();

                var list = (System.Windows.Controls.ListBox)view.FindName("ActivityList");
                Assert.IsNotNull(list, "ActivityList must exist for virtualization assertions.");
                Assert.IsTrue(System.Windows.Controls.VirtualizingPanel.GetIsVirtualizing(list));
                Assert.AreEqual(4, list.Items.Count);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "Logs view materialization timed out.");
        if (failure is not null) Assert.Fail(failure.ToString());
    }

    private static void Seed(AppRuntimeService runtime)
    {
        runtime.Activity.Clear();
        runtime.Activity.Add(new ActivityItemViewModel { Time = "10:00", Message = "Applied profile for 'cs2'.", Level = "Info" });
        runtime.Activity.Add(new ActivityItemViewModel { Time = "10:01", Message = "Temporary optimization for 'cs2' did not apply.", Level = "Warn" });
        runtime.Activity.Add(new ActivityItemViewModel { Time = "10:02", Message = "Capture failed: device lost.", Level = "Error" });
        runtime.Activity.Add(new ActivityItemViewModel { Time = "10:03", Message = "Ready.", Level = "Info" });
    }

    private static int CountView(LogsViewModel vm)
    {
        int count = 0;
        foreach (var _ in vm.ActivityView) count++;
        return count;
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

    private AppRuntimeService CreateRuntime()
    {
        string settingsPath = Path.Combine(_tempDirectory, $"settings-{Guid.NewGuid():N}.json");
        return new AppRuntimeService(settingsPath);
    }
}
