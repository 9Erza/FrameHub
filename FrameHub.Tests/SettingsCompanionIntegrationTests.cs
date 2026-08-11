using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.Companion;
using FrameHub.Core.Models;
using FrameHub.Core.Services;
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
