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
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.PairingStatusMessage), "Failed StartPairing must surface a user-facing message instead of silently doing nothing.");
    }

    [TestMethod]
    public void StartPairing_WithCompanionStopped_ReportsStateAndCreatesNoSession()
    {
        using var runtime = CreateTestRuntime();
        var localization = new LocalizationService(runtime.SettingsService);
        var vm = new SettingsViewModel(localization, runtime);

        Assert.IsFalse(vm.IsPairingSectionVisible, "Pairing UI must not be presented while the Companion service is not running.");

        vm.StartPairingCommand.Execute(null);

        Assert.IsFalse(vm.IsPairingActive);
        Assert.AreEqual(string.Empty, vm.PairingUrl);
        Assert.AreEqual(string.Empty, vm.PairingToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.PairingStatusMessage), "StartPairing without a running server must explain the required state.");
    }

    [TestMethod]
    public async Task StartPairing_LoopbackOnly_WhileRunning_ExposesTokenAndRootFragmentUrl()
    {
        int port = GetFreePort();
        using var runtime = CreateTestRuntime();
        var localization = new LocalizationService(runtime.SettingsService);
        var vm = new SettingsViewModel(localization, runtime);

        vm.CompanionPort = port;
        vm.CompanionEnabled = true;
        await Task.Delay(150);

        Assert.IsTrue(vm.IsCompanionRunning);
        Assert.IsTrue(vm.IsPairingSectionVisible, "Pairing UI must be presented whenever the Companion service is running, including localhost-only mode.");

        vm.StartPairingCommand.Execute(null);

        Assert.IsTrue(vm.IsPairingActive);
        Assert.AreEqual(string.Empty, vm.PairingStatusMessage);
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.PairingToken), "The pairing token must be surfaced for manual entry.");
        StringAssert.StartsWith(vm.PairingUrl, $"http://127.0.0.1:{port}/#v=1&t=", "Pairing URL must target the served root page with a fragment token.");
        Assert.IsFalse(vm.PairingUrl.Contains("/pair"), "Pairing URL must not reference the nonexistent /pair page.");
        Assert.IsFalse(vm.PairingUrl.Contains('?'), "Pairing token must never be carried in a query string.");
        Assert.IsTrue(vm.PairingUrl.EndsWith(vm.PairingToken, StringComparison.Ordinal));
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.CompanionPairingActiveText), "Localized pairing window title must be available.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.CompanionPairingLanHintText), "LAN pairing hint must be available while LAN access is disabled.");

        vm.CancelPairingCommand.Execute(null);
        Assert.IsFalse(vm.IsPairingActive);
    }

    [TestMethod]
    public void Localization_PairingKeys_ExistInBothLanguages_WithoutStaleClaims()
    {
        foreach (string key in new[]
        {
            "Settings.CompanionDescription",
            "Settings.CompanionPairingToken",
            "Settings.CompanionPairingLanHint",
            "Settings.CompanionPairingWaitRunning",
            "Settings.CompanionPairingLanUnavailable",
            "Settings.CompanionPairingActive"
        })
        {
            Assert.IsTrue(LocalizationService.EnglishKeys.Contains(key), $"Missing English key {key}");
            Assert.IsTrue(LocalizationService.PolishKeys.Contains(key), $"Missing Polish key {key}");
        }

        string enDescription = LocalizationService.Translate("Settings.CompanionDescription", "en");
        string plDescription = LocalizationService.Translate("Settings.CompanionDescription", "pl");
        Assert.IsFalse(enDescription.Contains("future update", StringComparison.OrdinalIgnoreCase), "EN description must not claim LAN pairing is only planned.");
        Assert.IsFalse(plDescription.Contains("planowane", StringComparison.OrdinalIgnoreCase), "PL description must not claim LAN pairing is only planned.");
        Assert.IsFalse(plDescription.Contains("localhost", StringComparison.OrdinalIgnoreCase), "PL description must not claim localhost-only mode.");
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
    public void PairedDeviceItemViewModel_TwoRecordsWithDistinctNames_ProjectAsDistinctDevices()
    {
        string storePath = Path.Combine(_tempDirectory, "paired-devices-distinct.json");
        var store = new DeviceRecordStore(storePath);
        var recordA = new PairedDeviceRecord { Id = Guid.NewGuid(), DisplayName = "Erza-PC", CredentialHash = "hash-a" };
        var recordB = new PairedDeviceRecord { Id = Guid.NewGuid(), DisplayName = "Erza-iPhone", CredentialHash = "hash-b" };
        store.AddDevice(recordA);
        store.AddDevice(recordB);

        var vmA = new PairedDeviceItemViewModel(recordA, _ => { }, (_, _, _) => { }, pairedLabel: "Paired", lastUsedLabel: "Last used");
        var vmB = new PairedDeviceItemViewModel(recordB, _ => { }, (_, _, _) => { }, pairedLabel: "Paired", lastUsedLabel: "Last used");

        Assert.AreEqual("Erza-PC", vmA.DisplayName);
        Assert.AreEqual(recordA.Id, vmA.Id);
        Assert.AreEqual("Erza-iPhone", vmB.DisplayName);
        Assert.AreEqual(recordB.Id, vmB.Id);
        Assert.AreNotEqual(vmA.Id, vmB.Id);
        StringAssert.StartsWith(vmA.PairedOnText, "Paired: ");
        StringAssert.StartsWith(vmA.LastUsedDisplay, "Last used: ");
    }

    [TestMethod]
    public void PairedDeviceItemViewModel_ScopeEdit_TargetsOnlyThatDeviceId()
    {
        string storePath = Path.Combine(_tempDirectory, "paired-devices-targeting.json");
        var store = new DeviceRecordStore(storePath);
        var recordA = new PairedDeviceRecord { Id = Guid.NewGuid(), DisplayName = "Device A", Scopes = new List<string> { CompanionScopes.ReadStatus } };
        var recordB = new PairedDeviceRecord { Id = Guid.NewGuid(), DisplayName = "Device B", Scopes = new List<string> { CompanionScopes.ReadStatus } };
        store.AddDevice(recordA);
        store.AddDevice(recordB);

        var vmA = new PairedDeviceItemViewModel(
            recordA,
            _ => { },
            (id, scope, enabled) => { if (enabled) store.GrantScope(id, scope); else store.RevokeScope(id, scope); });

        var scopesBBefore = store.GetDeviceById(recordB.Id)!.Scopes.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();

        vmA.WriteLaunchEnabled = true;

        var storedA = store.GetDeviceById(recordA.Id)!;
        Assert.IsTrue(storedA.Scopes.Contains(CompanionScopes.WriteLaunch), "Device A must receive the granted scope.");
        Assert.IsTrue(storedA.Scopes.Contains(CompanionScopes.ReadLibrary), "WriteLaunch cascade must grant ReadLibrary on device A.");

        var storedB = store.GetDeviceById(recordB.Id)!;
        var scopesBAfter = storedB.Scopes.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        CollectionAssert.AreEqual(scopesBBefore, scopesBAfter, "Device B scopes must be untouched by a device A edit.");
        Assert.IsFalse(storedB.Scopes.Contains(CompanionScopes.WriteLaunch));
    }

    [TestMethod]
    public void PairedDeviceItemViewModel_ScopeEdit_PreservesUnrelatedExistingScopes()
    {
        string storePath = Path.Combine(_tempDirectory, "paired-devices-preserve.json");
        var store = new DeviceRecordStore(storePath);
        var record = new PairedDeviceRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = "Preserved Device",
            Scopes = new List<string> { CompanionScopes.ReadStatus, CompanionScopes.ReadTelemetry, CompanionScopes.ReadOptimization }
        };
        store.AddDevice(record);

        var vm = new PairedDeviceItemViewModel(
            record,
            _ => { },
            (id, scope, enabled) => { if (enabled) store.GrantScope(id, scope); else store.RevokeScope(id, scope); });

        vm.ReadLibraryEnabled = true;
        vm.ReadTelemetryEnabled = false;

        var stored = store.GetDeviceById(record.Id)!;
        Assert.IsTrue(stored.Scopes.Contains(CompanionScopes.ReadStatus), "Unrelated ReadStatus scope must survive.");
        Assert.IsTrue(stored.Scopes.Contains(CompanionScopes.ReadOptimization), "Unrelated ReadOptimization scope must survive.");
        Assert.IsTrue(stored.Scopes.Contains(CompanionScopes.ReadLibrary));
        Assert.IsFalse(stored.Scopes.Contains(CompanionScopes.ReadTelemetry), "Explicitly revoked scope must be removed.");
    }

    [TestMethod]
    public void RevokeDeviceWithConfirmation_Confirmed_RemovesOnlyTargetDevice()
    {
        string storePath = Path.Combine(_tempDirectory, "paired-devices-revoke-yes.json");
        var store = new DeviceRecordStore(storePath);
        var recordA = new PairedDeviceRecord { Id = Guid.NewGuid(), DisplayName = "Device A", Scopes = new List<string> { CompanionScopes.ReadStatus, CompanionScopes.ReadLibrary } };
        var recordB = new PairedDeviceRecord { Id = Guid.NewGuid(), DisplayName = "Device B", Scopes = new List<string> { CompanionScopes.ReadStatus, CompanionScopes.ReadTelemetry } };
        store.AddDevice(recordA);
        store.AddDevice(recordB);

        string? confirmedName = null;
        bool removed = SettingsViewModel.RevokeDeviceWithConfirmation(store, recordA.Id, name => { confirmedName = name; return true; });

        Assert.IsTrue(removed);
        Assert.AreEqual("Device A", confirmedName, "Confirmation must name the device being removed.");
        Assert.IsNull(store.GetDeviceById(recordA.Id), "Only device A may be removed.");
        var storedB = store.GetDeviceById(recordB.Id);
        Assert.IsNotNull(storedB, "Device B must remain paired.");
        CollectionAssert.AreEquivalent(new[] { CompanionScopes.ReadStatus, CompanionScopes.ReadTelemetry }, storedB.Scopes.ToList(), "Device B scopes must be intact.");
    }

    [TestMethod]
    public void RevokeDeviceWithConfirmation_Cancelled_PerformsNoMutation()
    {
        string storePath = Path.Combine(_tempDirectory, "paired-devices-revoke-cancel.json");
        var store = new DeviceRecordStore(storePath);
        var recordA = new PairedDeviceRecord { Id = Guid.NewGuid(), DisplayName = "Device A", Scopes = new List<string> { CompanionScopes.ReadStatus } };
        var recordB = new PairedDeviceRecord { Id = Guid.NewGuid(), DisplayName = "Device B", Scopes = new List<string> { CompanionScopes.ReadTelemetry } };
        store.AddDevice(recordA);
        store.AddDevice(recordB);

        bool removed = SettingsViewModel.RevokeDeviceWithConfirmation(store, recordA.Id, _ => false);

        Assert.IsFalse(removed);
        Assert.IsNotNull(store.GetDeviceById(recordA.Id), "Cancel must not remove any device.");
        Assert.IsNotNull(store.GetDeviceById(recordB.Id), "Cancel must not remove any device.");
        CollectionAssert.AreEquivalent(new[] { CompanionScopes.ReadStatus }, store.GetDeviceById(recordA.Id)!.Scopes.ToList(), "Cancel must not change scopes.");
        CollectionAssert.AreEquivalent(new[] { CompanionScopes.ReadTelemetry }, store.GetDeviceById(recordB.Id)!.Scopes.ToList(), "Cancel must not change scopes.");

        Assert.IsFalse(SettingsViewModel.RevokeDeviceWithConfirmation(store, Guid.NewGuid(), _ => true), "Revoking an unknown id must fail closed without mutation.");
        Assert.AreEqual(2, store.Devices.Count);
    }

    [TestMethod]
    public void PairedDeviceItemViewModel_ExposesNoCredentialOrTokenMaterial()
    {
        string[] forbiddenFragments = { "Credential", "Hash", "Token", "Secret", "PairingKey", "Auth" };
        foreach (var property in typeof(PairedDeviceItemViewModel).GetProperties())
        {
            foreach (string fragment in forbiddenFragments)
            {
                Assert.IsFalse(property.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase),
                    $"PairedDeviceItemViewModel must not expose '{property.Name}' via the Settings projection.");
            }
        }
    }

    [TestMethod]
    public void CompanionPairedDeviceUi_NewLocalizationKeys_HaveEnPlParity()
    {
        foreach (string key in new[]
        {
            "Settings.CompanionPaired",
            "Settings.CompanionLastUsed",
            "Settings.CompanionRevokeConfirmTitle",
            "Settings.CompanionRevokeConfirmMessage",
            "Settings.CompanionPermissions",
            "Settings.CompanionPermissionArea",
            "Settings.CompanionPermissionRead",
            "Settings.CompanionPermissionControl",
            "Settings.CompanionAreaTelemetry",
            "Settings.CompanionAreaLibrary",
            "Settings.CompanionAreaBackgroundApps",
            "Settings.CompanionAreaOptimization",
            "Settings.CompanionAreaBenchmarks"
        })
        {
            Assert.IsTrue(LocalizationService.EnglishKeys.Contains(key), $"Missing English key {key}");
            Assert.IsTrue(LocalizationService.PolishKeys.Contains(key), $"Missing Polish key {key}");
        }

        string enMessage = LocalizationService.Translate("Settings.CompanionRevokeConfirmMessage", "en");
        string plMessage = LocalizationService.Translate("Settings.CompanionRevokeConfirmMessage", "pl");
        StringAssert.Contains(enMessage, "{0}", "EN confirmation message must keep the device-name placeholder.");
        StringAssert.Contains(plMessage, "{0}", "PL confirmation message must keep the device-name placeholder.");
    }

    [TestMethod]
    public void PairedDeviceItemViewModel_MatrixHeadersAndLabels_ExposeExpectedDefaultsAndOverrides()
    {
        var record = new PairedDeviceRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test Phone",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Scopes = new List<string> { CompanionScopes.ReadTelemetry, CompanionScopes.ReadLibrary }
        };

        // Defaults
        var defaultVm = new PairedDeviceItemViewModel(record, _ => { }, (_, _, _) => { });
        Assert.AreEqual("Permissions", defaultVm.PermissionsHeader);
        Assert.AreEqual("Area", defaultVm.AreaHeader);
        Assert.AreEqual("Read", defaultVm.ReadHeader);
        Assert.AreEqual("Control", defaultVm.ControlHeader);
        Assert.AreEqual("Telemetry", defaultVm.AreaTelemetryLabel);
        Assert.AreEqual("Game library", defaultVm.AreaLibraryLabel);
        Assert.AreEqual("Background apps", defaultVm.AreaBackgroundAppsLabel);
        Assert.AreEqual("Optimization", defaultVm.AreaOptimizationLabel);
        Assert.AreEqual("Benchmarks", defaultVm.AreaBenchmarksLabel);

        // Custom localized injection
        var customVm = new PairedDeviceItemViewModel(
            record,
            _ => { },
            (_, _, _) => { },
            permissionsHeader: "Uprawnienia",
            areaHeader: "Obszar",
            readHeader: "Odczyt",
            controlHeader: "Sterowanie",
            areaTelemetryLabel: "Telemetria",
            areaLibraryLabel: "Biblioteka gier",
            areaBackgroundAppsLabel: "Aplikacje w tle",
            areaOptimizationLabel: "Optymalizacja",
            areaBenchmarksLabel: "Benchmarki");

        Assert.AreEqual("Uprawnienia", customVm.PermissionsHeader);
        Assert.AreEqual("Obszar", customVm.AreaHeader);
        Assert.AreEqual("Odczyt", customVm.ReadHeader);
        Assert.AreEqual("Sterowanie", customVm.ControlHeader);
        Assert.AreEqual("Telemetria", customVm.AreaTelemetryLabel);
        Assert.AreEqual("Biblioteka gier", customVm.AreaLibraryLabel);
        Assert.AreEqual("Aplikacje w tle", customVm.AreaBackgroundAppsLabel);
        Assert.AreEqual("Optymalizacja", customVm.AreaOptimizationLabel);
        Assert.AreEqual("Benchmarki", customVm.AreaBenchmarksLabel);
    }

    [TestMethod]
    public void CompanionPairedDeviceUi_SettingsViewXaml_ContainsStructuredPermissionMatrix()
    {
        string? repoRoot = FindRepoRoot();
        if (repoRoot == null)
        {
            Assert.Inconclusive("Repository source root was not discoverable from the test assembly location.");
        }

        string xaml = File.ReadAllText(Path.Combine(repoRoot!, "FrameHub.App", "Views", "SettingsView.xaml"));

        // 1. Verify PairedDevices ItemsControl section exists
        int listStart = xaml.IndexOf("ItemsSource=\"{Binding PairedDevices}\"", StringComparison.Ordinal);
        Assert.IsTrue(listStart >= 0, "SettingsView must contain the PairedDevices ItemsControl.");
        int listEnd = xaml.IndexOf("</ItemsControl>", listStart, StringComparison.Ordinal);
        Assert.IsTrue(listEnd > listStart, "PairedDevices ItemsControl closing tag must exist.");
        string templateXaml = xaml.Substring(listStart, listEnd - listStart);

        // 2. Old free-flowing WrapPanel must NOT exist in the template
        Assert.IsFalse(templateXaml.Contains("<WrapPanel", StringComparison.OrdinalIgnoreCase),
            "Paired devices template must not use a free-flowing WrapPanel for scope permissions.");

        // 3. Header & Metadata elements
        StringAssert.Contains(templateXaml, "Text=\"{Binding DisplayName}\"");
        StringAssert.Contains(templateXaml, "Command=\"{Binding RevokeCommand}\"");
        StringAssert.Contains(templateXaml, "Text=\"{Binding PairedOnText}\"");
        StringAssert.Contains(templateXaml, "Text=\"{Binding LastUsedDisplay}\"");
        StringAssert.Contains(templateXaml, "Text=\"{Binding PermissionsHeader}\"");
        StringAssert.Contains(templateXaml, "Text=\"{Binding AreaHeader}\"");
        StringAssert.Contains(templateXaml, "Text=\"{Binding ReadHeader}\"");
        StringAssert.Contains(templateXaml, "Text=\"{Binding ControlHeader}\"");

        // 4. Area labels
        StringAssert.Contains(templateXaml, "Text=\"{Binding AreaTelemetryLabel}\"");
        StringAssert.Contains(templateXaml, "Text=\"{Binding AreaLibraryLabel}\"");
        StringAssert.Contains(templateXaml, "Text=\"{Binding AreaBackgroundAppsLabel}\"");
        StringAssert.Contains(templateXaml, "Text=\"{Binding AreaOptimizationLabel}\"");
        StringAssert.Contains(templateXaml, "Text=\"{Binding AreaBenchmarksLabel}\"");

        // 5. All 9 scope bindings in matrix
        StringAssert.Contains(templateXaml, "IsChecked=\"{Binding ReadTelemetryEnabled, Mode=TwoWay}\"");
        StringAssert.Contains(templateXaml, "IsChecked=\"{Binding ReadLibraryEnabled, Mode=TwoWay}\"");
        StringAssert.Contains(templateXaml, "IsChecked=\"{Binding WriteLaunchEnabled, Mode=TwoWay}\"");
        StringAssert.Contains(templateXaml, "IsChecked=\"{Binding ReadBackgroundAppsEnabled, Mode=TwoWay}\"");
        StringAssert.Contains(templateXaml, "IsChecked=\"{Binding WriteBackgroundAppsEnabled, Mode=TwoWay}\"");
        StringAssert.Contains(templateXaml, "IsChecked=\"{Binding ReadOptimizationEnabled, Mode=TwoWay}\"");
        StringAssert.Contains(templateXaml, "IsChecked=\"{Binding WriteOptimizationEnabled, Mode=TwoWay}\"");
        StringAssert.Contains(templateXaml, "IsChecked=\"{Binding ReadBenchmarksEnabled, Mode=TwoWay}\"");
        StringAssert.Contains(templateXaml, "IsChecked=\"{Binding WriteBenchmarksEnabled, Mode=TwoWay}\"");

        // 6. Telemetry control unavailable placeholder
        StringAssert.Contains(templateXaml, "Text=\"—\"");
    }

    private static string? FindRepoRoot()
    {
        string? directory = Path.GetDirectoryName(typeof(SettingsCompanionIntegrationTests).Assembly.Location);
        while (directory != null && !Directory.Exists(Path.Combine(directory, "FrameHub.App")))
        {
            directory = Path.GetDirectoryName(directory);
        }
        return directory;
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
