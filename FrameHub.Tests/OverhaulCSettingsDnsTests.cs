using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.App.Views;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Network;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

/// <summary>
/// UI overhaul part C (Ember C5): Settings subnavigation, normal restart event,
/// read-only DNS panel and the successor early-crash handshake. DNS probes use
/// only validation paths (invalid IP / empty host) so no network traffic leaves
/// the machine; adapter enumeration is read-only.
/// </summary>
[TestClass]
public sealed class OverhaulCSettingsDnsTests
{
    private string _tempDirectory = null!;
    private string? _previousDataDir;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.EmberC5", Guid.NewGuid().ToString("N"));
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
    public void SettingsCategories_HaveSixLocalizedEntries()
    {
        RunOnSta(() =>
        {
            using var runtime = CreateRuntime();
            var localization = new LocalizationService(runtime.SettingsService);
            foreach (string language in new[] { "en", "pl" })
            {
                localization.SetLanguage(language);
                var vm = new SettingsViewModel(localization, runtime);
                Assert.AreEqual(6, vm.SettingsCategories.Count);
                Assert.IsTrue(vm.SettingsCategories.All(c => !string.IsNullOrWhiteSpace(c)), language);
                Assert.IsTrue(vm.IsGeneralVisible);
            }
        });
    }

    [TestMethod]
    public void SelectedCategory_SwitchesVisibilityExclusively()
    {
        RunOnSta(() =>
        {
            using var runtime = CreateRuntime();
            var localization = new LocalizationService(runtime.SettingsService);
            var vm = new SettingsViewModel(localization, runtime);

            vm.SelectedSettingsCategoryIndex = 3;
            Assert.IsTrue(vm.IsCompanionNetworkVisible);
            Assert.IsFalse(vm.IsGeneralVisible);
            Assert.IsFalse(vm.IsLanguageVisible);
            Assert.IsFalse(vm.IsStartupWindowVisible);
            Assert.IsFalse(vm.IsBenchmarksVisible);
            Assert.IsFalse(vm.IsAdvancedDiagnosticsVisible);

            vm.SelectedSettingsCategoryIndex = 99;
            Assert.AreEqual(5, vm.SelectedSettingsCategoryIndex, "Out-of-range index must clamp, not throw.");
            Assert.IsTrue(vm.IsAdvancedDiagnosticsVisible);
        });
    }

    [TestMethod]
    public void RestartCommand_RaisesNormalRestartRequest()
    {
        RunOnSta(() =>
        {
            using var runtime = CreateRuntime();
            var localization = new LocalizationService(runtime.SettingsService);
            var vm = new SettingsViewModel(localization, runtime);
            bool raised = false;
            vm.NormalRestartRequested += () => raised = true;
            vm.RestartCommand.Execute(null);
            Assert.IsTrue(raised);
        });
    }

    [TestMethod]
    public void DnsPanel_LoadsAdaptersAndPresetsWithoutMutation()
    {
        RunOnSta(() =>
        {
            using var runtime = CreateRuntime();
            var localization = new LocalizationService(runtime.SettingsService);
            localization.SetLanguage("en");
            var vm = new SettingsViewModel(localization, runtime);
            Assert.IsNotNull(vm.Dns);
            Assert.AreEqual(4, vm.Dns.Presets.Count, "Cloudflare/Google/Quad9/OpenDNS presets expected.");
            Assert.IsTrue(vm.Dns.Presets.All(p => !string.IsNullOrWhiteSpace(p.DohTemplate)));
            // Adapters enumeration is read-only; on any machine it must not throw.
            Assert.IsNotNull(vm.Dns.Adapters);
            Assert.AreEqual("example.com", vm.Dns.Hostname);
        });
    }

    [TestMethod]
    public void DnsProbe_ValidationPathsNeedNoNetwork()
    {
        var service = new DnsDiagnosticsService();
        var badResolver = service.MeasureResolverLatencyAsync("example.com", "999.999.999.999", TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        Assert.IsFalse(badResolver.Success, "Invalid IP must fail before any socket is opened.");

        var emptyHost = service.MeasureResolverLatencyAsync("   ", "1.1.1.1", TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        Assert.IsFalse(emptyHost.Success, "Empty hostname must fail before any socket is opened.");
    }

    [TestMethod]
    public void IsSuccessorAlive_AliveVsExitedVsMissing()
    {
        string ping = Path.Combine(Environment.SystemDirectory, "ping.exe");
        if (!File.Exists(ping))
        {
            Assert.Inconclusive("ping.exe is not available on this machine.");
        }

        using (var running = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = ping,
            Arguments = "-n 60 127.0.0.1",
            UseShellExecute = false,
            CreateNoWindow = true
        })!)
        {
            try
            {
                Assert.IsTrue(
                    SettingsService.IsSuccessorAlive(running.Id, TimeSpan.FromMilliseconds(300)),
                    "A running process must report alive inside the grace window.");
            }
            finally
            {
                try { running.Kill(); } catch { }
            }

            Assert.IsTrue(running.WaitForExit(10000), "Cleanup: ping must terminate after Kill.");
            Assert.IsFalse(
                SettingsService.IsSuccessorAlive(running.Id, TimeSpan.FromMilliseconds(500)),
                "An exited process must not report alive.");
        }

        Assert.IsFalse(SettingsService.IsSuccessorAlive(int.MaxValue, TimeSpan.FromMilliseconds(200)));
    }

