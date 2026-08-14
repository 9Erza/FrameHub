using FrameHub.Core.Models.SessionOptimization;
using FrameHub.Core.Services.SessionOptimization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace FrameHub.Tests;

[TestClass]
public sealed class SessionStateServiceTests
{
    private string _tempDirectory = null!;
    private string _statePath = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub_SessionStateTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _statePath = Path.Combine(_tempDirectory, "active_session.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, recursive: true);
        }
        catch { }
    }

    [TestMethod]
    public void Load_MalformedReadablePrimary_UsesValidTypedBackup()
    {
        ActiveSessionState backup = CreateState("backup");
        File.WriteAllText(_statePath, "{ malformed json");
        File.WriteAllText(_statePath + ".bak", JsonSerializer.Serialize(backup));

        ActiveSessionState? loaded = new SessionStateService(_statePath).Load();

        Assert.AreEqual("backup", loaded?.SessionId);
    }

    [TestMethod]
    public void Load_MissingPrimary_UsesValidTypedBackup()
    {
        File.WriteAllText(_statePath + ".bak", JsonSerializer.Serialize(CreateState("backup-only")));

        ActiveSessionState? loaded = new SessionStateService(_statePath).Load();

        Assert.AreEqual("backup-only", loaded?.SessionId);
    }

    [TestMethod]
    public void Load_ValidPrimary_WinsOverOlderValidBackup()
    {
        File.WriteAllText(_statePath, JsonSerializer.Serialize(CreateState("primary")));
        File.WriteAllText(_statePath + ".bak", JsonSerializer.Serialize(CreateState("older-backup")));

        ActiveSessionState? loaded = new SessionStateService(_statePath).Load();

        Assert.AreEqual("primary", loaded?.SessionId);
    }

    [TestMethod]
    public void Load_BothFilesMalformed_ReturnsNull()
    {
        File.WriteAllText(_statePath, "not json");
        File.WriteAllText(_statePath + ".bak", "also not json");

        Assert.IsNull(new SessionStateService(_statePath).Load());
    }

    [TestMethod]
    public void Load_ValidLegacyStateWithoutNewWalFields_RemainsCompatible()
    {
        const string legacyJson = """
            {
              "SessionId": "legacy",
              "IsActive": true,
              "RecoveryPhase": 3,
              "PlannedProcesses": [],
              "SuspendedProcesses": []
            }
            """;
        File.WriteAllText(_statePath, legacyJson);

        ActiveSessionState? loaded = new SessionStateService(_statePath).Load();

        Assert.AreEqual("legacy", loaded?.SessionId);
        Assert.IsNotNull(loaded?.AmbiguousProcesses);
        Assert.IsNull(loaded?.PendingResume);
        Assert.IsNull(loaded?.OriginalTaskbarVisibility);
    }

    [TestMethod]
    public void Load_StructurallyInvalidPrimary_UsesValidBackup()
    {
        File.WriteAllText(_statePath, """
            { "SessionId": "invalid", "RecoveryPhase": 999, "PlannedProcesses": [], "SuspendedProcesses": [] }
            """);
        File.WriteAllText(_statePath + ".bak", JsonSerializer.Serialize(CreateState("valid-backup")));

        ActiveSessionState? loaded = new SessionStateService(_statePath).Load();

        Assert.AreEqual("valid-backup", loaded?.SessionId);
    }

    [TestMethod]
    public void EmptyObjectPrimaryFallsBackToValidBackup()
    {
        File.WriteAllText(_statePath, "{}");
        File.WriteAllText(_statePath + ".bak", JsonSerializer.Serialize(CreateState("empty-primary-backup")));

        ActiveSessionState? loaded = new SessionStateService(_statePath).Load();

        Assert.AreEqual("empty-primary-backup", loaded?.SessionId);
    }

    [TestMethod]
    public void MissingRequiredMemberPrimaryFallsBackToValidBackup()
    {
        File.WriteAllText(_statePath, """
            { "IsActive": true, "SuspendedProcesses": [] }
            """);
        File.WriteAllText(_statePath + ".bak", JsonSerializer.Serialize(CreateState("missing-identity-backup")));

        ActiveSessionState? loaded = new SessionStateService(_statePath).Load();

        Assert.AreEqual("missing-identity-backup", loaded?.SessionId);
    }

    [TestMethod]
    public void MissingCoreCollectionPrimaryFallsBackToValidBackup()
    {
        File.WriteAllText(_statePath, """
            { "IsActive": true, "SessionId": "missing-collection" }
            """);
        File.WriteAllText(_statePath + ".bak", JsonSerializer.Serialize(CreateState("missing-collection-backup")));

        ActiveSessionState? loaded = new SessionStateService(_statePath).Load();

        Assert.AreEqual("missing-collection-backup", loaded?.SessionId);
    }

    [TestMethod]
    public void ValidLegacyPrimaryStillLoads()
    {
        const string legacyJson = """
            {
              "IsActive": true,
              "SessionId": "actual-legacy",
              "Trigger": "Manual",
              "GameId": "legacy-game",
              "GameName": "Legacy Game",
              "GameProcessName": "legacy.exe",
              "StartedAtUtc": "2026-01-02T03:04:05Z",
              "TaskbarHidden": false,
              "IsRecoveryPending": true,
              "SuspendedProcesses": []
            }
            """;
        File.WriteAllText(_statePath, legacyJson);

        ActiveSessionState? loaded = new SessionStateService(_statePath).Load();

        Assert.AreEqual("actual-legacy", loaded?.SessionId);
        Assert.IsTrue(loaded?.IsActive);
        Assert.IsNotNull(loaded?.AmbiguousProcesses);
    }

    [TestMethod]
    public void ValidWalPrimaryStillLoads()
    {
        ActiveSessionState wal = CreateState("wal");
        wal.RecoveryPhase = SessionRecoveryPhase.Restoring;
        wal.IsRecoveryPending = true;
        wal.PendingResume = new SuspendedProcessRecord
        {
            ProcessId = 42,
            ProcessName = "background.exe",
            ProcessStartTimeUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)
        };
        File.WriteAllText(_statePath, JsonSerializer.Serialize(wal));

        ActiveSessionState? loaded = new SessionStateService(_statePath).Load();

        Assert.AreEqual("wal", loaded?.SessionId);
        Assert.AreEqual(SessionRecoveryPhase.Restoring, loaded?.RecoveryPhase);
        Assert.AreEqual(42, loaded?.PendingResume?.ProcessId);
    }

    [TestMethod]
    public void StructurallyInvalidPrimaryAndInvalidBackupReturnsNull()
    {
        File.WriteAllText(_statePath, "{}");
        File.WriteAllText(_statePath + ".bak", """
            { "IsActive": true, "SessionId": "missing-process-collection" }
            """);

        Assert.IsNull(new SessionStateService(_statePath).Load());
    }

    private static ActiveSessionState CreateState(string sessionId) => new()
    {
        SessionId = sessionId,
        IsActive = true,
        RecoveryPhase = SessionRecoveryPhase.Active
    };
}
