using System.IO;
using System.Text;
using FrameHub.Companion.Models;
using FrameHub.Companion.Pairing;
using FrameHub.Companion.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Companion.Tests;

[TestClass]
public sealed class PairingEngineTests
{
    private string _tempDirectory = null!;
    private string _tempStorePath = null!;
    private DeviceRecordStore _store = null!;
    private DateTimeOffset _currentTime;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _tempStorePath = Path.Combine(_tempDirectory, "paired-devices.json");
        _store = new DeviceRecordStore(_tempStorePath);
        _currentTime = new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { }
        }
    }

    private PairingEngine CreateEngine()
    {
        return new PairingEngine(_store, () => _currentTime);
    }

    [TestMethod]
    public void StartPairingSession_Generates256BitEntropyToken()
    {
        var engine = CreateEngine();
        var status = engine.StartPairingSession("192.168.1.50", 47821);

        Assert.IsTrue(status.IsActive);
        Assert.IsFalse(string.IsNullOrWhiteSpace(status.PairingToken));
        Assert.IsNotNull(status.PairingUrl);
        Assert.IsTrue(status.PairingUrl.Contains("http://192.168.1.50:47821/#v=1&t="), "Pairing URL must target the root frontend page with a fragment token.");

        // Base64Url decoded token must be 32 bytes (256 bits)
        string token = status.PairingToken!;
        string base64 = token.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        byte[] decoded = Convert.FromBase64String(base64);
        Assert.AreEqual(32, decoded.Length);
    }

    [TestMethod]
    public void StartPairingSession_UrlUsesRootFragment_NotPairRouteOrQuery()
    {
        var engine = CreateEngine();
        var status = engine.StartPairingSession("192.168.1.50", 47821);

        string url = status.PairingUrl!;
        Assert.IsTrue(url.StartsWith("http://192.168.1.50:47821/#", StringComparison.Ordinal), "URL must address the served root frontend page.");
        Assert.IsFalse(url.Contains("/pair"), "No /pair page or route exists; the URL must not reference it.");
        Assert.IsFalse(url.Contains('?'), "The pairing token must stay in the URL fragment and never become a query parameter.");
        Assert.IsTrue(url.EndsWith($"#v=1&t={status.PairingToken}", StringComparison.Ordinal), "Fragment must carry the version flag and the full token.");
    }

    [TestMethod]
    public void ThrowingStatusSubscriber_DoesNotBreakPairingSessionCreation()
    {
        var engine = CreateEngine();
        int healthySubscriberCalls = 0;
        engine.SessionStatusChanged += (_, _) => throw new InvalidOperationException("subscriber failure");
        engine.SessionStatusChanged += (_, _) => healthySubscriberCalls++;

        PairingSessionStatus status = engine.StartPairingSession("127.0.0.1", 47821);

        Assert.IsTrue(status.IsActive);
        Assert.AreEqual(1, healthySubscriberCalls);
    }

    [TestMethod]
    public async Task PairingToken_ExpiresAfterTTL()
    {
        var engine = CreateEngine();
        var status = engine.StartPairingSession("127.0.0.1", 47821);
        string token = status.PairingToken!;

        // Advance clock past 3 minutes TTL
        _currentTime = _currentTime.AddMinutes(3).AddSeconds(1);

        var result = await engine.SubmitPairingRequestAsync(token, "Phone", "192.168.1.100");

        Assert.AreEqual(PairingResultStatus.Timeout, result.Status);
        Assert.IsNull(result.PlaintextCredential);
    }

    [TestMethod]
    public async Task CancelPairingSession_RejectsSubmission()
    {
        var engine = CreateEngine();
        var status = engine.StartPairingSession("127.0.0.1", 47821);
        string token = status.PairingToken!;

        engine.CancelPairingSession();

        var result = await engine.SubmitPairingRequestAsync(token, "Phone", "192.168.1.100");

        Assert.AreEqual(PairingResultStatus.InvalidToken, result.Status);
    }

    [TestMethod]
    public async Task StartingNewSession_InvalidatesPreviousToken()
    {
        var engine = CreateEngine();
        var status1 = engine.StartPairingSession("127.0.0.1", 47821);
        string token1 = status1.PairingToken!;

        var status2 = engine.StartPairingSession("127.0.0.1", 47821);
        string token2 = status2.PairingToken!;

        Assert.AreNotEqual(token1, token2);

        var result1 = await engine.SubmitPairingRequestAsync(token1, "Phone", "192.168.1.100");
        Assert.AreEqual(PairingResultStatus.InvalidToken, result1.Status);
    }

    [TestMethod]
    public async Task ValidToken_CanBeClaimedOnlyOnce()
    {
        var engine = CreateEngine();
        var status = engine.StartPairingSession("127.0.0.1", 47821);
        string token = status.PairingToken!;

        var task1 = engine.SubmitPairingRequestAsync(token, "Phone 1", "192.168.1.100");
        var task2 = engine.SubmitPairingRequestAsync(token, "Phone 2", "192.168.1.101");

        // The second submission must immediately fail because token was claimed
        var result2 = await task2;
        Assert.AreEqual(PairingResultStatus.InvalidToken, result2.Status);

        // Deny task1 to clean up
        engine.DenyPendingRequest();
        var result1 = await task1;
        Assert.AreEqual(PairingResultStatus.Denied, result1.Status);
    }

    [TestMethod]
    public async Task DesktopAllow_IssuesPermanent256BitCredentialAndPersistsRecord()
    {
        var engine = CreateEngine();
        var status = engine.StartPairingSession("127.0.0.1", 47821);
        string token = status.PairingToken!;

        var submitTask = engine.SubmitPairingRequestAsync(token, "My iPhone", "192.168.1.100");

        var currentStatus = engine.GetCurrentStatus();
        Assert.IsNotNull(currentStatus.PendingRequest);
        Assert.AreEqual("My iPhone", currentStatus.PendingRequest.DisplayName);
        Assert.AreEqual("192.168.1.100", currentStatus.PendingRequest.SourceIp);

        // Secret token must NOT be present in pending request or current status
        Assert.IsNull(currentStatus.PairingToken);

        bool allowed = engine.AllowPendingRequest(out string? credential, out var record);

        Assert.IsTrue(allowed);
        Assert.IsNotNull(credential);
        Assert.IsNotNull(record);

        var result = await submitTask;
        Assert.AreEqual(PairingResultStatus.Approved, result.Status);
        Assert.AreEqual(credential, result.PlaintextCredential);

        // Verify device record was persisted to store
        Assert.AreEqual(1, _store.Devices.Count);
        Assert.AreEqual("My iPhone", _store.Devices[0].DisplayName);
        Assert.AreEqual(record.CredentialHash, _store.Devices[0].CredentialHash);

        // Plaintext credential must NOT match stored hash directly (it's SHA-256 hashed)
        Assert.AreNotEqual(credential, _store.Devices[0].CredentialHash);
    }

    [TestMethod]
    public async Task DesktopDeny_IssuesNoCredentialAndDoesNotPersistRecord()
    {
        var engine = CreateEngine();
        var status = engine.StartPairingSession("127.0.0.1", 47821);
        string token = status.PairingToken!;

        var submitTask = engine.SubmitPairingRequestAsync(token, "Unknown Device", "192.168.1.200");

        bool denied = engine.DenyPendingRequest();
        Assert.IsTrue(denied);

        var result = await submitTask;
        Assert.AreEqual(PairingResultStatus.Denied, result.Status);
        Assert.IsNull(result.PlaintextCredential);
        Assert.AreEqual(0, _store.Devices.Count);
    }

    [TestMethod]
    public async Task CompletingPendingRequest_PublishesInactivePairingStatus()
    {
        var engine = CreateEngine();
        var statuses = new List<PairingSessionStatus>();
        engine.SessionStatusChanged += (_, status) => statuses.Add(status);
        string token = engine.StartPairingSession("127.0.0.1", 47821).PairingToken!;
        Task<PairingApprovalResult> submitTask = engine.SubmitPairingRequestAsync(token, "Phone", "192.168.1.100");

        Assert.IsTrue(engine.DenyPendingRequest());
        await submitTask;

        Assert.IsTrue(statuses.Any(status => status.PendingRequest != null));
        Assert.IsFalse(statuses[^1].IsActive);
        Assert.IsNull(statuses[^1].PendingRequest);
    }

    [TestMethod]
    public async Task FaultedStore_PreventsApprovalCredentialIssuance()
    {
        // Corrupt store
        File.WriteAllText(_tempStorePath, "{ invalid json }");
        _store.Load();
        Assert.IsTrue(_store.IsFaulted);

        var engine = CreateEngine();
        var status = engine.StartPairingSession("127.0.0.1", 47821);
        string token = status.PairingToken!;

        var submitTask = engine.SubmitPairingRequestAsync(token, "Phone", "192.168.1.100");

        bool allowed = engine.AllowPendingRequest(out string? cred, out _);
        Assert.IsFalse(allowed);
        Assert.IsNull(cred);

        var result = await submitTask;
        Assert.AreEqual(PairingResultStatus.StoreFaulted, result.Status);
    }

    [TestMethod]
    public async Task ApprovalPersistenceFailure_IssuesNoCredentialAndFailsClosed()
    {
        string unwritableStorePath = Path.Combine(_tempDirectory, "store-as-directory");
        Directory.CreateDirectory(unwritableStorePath);
        var store = new DeviceRecordStore(unwritableStorePath);
        var engine = new PairingEngine(store, () => _currentTime);
        string token = engine.StartPairingSession("127.0.0.1", 47821).PairingToken!;
        Task<PairingApprovalResult> submitTask = engine.SubmitPairingRequestAsync(token, "Phone", "192.168.1.100");

        bool allowed = engine.AllowPendingRequest(out string? credential, out var record);
        PairingApprovalResult result = await submitTask;

        Assert.IsFalse(allowed);
        Assert.IsNull(credential);
        Assert.IsNull(record);
        Assert.AreEqual(PairingResultStatus.StoreFaulted, result.Status);
        Assert.IsTrue(store.IsFaulted);
        Assert.AreEqual(0, store.Devices.Count);
    }

    [TestMethod]
    public async Task ClientDisconnect_CancelsPendingRequestAndIssuesNoCredential()
    {
        var engine = CreateEngine();
        var status = engine.StartPairingSession("127.0.0.1", 47821);
        string token = status.PairingToken!;

        using var cts = new CancellationTokenSource();
        var submitTask = engine.SubmitPairingRequestAsync(token, "Disconnecting Phone", "192.168.1.150", cts.Token);

        Assert.IsNotNull(engine.GetCurrentStatus().PendingRequest);

        // Cancel client request
        cts.Cancel();

        var result = await submitTask;
        Assert.AreEqual(PairingResultStatus.Disconnected, result.Status);
        Assert.IsNull(engine.GetCurrentStatus().PendingRequest);
        Assert.AreEqual(0, _store.Devices.Count);
    }
}
