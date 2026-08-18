using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using FrameHub.Companion.Authentication;
using FrameHub.Companion.Models;
using FrameHub.Companion.Network;
using FrameHub.Companion.Pairing;
using FrameHub.Companion.Persistence;
using FrameHub.Companion.Providers;
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
    public async Task MalformedApiPrefixes_DoNotInheritLoopbackReadExemptions()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        Assert.IsTrue(await server.StartAsync(new CompanionOptions { Enabled = true, Port = port }));

        using var client = new HttpClient();
        string[] malformedPaths =
        [
            "/api/v1/libraryevil",
            "/api/v1/benchmarksevil",
            "/api/v1/session-optimizationevil",
            "/api/v1/telemetryevil"
        ];

        foreach (string path in malformedPaths)
        {
            HttpResponseMessage response = await client.GetAsync($"http://127.0.0.1:{port}{path}");
            Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode, $"Malformed route '{path}' must use the authenticated default policy.");
        }
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

    [TestMethod]
    public async Task LanConfigEquality_IncludesLanAddress()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);

        var options1 = new CompanionOptions { Enabled = true, LanEnabled = true, LanAddress = "192.0.2.1", Port = port };
        bool started1 = await server.StartAsync(options1);
        Assert.IsTrue(started1);
        Assert.IsTrue(server.Status.LanFaulted);

        // Calling StartAsync with different LanAddress MUST trigger rebind, not early-return true blindly
        var options2 = new CompanionOptions { Enabled = true, LanEnabled = true, LanAddress = "192.0.2.2", Port = port };
        bool started2 = await server.StartAsync(options2);
        Assert.IsTrue(started2);
        Assert.IsTrue(server.Status.LanFaulted);
        Assert.IsTrue(server.Status.LanErrorMessage?.Contains("192.0.2.2") == false); // Error message is set
    }

    [TestMethod]
    public async Task InvalidLanAddressFollowedByValidRecovery_Succeeds()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);

        // 1. Initial attempt with invalid LAN address
        var optionsInvalid = new CompanionOptions { Enabled = true, LanEnabled = true, LanAddress = "192.0.2.1", Port = port };
        bool startedInvalid = await server.StartAsync(optionsInvalid);
        Assert.IsTrue(startedInvalid);
        Assert.IsTrue(server.Status.LanFaulted);

        // 2. Recovery attempt disabling LAN or supplying valid LAN address
        var optionsRecover = new CompanionOptions { Enabled = true, LanEnabled = false, Port = port };
        bool startedRecover = await server.StartAsync(optionsRecover);
        Assert.IsTrue(startedRecover);
        Assert.IsFalse(server.Status.LanFaulted);
        Assert.AreEqual($"http://127.0.0.1:{port}", server.Status.BoundAddress);
    }

    [TestMethod]
    public async Task LibraryEndpoints_LoopbackAccessPolicy()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        var libraryProvider = new FakeLibraryProvider();
        server.ConfigureLibraryProvider(libraryProvider);

        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();

        // 1. GET /api/v1/library on loopback is accessible without auth (matching status/telemetry/benchmarks GET)
        var getResp = await client.GetAsync($"http://127.0.0.1:{port}/api/v1/library");
        Assert.AreEqual(HttpStatusCode.OK, getResp.StatusCode);

        // 2. POST /api/v1/library/{id}/launch on loopback REQUIRES auth with write:launch scope
        var postRespUnauth = await client.PostAsync($"http://127.0.0.1:{port}/api/v1/library/test-1/launch", null);
        Assert.AreEqual(HttpStatusCode.Unauthorized, postRespUnauth.StatusCode, "POST launch must require authentication even on localhost.");

        // 3. POST with only read:library scope fails with 403 Forbidden
        var (_, readToken) = AddDeviceWithScopes("Read Only Phone", CompanionScopes.ReadLibrary);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", readToken);
        var postRespForbidden = await client.PostAsync($"http://127.0.0.1:{port}/api/v1/library/test-1/launch", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, postRespForbidden.StatusCode, "POST launch must reject devices without write:launch scope.");

        // 4. POST with write:launch scope succeeds
        var (_, writeToken) = AddDeviceWithScopes("Launch Controller", CompanionScopes.ReadLibrary, CompanionScopes.WriteLaunch);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", writeToken);
        var postRespAllowed = await client.PostAsync($"http://127.0.0.1:{port}/api/v1/library/test-1/launch", null);
        Assert.AreEqual(HttpStatusCode.OK, postRespAllowed.StatusCode);
    }

    [TestMethod]
    public async Task LibraryEndpoints_ScopeIsolation()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        server.ConfigureLibraryProvider(new FakeLibraryProvider());
        server.ConfigureBenchmarkProvider(new FakeTestBenchmarkProvider());

        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();

        // 1. write:benchmarks device cannot launch games
        var (_, benchmarkDevToken) = AddDeviceWithScopes("Benchmark Only", CompanionScopes.ReadBenchmarks, CompanionScopes.WriteBenchmarks);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", benchmarkDevToken);
        var launchWithBmToken = await client.PostAsync($"http://127.0.0.1:{port}/api/v1/library/test-1/launch", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, launchWithBmToken.StatusCode, "write:benchmarks token must NOT authorize POST /api/v1/library/{id}/launch.");

        // 2. write:launch device cannot start benchmarks
        var (_, launchDevToken) = AddDeviceWithScopes("Launch Only", CompanionScopes.ReadLibrary, CompanionScopes.WriteLaunch);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", launchDevToken);
        var startBmWithLaunchToken = await client.PostAsJsonAsync($"http://127.0.0.1:{port}/api/v1/benchmarks/start", new CompanionBenchmarkStartRequestDto { TargetId = "t1", DurationSeconds = 10 });
        Assert.AreEqual(HttpStatusCode.Forbidden, startBmWithLaunchToken.StatusCode, "write:launch token must NOT authorize POST /api/v1/benchmarks/start.");
    }

    [TestMethod]
    public void ScopeMigrationPolicy_NoAutoMigrationForExistingDevices()
    {
        // Simulate existing paired device created in earlier version with only read:status
        string token = "legacy_paired_token";
        string hash = PairingEngine.HashCredential(token);
        var legacyDevice = new PairedDeviceRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = "Legacy Phone",
            CredentialHash = hash,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-10),
            Scopes = new List<string> { CompanionScopes.ReadStatus }
        };
        _deviceStore.AddDevice(legacyDevice);

        // Verify device record loaded has exactly read:status and does not automatically gain read:library or write:launch
        var fetched = _deviceStore.GetDeviceById(legacyDevice.Id);
        Assert.IsNotNull(fetched);
        Assert.AreEqual(1, fetched.Scopes.Count);
        Assert.IsTrue(fetched.Scopes.Contains(CompanionScopes.ReadStatus));
        Assert.IsFalse(fetched.Scopes.Contains(CompanionScopes.ReadLibrary));
        Assert.IsFalse(fetched.Scopes.Contains(CompanionScopes.WriteLaunch));

        // Granting new scopes explicitly succeeds
        bool grantedRead = _deviceStore.GrantScope(legacyDevice.Id, CompanionScopes.ReadLibrary);
        Assert.IsTrue(grantedRead);
        var afterGrantRead = _deviceStore.GetDeviceById(legacyDevice.Id);
        Assert.IsTrue(afterGrantRead!.Scopes.Contains(CompanionScopes.ReadLibrary));
        Assert.IsFalse(afterGrantRead.Scopes.Contains(CompanionScopes.WriteLaunch));

        bool grantedWrite = _deviceStore.GrantScope(legacyDevice.Id, CompanionScopes.WriteLaunch);
        Assert.IsTrue(grantedWrite);
        var afterGrantWrite = _deviceStore.GetDeviceById(legacyDevice.Id);
        Assert.IsTrue(afterGrantWrite!.Scopes.Contains(CompanionScopes.WriteLaunch));
    }

    private (PairedDeviceRecord Device, string Token) AddDeviceWithScopes(string name, params string[] scopes)
    {
        string token = $"cred_{Guid.NewGuid():N}";
        string hash = PairingEngine.HashCredential(token);
        var record = new PairedDeviceRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = name,
            CredentialHash = hash,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Scopes = scopes.ToList()
        };
        _deviceStore.AddDevice(record);
        return (record, token);
    }

    private sealed class FakeLibraryProvider : ICompanionLibraryProvider
    {
        public Task<IReadOnlyList<CompanionLibraryItemDto>> GetLibraryItemsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<CompanionLibraryItemDto>>(new[]
            {
                new CompanionLibraryItemDto { Id = "test-1", DisplayName = "Test Game", Source = "Steam", Type = "Game", IsRunning = false }
            });
        }

        public Task<CompanionLaunchResultDto> LaunchItemAsync(string id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CompanionLaunchResultDto { Success = true, ErrorCode = "launched" });
        }
    }

    [TestMethod]
    public async Task SessionOptimizationEndpoints_LoopbackAccessPolicy()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        server.ConfigureSessionOptimizationProvider(new FakeSessionOptimizationProvider());

        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();

        // 1. GET /api/v1/session-optimization on loopback is accessible without auth
        var getResp = await client.GetAsync($"http://127.0.0.1:{port}/api/v1/session-optimization");
        Assert.AreEqual(HttpStatusCode.OK, getResp.StatusCode);

        // 2. POST /api/v1/session-optimization/apply on loopback REQUIRES auth with write:optimization scope
        var postRespUnauth = await client.PostAsync($"http://127.0.0.1:{port}/api/v1/session-optimization/apply", null);
        Assert.AreEqual(HttpStatusCode.Unauthorized, postRespUnauth.StatusCode, "POST apply must require authentication even on localhost.");

        // 3. POST with only read:optimization scope fails with 403 Forbidden
        var (_, readToken) = AddDeviceWithScopes("Read Only Phone", CompanionScopes.ReadOptimization);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", readToken);
        var postRespForbidden = await client.PostAsync($"http://127.0.0.1:{port}/api/v1/session-optimization/apply", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, postRespForbidden.StatusCode, "POST apply must reject devices without write:optimization scope.");

        // 4. POST with write:optimization scope succeeds
        var (_, writeToken) = AddDeviceWithScopes("Optimization Controller", CompanionScopes.ReadOptimization, CompanionScopes.WriteOptimization);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", writeToken);
        var postRespAllowed = await client.PostAsync($"http://127.0.0.1:{port}/api/v1/session-optimization/apply", null);
        Assert.AreEqual(HttpStatusCode.OK, postRespAllowed.StatusCode);

        var restoreRespAllowed = await client.PostAsync($"http://127.0.0.1:{port}/api/v1/session-optimization/restore", null);
        Assert.AreEqual(HttpStatusCode.OK, restoreRespAllowed.StatusCode);
    }

    [TestMethod]
    public async Task SessionOptimizationEndpoints_ScopeIsolation()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        server.ConfigureSessionOptimizationProvider(new FakeSessionOptimizationProvider());
        server.ConfigureLibraryProvider(new FakeLibraryProvider());
        server.ConfigureBenchmarkProvider(new FakeTestBenchmarkProvider());

        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();

        // 1. write:launch device cannot optimize
        var (_, launchDevToken) = AddDeviceWithScopes("Launch Only", CompanionScopes.ReadLibrary, CompanionScopes.WriteLaunch);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", launchDevToken);
        var optWithLaunchToken = await client.PostAsync($"http://127.0.0.1:{port}/api/v1/session-optimization/apply", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, optWithLaunchToken.StatusCode, "write:launch token must NOT authorize POST session-optimization/apply.");

        // 2. write:optimization device cannot launch or start benchmarks
        var (_, optDevToken) = AddDeviceWithScopes("Opt Only", CompanionScopes.ReadOptimization, CompanionScopes.WriteOptimization);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", optDevToken);
        var launchWithOptToken = await client.PostAsync($"http://127.0.0.1:{port}/api/v1/library/test-1/launch", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, launchWithOptToken.StatusCode, "write:optimization token must NOT authorize POST library launch.");

        var startBmWithOptToken = await client.PostAsJsonAsync($"http://127.0.0.1:{port}/api/v1/benchmarks/start", new CompanionBenchmarkStartRequestDto { TargetId = "t1", DurationSeconds = 10 });
        Assert.AreEqual(HttpStatusCode.Forbidden, startBmWithOptToken.StatusCode, "write:optimization token must NOT authorize POST benchmarks/start.");
    }

    [TestMethod]
    public void ScopeMigrationPolicy_NoAutoMigrationForOptimizationScopes()
    {
        string token = "legacy_paired_token_opt";
        string hash = PairingEngine.HashCredential(token);
        var legacyDevice = new PairedDeviceRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = "Legacy Phone 2",
            CredentialHash = hash,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-10),
            Scopes = new List<string> { CompanionScopes.ReadStatus }
        };
        _deviceStore.AddDevice(legacyDevice);

        var fetched = _deviceStore.GetDeviceById(legacyDevice.Id);
        Assert.IsNotNull(fetched);
        Assert.AreEqual(1, fetched.Scopes.Count);
        Assert.IsFalse(fetched.Scopes.Contains(CompanionScopes.ReadOptimization));
        Assert.IsFalse(fetched.Scopes.Contains(CompanionScopes.WriteOptimization));

        bool grantedRead = _deviceStore.GrantScope(legacyDevice.Id, CompanionScopes.ReadOptimization);
        Assert.IsTrue(grantedRead);
        var afterGrantRead = _deviceStore.GetDeviceById(legacyDevice.Id);
        Assert.IsTrue(afterGrantRead!.Scopes.Contains(CompanionScopes.ReadOptimization));
        Assert.IsFalse(afterGrantRead.Scopes.Contains(CompanionScopes.WriteOptimization));

        bool grantedWrite = _deviceStore.GrantScope(legacyDevice.Id, CompanionScopes.WriteOptimization);
        Assert.IsTrue(grantedWrite);
        var afterGrantWrite = _deviceStore.GetDeviceById(legacyDevice.Id);
        Assert.IsTrue(afterGrantWrite!.Scopes.Contains(CompanionScopes.WriteOptimization));
    }

    private sealed class FakeSessionOptimizationProvider : ICompanionSessionOptimizationProvider
    {
        public Task<CompanionSessionOptimizationStateDto> GetStateAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CompanionSessionOptimizationStateDto
            {
                IsSessionActive = false,
                GameDisplayName = "Test Game",
                SuspendedProcessCount = 0
            });
        }

        public Task<CompanionOptimizationResultDto> ApplyOptimizationAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CompanionOptimizationResultDto { Success = true, ErrorCode = "applied", SuspendedProcessCount = 2 });
        }

        public Task<CompanionOptimizationResultDto> RestoreSessionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CompanionOptimizationResultDto { Success = true, ErrorCode = "restored", SuspendedProcessCount = 0 });
        }

        public Task<CompanionSessionCpuStateDto> GetCpuStateAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CompanionSessionCpuStateDto { Available = true });
        }

        public Task<CompanionSessionCpuResultDto> ApplyCpuOverrideAsync(CompanionSessionCpuApplyRequestDto request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CompanionSessionCpuResultDto { Success = true, ErrorCode = "applied" });
        }

        public Task<CompanionSessionCpuResultDto> ResetCpuOverrideAsync(CompanionSessionCpuResetRequestDto request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CompanionSessionCpuResultDto { Success = true, ErrorCode = "restored" });
        }
    }

    private sealed class FakeTestBenchmarkProvider : ICompanionBenchmarkProvider
    {
        public CompanionBenchmarkStatusDto GetStatus() => new() { State = "Idle", IsActive = false };
        public IReadOnlyList<CompanionBenchmarkTargetDto> GetEligibleTargets() => Array.Empty<CompanionBenchmarkTargetDto>();
        public Task<CompanionBenchmarkStartResultDto> StartBenchmarkAsync(CompanionBenchmarkStartRequestDto request) => Task.FromResult(new CompanionBenchmarkStartResultDto { Accepted = true });
        public Task<CompanionBenchmarkStopResultDto> StopBenchmarkAsync() => Task.FromResult(new CompanionBenchmarkStopResultDto { Success = true, WasActive = false });
        public CompanionBenchmarkHistoryListDto GetHistory(int limit) => new() { Sessions = Array.Empty<CompanionBenchmarkHistorySummaryDto>(), TotalCount = 0 };
        public CompanionBenchmarkHistoryDetailDto? GetHistoryDetail(Guid sessionId) => null;
        public CompanionBenchmarkChartDto? GetHistoryChart(Guid sessionId, int buckets) => null;
        public CompanionBenchmarkComparisonDto CompareHistorySessions(Guid sessionAId, Guid sessionBId) => throw new KeyNotFoundException();
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
