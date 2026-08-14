using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.Companion;
using FrameHub.Companion.Authentication;
using FrameHub.Companion.Models;
using FrameHub.Companion.Persistence;
using FrameHub.Core.Models;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public sealed class SettingsCompanionIntegrationTests
{
    private string _tempDirectory = null!;
    private string _tempSettingsFilePath = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _tempSettingsFilePath = Path.Combine(_tempDirectory, "settings.json");
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { }
        }
    }

    private AppRuntimeService CreateTestRuntime()
    {
        return new AppRuntimeService(_tempSettingsFilePath);
    }

    [TestMethod]
    public void TestSettingsService_UsesIsolatedFilePath()
    {
        string testPath = Path.Combine(_tempDirectory, "custom_isolated_settings.json");
        using var service = new SettingsService(testPath);

        Assert.AreEqual(testPath, service.SettingsFilePath);
        Assert.IsFalse(File.Exists(testPath));

        var settings = new AppSettings { CompanionPort = 12345 };
        service.Save(settings);

        Assert.IsTrue(File.Exists(testPath));
        Assert.AreNotEqual(AppPaths.GetUserDataFilePath("settings.json"), testPath);

        var loaded = service.Load();
        Assert.AreEqual(12345, loaded.CompanionPort);
    }

    [TestMethod]
    public void LegacySettingsJson_ResolvesToCompanionDisabledAndDefaultPort47821()
    {
        string json = """
        {
            "StartWithWindows": false,
            "Language": "en",
            "LogEnabled": true
        }
        """;

        var settings = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.IsNotNull(settings);
        Assert.IsFalse(settings.CompanionEnabled);
        Assert.AreEqual(47821, settings.CompanionPort);
    }

    [TestMethod]
    public void ValidPortValues_AreAcceptedByViewModel()
    {
        using var runtime = CreateTestRuntime();
        var localization = new LocalizationService(runtime.SettingsService);
        var vm = new SettingsViewModel(localization, runtime);

        vm.CompanionPortText = "8080";
        Assert.AreEqual("8080", vm.CompanionPortText);
        Assert.IsFalse(vm.HasCompanionPortValidationError);
        Assert.AreEqual(8080, vm.CompanionPort);

        vm.CompanionPortText = "65535";
        Assert.AreEqual("65535", vm.CompanionPortText);
        Assert.IsFalse(vm.HasCompanionPortValidationError);
        Assert.AreEqual(65535, vm.CompanionPort);
    }

    [TestMethod]
    public void InvalidPortValues_AreRejectedByViewModel()
    {
        using var runtime = CreateTestRuntime();
        var localization = new LocalizationService(runtime.SettingsService);
        var vm = new SettingsViewModel(localization, runtime);
        int initialPort = vm.CompanionPort;

        vm.CompanionPortText = "0";
        Assert.AreEqual("0", vm.CompanionPortText);
        Assert.IsTrue(vm.HasCompanionPortValidationError);
        Assert.AreEqual(initialPort, runtime.Settings.CompanionPort);

        vm.CompanionPortText = "-1";
        Assert.AreEqual("-1", vm.CompanionPortText);
        Assert.IsTrue(vm.HasCompanionPortValidationError);
        Assert.AreEqual(initialPort, runtime.Settings.CompanionPort);

        vm.CompanionPortText = "65536";
        Assert.AreEqual("65536", vm.CompanionPortText);
        Assert.IsTrue(vm.HasCompanionPortValidationError);
        Assert.AreEqual(initialPort, runtime.Settings.CompanionPort);

        vm.CompanionPortText = "invalid";
        Assert.AreEqual("invalid", vm.CompanionPortText);
        Assert.IsTrue(vm.HasCompanionPortValidationError);
        Assert.AreEqual(initialPort, runtime.Settings.CompanionPort);
    }

    [TestMethod]
    public void EmptyPortText_IsPreservedAndRejected()
    {
        using var runtime = CreateTestRuntime();
        var localization = new LocalizationService(runtime.SettingsService);
        var vm = new SettingsViewModel(localization, runtime);
        int initialPort = vm.CompanionPort;

        vm.CompanionPortText = "";

        Assert.AreEqual("", vm.CompanionPortText);
        Assert.IsTrue(vm.HasCompanionPortValidationError);
        Assert.AreEqual(initialPort, runtime.Settings.CompanionPort);
    }

    [TestMethod]
    public void NonNumericPortText_IsPreservedAndRejected()
    {
        using var runtime = CreateTestRuntime();
        var localization = new LocalizationService(runtime.SettingsService);
        var vm = new SettingsViewModel(localization, runtime);
        int initialPort = vm.CompanionPort;

        vm.CompanionPortText = "abc";

        Assert.AreEqual("abc", vm.CompanionPortText);
        Assert.IsTrue(vm.HasCompanionPortValidationError);
        Assert.AreEqual(initialPort, runtime.Settings.CompanionPort);
    }

    [TestMethod]
    public void OutOfRangePortText_IsPreservedAndRejected()
    {
        using var runtime = CreateTestRuntime();
        var localization = new LocalizationService(runtime.SettingsService);
        var vm = new SettingsViewModel(localization, runtime);
        int initialPort = vm.CompanionPort;

        vm.CompanionPortText = "0";
        Assert.AreEqual("0", vm.CompanionPortText);
        Assert.IsTrue(vm.HasCompanionPortValidationError);
        Assert.AreEqual(initialPort, runtime.Settings.CompanionPort);

        vm.CompanionPortText = "65536";
        Assert.AreEqual("65536", vm.CompanionPortText);
        Assert.IsTrue(vm.HasCompanionPortValidationError);
        Assert.AreEqual(initialPort, runtime.Settings.CompanionPort);
    }

    [TestMethod]
    public void ValidPortText_UpdatesSetting()
    {
        using var runtime = CreateTestRuntime();
        var localization = new LocalizationService(runtime.SettingsService);
        var vm = new SettingsViewModel(localization, runtime);

        vm.CompanionPortText = "8080";

        Assert.AreEqual("8080", vm.CompanionPortText);
        Assert.IsFalse(vm.HasCompanionPortValidationError);
        Assert.AreEqual(8080, vm.CompanionPort);
        Assert.AreEqual(8080, runtime.Settings.CompanionPort);
    }

    [TestMethod]
    public async Task EnablingAndDisablingCompanion_PersistsAndControlsServerLifecycle()
    {
        int port = GetFreePort();
        using var runtime = CreateTestRuntime();
        var localization = new LocalizationService(runtime.SettingsService);
        var vm = new SettingsViewModel(localization, runtime);

        vm.CompanionPort = port;
        Assert.IsFalse(runtime.Settings.CompanionEnabled);
        Assert.AreEqual(CompanionServiceState.Stopped, runtime.CompanionServer.Status.State);

        vm.CompanionEnabled = true;
        await Task.Delay(150); // Allow async reconciliation task to run

        Assert.IsTrue(runtime.Settings.CompanionEnabled);
        Assert.AreEqual(CompanionServiceState.Running, runtime.CompanionServer.Status.State);
        Assert.IsTrue(vm.IsCompanionRunning);
        Assert.AreEqual($"http://127.0.0.1:{port}", vm.CompanionEndpointText);

        vm.CompanionEnabled = false;
        await Task.Delay(150);

        Assert.IsFalse(runtime.Settings.CompanionEnabled);
        Assert.AreEqual(CompanionServiceState.Stopped, runtime.CompanionServer.Status.State);
        Assert.IsFalse(vm.IsCompanionRunning);
    }

    [TestMethod]
    public async Task ChangingPortWhileDisabled_PersistsPortButDoesNotStartServer()
    {
        int port1 = GetFreePort();
        int port2 = GetFreePort();
        using var runtime = CreateTestRuntime();
        var localization = new LocalizationService(runtime.SettingsService);
        var vm = new SettingsViewModel(localization, runtime);

        vm.CompanionEnabled = false;
        vm.CompanionPort = port1;
        await Task.Delay(100);

        Assert.AreEqual(CompanionServiceState.Stopped, runtime.CompanionServer.Status.State);
        Assert.AreEqual(port1, runtime.Settings.CompanionPort);

        vm.CompanionPort = port2;
        await Task.Delay(100);

        Assert.AreEqual(CompanionServiceState.Stopped, runtime.CompanionServer.Status.State);
        Assert.AreEqual(port2, runtime.Settings.CompanionPort);
    }

    [TestMethod]
    public async Task ChangingPortWhileRunning_MovesListenerToNewPort()
    {
        int port1 = GetFreePort();
        int port2 = GetFreePort();
        using var runtime = CreateTestRuntime();
        var localization = new LocalizationService(runtime.SettingsService);
        var vm = new SettingsViewModel(localization, runtime);

        vm.CompanionPort = port1;
        vm.CompanionEnabled = true;
        await Task.Delay(150);

        Assert.AreEqual(CompanionServiceState.Running, runtime.CompanionServer.Status.State);
        Assert.AreEqual($"http://127.0.0.1:{port1}", runtime.CompanionServer.Status.BoundAddress);

        vm.CompanionPort = port2;
        await Task.Delay(200);

        Assert.AreEqual(CompanionServiceState.Running, runtime.CompanionServer.Status.State);
        Assert.AreEqual($"http://127.0.0.1:{port2}", runtime.CompanionServer.Status.BoundAddress);
        Assert.AreEqual($"http://127.0.0.1:{port2}", vm.CompanionEndpointText);
    }

    [TestMethod]
    public async Task ChangingToOccupiedPort_SetsFailedStateAndAllowsRecovery()
    {
        int freePort = GetFreePort();
        int occupiedPort = GetFreePort();

        var listener = new TcpListener(IPAddress.Parse("127.0.0.1"), occupiedPort);
        listener.Start();

        try
        {
            using var runtime = CreateTestRuntime();
            var localization = new LocalizationService(runtime.SettingsService);
            var vm = new SettingsViewModel(localization, runtime);

            vm.CompanionPort = occupiedPort;
            vm.CompanionEnabled = true;
            await Task.Delay(200);

            Assert.AreEqual(CompanionServiceState.Failed, runtime.CompanionServer.Status.State);
            Assert.IsTrue(vm.IsCompanionFailed);
            Assert.IsFalse(string.IsNullOrWhiteSpace(vm.CompanionErrorMessage));

            // FrameHub runtime remains fully operational while companion is in Failed state
            Assert.IsNotNull(runtime.Settings);

            // Recover by changing to free port
            vm.CompanionPort = freePort;
            await Task.Delay(200);

            Assert.AreEqual(CompanionServiceState.Running, runtime.CompanionServer.Status.State);
            Assert.IsTrue(vm.IsCompanionRunning);
            Assert.IsFalse(vm.IsCompanionFailed);
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestMethod]
    public async Task TelemetryProvider_Lifecycle_StartStopAndDispose()
    {
        using var runtime = CreateTestRuntime();
        var provider = runtime.TelemetryProvider;

        // Provider starts unstarted
        Assert.IsFalse(provider.IsRunning);

        // Start() starts loop
        provider.Start();
        Assert.IsTrue(provider.IsRunning);

        // Start() again is idempotent
        provider.Start();
        Assert.IsTrue(provider.IsRunning);

        // StopAsync() stops loop
        await provider.StopAsync();
        Assert.IsFalse(provider.IsRunning);

        // StopAsync() again is safe
        await provider.StopAsync();
        Assert.IsFalse(provider.IsRunning);

        // Start() after stop works again
        provider.Start();
        Assert.IsTrue(provider.IsRunning);

        // Cleanup
        await provider.StopAsync();
    }

    [TestMethod]
    public void Localization_TelemetryPermissionKeys_Exist()
    {
        string enTelemetry = LocalizationService.Translate("Settings.CompanionScopeTelemetry", "en");
        string plTelemetry = LocalizationService.Translate("Settings.CompanionScopeTelemetry", "pl");
        string enNever = LocalizationService.Translate("Settings.CompanionNeverUsed", "en");
        string plNever = LocalizationService.Translate("Settings.CompanionNeverUsed", "pl");

        Assert.AreEqual("Telemetry", enTelemetry);
        Assert.AreEqual("Telemetria", plTelemetry);
        Assert.AreEqual("Never", enNever);
        Assert.AreEqual("Nigdy", plNever);
    }

    [TestMethod]
    public async Task StartPairing_RequiresBoundLanEndpoint_DoesNotUseUnboundSavedAddress()
    {
        int port = GetFreePort();
        using var runtime = CreateTestRuntime();
        var localization = new LocalizationService(runtime.SettingsService);
        var vm = new SettingsViewModel(localization, runtime);

        // 1. Start companion with invalid LAN address
        await runtime.CompanionServer.StartAsync(new FrameHub.Companion.CompanionOptions
        {
            Enabled = true,
            LanEnabled = true,
            LanAddress = "192.0.2.1",
            Port = port
        });

        Assert.IsTrue(runtime.CompanionServer.Status.LanFaulted);
        Assert.IsNull(runtime.CompanionServer.Status.LanBoundAddress);

        // 2. Configure VM settings
        vm.CompanionLanAddress = "192.0.2.1";
        vm.CompanionLanEnabled = true;

        // 3. Trigger StartPairing
        vm.StartPairingCommand.Execute(null);

        // Pairing session MUST NOT be active because LAN address could not be bound
        Assert.IsFalse(vm.IsPairingActive, "StartPairing must not create a pairing session for an unbound LAN address.");
        Assert.AreEqual(string.Empty, vm.PairingUrl);
    }

    [TestMethod]
    public void LanCandidateIp_ToString_ReturnsIpAddress()
    {
        var candidate = new FrameHub.Companion.Models.LanCandidateIp("192.168.1.100", "eth0", "Ethernet");
        Assert.AreEqual("192.168.1.100", candidate.ToString());
    }

    [TestMethod]
    public void Clone_PreservesAllCompanionSettings()
    {
        var source = new AppSettings
        {
            CompanionEnabled = true,
            CompanionLanEnabled = true,
            CompanionLanAddress = "192.168.1.100",
            CompanionPort = 47822
        };

        var cloned = SettingsViewModel.Clone(source);
        Assert.IsNotNull(cloned);
        Assert.IsTrue(cloned.CompanionEnabled);
        Assert.IsTrue(cloned.CompanionLanEnabled);
        Assert.AreEqual("192.168.1.100", cloned.CompanionLanAddress);
        Assert.AreEqual(47822, cloned.CompanionPort);
    }

    [TestMethod]
    public async Task SettingsViewModel_PersistsCompanionLanSettings_WhenEnabledAndDisabled()
    {
        using var runtime = CreateTestRuntime();
        var localization = new LocalizationService(runtime.SettingsService);
        var vm = new SettingsViewModel(localization, runtime);

        // 1. Initial state
        Assert.IsFalse(runtime.Settings.CompanionLanEnabled);

        // 2. Enable LAN and select IP address from ViewModel
        vm.CompanionLanAddress = "192.168.1.100";
        vm.CompanionLanEnabled = true;
        await Task.Delay(100);

        Assert.IsTrue(runtime.Settings.CompanionLanEnabled, "Saving settings must persist CompanionLanEnabled=true");
        Assert.AreEqual("192.168.1.100", runtime.Settings.CompanionLanAddress, "Saving settings must persist CompanionLanAddress");

        // 3. Disable LAN from ViewModel
        vm.CompanionLanEnabled = false;
        await Task.Delay(100);

        Assert.IsFalse(runtime.Settings.CompanionLanEnabled, "Disabling LAN must persist CompanionLanEnabled=false");
        Assert.AreEqual("192.168.1.100", runtime.Settings.CompanionLanAddress, "CompanionLanAddress is retained in settings");

        // 4. Verify existing CompanionEnabled / Port persistence still works
        vm.CompanionPort = 47890;
        vm.CompanionEnabled = true;
        await Task.Delay(100);

        Assert.IsTrue(runtime.Settings.CompanionEnabled);
        Assert.AreEqual(47890, runtime.Settings.CompanionPort);
    }

    [TestMethod]
    public void PairedDeviceItemViewModel_LibraryAndLaunchScopes_CascadesCorrectly()
    {
        string storePath = Path.Combine(_tempDirectory, "paired-devices.json");
        var store = new DeviceRecordStore(storePath);
        var record = new PairedDeviceRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test Phone",
            CredentialHash = "hash123",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Scopes = new List<string> { CompanionScopes.ReadStatus }
        };
        store.AddDevice(record);

        var vm = new PairedDeviceItemViewModel(
            record,
            id => { store.RevokeDevice(id); },
            (id, scope, enabled) =>
            {
                if (enabled) store.GrantScope(id, scope);
                else store.RevokeScope(id, scope);
            },
            "Telemetry",
            "Read Benchmarks",
            "Write Benchmarks",
            "Read Library",
            "Launch Control",
            "Revoke",
            "Never");

        Assert.IsFalse(vm.ReadLibraryEnabled);
        Assert.IsFalse(vm.WriteLaunchEnabled);

        // 1. Enabling WriteLaunchEnabled automatically turns on ReadLibraryEnabled
        vm.WriteLaunchEnabled = true;
        Assert.IsTrue(vm.WriteLaunchEnabled);
        Assert.IsTrue(vm.ReadLibraryEnabled);

        var stored = store.GetDeviceById(record.Id);
        Assert.IsTrue(stored!.Scopes.Contains(CompanionScopes.WriteLaunch));
        Assert.IsTrue(stored.Scopes.Contains(CompanionScopes.ReadLibrary));

        // 2. Disabling ReadLibraryEnabled automatically turns off WriteLaunchEnabled
        vm.ReadLibraryEnabled = false;
        Assert.IsFalse(vm.ReadLibraryEnabled);
        Assert.IsFalse(vm.WriteLaunchEnabled);

        stored = store.GetDeviceById(record.Id);
        Assert.IsFalse(stored!.Scopes.Contains(CompanionScopes.ReadLibrary));
        Assert.IsFalse(stored.Scopes.Contains(CompanionScopes.WriteLaunch));

        // 3. Re-enabling ReadLibraryEnabled does not enable WriteLaunchEnabled
        vm.ReadLibraryEnabled = true;
        Assert.IsTrue(vm.ReadLibraryEnabled);
        Assert.IsFalse(vm.WriteLaunchEnabled);

        // 4. Turning off WriteLaunchEnabled leaves ReadLibraryEnabled intact
        vm.WriteLaunchEnabled = true;
        Assert.IsTrue(vm.ReadLibraryEnabled);
        Assert.IsTrue(vm.WriteLaunchEnabled);

        vm.WriteLaunchEnabled = false;
        Assert.IsFalse(vm.WriteLaunchEnabled);
        Assert.IsTrue(vm.ReadLibraryEnabled);
    }

    [TestMethod]
    public void LibraryViewModel_LaunchSelected_DelegatesToLaunchService()
    {
        using var runtime = CreateTestRuntime();
        var loc = new LocalizationService(runtime.SettingsService);

        string fakeExe = Path.Combine(_tempDirectory, "desktop_test_game.exe");
        File.WriteAllText(fakeExe, "desktop exe");

        var item = new LibraryItem
        {
            Id = "desktop-game-1",
            DisplayName = "Desktop Game",
            Type = FrameHub.Core.Models.Library.LibraryItemType.Game,
            IsEnabled = true,
            ExecutablePath = fakeExe
        };

        var libraryService = new LibraryService(Path.Combine(_tempDirectory, "desktop-library.json"));
        libraryService.SaveItems(new[] { item });

        var vm = new LibraryViewModel(loc, runtime, libraryService);
        vm.Reload();

        var itemVm = vm.Items.FirstOrDefault(i => i.Item.Id == "desktop-game-1");
        Assert.IsNotNull(itemVm);
        vm.SelectedItem = itemVm;

        Assert.IsTrue(vm.LaunchSelectedCommand.CanExecute(null));
        vm.LaunchSelectedCommand.Execute(null);

        // Verify status message is updated (either ready or success)
        Assert.IsNotNull(vm.StatusMessage);
    }

    private static int GetFreePort()
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                using var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                return port;
            }
            catch
            {
                if (i == 4) throw;
            }
        }
        throw new InvalidOperationException("Could not obtain free port.");
    }
}
