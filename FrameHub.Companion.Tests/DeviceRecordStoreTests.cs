using System.IO;
using FrameHub.Companion.Pairing;
using FrameHub.Companion.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Companion.Tests;

[TestClass]
public sealed class DeviceRecordStoreTests
{
    private string _tempDirectory = null!;
    private string _tempStorePath = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _tempStorePath = Path.Combine(_tempDirectory, "paired-devices.json");
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
    public void MissingFile_ReturnsEmptyHealthyStore()
    {
        Assert.IsFalse(File.Exists(_tempStorePath));
        var store = new DeviceRecordStore(_tempStorePath);

        Assert.IsFalse(store.IsFaulted);
        Assert.IsNull(store.FaultMessage);
        Assert.AreEqual(0, store.Devices.Count);
    }

    [TestMethod]
    public void SaveAndLoadValidDevice_PersistsHashButNotPlaintext()
    {
        var store = new DeviceRecordStore(_tempStorePath);
        string plaintextCred = "secret_credential_12345_67890";
        string hash = PairingEngine.HashCredential(plaintextCred);

        var record = new PairedDeviceRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test Phone",
            CredentialHash = hash,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Scopes = new List<string> { "read:status" }
        };

        bool added = store.AddDevice(record);
        Assert.IsTrue(added);
        Assert.IsTrue(File.Exists(_tempStorePath));

        string rawJson = File.ReadAllText(_tempStorePath);
        Assert.IsFalse(rawJson.Contains(plaintextCred));

        // Load into new store instance and verify semantic data deserialization
        var loadedStore = new DeviceRecordStore(_tempStorePath);
        Assert.IsFalse(loadedStore.IsFaulted);
        Assert.AreEqual(1, loadedStore.Devices.Count);
        Assert.AreEqual("Test Phone", loadedStore.Devices[0].DisplayName);
        Assert.AreEqual(hash, loadedStore.Devices[0].CredentialHash);
        Assert.IsNotNull(loadedStore.FindByCredentialHash(hash));
    }

    [TestMethod]
    public void CorruptedFile_SetsFaultedStateAndDoesNotOverwrite()
    {
        string invalidJson = "CORRUPTED_JSON_CONTENT{{{";
        File.WriteAllText(_tempStorePath, invalidJson);

        var store = new DeviceRecordStore(_tempStorePath);

        Assert.IsTrue(store.IsFaulted);
        Assert.IsFalse(string.IsNullOrWhiteSpace(store.FaultMessage));
        Assert.AreEqual(0, store.Devices.Count);

        // Verify corrupted file was NOT silently overwritten or deleted
        Assert.IsTrue(File.Exists(_tempStorePath));
        Assert.AreEqual(invalidJson, File.ReadAllText(_tempStorePath));

        // Attempting write operations must throw InvalidOperationException
        var record = new PairedDeviceRecord { DisplayName = "Device" };
        Assert.ThrowsException<InvalidOperationException>(() => store.AddDevice(record));
        Assert.ThrowsException<InvalidOperationException>(() => store.RevokeDevice(Guid.NewGuid()));
    }

    [TestMethod]
    public void ResetStore_ClearsFaultedStateAndRemovesCorruptedFile()
    {
        File.WriteAllText(_tempStorePath, "INVALID_JSON");
        var store = new DeviceRecordStore(_tempStorePath);
        Assert.IsTrue(store.IsFaulted);

        store.ResetStore();

        Assert.IsFalse(store.IsFaulted);
        Assert.IsNull(store.FaultMessage);
        Assert.AreEqual(0, store.Devices.Count);
        Assert.IsTrue(File.Exists(_tempStorePath));
        Assert.AreNotEqual("INVALID_JSON", File.ReadAllText(_tempStorePath));
    }

    [TestMethod]
    public void RevokeDevice_RemovesRecordAndPersists()
    {
        var store = new DeviceRecordStore(_tempStorePath);
        var device1 = new PairedDeviceRecord { Id = Guid.NewGuid(), DisplayName = "Device 1", CredentialHash = "hash1" };
        var device2 = new PairedDeviceRecord { Id = Guid.NewGuid(), DisplayName = "Device 2", CredentialHash = "hash2" };

        store.AddDevice(device1);
        store.AddDevice(device2);

        Assert.AreEqual(2, store.Devices.Count);

        bool revoked = store.RevokeDevice(device1.Id);
        Assert.IsTrue(revoked);
        Assert.AreEqual(1, store.Devices.Count);
        Assert.AreEqual(device2.Id, store.Devices[0].Id);

        var loadedStore = new DeviceRecordStore(_tempStorePath);
        Assert.AreEqual(1, loadedStore.Devices.Count);
        Assert.AreEqual(device2.Id, loadedStore.Devices[0].Id);
    }

    [TestMethod]
    public void ConcurrentStoreWrites_DoNotCorruptJson()
    {
        var store = new DeviceRecordStore(_tempStorePath);

        var tasks = Enumerable.Range(0, 20).Select(i => Task.Run(() =>
        {
            var record = new PairedDeviceRecord
            {
                Id = Guid.NewGuid(),
                DisplayName = $"Device {i}",
                CredentialHash = $"hash_{i}"
            };
            store.AddDevice(record);
        })).ToArray();

        Task.WaitAll(tasks);

        var loadedStore = new DeviceRecordStore(_tempStorePath);
        Assert.IsFalse(loadedStore.IsFaulted);
        Assert.AreEqual(20, loadedStore.Devices.Count);
    }

    [TestMethod]
    public void UpdateLastUsed_DoesNotTriggerFileWrite()
    {
        var store = new DeviceRecordStore(_tempStorePath);
        var record = new PairedDeviceRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test Phone",
            CredentialHash = "hash123",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        store.AddDevice(record);
        Assert.IsTrue(File.Exists(_tempStorePath));

        string initialFileContent = File.ReadAllText(_tempStorePath);
        DateTimeOffset newLastUsed = DateTimeOffset.UtcNow.AddHours(1);

        bool updated = store.UpdateLastUsed(record.Id, newLastUsed);
        Assert.IsTrue(updated);

        // Verify in-memory state updated
        Assert.AreEqual(newLastUsed, store.Devices[0].LastUsedAtUtc);

        // Verify file content on disk was NOT written/changed
        string fileContentAfterAuth = File.ReadAllText(_tempStorePath);
        Assert.AreEqual(initialFileContent, fileContentAfterAuth);
    }

    [TestMethod]
    public void DisplayName_SpecialCharactersAndUnicode_RoundTripsWithDefaultEscaping()
    {
        var store = new DeviceRecordStore(_tempStorePath);
        string complexDisplayName = "Eryk's Phone <Gamer & Pro> ĄĆĘŁŃÓŚŹŻ ążćęłńóśźż \"Quotes\"";

        var record = new PairedDeviceRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = complexDisplayName,
            CredentialHash = "hash_complex",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        store.AddDevice(record);

        var loadedStore = new DeviceRecordStore(_tempStorePath);
        Assert.IsFalse(loadedStore.IsFaulted);
        Assert.AreEqual(1, loadedStore.Devices.Count);
        Assert.AreEqual(complexDisplayName, loadedStore.Devices[0].DisplayName);
    }
}