    [TestMethod]
    public void DnsCompareTargets_CoverIpv4AndIpv6PerPreset()
    {
        // QA R1 P3: the latency compare used to probe IPv4 only. Every preset
        // contributes both families, family-labelled; custom resolver appended.
        var targets = DnsDiagnosticsPanelViewModel.BuildCompareTargets(
            DnsDiagnosticsService.Presets, string.Empty, "Custom");

        Assert.AreEqual(8, targets.Count, "4 presets x IPv4+IPv6 expected.");
        foreach (var preset in DnsDiagnosticsService.Presets)
        {
            Assert.IsTrue(
                targets.Any(t => t.Label == $"{preset.DisplayName} (IPv4)" && t.Ip == preset.Ipv4Servers[0]),
                $"Missing IPv4 target for {preset.DisplayName}.");
            Assert.IsTrue(
                targets.Any(t => t.Label == $"{preset.DisplayName} (IPv6)" && t.Ip == preset.Ipv6Servers[0]),
                $"Missing IPv6 target for {preset.DisplayName}.");
        }

        var withCustom = DnsDiagnosticsPanelViewModel.BuildCompareTargets(
            DnsDiagnosticsService.Presets, "9.9.9.10", "Custom");
        Assert.AreEqual(9, withCustom.Count);
        Assert.AreEqual("9.9.9.10", withCustom[^1].Ip);
    }

    [TestMethod]
    public void SettingsDnsKeys_HaveEnAndPlValues()
    {
        using var runtime = CreateRuntime();
        var localization = new LocalizationService(runtime.SettingsService);
        string[] keys =
        [
            "Settings.RestartNormalButton", "Settings.RestartNormalDescription", "Settings.RestartSectionTitle",
            "Settings.Category.General", "Settings.Category.Language", "Settings.Category.StartupWindow",
            "Settings.Category.CompanionNetwork", "Settings.Category.Benchmarks", "Settings.Category.AdvancedDiagnostics",
            "Shell.Restart.SuccessorDied",
            "Dns.Title", "Dns.Description", "Dns.ReadOnlyNotice", "Dns.AdaptersTitle", "Dns.NoAdapters",
            "Dns.AdapterName", "Dns.AdapterStatus", "Dns.AdapterDns", "Dns.NoResolver", "Dns.SourceDhcp",
            "Dns.SourceStatic", "Dns.AdaptersFailed", "Dns.PresetsTitle", "Dns.CompareTitle", "Dns.Hostname",
            "Dns.CustomResolver", "Dns.CustomLabel", "Dns.Compare", "Dns.CancelCompare", "Dns.RefreshAdapters",
            "Dns.StatusIdle", "Dns.HostnameRequired", "Dns.CustomInvalid", "Dns.Comparing", "Dns.Cancelled",
            "Dns.CompareDone", "Dns.LatencyValue", "Dns.ProbeFailed"
        ];
        foreach (string key in keys)
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
    public void SettingsView_MaterializesInEveryCategory()
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
                var vm = new SettingsViewModel(localization, runtime);
                var view = new SettingsView { DataContext = vm };
                for (int category = 0; category < 6; category++)
                {
                    vm.SelectedSettingsCategoryIndex = category;
                    view.Measure(new System.Windows.Size(1400, 900));
                    view.Arrange(new System.Windows.Rect(0, 0, 1400, 900));
                    view.UpdateLayout();
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(60)), "Settings view materialization timed out.");
        if (failure is not null) Assert.Fail(failure.ToString());
    }

    [TestMethod]
    public void DnsSection_VisibilityFollowsCompanionNetworkCategoryOnly()
    {
        // QA R3/R4 P2: the DNS Border used to set DataContext="{Binding Dns}"
        // on itself, so its own Visibility binding resolved against
        // DnsDiagnosticsPanelViewModel (no such property) and stayed Visible
        // in all 6 categories. Visibility must stay on the Border (bound to
        // SettingsViewModel) with Dns as DataContext only of the inner panel.
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
                var vm = new SettingsViewModel(localization, runtime);
                var view = new SettingsView { DataContext = vm };
                view.Measure(new System.Windows.Size(1400, 900));
                view.Arrange(new System.Windows.Rect(0, 0, 1400, 900));
                view.UpdateLayout();

                var border = (System.Windows.Controls.Border)view.FindName("DnsSectionBorder");
                var content = (System.Windows.FrameworkElement)view.FindName("DnsSectionContent");
                Assert.IsNotNull(border, "DnsSectionBorder must exist for the per-category visibility assertion.");
                Assert.IsNotNull(content, "DnsSectionContent must exist for the DataContext assertion.");
                // DataContext ownership: Border sees SettingsViewModel (so its
                // Visibility binding resolves), inner panel sees the DNS VM.
                Assert.IsTrue(ReferenceEquals(vm, border!.DataContext), "DnsSectionBorder.DataContext must be the SettingsViewModel.");
                Assert.IsTrue(ReferenceEquals(vm.Dns, content!.DataContext), "DnsSectionContent.DataContext must be the Dns panel VM.");

                for (int category = 0; category < 6; category++)
                {
                    vm.SelectedSettingsCategoryIndex = category;
                    view.UpdateLayout();
                    var expected = category == 3
                        ? System.Windows.Visibility.Visible
                        : System.Windows.Visibility.Collapsed;
                    Assert.AreEqual(
                        expected, border.Visibility,
                        $"DNS section must be {(category == 3 ? "Visible" : "Collapsed")} in category {category}.");
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(60)), "DNS section visibility probe timed out.");
        if (failure is not null) Assert.Fail(failure.ToString());
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
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(60)), "STA test host timed out.");
        if (failure is not null) Assert.Fail(failure.ToString());
    }

    private AppRuntimeService CreateRuntime()
    {
        string settingsPath = Path.Combine(_tempDirectory, $"settings-{Guid.NewGuid():N}.json");
        return new AppRuntimeService(settingsPath);
    }
}
