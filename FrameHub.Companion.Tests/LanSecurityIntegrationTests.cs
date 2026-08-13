using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using FrameHub.Companion.Models;
using FrameHub.Companion.Network;
using FrameHub.Companion.Pairing;
using FrameHub.Companion.Persistence;
using FrameHub.Core.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Companion.Tests;

[TestClass]
public sealed class LanSecurityIntegrationTests
{
    private string _tempDirectory = null!;
    private string _tempStorePath = null!;
    private DeviceRecordStore _deviceStore = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _tempStorePath = Path.Combine(_tempDirectory, "paired-devices.json");
        _deviceStore = new DeviceRecordStore(_tempStorePath);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { }
        }
    }

    [TestMethod]
    public void LanAddressService_RejectsLoopbackWildcardAndIPv6()
    {
        Assert.IsFalse(LanAddressService.IsValidLanAddress("127.0.0.1"));
        Assert.IsFalse(LanAddressService.IsValidLanAddress("0.0.0.0"));
        Assert.IsFalse(LanAddressService.IsValidLanAddress("::1"));
        Assert.IsFalse(LanAddressService.IsValidLanAddress("255.255.255.255"));
        Assert.IsFalse(LanAddressService.IsValidLanAddress(null));
        Assert.IsFalse(LanAddressService.IsValidLanAddress("invalid_ip"));
    }

    [TestMethod]
    public void Rfc1918ValidationBoundaries_StrictlyEnforced()
    {
        // Accepted RFC1918
        Assert.IsTrue(LanAddressService.IsRfc1918Private(IPAddress.Parse("10.0.0.1")));
        Assert.IsTrue(LanAddressService.IsRfc1918Private(IPAddress.Parse("10.255.255.254")));
        Assert.IsTrue(LanAddressService.IsRfc1918Private(IPAddress.Parse("172.16.0.1")));
        Assert.IsTrue(LanAddressService.IsRfc1918Private(IPAddress.Parse("172.31.255.254")));
        Assert.IsTrue(LanAddressService.IsRfc1918Private(IPAddress.Parse("192.168.0.1")));
        Assert.IsTrue(LanAddressService.IsRfc1918Private(IPAddress.Parse("192.168.255.254")));

        // Rejected non-RFC1918
        Assert.IsFalse(LanAddressService.IsRfc1918Private(IPAddress.Parse("172.32.0.1")));
        Assert.IsFalse(LanAddressService.IsRfc1918Private(IPAddress.Parse("172.15.255.255")));
        Assert.IsFalse(LanAddressService.IsRfc1918Private(IPAddress.Parse("169.254.1.1"))); // APIPA
        Assert.IsFalse(LanAddressService.IsRfc1918Private(IPAddress.Parse("100.64.0.1"))); // CGNAT / Tailscale
        Assert.IsFalse(LanAddressService.IsRfc1918Private(IPAddress.Parse("8.8.8.8"))); // Public DNS
        Assert.IsFalse(LanAddressService.IsRfc1918Private(IPAddress.Parse("1.1.1.1"))); // Public DNS
        Assert.IsFalse(LanAddressService.IsRfc1918Private(IPAddress.Parse("127.0.0.1"))); // Loopback
        Assert.IsFalse(LanAddressService.IsRfc1918Private(IPAddress.Parse("::1"))); // IPv6

        // IsValidLanAddress rejection checks
        Assert.IsFalse(LanAddressService.IsValidLanAddress("169.254.1.1"));
        Assert.IsFalse(LanAddressService.IsValidLanAddress("100.64.0.1"));
        Assert.IsFalse(LanAddressService.IsValidLanAddress("8.8.8.8"));
        Assert.IsFalse(LanAddressService.IsValidLanAddress("172.32.0.1"));
    }

    [TestMethod]
    public async Task LanDisabled_OnlyLoopbackListenerBinds()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        var options = new CompanionOptions { Enabled = true, LanEnabled = false, Port = port };

        bool started = await server.StartAsync(options);
        Assert.IsTrue(started);

        Assert.AreEqual($"http://127.0.0.1:{port}", server.Status.BoundAddress);
        Assert.IsNull(server.Status.LanBoundAddress);
        Assert.IsFalse(server.Status.LanFaulted);
    }

    [TestMethod]
    public async Task InvalidLanAddress_FailsLanClosedWhileLoopbackStaysRunning()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        var options = new CompanionOptions
        {
            Enabled = true,
            LanEnabled = true,
            LanAddress = "192.0.2.1", // Test IP not assigned to any local interface
            Port = port
        };

        bool started = await server.StartAsync(options);
        Assert.IsTrue(started);

        Assert.AreEqual($"http://127.0.0.1:{port}", server.Status.BoundAddress);
        Assert.IsNull(server.Status.LanBoundAddress);
        Assert.IsTrue(server.Status.LanFaulted);
        Assert.IsNotNull(server.Status.LanErrorMessage);
    }

    [TestMethod]
    public async Task StatusEndpoint_UnauthenticatedOnLoopback_RequiresAuthOnLan()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        var options = new CompanionOptions { Enabled = true, LanEnabled = false, Port = port };

        await server.StartAsync(options);

        using var client = new HttpClient();

        // 1. Loopback GET /api/v1/status -> 200 OK (Unauthenticated allowed for localhost)
        var responseLoopback = await client.GetAsync($"http://127.0.0.1:{port}/api/v1/status");
        Assert.AreEqual(HttpStatusCode.OK, responseLoopback.StatusCode);
    }

    [TestMethod]
    public async Task StatusEndpoint_OnLan_RejectsUnauthenticatedAndRequiresScope()
    {
        var candidates = LanAddressService.GetAvailableLanAddresses();
        if (candidates.Count == 0)
        {
            Assert.Inconclusive("No active non-loopback LAN IPv4 interfaces detected on host system.");
            return;
        }

        string lanIp = candidates[0].IpAddress;
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        var options = new CompanionOptions
        {
            Enabled = true,
            LanEnabled = true,
            LanAddress = lanIp,
            Port = port
        };

        bool started = await server.StartAsync(options);
        Assert.IsTrue(started);
        Assert.AreEqual($"http://{lanIp}:{port}", server.Status.LanBoundAddress);

        using var client = new HttpClient();

        // 1. Unauthenticated request to LAN IP -> 401 Unauthorized
        var res1 = await client.GetAsync($"http://{lanIp}:{port}/api/v1/status");
        Assert.AreEqual(HttpStatusCode.Unauthorized, res1.StatusCode);

        // 2. Add device record without read:status scope
        string noScopeCred = "no_scope_credential_12345";
        string noScopeHash = PairingEngine.HashCredential(noScopeCred);
        _deviceStore.AddDevice(new PairedDeviceRecord
        {
            DisplayName = "Limited Device",
            CredentialHash = noScopeHash,
            Scopes = new List<string> { "other:scope" }
        });

        using var req2 = new HttpRequestMessage(HttpMethod.Get, $"http://{lanIp}:{port}/api/v1/status");
        req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", noScopeCred);
        var res2 = await client.SendAsync(req2);
        Assert.AreEqual(HttpStatusCode.Forbidden, res2.StatusCode);

        // 3. Add device record with read:status scope
        string validCred = "valid_lan_credential_12345";
        string validHash = PairingEngine.HashCredential(validCred);
        _deviceStore.AddDevice(new PairedDeviceRecord
        {
            DisplayName = "Valid Device",
            CredentialHash = validHash,
            Scopes = new List<string> { "read:status" }
        });

        using var req3 = new HttpRequestMessage(HttpMethod.Get, $"http://{lanIp}:{port}/api/v1/status");
        req3.Headers.Authorization = new AuthenticationHeaderValue("Bearer", validCred);
        var res3 = await client.SendAsync(req3);
        Assert.AreEqual(HttpStatusCode.OK, res3.StatusCode);
    }

    [TestMethod]
    public async Task PairingEndpoint_UnavailableOutsidePairingWindow()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        var options = new CompanionOptions { Enabled = true, Port = port };
        await server.StartAsync(options);

        using var client = new HttpClient();
        var body = new PairingRequestDto { PairingToken = "dummy_token", DisplayName = "Phone" };

        var response = await client.PostAsJsonAsync($"http://127.0.0.1:{port}/api/v1/pairing/request", body);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task HostHeaderValidation_RejectsInvalidHostHeader()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        var options = new CompanionOptions { Enabled = true, Port = port };
        await server.StartAsync(options);

        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/api/v1/status");
        request.Headers.Host = "malicious-domain.com";

        var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task FullPairingAndLANAuth_EndToEndFlow()
    {
        var candidates = LanAddressService.GetAvailableLanAddresses();
        string lanIp = candidates.Count > 0 ? candidates[0].IpAddress : "127.0.0.1";
        bool isRealLan = candidates.Count > 0;

        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        var options = new CompanionOptions
        {
            Enabled = true,
            LanEnabled = isRealLan,
            LanAddress = isRealLan ? lanIp : null,
            Port = port
        };

        await server.StartAsync(options);

        // 1. Start pairing session
        var session = server.PairingEngine.StartPairingSession(lanIp, port);
        string token = session.PairingToken!;

        // 2. Submit pairing request from client
        using var client = new HttpClient();
        var pairingTask = client.PostAsJsonAsync(
            $"http://127.0.0.1:{port}/api/v1/pairing/request",
            new PairingRequestDto { PairingToken = token, DisplayName = "EndToEnd Test Phone" });

        // Allow time for pending request to arrive
        await Task.Delay(100);
        Assert.IsTrue(server.PairingEngine.GetCurrentStatus().PendingRequest != null);

        // 3. Desktop approves request
        bool approved = server.PairingEngine.AllowPendingRequest(out string? credential, out var deviceRecord);
        Assert.IsTrue(approved);
        Assert.IsNotNull(credential);

        var pairingResponse = await pairingTask;
        Assert.AreEqual(HttpStatusCode.OK, pairingResponse.StatusCode);

        var responseDto = await pairingResponse.Content.ReadFromJsonAsync<PairingResponseDto>();
        Assert.IsNotNull(responseDto);
        Assert.AreEqual(credential, responseDto.Credential);

        if (isRealLan)
        {
            // 4. Client accesses LAN status with issued credential -> 200 OK
            using var lanReq = new HttpRequestMessage(HttpMethod.Get, $"http://{lanIp}:{port}/api/v1/status");
            lanReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
            var lanRes = await client.SendAsync(lanReq);
            Assert.AreEqual(HttpStatusCode.OK, lanRes.StatusCode);

            // 5. Revoke device on desktop
            _deviceStore.RevokeDevice(deviceRecord!.Id);

            // 6. Client accesses LAN status with revoked credential -> 401 Unauthorized
            using var revokedReq = new HttpRequestMessage(HttpMethod.Get, $"http://{lanIp}:{port}/api/v1/status");
            revokedReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
            var revokedRes = await client.SendAsync(revokedReq);
            Assert.AreEqual(HttpStatusCode.Unauthorized, revokedRes.StatusCode);
        }
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
        throw new InvalidOperationException("Could not obtain a free port.");
    }
}
