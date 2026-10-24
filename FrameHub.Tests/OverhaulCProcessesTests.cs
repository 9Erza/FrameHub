using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.App.Views;
using FrameHub.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

/// <summary>
/// UI overhaul part C (Ember C1): Processes &amp; CPU professional grid —
/// search/filter, protection status, mutation gate and WPF virtualization.
/// All rows are synthetic; no real process is observed or mutated.
/// </summary>
[TestClass]
public sealed class OverhaulCProcessesTests
{
    private string _tempDirectory = null!;
    private string? _previousDataDir;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.EmberC1", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        // Isolate ALL user-data paths (profiles.json, logs) from the real %APPDATA%.
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
    public void ResolveStatusKey_ProtectedWinsOverManaged()
    {
        Assert.AreEqual("Processes.StatusProtected", ProcessesViewModel.ResolveStatusKey("vgc", hasManagedProfile: true));
        Assert.AreEqual("Processes.StatusProtected", ProcessesViewModel.ResolveStatusKey("League of Legends", hasManagedProfile: true));
        Assert.AreEqual("Processes.StatusManaged", ProcessesViewModel.ResolveStatusKey("cs2", hasManagedProfile: true));
        Assert.AreEqual("Processes.StatusAvailable", ProcessesViewModel.ResolveStatusKey("cs2", hasManagedProfile: false));
        Assert.AreEqual("Processes.StatusAvailable", ProcessesViewModel.ResolveStatusKey(null, hasManagedProfile: false));
    }

    [TestMethod]
    public void ResolveStatusKey_AllKeysHaveEnAndPlValues()
    {
        using var runtime = CreateRuntime();
        var localization = new LocalizationService(runtime.SettingsService);
        foreach (string key in new[]
        {
            "Processes.SearchPlaceholder", "Processes.Status", "Processes.StatusProtected",
            "Processes.StatusManaged", "Processes.StatusAvailable", "Processes.PhysicalCores",
            "Processes.ThreadCores", "Processes.DetailsTitle", "Processes.Executable",
            "Processes.ExecutableUnknown", "Processes.Topology", "Processes.TopologyValue",
            "Processes.FilteredCount", "Processes.ProtectionReason", "Processes.BenchmarkActiveReason",
            "Processes.Activity.ApplyBlocked"
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
            // Note: en == pl is legitimate for shared words (e.g. "Status", "RAM").
        }
    }

    [TestMethod]
    public void SearchText_FiltersByNameOrPid()
    {
        RunOnSta(() =>
        {
            using var runtime = CreateRuntime();
            var localization = new LocalizationService(runtime.SettingsService);
            using var vm = new ProcessesViewModel(localization, runtime);
            vm.Processes.Add(new ProcessItem { Id = 100, Name = "cs2" });
            vm.Processes.Add(new ProcessItem { Id = 200, Name = "notepad" });
            vm.Processes.Add(new ProcessItem { Id = 300, Name = "cs2_helper" });

            Assert.AreEqual(3, CountView(vm));

            vm.SearchText = "cs2";
            Assert.AreEqual(2, CountView(vm));

            vm.SearchText = "200";
            Assert.AreEqual(1, CountView(vm));

            vm.SearchText = string.Empty;
            Assert.AreEqual(3, CountView(vm));
            Assert.IsTrue(vm.FilteredCountText.Contains("3", StringComparison.Ordinal));
        });
    }

    [TestMethod]
    public void ProtectedSelection_DisablesApplyWithReason()
    {
        RunOnSta(() =>
        {
            using var runtime = CreateRuntime();
            var localization = new LocalizationService(runtime.SettingsService);
            localization.SetLanguage("en");
            using var vm = new ProcessesViewModel(localization, runtime);
            var guarded = new ProcessItem { Id = 1, Name = "vgc", IsProtected = true, StatusDisplay = "Protected" };
            var normal = new ProcessItem { Id = 2, Name = "cs2", IsProtected = false, StatusDisplay = "Available" };
            vm.Processes.Add(guarded);
            vm.Processes.Add(normal);

            vm.SelectedProcess = guarded;
            Assert.IsFalse(vm.CanApplyToSelection);
            Assert.IsTrue(vm.IsApplyDisabledReasonVisible);
            Assert.IsTrue(vm.ApplyDisabledReason.Contains("Protected", StringComparison.OrdinalIgnoreCase));

            vm.SelectedProcess = normal;
            Assert.IsFalse(vm.IsBenchmarkActive, "Fresh coordinator must not report an active capture.");
            Assert.IsTrue(vm.CanApplyToSelection);
            Assert.IsFalse(vm.IsApplyDisabledReasonVisible);
        });
    }

    [TestMethod]
    public void ProcessesGrid_KeepsVirtualizationWith1000SyntheticRows()
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
                using var vm = new ProcessesViewModel(localization, runtime);
                for (int i = 0; i < 1000; i++)
                {
                    vm.Processes.Add(new ProcessItem { Id = 1000 + i, Name = "synthetic-" + i });
                }

                var view = new ProcessesView { DataContext = vm };
                view.Measure(new System.Windows.Size(1400, 900));
                view.Arrange(new System.Windows.Rect(0, 0, 1400, 900));
                view.UpdateLayout();

                var grid = (System.Windows.Controls.DataGrid)view.FindName("ProcessGrid");
                Assert.IsNotNull(grid, "ProcessGrid must exist for virtualization assertions.");
                Assert.IsTrue(System.Windows.Controls.VirtualizingPanel.GetIsVirtualizing(grid));
                Assert.AreEqual(
                    System.Windows.Controls.VirtualizationMode.Recycling,
                    System.Windows.Controls.VirtualizingPanel.GetVirtualizationMode(grid));
                Assert.AreEqual(1000, CountView(vm));
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "Processes grid materialization timed out.");
        if (failure is not null) Assert.Fail(failure.ToString());
    }

    private static int CountView(ProcessesViewModel vm)
    {
        int count = 0;
        foreach (var _ in vm.ProcessesView) count++;
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
