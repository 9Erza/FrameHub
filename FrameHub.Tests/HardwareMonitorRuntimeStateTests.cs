using System.IO;
using System.Net.WebSockets;
using System.Reflection;
using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.Companion;
using FrameHub.Companion.Authentication;
using FrameHub.Companion.Pairing;
using FrameHub.Companion.Persistence;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Benchmarking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

/// <summary>
/// Runtime hardware monitor state matrix, Desktop presentation mapping, and Companion
/// consumer behavior. All tests use a fake sensor backend; no LibreHardwareMonitor
/// Computer is opened, no PawnIO/elevation is required.
/// </summary>
[TestClass]
public sealed class HardwareMonitorRuntimeStateTests
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

    private AppRuntimeService CreateRuntime(FakeHardwareBackend backend) =>
        new(_tempSettingsFilePath, backend);

    // 1. enabled=false, consumers=0 => backend closed
    [TestMethod]
    public void Disabled_NoConsumers_BackendStaysClosed()
    {
        using var backend = new FakeHardwareBackend();
        using var runtime = CreateRuntime(backend);

        Assert.IsFalse(runtime.Settings.HardwareMonitorEnabled);
        Assert.IsFalse(backend.IsInitialized);
        Assert.IsFalse(runtime.IsHardwareMonitoringActive);
        Assert.AreEqual(0, backend.StartCount);
    }

    // 2. enabled=false, consumer acquired => backend remains closed
    [TestMethod]
    public void Disabled_ConsumerAcquired_BackendRemainsClosedAndMetricsEmpty()
    {
        using var backend = new FakeHardwareBackend();
        using var runtime = CreateRuntime(backend);

        using (runtime.AcquireHardwareLease())
        {
            Assert.AreEqual(1, runtime.HardwareConsumerCountForTesting);
            Assert.IsFalse(backend.IsInitialized, "Consumer registration must not force hardware on while the global setting is off.");
            Assert.IsFalse(runtime.IsHardwareMonitoringActive);
            var metrics = runtime.GetHardwareMetrics();
            Assert.AreEqual(0, metrics.CpuLoad, "Disabled monitoring must not touch the sensor backend.");
            Assert.AreEqual(0, backend.MetricsReadCount);
        }

        Assert.AreEqual(0, runtime.HardwareConsumerCountForTesting);
    }

    // 3. enabled=true, consumers=0 => backend closed
    [TestMethod]
    public void Enabled_NoConsumers_BackendStaysClosed()
    {
        using var backend = new FakeHardwareBackend();
        using var runtime = CreateRuntime(backend);
        runtime.SetHardwareMonitorEnabled(true);

        Assert.IsTrue(runtime.Settings.HardwareMonitorEnabled);
        Assert.IsFalse(backend.IsInitialized, "Setting enabled alone must not open sensors without a consumer.");
        Assert.IsFalse(runtime.IsHardwareMonitoringActive);
    }

    // 4. enabled=true, first consumer acquired => backend starts
    [TestMethod]
    public void Enabled_FirstConsumer_OpensBackend()
    {
        using var backend = new FakeHardwareBackend();
        using var runtime = CreateRuntime(backend);
        runtime.SetHardwareMonitorEnabled(true);

        using (runtime.AcquireHardwareLease())
        {
            Assert.IsTrue(backend.IsInitialized);
            Assert.IsTrue(runtime.IsHardwareMonitoringActive);
            Assert.AreEqual(1, backend.StartCount);
        }
    }

    // 5. enabled=true, second consumer acquired => no second backend/start owner
    [TestMethod]
    public void Enabled_SecondConsumer_DoesNotStartSecondBackend()
    {
        using var backend = new FakeHardwareBackend();
        using var runtime = CreateRuntime(backend);
        runtime.SetHardwareMonitorEnabled(true);

        using (runtime.AcquireHardwareLease())
        using (runtime.AcquireHardwareLease())
        {
            Assert.AreEqual(2, runtime.HardwareConsumerCountForTesting);
            Assert.AreEqual(1, backend.StartCount, "Only the first consumer may open the backend.");
            Assert.IsTrue(runtime.IsHardwareMonitoringActive);
        }
    }

    // 6. one of two consumers released => backend remains active
    [TestMethod]
    public void Enabled_OneOfTwoConsumersReleased_BackendRemainsActive()
    {
        using var backend = new FakeHardwareBackend();
        using var runtime = CreateRuntime(backend);
        runtime.SetHardwareMonitorEnabled(true);

        var first = runtime.AcquireHardwareLease();
        using var second = runtime.AcquireHardwareLease();
        first.Dispose();

        Assert.AreEqual(1, runtime.HardwareConsumerCountForTesting);
        Assert.IsTrue(backend.IsInitialized);
        Assert.IsTrue(runtime.IsHardwareMonitoringActive);
    }

    // 7. final consumer released => backend closes sensors
    [TestMethod]
    public void Enabled_FinalConsumerReleased_ClosesBackendSensors()
    {
        using var backend = new FakeHardwareBackend();
        using var runtime = CreateRuntime(backend);
        runtime.SetHardwareMonitorEnabled(true);

        var first = runtime.AcquireHardwareLease();
        var second = runtime.AcquireHardwareLease();
        first.Dispose();
        second.Dispose();

        Assert.AreEqual(0, runtime.HardwareConsumerCountForTesting);
        Assert.IsFalse(backend.IsInitialized, "Final consumer leaving must close the LibreHardwareMonitor Computer.");
        Assert.AreEqual(1, backend.ClosedSensorCount);
        Assert.IsFalse(runtime.IsHardwareMonitoringActive);
    }

    // 8. enabled true -> false while consumers exist => closes immediately
    [TestMethod]
    public void Enabled_DisabledWhileConsumersExist_ClosesImmediately()
    {
        using var backend = new FakeHardwareBackend();
        using var runtime = CreateRuntime(backend);
        runtime.SetHardwareMonitorEnabled(true);

        using (runtime.AcquireHardwareLease())
        {
            Assert.IsTrue(backend.IsInitialized);
            runtime.SetHardwareMonitorEnabled(false);

            Assert.IsFalse(runtime.Settings.HardwareMonitorEnabled);
            Assert.IsFalse(backend.IsInitialized, "Disabling the global setting must close sensors even with consumers registered.");
            Assert.AreEqual(1, backend.ClosedSensorCount);
            Assert.IsFalse(runtime.IsHardwareMonitoringActive);
        }
    }

    // 9. enabled false -> true while consumer already exists => opens
    [TestMethod]
    public void Disabled_EnabledWhileConsumerExists_OpensBackend()
    {
        using var backend = new FakeHardwareBackend();
        using var runtime = CreateRuntime(backend);

        using (runtime.AcquireHardwareLease())
        {
            Assert.IsFalse(backend.IsInitialized);
            runtime.SetHardwareMonitorEnabled(true);
            Assert.IsTrue(backend.IsInitialized, "Enabling while a consumer is registered must open the backend.");
            Assert.IsTrue(runtime.IsHardwareMonitoringActive);
        }
    }

    // 10. setting persists through SettingsService reload
    [TestMethod]
    public void HardwareMonitorEnabled_PersistsThroughSettingsReload()
    {
        using (var backend = new FakeHardwareBackend())
        using (var runtime = CreateRuntime(backend))
        {
            runtime.SetHardwareMonitorEnabled(true);
        }

        using (var reloadService = new SettingsService(_tempSettingsFilePath))
        {
            Assert.IsTrue(reloadService.LoadSettings().HardwareMonitorEnabled, "ON must survive restart.");
        }

        using (var backend = new FakeHardwareBackend())
        using (var runtime = CreateRuntime(backend))
        {
            runtime.SetHardwareMonitorEnabled(false);
        }

        using (var reloadService = new SettingsService(_tempSettingsFilePath))
        {
            Assert.IsFalse(reloadService.LoadSettings().HardwareMonitorEnabled, "OFF must survive restart.");
        }
    }

    // 11. CPU load mapping
    [TestMethod]
    public void CpuLoad_MapsToDisplayText()
    {
        Assert.AreEqual("30%", HardwareViewModel.FormatLoadPercent(isMonitorEnabled: true, load: 30.25));
        Assert.AreEqual("7%", HardwareViewModel.FormatLoadPercent(isMonitorEnabled: true, load: 7.4));
        Assert.AreEqual("--%", HardwareViewModel.FormatLoadPercent(isMonitorEnabled: false, load: 30.25));
    }

    // 12. RAM used/total formatting
    [TestMethod]
    public void Ram_UsedTotalFormatting_UsesUsedPlusAvailable()
    {
        Assert.AreEqual($"{18.7:N1} / {31.1:N1} GB", HardwareViewModel.FormatUsedTotalGb(isMonitorEnabled: true, usedGb: 18.7, totalGb: 31.1));
        Assert.AreEqual("-- / -- GB", HardwareViewModel.FormatUsedTotalGb(isMonitorEnabled: true, usedGb: 18.7, totalGb: 0), "Unknown total must stay unavailable.");
        Assert.AreEqual("-- / -- GB", HardwareViewModel.FormatUsedTotalGb(isMonitorEnabled: false, usedGb: 18.7, totalGb: 31.1));
    }

    // 13. VRAM used/total formatting from honest byte values
    [TestMethod]
    public void Vram_UsedTotalFormatting_UsesByteValuesOnly()
    {
        const double gib = 1024.0 * 1024.0 * 1024.0;
        string expected = $"{5.12 * gib / gib:N1} / {14.81 * gib / gib:N1} GB";
        Assert.AreEqual(expected, HardwareViewModel.FormatVramUsedTotalGb(isMonitorEnabled: true, usedBytes: (long)(5.12 * gib), totalBytes: (long)(14.81 * gib)));
        Assert.AreEqual("-- / -- GB", HardwareViewModel.FormatVramUsedTotalGb(isMonitorEnabled: true, usedBytes: null, totalBytes: (long)(15.9 * gib)), "Missing used bytes must not invent a value.");
        Assert.AreEqual("-- / -- GB", HardwareViewModel.FormatVramUsedTotalGb(isMonitorEnabled: true, usedBytes: (long)(5.1 * gib), totalBytes: null), "Missing total bytes must not be invented.");
        Assert.AreEqual("-- / -- GB", HardwareViewModel.FormatVramUsedTotalGb(isMonitorEnabled: false, usedBytes: (long)(5.1 * gib), totalBytes: (long)(15.9 * gib)));
    }

    // 14. CPU elevation hint shown for unavailable temp + non-elevated
    [TestMethod]
    public void CpuElevationHint_ShownForMissingTempWhenNotElevated()
    {
        Assert.IsTrue(HardwareViewModel.ShouldShowCpuElevationHint(isMonitorEnabled: true, cpuTemp: null, isProcessElevated: false));
        Assert.IsTrue(HardwareViewModel.ShouldShowCpuElevationHint(isMonitorEnabled: true, cpuTemp: 0.0, isProcessElevated: false));
        Assert.IsFalse(HardwareViewModel.ShouldShowCpuElevationHint(isMonitorEnabled: false, cpuTemp: null, isProcessElevated: false), "Disabled monitoring must not show the hint.");
    }

    // 15. CPU elevation hint hidden for valid temp
    [TestMethod]
    public void CpuElevationHint_HiddenForValidTemp()
    {
        Assert.IsFalse(HardwareViewModel.ShouldShowCpuElevationHint(isMonitorEnabled: true, cpuTemp: 57.8, isProcessElevated: false));
    }

    // 16. CPU elevation hint hidden when elevated
    [TestMethod]
    public void CpuElevationHint_HiddenWhenElevated()
    {
        Assert.IsFalse(HardwareViewModel.ShouldShowCpuElevationHint(isMonitorEnabled: true, cpuTemp: null, isProcessElevated: true));
    }

    // 17. Hardware page activation must not leak multiple consumer leases
    [TestMethod]
    public async Task HardwarePage_ActivationLifecycle_HoldsExactlyOneConsumerAndNeverChangesSetting()
    {
        using var backend = new FakeHardwareBackend();
        using var runtime = CreateRuntime(backend);
        runtime.SetHardwareMonitorEnabled(true);
        var localization = new LocalizationService(runtime.SettingsService);
        using var vm = new HardwareViewModel(localization, runtime, isElevatedProbe: () => false);

        vm.Activate();
        Assert.AreEqual(1, runtime.HardwareConsumerCountForTesting, "Opening the page registers exactly one consumer.");

        vm.Activate();
        Assert.AreEqual(1, runtime.HardwareConsumerCountForTesting, "Repeated activation must not add a second lease.");

        vm.Deactivate();
        await WaitForAsync(() => runtime.HardwareConsumerCountForTesting == 0);
        Assert.AreEqual(0, runtime.HardwareConsumerCountForTesting, "Leaving the page releases the consumer (an in-flight poll releases in its completion).");

        vm.Activate();
        Assert.AreEqual(1, runtime.HardwareConsumerCountForTesting, "Returning registers exactly one consumer again.");

        vm.Deactivate();
        vm.Dispose();
        Assert.AreEqual(0, runtime.HardwareConsumerCountForTesting, "Disposal must not leak a lease.");
        Assert.IsTrue(runtime.Settings.HardwareMonitorEnabled, "Navigation must never change the persisted setting.");
    }

    // 18. disabled hardware still permits CurrentGame / LivePerformance snapshots
    [TestMethod]
    public void TelemetryProvider_HardwareDisabled_StillPublishesCurrentGameAndLivePerformance()
    {
        using var backend = new FakeHardwareBackend();
        using var runtime = CreateRuntime(backend);
        using var activeGame = new FakeActiveGameMonitor();
        using var liveTelemetry = new FakeLiveTelemetryService();

        var provider = new AppTelemetrySnapshotProvider(runtime, activeGame, liveTelemetry);

        using (runtime.AcquireHardwareLease())
        {
            provider.UpdateSnapshotOnce();
            var snapshot = provider.CurrentSnapshot;
            Assert.IsNull(snapshot.Hardware, "Hardware must stay null while the global setting is off.");
            Assert.IsNotNull(snapshot.CurrentGame, "Active Game telemetry is independent of hardware monitoring.");
            Assert.IsNotNull(snapshot.LivePerformance, "Live PresentMon telemetry is independent of hardware monitoring.");
        }
    }

    // 22. exactly one hardware monitor backend owner in the App runtime
    [TestMethod]
    public void AppRuntime_OwnsExactlyOneHardwareBackendField()
    {
        var fields = typeof(AppRuntimeService)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(f => f.FieldType == typeof(IHardwareMonitorBackend))
            .ToList();
        Assert.AreEqual(1, fields.Count, "There must remain exactly one HardwareMonitorService/Computer owner in the runtime.");
    }

    // 18-20 Companion integration: real WebSocket lease behavior through the runtime
    [TestMethod]
    public async Task WebSocket_WithHardwareDisabled_RegistersConsumerButNeverInitializesBackend_AndCloseReleasesIt()
    {
        string storePath = Path.Combine(_tempDirectory, "paired-devices.json");
        using var backend = new FakeHardwareBackend();
        using var runtime = CreateRuntime(backend);
        using var server = new CompanionServer(new DeviceRecordStore(storePath));
        server.ConfigureTelemetryProvider(runtime.TelemetryProvider, runtime.AcquireHardwareLease);

        var device = new PairedDeviceRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test Device",
            CredentialHash = PairingEngine.HashCredential("hw-test-token"),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Scopes = new List<string> { CompanionScopes.ReadStatus, CompanionScopes.ReadTelemetry }
        };
        server.DeviceStore.AddDevice(device);

        Assert.IsTrue(await server.StartAsync(new CompanionOptions { Enabled = true, Port = 47831, LanEnabled = false }));
        try
        {
            string ticket = server.TicketStore.IssueTicket(device.Id, TimeSpan.FromSeconds(30));
            using var ws = new ClientWebSocket();
            ws.Options.AddSubProtocol("framehub.v1");
            ws.Options.AddSubProtocol($"ticket.{ticket}");
            await ws.ConnectAsync(new Uri("ws://127.0.0.1:47831/api/v1/telemetry/ws"), CancellationToken.None);

            await WaitForAsync(() => runtime.HardwareConsumerCountForTesting == 1);
            Assert.AreEqual(1, runtime.HardwareConsumerCountForTesting, "One connected telemetry WebSocket is one consumer.");
            Assert.IsFalse(backend.IsInitialized, "Connected Companion must not open hardware sensors while the setting is off.");

            runtime.TelemetryProvider.UpdateSnapshotOnce();
            Assert.IsNull(runtime.TelemetryProvider.CurrentSnapshot.Hardware);

            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
            await WaitForAsync(() => runtime.HardwareConsumerCountForTesting == 0);
            Assert.AreEqual(0, runtime.HardwareConsumerCountForTesting, "WebSocket close releases exactly one consumer.");
            Assert.IsFalse(backend.IsInitialized);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    // 19. connected WebSocket with hardware enabled counts as one consumer and keeps backend open
    [TestMethod]
    public async Task WebSocket_WithHardwareEnabled_KeepsBackendOpenUntilDisconnect()
    {
        string storePath = Path.Combine(_tempDirectory, "paired-devices.json");
        using var backend = new FakeHardwareBackend();
        using var runtime = CreateRuntime(backend);
        runtime.SetHardwareMonitorEnabled(true);
        using var server = new CompanionServer(new DeviceRecordStore(storePath));
        server.ConfigureTelemetryProvider(runtime.TelemetryProvider, runtime.AcquireHardwareLease);

        var device = new PairedDeviceRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test Device",
            CredentialHash = PairingEngine.HashCredential("hw-test-token-2"),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Scopes = new List<string> { CompanionScopes.ReadStatus, CompanionScopes.ReadTelemetry }
        };
        server.DeviceStore.AddDevice(device);

        Assert.IsTrue(await server.StartAsync(new CompanionOptions { Enabled = true, Port = 47832, LanEnabled = false }));
        try
        {
            string ticket = server.TicketStore.IssueTicket(device.Id, TimeSpan.FromSeconds(30));
            using var ws = new ClientWebSocket();
            ws.Options.AddSubProtocol("framehub.v1");
            ws.Options.AddSubProtocol($"ticket.{ticket}");
            await ws.ConnectAsync(new Uri("ws://127.0.0.1:47832/api/v1/telemetry/ws"), CancellationToken.None);

            await WaitForAsync(() => runtime.HardwareConsumerCountForTesting == 1);
            Assert.IsTrue(backend.IsInitialized, "Connected Companion is a valid consumer while the setting is on.");
            Assert.IsTrue(runtime.IsHardwareMonitoringActive);

            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
            await WaitForAsync(() => runtime.HardwareConsumerCountForTesting == 0);
            Assert.IsFalse(backend.IsInitialized, "Last Companion disconnect must close the sensors.");
        }
        finally
        {
            await server.StopAsync();
        }
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(50);
        }
        Assert.Fail("Condition was not met within the timeout.");
    }

    private sealed class FakeHardwareBackend : IHardwareMonitorBackend
    {
        public HardwareMetrics Metrics { get; set; } = new();
        public int StartCount { get; private set; }
        public int ClosedSensorCount { get; private set; }
        public int MetricsReadCount { get; private set; }
        public bool IsInitialized { get; private set; }
        public bool Disposed { get; private set; }

        public void Configure(bool enableStorageSensors) { }
        public void Start() { StartCount++; IsInitialized = true; }
        public void Stop(bool closeSensors = false)
        {
            if (closeSensors)
            {
                ClosedSensorCount++;
                IsInitialized = false;
            }
        }
        public HardwareMetrics GetAllMetrics()
        {
            if (!IsInitialized) throw new InvalidOperationException("Backend must not be read while closed.");
            MetricsReadCount++;
            return Metrics;
        }
        public void Dispose() { Disposed = true; IsInitialized = false; }
    }

    private sealed class FakeActiveGameMonitor : IActiveGameMonitor
    {
        public ActiveGameSnapshot? CurrentSnapshot { get; } = new(
            new LibraryItem { Id = "lib-1", DisplayName = "Test Game" },
            new BenchmarkProcessIdentity { ProcessId = 4242, ProcessName = "testgame", StartTimeUtc = DateTime.UtcNow }
        );
        public void Start() { }
        public Task StopAsync() => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class FakeLiveTelemetryService : ILivePerformanceTelemetryService
    {
        public LivePerformanceSnapshot? CurrentSnapshot { get; } = new(
            ProcessId: 4242,
            LibraryItemId: "lib-1",
            SwapChainAddress: "0x1234",
            CurrentFps: 240.0,
            CurrentFrametimeMs: 4.2,
            OnePercentLowFps: 180.0,
            PointOnePercentLowFps: 150.0,
            SampleCount: 100,
            CapturedAtUtc: DateTimeOffset.UtcNow
        );
        public void Start() { }
        public Task StopAsync() => Task.CompletedTask;
        public void Dispose() { }
    }
}
