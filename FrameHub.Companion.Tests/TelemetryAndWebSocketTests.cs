using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FrameHub.Companion.Authentication;
using FrameHub.Companion.Models;
using FrameHub.Companion.Pairing;
using FrameHub.Companion.Persistence;
using FrameHub.Companion.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Companion.Tests;

[TestClass]
public sealed class TelemetryAndWebSocketTests
{
    private string _tempDirectory = null!;
    private string _tempFile = null!;
    private DeviceRecordStore _store = null!;
    private WebSocketTicketStore _ticketStore = null!;
    private DateTimeOffset _now;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.TelemetryTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _tempFile = Path.Combine(_tempDirectory, "paired-devices.json");
        _store = new DeviceRecordStore(_tempFile);
        _now = new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
        _ticketStore = new WebSocketTicketStore(() => _now);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { }
        }
    }

    [TestMethod]
    public void DeviceRecordStore_DefaultScopeIsReadStatusOnly()
    {
        var record = new PairedDeviceRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test Phone",
            CredentialHash = PairingEngine.HashCredential("secret-token"),
            CreatedAtUtc = _now
        };

        _store.AddDevice(record);

        var loaded = _store.GetDeviceById(record.Id);
        Assert.IsNotNull(loaded);
        Assert.AreEqual(1, loaded.Scopes.Count);
        Assert.AreEqual(CompanionScopes.ReadStatus, loaded.Scopes[0]);
        Assert.IsFalse(loaded.Scopes.Contains(CompanionScopes.ReadTelemetry));
    }

    [TestMethod]
    public void DeviceRecordStore_GrantAndRevokeScope_PersistsAtomically()
    {
        var record = new PairedDeviceRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test Phone",
            CredentialHash = PairingEngine.HashCredential("secret-token"),
            CreatedAtUtc = _now
        };
        _store.AddDevice(record);

        // Grant scope
        bool granted = _store.GrantScope(record.Id, CompanionScopes.ReadTelemetry);
        Assert.IsTrue(granted);

        var reloadedStore = new DeviceRecordStore(_tempFile);
        var loaded = reloadedStore.GetDeviceById(record.Id);
        Assert.IsNotNull(loaded);
        Assert.IsTrue(loaded.Scopes.Contains(CompanionScopes.ReadTelemetry));

        // Revoke scope
        bool revoked = _store.RevokeScope(record.Id, CompanionScopes.ReadTelemetry);
        Assert.IsTrue(revoked);

        var reloadedStore2 = new DeviceRecordStore(_tempFile);
        var loaded2 = reloadedStore2.GetDeviceById(record.Id);
        Assert.IsNotNull(loaded2);
        Assert.IsFalse(loaded2.Scopes.Contains(CompanionScopes.ReadTelemetry));
    }

    [TestMethod]
    public void DeviceRecordStore_ScopeIsolation_ExternalMutationFails()
    {
        var record = new PairedDeviceRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test Phone",
            CredentialHash = PairingEngine.HashCredential("secret-token"),
            CreatedAtUtc = _now,
            Scopes = new[] { CompanionScopes.ReadStatus }
        };
        _store.AddDevice(record);

        var fetched = _store.GetDeviceById(record.Id);
        Assert.IsNotNull(fetched);

        // Attempt to downcast and mutate array
        var scopesArray = (string[])fetched.Scopes;
        scopesArray[0] = CompanionScopes.ReadTelemetry;

        // Verify internal store state remains unchanged
        var refetched = _store.GetDeviceById(record.Id);
        Assert.IsNotNull(refetched);
        Assert.AreEqual(CompanionScopes.ReadStatus, refetched.Scopes[0]);
        Assert.IsFalse(refetched.Scopes.Contains(CompanionScopes.ReadTelemetry));
    }

    [TestMethod]
    public void DeviceRecordStore_InvalidScope_Rejected()
    {
        var record = new PairedDeviceRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test Phone",
            CredentialHash = PairingEngine.HashCredential("secret-token"),
            CreatedAtUtc = _now
        };
        _store.AddDevice(record);

        bool granted = _store.GrantScope(record.Id, "invalid:scope");
        Assert.IsFalse(granted);
    }

    [TestMethod]
    public void WebSocketTicketStore_IssueAndConsume_OneUseOnly()
    {
        var record = new PairedDeviceRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test Phone",
            CredentialHash = PairingEngine.HashCredential("secret-token"),
            CreatedAtUtc = _now,
            Scopes = new List<string> { CompanionScopes.ReadStatus, CompanionScopes.ReadTelemetry }
        };
        _store.AddDevice(record);

        string ticket = _ticketStore.IssueTicket(record.Id, TimeSpan.FromSeconds(30));
        Assert.IsFalse(string.IsNullOrWhiteSpace(ticket));

        // First consume succeeds
        bool firstResult = _ticketStore.TryConsumeTicket(ticket, _store, out Guid consumedId);
        Assert.IsTrue(firstResult);
        Assert.AreEqual(record.Id, consumedId);

        // Second consume fails (one-use)
        bool secondResult = _ticketStore.TryConsumeTicket(ticket, _store, out _);
        Assert.IsFalse(secondResult);
    }

    [TestMethod]
    public void WebSocketTicketStore_MaxOneTicketPerDevice()
    {
        var record = new PairedDeviceRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test Phone",
            CredentialHash = PairingEngine.HashCredential("secret-token"),
            CreatedAtUtc = _now,
            Scopes = new List<string> { CompanionScopes.ReadStatus, CompanionScopes.ReadTelemetry }
        };
        _store.AddDevice(record);

        string ticket1 = _ticketStore.IssueTicket(record.Id, TimeSpan.FromSeconds(30));
        string ticket2 = _ticketStore.IssueTicket(record.Id, TimeSpan.FromSeconds(30));

        Assert.AreNotEqual(ticket1, ticket2);

        // Ticket 1 was invalidated by ticket 2 issuance
        bool consume1 = _ticketStore.TryConsumeTicket(ticket1, _store, out _);
        Assert.IsFalse(consume1);

        // Ticket 2 succeeds
        bool consume2 = _ticketStore.TryConsumeTicket(ticket2, _store, out Guid consumedId);
        Assert.IsTrue(consume2);
        Assert.AreEqual(record.Id, consumedId);
    }

    [TestMethod]
    public async Task WebSocketTicketStore_ConcurrentConsumption_ExactlyOneSucceeds()
    {
        var record = new PairedDeviceRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test Phone",
            CredentialHash = PairingEngine.HashCredential("secret-token"),
            CreatedAtUtc = _now,
            Scopes = new List<string> { CompanionScopes.ReadStatus, CompanionScopes.ReadTelemetry }
        };
        _store.AddDevice(record);

        string ticket = _ticketStore.IssueTicket(record.Id, TimeSpan.FromSeconds(30));

        int successCount = 0;
        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(() =>
        {
            if (_ticketStore.TryConsumeTicket(ticket, _store, out Guid _))
            {
                Interlocked.Increment(ref successCount);
            }
        })).ToArray();

        await Task.WhenAll(tasks);
        Assert.AreEqual(1, successCount);
    }

    [TestMethod]
    public void WebSocketTicketStore_RevokeScopeBeforeUpgrade_RejectsUpgrade()
    {
        var record = new PairedDeviceRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test Phone",
            CredentialHash = PairingEngine.HashCredential("secret-token"),
            CreatedAtUtc = _now,
            Scopes = new List<string> { CompanionScopes.ReadStatus, CompanionScopes.ReadTelemetry }
        };
        _store.AddDevice(record);

        string ticket = _ticketStore.IssueTicket(record.Id, TimeSpan.FromSeconds(30));

        // Revoke scope before consume
        _store.RevokeScope(record.Id, CompanionScopes.ReadTelemetry);

        bool result = _ticketStore.TryConsumeTicket(ticket, _store, out _);
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void WebSocketTicketStore_ExpiredTicket_Fails()
    {
        var record = new PairedDeviceRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test Phone",
            CredentialHash = PairingEngine.HashCredential("secret-token"),
            CreatedAtUtc = _now,
            Scopes = new List<string> { CompanionScopes.ReadStatus, CompanionScopes.ReadTelemetry }
        };
        _store.AddDevice(record);

        string ticket = _ticketStore.IssueTicket(record.Id, TimeSpan.FromSeconds(30));

        // Advance clock by 31 seconds
        _now = _now.AddSeconds(31);

        bool result = _ticketStore.TryConsumeTicket(ticket, _store, out _);
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void WebSocketTicketStore_RevokedDevice_FailsTicketConsumption()
    {
        var record = new PairedDeviceRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test Phone",
            CredentialHash = PairingEngine.HashCredential("secret-token"),
            CreatedAtUtc = _now,
            Scopes = new List<string> { CompanionScopes.ReadStatus, CompanionScopes.ReadTelemetry }
        };
        _store.AddDevice(record);

        string ticket = _ticketStore.IssueTicket(record.Id, TimeSpan.FromSeconds(30));

        // Revoke device before ticket consume
        _store.RevokeDevice(record.Id);

        bool result = _ticketStore.TryConsumeTicket(ticket, _store, out _);
        Assert.IsFalse(result);
    }

    [TestMethod]
    public async Task Rest_TelemetryEndpoint_LocalhostAndLanAuthorization()
    {
        using var server = new CompanionServer(_store, () => _now);
        var provider = new TestTelemetrySnapshotProvider();
        server.ConfigureTelemetryProvider(provider);

        int port = 49100;
        var options = new CompanionOptions
        {
            Enabled = true,
            Port = port,
            LanEnabled = true,
            LanAddress = "192.168.1.50"
        };

        // Inject paired device
        string tokenStr = "test-token-1234567890";
        var device = new PairedDeviceRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test Device",
            CredentialHash = PairingEngine.HashCredential(tokenStr),
            CreatedAtUtc = _now,
            Scopes = new List<string> { CompanionScopes.ReadStatus } // Status only!
        };
        _store.AddDevice(device);

        var started = await server.StartAsync(options);
        Assert.IsTrue(started);

        try
        {
            using var client = new HttpClient();

            // 1. Localhost unauthenticated GET /api/v1/telemetry -> 200 OK
            var localResp = await client.GetAsync($"http://127.0.0.1:{port}/api/v1/telemetry");
            Assert.AreEqual(HttpStatusCode.OK, localResp.StatusCode);

            // 2. POST /api/v1/telemetry/ws-ticket without read:telemetry -> 403 Forbidden
            var wsTicketReq = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/api/v1/telemetry/ws-ticket");
            wsTicketReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenStr);
            var wsTicketResp = await client.SendAsync(wsTicketReq);
            Assert.AreEqual(HttpStatusCode.Forbidden, wsTicketResp.StatusCode);

            // 3. Grant read:telemetry scope to device
            _store.GrantScope(device.Id, CompanionScopes.ReadTelemetry);

            // 4. POST /api/v1/telemetry/ws-ticket with read:telemetry -> 200 OK with ticket DTO
            var wsTicketReq2 = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/api/v1/telemetry/ws-ticket");
            wsTicketReq2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenStr);
            var wsTicketResp2 = await client.SendAsync(wsTicketReq2);
            Assert.AreEqual(HttpStatusCode.OK, wsTicketResp2.StatusCode);

            string body = await wsTicketResp2.Content.ReadAsStringAsync();
            var ticketDto = JsonSerializer.Deserialize<WebSocketTicketResponseDto>(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            Assert.IsNotNull(ticketDto);
            Assert.IsFalse(string.IsNullOrWhiteSpace(ticketDto.Ticket));
            Assert.AreEqual(30, ticketDto.ExpiresInSeconds);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [TestMethod]
    public async Task WebSocket_SubprotocolHandshakeAndPolicyViolation()
    {
        using var server = new CompanionServer(_store, () => _now);
        var provider = new TestTelemetrySnapshotProvider();
        server.ConfigureTelemetryProvider(provider);

        int port = 49101;
        var options = new CompanionOptions
        {
            Enabled = true,
            Port = port,
            LanEnabled = false
        };

        string tokenStr = "ws-test-token-abcdef";
        var device = new PairedDeviceRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test Phone",
            CredentialHash = PairingEngine.HashCredential(tokenStr),
            CreatedAtUtc = _now,
            Scopes = new List<string> { CompanionScopes.ReadStatus, CompanionScopes.ReadTelemetry }
        };
        _store.AddDevice(device);

        var started = await server.StartAsync(options);
        Assert.IsTrue(started);

        try
        {
            // Issue ticket directly
            string ticket = server.TicketStore.IssueTicket(device.Id, TimeSpan.FromSeconds(30));

            using var ws = new ClientWebSocket();
            ws.Options.AddSubProtocol("framehub.v1");
            ws.Options.AddSubProtocol($"ticket.{ticket}");

            var uri = new Uri($"ws://127.0.0.1:{port}/api/v1/telemetry/ws");
            await ws.ConnectAsync(uri, CancellationToken.None);

            Assert.AreEqual(WebSocketState.Open, ws.State);
            Assert.AreEqual("framehub.v1", ws.SubProtocol); // Secret ticket protocol is NOT echoed back!

            // Receive telemetry snapshot
            var buffer = new byte[2048];
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            Assert.AreEqual(WebSocketMessageType.Text, result.MessageType);
            string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            Assert.IsTrue(json.Contains("capturedAtUtc", StringComparison.OrdinalIgnoreCase));

            // Send client text message -> triggers PolicyViolation close
            byte[] clientMsg = Encoding.UTF8.GetBytes("hello from client");
            await ws.SendAsync(new ArraySegment<byte>(clientMsg), WebSocketMessageType.Text, true, CancellationToken.None);

            var closeResult = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            Assert.AreEqual(WebSocketMessageType.Close, closeResult.MessageType);
            Assert.AreEqual(WebSocketCloseStatus.PolicyViolation, closeResult.CloseStatus);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [TestMethod]
    public void CompanionScopes_WriteTelemetry_IsKnownScope()
    {
        Assert.AreEqual("write:telemetry", CompanionScopes.WriteTelemetry);
        Assert.IsTrue(CompanionScopes.KnownScopes.Contains(CompanionScopes.WriteTelemetry));
    }

    [TestMethod]
    public void NullCompanionHardwareMonitoringProvider_ReturnsDisabledAndInactive()
    {
        var provider = new NullCompanionHardwareMonitoringProvider();
        var status = provider.GetStatus();
        Assert.IsFalse(status.Enabled);
        Assert.IsFalse(status.Active);

        var updated = provider.SetEnabled(true);
        Assert.IsFalse(updated.Enabled);
        Assert.IsFalse(updated.Active);
    }

    [TestMethod]
    public async Task HardwareMonitorApi_GetAndPost_EnforcesSecurityAndScopes()
    {
        int port = GetFreePort();
        using var server = new CompanionServer(_store, () => _now);
        server.ConfigureTelemetryProvider(new TestTelemetrySnapshotProvider());
        var mockProvider = new TestHardwareMonitoringProvider(enabled: false, active: false);
        server.ConfigureHardwareMonitoringProvider(mockProvider);

        var started = await server.StartAsync(new CompanionOptions
        {
            Enabled = true,
            Port = port,
            LanEnabled = false
        });
        Assert.IsTrue(started);

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

            // 1. GET /api/v1/telemetry/hardware-monitor (loopback unauthenticated allows read)
            var getResp = await client.GetAsync("/api/v1/telemetry/hardware-monitor");
            Assert.AreEqual(HttpStatusCode.OK, getResp.StatusCode);
            var statusDto = await getResp.Content.ReadFromJsonAsync<HardwareMonitoringStatusDto>();
            Assert.IsNotNull(statusDto);
            Assert.IsFalse(statusDto.Enabled);
            Assert.IsFalse(statusDto.Active);

            // 2. POST /api/v1/telemetry/hardware-monitor without Auth header -> 401 Unauthorized (even on loopback!)
            var postUnauthResp = await client.PostAsJsonAsync("/api/v1/telemetry/hardware-monitor", new SetHardwareMonitoringRequestDto(true));
            Assert.AreEqual(HttpStatusCode.Unauthorized, postUnauthResp.StatusCode, "POST to hardware-monitor must require auth even on loopback.");

            // 3. Create paired device with read:telemetry only (no write:telemetry)
            string token = "test-token-read-only";
            var deviceReadOnly = new PairedDeviceRecord
            {
                Id = Guid.NewGuid(),
                DisplayName = "Read Only Device",
                CredentialHash = PairingEngine.HashCredential(token),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Scopes = new List<string> { CompanionScopes.ReadTelemetry }
            };
            server.DeviceStore.AddDevice(deviceReadOnly);

            // POST with read-only token -> 403 Forbidden
            using var readOnlyReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/telemetry/hardware-monitor");
            readOnlyReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            readOnlyReq.Content = JsonContent.Create(new SetHardwareMonitoringRequestDto(true));
            var postForbiddenResp = await client.SendAsync(readOnlyReq);
            Assert.AreEqual(HttpStatusCode.Forbidden, postForbiddenResp.StatusCode, "POST without write:telemetry must return 403.");

            // 4. Create paired device with write:telemetry scope
            string writeToken = "test-token-write";
            var deviceWrite = new PairedDeviceRecord
            {
                Id = Guid.NewGuid(),
                DisplayName = "Write Device",
                CredentialHash = PairingEngine.HashCredential(writeToken),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Scopes = new List<string> { CompanionScopes.WriteTelemetry }
            };
            server.DeviceStore.AddDevice(deviceWrite);

            // POST with write token -> 200 OK and toggles provider
            using var writeReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/telemetry/hardware-monitor");
            writeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", writeToken);
            writeReq.Content = JsonContent.Create(new SetHardwareMonitoringRequestDto(true));
            var postSuccessResp = await client.SendAsync(writeReq);
            Assert.AreEqual(HttpStatusCode.OK, postSuccessResp.StatusCode);
            var updatedDto = await postSuccessResp.Content.ReadFromJsonAsync<HardwareMonitoringStatusDto>();
            Assert.IsNotNull(updatedDto);
            Assert.IsTrue(updatedDto.Enabled);
            Assert.IsTrue(mockProvider.GetStatus().Enabled);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    private sealed class TestHardwareMonitoringProvider : ICompanionHardwareMonitoringProvider
    {
        private bool _enabled;
        private bool _active;

        public TestHardwareMonitoringProvider(bool enabled, bool active)
        {
            _enabled = enabled;
            _active = active;
        }

        public HardwareMonitoringStatusDto GetStatus() => new(_enabled, _active);

        public HardwareMonitoringStatusDto SetEnabled(bool enabled)
        {
            _enabled = enabled;
            _active = enabled;
            return GetStatus();
        }
    }

    private sealed class TestTelemetrySnapshotProvider : ITelemetrySnapshotProvider
    {
        public CompanionTelemetrySnapshot CurrentSnapshot { get; } = new(
            CapturedAtUtc: DateTimeOffset.UtcNow,
            Hardware: new HardwareTelemetrySnapshot(
                CpuUtilizationPercent: 25.5,
                CpuTemperatureCelsius: 55.0,
                GpuUtilizationPercent: 90.0,
                GpuTemperatureCelsius: 65.0,
                RamUsedBytes: 8500000000,
                RamTotalBytes: 16000000000,
                VramUsedBytes: 4000000000,
                VramTotalBytes: 8000000000
            ),
            CurrentGame: new CurrentGameSnapshot(
                LibraryItemId: "cs2-id",
                DisplayName: "Counter-Strike 2",
                IsRunning: true,
                ProcessStartTimeUtc: DateTimeOffset.UtcNow.AddMinutes(-10)
            ),
            HardwareMonitor: new HardwareMonitoringStatusDto(Enabled: true, Active: true)
        );
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
