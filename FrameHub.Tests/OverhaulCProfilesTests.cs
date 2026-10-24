using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.App.Views;
using FrameHub.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

/// <summary>
/// UI overhaul part C (Ember C2): Profiles &amp; Rules — conflict detection,
/// editor validation, two-step delete, first-profile CTA and grid virtualization.
/// All profiles are synthetic; nothing is applied to real processes.
/// </summary>
[TestClass]
public sealed class OverhaulCProfilesTests
{
    private string _tempDirectory = null!;
    private string? _previousDataDir;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.EmberC2", Guid.NewGuid().ToString("N"));
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
    public void FindConflictingGroups_OnlyEnabledDuplicatesFlagged()
    {
        var profiles = new List<ProcessProfile>
        {
            NewProfile("a", "cs2", isEnabled: true),
            NewProfile("b", "CS2", isEnabled: true),
            NewProfile("c", "notepad", isEnabled: true),
            NewProfile("d", "notepad", isEnabled: false),
            NewProfile("e", "solo", isEnabled: true)
        };

        var groups = ProfilesViewModel.FindConflictingGroups(profiles);
        Assert.AreEqual(1, groups.Count);
        Assert.IsTrue(groups[0].All(p => p.ProcessName.Equals("cs2", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void DeleteTwoStep_RequestRaisesEvent_ConfirmDeletes()
    {
        RunOnSta(() =>
        {
            using var runtime = CreateRuntime();
            var localization = new LocalizationService(runtime.SettingsService);
            var vm = new ProfilesViewModel(localization, runtime);
            runtime.UpsertProfile(NewProfile("p1", "cs2", isEnabled: true));

            vm.SelectedProfile = vm.Profiles.First(p => p.Id == "p1");
            string? requested = null;
            vm.DeleteConfirmationRequested += name => requested = name;

            vm.RequestDeleteSelected();
            Assert.AreEqual("cs2", requested);
            Assert.IsTrue(runtime.Profiles.Any(p => p.Id == "p1"), "Request alone must not delete.");

            vm.ConfirmDeleteSelected();
            Assert.IsFalse(runtime.Profiles.Any(p => p.Id == "p1"));
        });
    }

    [TestMethod]
    public void EditorValidation_EmptyNameBlocks_ProtectedWarns()
    {
        RunOnSta(() =>
        {
            using var runtime = CreateRuntime();
            var localization = new LocalizationService(runtime.SettingsService);
            localization.SetLanguage("en");
            var vm = new ProfilesViewModel(localization, runtime);
            runtime.UpsertProfile(NewProfile("p1", "cs2", isEnabled: true));
            runtime.UpsertProfile(NewProfile("p2", "vgc", isEnabled: true));

            vm.SelectedProfile = vm.Profiles.First(p => p.Id == "p1");
            Assert.IsTrue(vm.IsEditorValid);
            Assert.IsFalse(vm.IsEditorNoticeVisible);

            vm.ClearAllCommand.Execute(null);
            Assert.IsTrue(vm.IsEditorNoticeVisible, "Zero-core mask must surface a notice, not apply silently.");

            vm.SelectedProfile = vm.Profiles.First(p => p.Id == "p2");
            Assert.IsTrue(vm.IsEditorNoticeVisible);
            Assert.IsTrue(vm.EditorNoticeText.Contains("Protected", StringComparison.OrdinalIgnoreCase));
        });
    }

    [TestMethod]
    public void CreateFirstProfileCommand_NavigatesToProcesses()
    {
        RunOnSta(() =>
        {
            using var runtime = CreateRuntime();
            var localization = new LocalizationService(runtime.SettingsService);
            var vm = new ProfilesViewModel(localization, runtime);
            bool raised = false;
            vm.CreateFirstProfileRequested += () => raised = true;
            vm.CreateFirstProfileCommand.Execute(null);
            Assert.IsTrue(raised);
        });
    }

    [TestMethod]
    public void ProfilesKeys_HaveEnAndPlValues()
    {
        using var runtime = CreateRuntime();
        var localization = new LocalizationService(runtime.SettingsService);
        foreach (string key in new[]
        {
            "Profiles.Count", "Profiles.ConflictWarning", "Profiles.CreateFirst",
            "Profiles.FirstProfileHint", "Profiles.ValidationNameEmpty", "Profiles.ValidationProtected",
            "Profiles.ValidationAllCores", "Profiles.DeleteConfirmTitle", "Profiles.DeleteConfirmMessage",
            "Profiles.DeleteConfirm", "Profiles.Activity.ApplyBlocked", "Dialog.Cancel", "Dialog.Confirm"
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
    public void ProfilesGrid_KeepsVirtualizationWith50SyntheticRows()
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
                for (int i = 0; i < 50; i++)
                {
                    runtime.UpsertProfile(NewProfile("bulk-" + i, "game-" + (i % 20), isEnabled: i % 2 == 0), replaceSameProcessName: false);
                }
                vm.RefreshProfiles();
                Assert.AreEqual(50, vm.Profiles.Count);

                var view = new ProfilesView { DataContext = vm };
                view.Measure(new System.Windows.Size(1400, 900));
                view.Arrange(new System.Windows.Rect(0, 0, 1400, 900));
                view.UpdateLayout();

                var grid = (System.Windows.Controls.DataGrid)view.FindName("ProfilesGrid");
                Assert.IsNotNull(grid, "ProfilesGrid must exist for virtualization assertions.");
                Assert.IsTrue(System.Windows.Controls.VirtualizingPanel.GetIsVirtualizing(grid));
                Assert.AreEqual(
                    System.Windows.Controls.VirtualizationMode.Recycling,
                    System.Windows.Controls.VirtualizingPanel.GetVirtualizationMode(grid));
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "Profiles grid materialization timed out.");
        if (failure is not null) Assert.Fail(failure.ToString());
    }

    private static ProcessProfile NewProfile(string id, string processName, bool isEnabled)
    {
        return new ProcessProfile
        {
            Id = id,
            ProcessName = processName,
            DisplayName = processName,
            Priority = "Normal",
            OptimizationMode = OptimizationMode.CpuSets,
            AffinityMask = 0xFF,
            IsEnabled = isEnabled,
            ApplyCoreOptimization = true,
            ApplyPriority = true
        };
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
