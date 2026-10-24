using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.App.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

/// <summary>
/// UI overhaul part C (Ember C3): Hardware Monitor — storage card with honest
/// unavailable state, only real sensor values, OFF-state panel. No hardware is
/// touched; all inputs are synthetic.
/// </summary>
[TestClass]
public sealed class OverhaulCHardwareTests
{
    private string _tempDirectory = null!;
    private string? _previousDataDir;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.EmberC3", Guid.NewGuid().ToString("N"));
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
    public void FormatStorageInfo_NeverInventsValues()
    {
        Assert.AreEqual("--", HardwareViewModel.FormatStorageInfo(false, "SSD: 41°C", "unavailable"));
        Assert.AreEqual("unavailable", HardwareViewModel.FormatStorageInfo(true, "No data", "unavailable"));
        Assert.AreEqual("unavailable", HardwareViewModel.FormatStorageInfo(true, "  ", "unavailable"));
        Assert.AreEqual("unavailable", HardwareViewModel.FormatStorageInfo(true, null, "unavailable"));
        Assert.AreEqual("NVMe: 43°C", HardwareViewModel.FormatStorageInfo(true, "NVMe: 43°C", "unavailable"));
    }

    [TestMethod]
    public void StorageKeys_HaveEnAndPlValues()
    {
        using var runtime = CreateRuntime();
        var localization = new LocalizationService(runtime.SettingsService);
        foreach (string key in new[] { "Hardware.Storage", "Hardware.StorageUnavailable" })
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
    public void HardwareView_MaterializesWithStorageCard()
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
                using var vm = new HardwareViewModel(localization, runtime);
                var view = new HardwareView { DataContext = vm };
                view.Measure(new System.Windows.Size(1400, 900));
                view.Arrange(new System.Windows.Rect(0, 0, 1400, 900));
                view.UpdateLayout();
                Assert.AreEqual("--", vm.StorageInfoText);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "Hardware view materialization timed out.");
        if (failure is not null) Assert.Fail(failure.ToString());
    }

    private AppRuntimeService CreateRuntime()
    {
        string settingsPath = Path.Combine(_tempDirectory, $"settings-{Guid.NewGuid():N}.json");
        return new AppRuntimeService(settingsPath);
    }
}
