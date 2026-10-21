using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FrameHub.App.Services;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Models.SessionOptimization;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Library;
using FrameHub.Core.Services.SessionOptimization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public sealed class SessionPowerPlanTests
{
    private string _tempDir = null!;
    private string _stateFilePath = null!;
    private string _settingsFilePath = null!;
    private string _libraryFilePath = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FrameHub_PowerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _stateFilePath = Path.Combine(_tempDir, "active_session.json");
        _settingsFilePath = Path.Combine(_tempDir, "session_optimization.json");
        _libraryFilePath = Path.Combine(_tempDir, "library.json");
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [TestMethod]
    public async Task StartSession_WhenPowerPlanOverrideDisabled_DoesNotSwitchPowerPlan()
    {
        // Arrange (Opt-in verification)
        var powerService = new TestPowerPlanService { ActiveScheme = PowerPlanService.BalancedSchemeGuid };
        var settingsService = new SessionOptimizationSettingsService(_settingsFilePath);
        var settings = settingsService.Load();
        settings.ApplyPowerPlanOverride = false;
        settingsService.Save(settings);

        var suspendService = new TestProcessSuspendService();
        var taskbarService = new TestTaskbarVisibilityService();
        using var coordinator = CreateCoordinator(powerService: powerService, settingsService: settingsService, suspendService: suspendService, taskbarService: taskbarService);
        var game = CreateGame();

        // Act
        var result = await coordinator.StartSessionAsync("Manual", game);

        // Assert
        Assert.IsTrue(result.Success, result.Message);
        Assert.IsFalse(result.PowerPlanSwitched, "PowerPlanSwitched must be false when override is disabled");
        Assert.AreEqual(0, powerService.SetSchemeCalls);
        Assert.AreEqual(PowerPlanService.BalancedSchemeGuid, powerService.ActiveScheme);

        var active = coordinator.ActiveSession;
        Assert.IsNotNull(active);
        Assert.IsFalse(active.PowerPlanChanged);
        Assert.IsNull(active.OriginalPowerPlanGuid);
    }

    [TestMethod]
    public async Task StartSession_WhenGlobalOverrideEnabled_SwitchesToHighPerformanceAndSavesOriginal()
    {
        // Arrange
        var powerService = new TestPowerPlanService { ActiveScheme = PowerPlanService.BalancedSchemeGuid };
        var settingsService = new SessionOptimizationSettingsService(_settingsFilePath);
        var settings = settingsService.Load();
        settings.ApplyPowerPlanOverride = true;
        settingsService.Save(settings);

        var suspendService = new TestProcessSuspendService();
        var taskbarService = new TestTaskbarVisibilityService();
        using var coordinator = CreateCoordinator(powerService: powerService, settingsService: settingsService, suspendService: suspendService, taskbarService: taskbarService);
        var game = CreateGame();

        // Act
        var result = await coordinator.StartSessionAsync("Manual", game);

        // Assert
        Assert.IsTrue(result.Success, result.Message);
        Assert.IsTrue(result.PowerPlanSwitched, "PowerPlanSwitched must be true when override is applied");
        Assert.AreEqual(PowerPlanService.HighPerformanceSchemeGuid, powerService.ActiveScheme);
        Assert.AreEqual(1, powerService.SetSchemeCalls);

        var active = coordinator.ActiveSession;
        Assert.IsNotNull(active);
        Assert.IsTrue(active.PowerPlanChanged);
        Assert.AreEqual(PowerPlanService.BalancedSchemeGuid, active.OriginalPowerPlanGuid);
        Assert.AreEqual(PowerPlanService.HighPerformanceSchemeGuid, active.AppliedPowerPlanGuid);
    }

    [TestMethod]
    public async Task StartSession_WhenPerGameOverrideTrue_OverridesGlobalFalse()
    {
        // Arrange
        var powerService = new TestPowerPlanService { ActiveScheme = PowerPlanService.BalancedSchemeGuid };
        var settingsService = new SessionOptimizationSettingsService(_settingsFilePath);
        var settings = settingsService.Load();
        settings.ApplyPowerPlanOverride = false;
        settings.GameSettings["test-game-1"] = new SessionGameSuspendSettings { ApplyPowerPlanOverride = true };
        settingsService.Save(settings);

        var suspendService = new TestProcessSuspendService();
        var taskbarService = new TestTaskbarVisibilityService();
        using var coordinator = CreateCoordinator(powerService: powerService, settingsService: settingsService, suspendService: suspendService, taskbarService: taskbarService);
        var game = CreateGame();

        // Act
        var result = await coordinator.StartSessionAsync("Manual", game);

        // Assert
        Assert.IsTrue(result.Success, result.Message);
        Assert.IsTrue(result.PowerPlanSwitched);
        Assert.AreEqual(PowerPlanService.HighPerformanceSchemeGuid, powerService.ActiveScheme);
    }

    [TestMethod]
    public async Task StartSession_WhenPerGameOverrideFalse_OverridesGlobalTrue()
    {
        // Arrange
        var powerService = new TestPowerPlanService { ActiveScheme = PowerPlanService.BalancedSchemeGuid };
        var settingsService = new SessionOptimizationSettingsService(_settingsFilePath);
        var settings = settingsService.Load();
        settings.ApplyPowerPlanOverride = true;
        settings.GameSettings["test-game-1"] = new SessionGameSuspendSettings { ApplyPowerPlanOverride = false };
        settingsService.Save(settings);

        var suspendService = new TestProcessSuspendService();
        var taskbarService = new TestTaskbarVisibilityService();
        using var coordinator = CreateCoordinator(powerService: powerService, settingsService: settingsService, suspendService: suspendService, taskbarService: taskbarService);
        var game = CreateGame();

        // Act
        var result = await coordinator.StartSessionAsync("Manual", game);

        // Assert
        Assert.IsTrue(result.Success, result.Message);
        Assert.IsFalse(result.PowerPlanSwitched, "Per-game false must override global true");
        Assert.AreEqual(0, powerService.SetSchemeCalls);
        Assert.AreEqual(PowerPlanService.BalancedSchemeGuid, powerService.ActiveScheme);
    }

    [TestMethod]
    public async Task StartSession_WhenPerGameOverrideNull_InheritsGlobalTrue()
    {
        // Arrange
        var powerService = new TestPowerPlanService { ActiveScheme = PowerPlanService.BalancedSchemeGuid };
        var settingsService = new SessionOptimizationSettingsService(_settingsFilePath);
        var settings = settingsService.Load();
        settings.ApplyPowerPlanOverride = true;
        settings.GameSettings["test-game-1"] = new SessionGameSuspendSettings { ApplyPowerPlanOverride = null };
        settingsService.Save(settings);

        var suspendService = new TestProcessSuspendService();
        var taskbarService = new TestTaskbarVisibilityService();
        using var coordinator = CreateCoordinator(powerService: powerService, settingsService: settingsService, suspendService: suspendService, taskbarService: taskbarService);
        var game = CreateGame();

        // Act
        var result = await coordinator.StartSessionAsync("Manual", game);

        // Assert
        Assert.IsTrue(result.Success, result.Message);
        Assert.IsTrue(result.PowerPlanSwitched, "Per-game null must inherit global true");
        Assert.AreEqual(PowerPlanService.HighPerformanceSchemeGuid, powerService.ActiveScheme);
    }

    [TestMethod]
    public async Task StartSession_WhenPerGameOverrideNull_InheritsGlobalFalse()
    {
        // Arrange
        var powerService = new TestPowerPlanService { ActiveScheme = PowerPlanService.BalancedSchemeGuid };
        var settingsService = new SessionOptimizationSettingsService(_settingsFilePath);
        var settings = settingsService.Load();
        settings.ApplyPowerPlanOverride = false;
        settings.GameSettings["test-game-1"] = new SessionGameSuspendSettings { ApplyPowerPlanOverride = null };
        settingsService.Save(settings);

        var suspendService = new TestProcessSuspendService();
        var taskbarService = new TestTaskbarVisibilityService();
        using var coordinator = CreateCoordinator(powerService: powerService, settingsService: settingsService, suspendService: suspendService, taskbarService: taskbarService);
        var game = CreateGame();

        // Act
        var result = await coordinator.StartSessionAsync("Manual", game);

        // Assert
        Assert.IsTrue(result.Success, result.Message);
        Assert.IsFalse(result.PowerPlanSwitched, "Per-game null must inherit global false");
        Assert.AreEqual(0, powerService.SetSchemeCalls);
        Assert.AreEqual(PowerPlanService.BalancedSchemeGuid, powerService.ActiveScheme);
    }

    [TestMethod]
    public async Task StartSession_WhenAlreadyOnHighPerformance_DoesNotPerformRedundantSwitch()
    {
        // Arrange
        var powerService = new TestPowerPlanService { ActiveScheme = PowerPlanService.HighPerformanceSchemeGuid };
        var settingsService = new SessionOptimizationSettingsService(_settingsFilePath);
        var settings = settingsService.Load();
        settings.ApplyPowerPlanOverride = true;
        settingsService.Save(settings);

        var suspendService = new TestProcessSuspendService();
        var taskbarService = new TestTaskbarVisibilityService();
        using var coordinator = CreateCoordinator(powerService: powerService, settingsService: settingsService, suspendService: suspendService, taskbarService: taskbarService);
        var game = CreateGame();

        // Act
        var result = await coordinator.StartSessionAsync("Manual", game);

        // Assert
        Assert.IsTrue(result.Success, result.Message);
        Assert.IsFalse(result.PowerPlanSwitched, "Should not indicate switched when already on target plan");
        Assert.AreEqual(0, powerService.SetSchemeCalls, "No SetActivePowerScheme call should be made when already on HighPerformance");
        Assert.AreEqual(PowerPlanService.HighPerformanceSchemeGuid, powerService.ActiveScheme);

        var active = coordinator.ActiveSession;
        Assert.IsNotNull(active);
        Assert.IsFalse(active.PowerPlanChanged);
    }

    [TestMethod]
    public async Task StartSession_WhenPowerSchemeUnreadable_FailsStartExplicitlyWithoutSuspending()
    {
        // Arrange
        var powerService = new TestPowerPlanService { ActiveScheme = null }; // read failure
        var settingsService = new SessionOptimizationSettingsService(_settingsFilePath);
        var settings = settingsService.Load();
        settings.ApplyPowerPlanOverride = true;
        settingsService.Save(settings);

        var suspendService = new TestProcessSuspendService();
        var taskbarService = new TestTaskbarVisibilityService();
        using var coordinator = CreateCoordinator(powerService: powerService, settingsService: settingsService, suspendService: suspendService, taskbarService: taskbarService);
        var game = CreateGame();

        // Act
        var result = await coordinator.StartSessionAsync("Manual", game);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.AreEqual("power_plan_read_failed", result.ErrorCode);
        Assert.AreEqual(0, suspendService.SuspendCalls, "No processes should be suspended when reading power plan fails before start");
    }

    [TestMethod]
    public async Task StartSession_WhenPowerPlanSetFails_RollsBackSuspendedProcessesAndReportsError()
    {
        // Arrange
        var powerService = new TestPowerPlanService
        {
            ActiveScheme = PowerPlanService.BalancedSchemeGuid,
            SetActiveResult = false // Native set failure
        };
        var settingsService = new SessionOptimizationSettingsService(_settingsFilePath);
        var settings = settingsService.Load();
        settings.ApplyPowerPlanOverride = true;
        settingsService.Save(settings);

        var suspendService = new TestProcessSuspendService();
        var taskbarService = new TestTaskbarVisibilityService();
        using var coordinator = CreateCoordinator(powerService: powerService, settingsService: settingsService, suspendService: suspendService, taskbarService: taskbarService);
        var game = CreateGame();

        // Act
        var result = await coordinator.StartSessionAsync("Manual", game);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.AreEqual("power_plan_set_failed", result.ErrorCode);
        Assert.AreEqual(1, suspendService.SuspendCalls);
        Assert.AreEqual(1, suspendService.ResumeCalls, "Suspended processes must be rolled back on native set failure");
        Assert.AreEqual(PowerPlanService.BalancedSchemeGuid, powerService.ActiveScheme);
    }

    [TestMethod]
    public async Task StartSession_WhenCancelledBeforePowerMutation_RollsBackSuspendedProcesses()
    {
        // Arrange
        var powerService = new TestPowerPlanService { ActiveScheme = PowerPlanService.BalancedSchemeGuid };
        var settingsService = new SessionOptimizationSettingsService(_settingsFilePath);
        var settings = settingsService.Load();
        settings.ApplyPowerPlanOverride = true;
        settingsService.Save(settings);

        using var cts = new CancellationTokenSource();
        var suspendService = new TestProcessSuspendService();
        suspendService.OnAfterSuspend = () => cts.Cancel();

        var taskbarService = new TestTaskbarVisibilityService();
        using var coordinator = CreateCoordinator(powerService: powerService, settingsService: settingsService, suspendService: suspendService, taskbarService: taskbarService);
        var game = CreateGame();

        // Act
        var result = await coordinator.StartSessionAsync("Manual", game, cts.Token);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, powerService.SetSchemeCalls, "Power plan must not be mutated if cancelled before power step");
        Assert.AreEqual(1, suspendService.SuspendCalls);
        Assert.AreEqual(1, suspendService.ResumeCalls, "Suspended processes must be rolled back on cancellation");
    }

    [TestMethod]
    public async Task StartSession_WalOrder_PersistsPendingAndTargetBeforeNativeCall()
    {
        // Arrange
        var powerService = new TestPowerPlanService { ActiveScheme = PowerPlanService.BalancedSchemeGuid };
        var settingsService = new SessionOptimizationSettingsService(_settingsFilePath);
        var settings = settingsService.Load();
        settings.ApplyPowerPlanOverride = true;
        settingsService.Save(settings);

        bool walVerifiedBeforeNativeCall = false;
        powerService.OnBeforeSetActive = () =>
        {
            // Inspect durable state file on disk before native set completes
            if (File.Exists(_stateFilePath))
            {
                string json = File.ReadAllText(_stateFilePath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                bool pending = root.GetProperty("PowerPlanMutationPending").GetBoolean();
                string? targetGuid = root.TryGetProperty("AppliedPowerPlanGuid", out var prop) ? prop.GetString() : null;
                string? originalGuid = root.TryGetProperty("OriginalPowerPlanGuid", out var orig) ? orig.GetString() : null;

                if (pending &&
                    string.Equals(targetGuid, PowerPlanService.HighPerformanceSchemeGuid.ToString(), StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(originalGuid, PowerPlanService.BalancedSchemeGuid.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    walVerifiedBeforeNativeCall = true;
                }
            }
        };

        var suspendService = new TestProcessSuspendService();
        var taskbarService = new TestTaskbarVisibilityService();
        using var coordinator = CreateCoordinator(powerService: powerService, settingsService: settingsService, suspendService: suspendService, taskbarService: taskbarService);
        var game = CreateGame();

        // Act
        var result = await coordinator.StartSessionAsync("Manual", game);

        // Assert
        Assert.IsTrue(result.Success);
        Assert.IsTrue(walVerifiedBeforeNativeCall, "WAL journal must durably persist pending mutation, target GUID, and original GUID BEFORE calling native SetActivePowerScheme");
    }

    [TestMethod]
    public async Task StopSession_WhenPowerPlanChanged_RestoresOriginalPlan()
    {
        // Arrange
        var powerService = new TestPowerPlanService { ActiveScheme = PowerPlanService.BalancedSchemeGuid };
        var settingsService = new SessionOptimizationSettingsService(_settingsFilePath);
        var settings = settingsService.Load();
        settings.ApplyPowerPlanOverride = true;
        settingsService.Save(settings);

        var suspendService = new TestProcessSuspendService();
        var taskbarService = new TestTaskbarVisibilityService();
        using var coordinator = CreateCoordinator(powerService: powerService, settingsService: settingsService, suspendService: suspendService, taskbarService: taskbarService);
        var game = CreateGame();

        var startResult = await coordinator.StartSessionAsync("Manual", game);
        Assert.IsTrue(startResult.Success);
        Assert.AreEqual(PowerPlanService.HighPerformanceSchemeGuid, powerService.ActiveScheme);

        // Act
        var restoreResult = await coordinator.StopSessionAsync();

        // Assert
        Assert.IsTrue(restoreResult.Success, restoreResult.Message);
        Assert.IsTrue(restoreResult.PowerPlanRestored, "PowerPlanRestored must be true");
        Assert.AreEqual(PowerPlanService.BalancedSchemeGuid, powerService.ActiveScheme, "Active scheme must be restored to original Balanced scheme");
        Assert.AreEqual(2, powerService.SetSchemeCalls);
    }

    [TestMethod]
    public async Task StopSession_WhenCurrentSchemeAlreadyOriginal_IsIdempotentWithoutNativeSet()
    {
        // Arrange
        var powerService = new TestPowerPlanService { ActiveScheme = PowerPlanService.BalancedSchemeGuid };
        var settingsService = new SessionOptimizationSettingsService(_settingsFilePath);
        var settings = settingsService.Load();
        settings.ApplyPowerPlanOverride = true;
        settingsService.Save(settings);

        var suspendService = new TestProcessSuspendService();
        var taskbarService = new TestTaskbarVisibilityService();
        using var coordinator = CreateCoordinator(powerService: powerService, settingsService: settingsService, suspendService: suspendService, taskbarService: taskbarService);
        var game = CreateGame();

        var startResult = await coordinator.StartSessionAsync("Manual", game);
        Assert.IsTrue(startResult.Success);

        // Simulate that the active scheme is already the original Balanced scheme
        powerService.ActiveScheme = PowerPlanService.BalancedSchemeGuid;
        int setCallsBeforeStop = powerService.SetSchemeCalls;

        // Act
        var restoreResult = await coordinator.StopSessionAsync();

        // Assert
        Assert.IsTrue(restoreResult.Success);
        Assert.IsTrue(restoreResult.PowerPlanRestored);
        Assert.AreEqual(setCallsBeforeStop, powerService.SetSchemeCalls, "No native SetScheme call should be made when current scheme is already original (idempotent)");
    }

    [TestMethod]
    public async Task StopSession_WhenUserChangedPowerPlanExternally_DoesNotOverwriteExternalChoice()
    {
        // Arrange
        var powerService = new TestPowerPlanService { ActiveScheme = PowerPlanService.BalancedSchemeGuid };
        var settingsService = new SessionOptimizationSettingsService(_settingsFilePath);
        var settings = settingsService.Load();
        settings.ApplyPowerPlanOverride = true;
        settingsService.Save(settings);

        var suspendService = new TestProcessSuspendService();
        var taskbarService = new TestTaskbarVisibilityService();
        using var coordinator = CreateCoordinator(powerService: powerService, settingsService: settingsService, suspendService: suspendService, taskbarService: taskbarService);
        var game = CreateGame();

        var startResult = await coordinator.StartSessionAsync("Manual", game);
        Assert.IsTrue(startResult.Success);
        Assert.AreEqual(PowerPlanService.HighPerformanceSchemeGuid, powerService.ActiveScheme);

        // Simulate external change outside FrameHub: user selected PowerSaver
        powerService.ActiveScheme = PowerPlanService.PowerSaverSchemeGuid;
        int callsBeforeRestore = powerService.SetSchemeCalls;

        // Act
        var restoreResult = await coordinator.StopSessionAsync();

        // Assert: restore succeeds and preserves external change
        Assert.IsTrue(restoreResult.Success, restoreResult.Message);
        Assert.AreEqual(PowerPlanService.PowerSaverSchemeGuid, powerService.ActiveScheme, "Must respect external user change instead of blindly overwriting back to Balanced");
        Assert.AreEqual(callsBeforeRestore, powerService.SetSchemeCalls, "No additional SetScheme call should be made when external change is detected");
    }

    [TestMethod]
    public async Task StartSession_WhenPostMutationPersistFails_RollsBackPowerPlan()
    {
        // Arrange
        var powerService = new TestPowerPlanService { ActiveScheme = PowerPlanService.BalancedSchemeGuid };
        var settingsService = new SessionOptimizationSettingsService(_settingsFilePath);
        var settings = settingsService.Load();
        settings.ApplyPowerPlanOverride = true;
        settingsService.Save(settings);

        var stateService = new TestSessionStateService(_stateFilePath) { FailSaveCalls = [6] };
        var suspendService = new TestProcessSuspendService();
        var taskbarService = new TestTaskbarVisibilityService();
        using var coordinator = CreateCoordinator(
            powerService: powerService,
            settingsService: settingsService,
            suspendService: suspendService,
            taskbarService: taskbarService,
            stateService: stateService);
        var game = CreateGame();

        // Act
        var startResult = await coordinator.StartSessionAsync("Manual", game);

        // Assert
        Assert.IsFalse(startResult.Success);
        Assert.AreEqual("state_persist_failed", startResult.ErrorCode);
        Assert.AreEqual(PowerPlanService.BalancedSchemeGuid, powerService.ActiveScheme, "Must rollback to original power scheme on post-mutation failure");
    }

    [TestMethod]
    public async Task Recovery_WhenCrashWithPendingMutation_RestoresOriginalIfOnTarget()
    {
        // Arrange
        var powerService = new TestPowerPlanService { ActiveScheme = PowerPlanService.HighPerformanceSchemeGuid };
        var state = new ActiveSessionState
        {
            IsActive = true,
            Trigger = "Manual",
            GameId = "test-game-1",
            PowerPlanMutationPending = true,
            OriginalPowerPlanGuid = PowerPlanService.BalancedSchemeGuid,
            AppliedPowerPlanGuid = PowerPlanService.HighPerformanceSchemeGuid,
            IsRecoveryPending = true
        };
        var stateService = new SessionStateService(_stateFilePath);
        stateService.Save(state);

        var settingsService = new SessionOptimizationSettingsService(_settingsFilePath);
        var suspendService = new TestProcessSuspendService();
        var taskbarService = new TestTaskbarVisibilityService();
        using var coordinator = CreateCoordinator(powerService: powerService, settingsService: settingsService, suspendService: suspendService, taskbarService: taskbarService);

        // Act
        var result = await coordinator.StopSessionAsync();

        // Assert
        Assert.IsTrue(result.Success, result.Message);
        Assert.IsTrue(result.PowerPlanRestored);
        Assert.AreEqual(PowerPlanService.BalancedSchemeGuid, powerService.ActiveScheme, "Crash recovery must restore original scheme when system is on applied scheme");
    }

    [TestMethod]
    public async Task Recovery_WhenCrashWithPendingMutation_PreservesExternalIfUserChanged()
    {
        // Arrange
        var powerService = new TestPowerPlanService { ActiveScheme = PowerPlanService.PowerSaverSchemeGuid };
        var state = new ActiveSessionState
        {
            IsActive = true,
            Trigger = "Manual",
            GameId = "test-game-1",
            PowerPlanMutationPending = true,
            OriginalPowerPlanGuid = PowerPlanService.BalancedSchemeGuid,
            AppliedPowerPlanGuid = PowerPlanService.HighPerformanceSchemeGuid,
            IsRecoveryPending = true
        };
        var stateService = new SessionStateService(_stateFilePath);
        stateService.Save(state);

        var settingsService = new SessionOptimizationSettingsService(_settingsFilePath);
        var suspendService = new TestProcessSuspendService();
        var taskbarService = new TestTaskbarVisibilityService();
        using var coordinator = CreateCoordinator(powerService: powerService, settingsService: settingsService, suspendService: suspendService, taskbarService: taskbarService);

        // Act
        var result = await coordinator.StopSessionAsync();

        // Assert
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(PowerPlanService.PowerSaverSchemeGuid, powerService.ActiveScheme, "Crash recovery must preserve external scheme change");
        Assert.AreEqual(0, powerService.SetSchemeCalls, "No native set should be called when external change is preserved");
    }

    [TestMethod]
    public async Task Recovery_WhenCurrentSchemeUnreadable_FailsClosedWithoutMutation()
    {
        // Arrange
        var powerService = new TestPowerPlanService { ActiveScheme = null }; // Unreadable
        var state = new ActiveSessionState
        {
            IsActive = true,
            Trigger = "Manual",
            GameId = "test-game-1",
            PowerPlanChanged = true,
            OriginalPowerPlanGuid = PowerPlanService.BalancedSchemeGuid,
            AppliedPowerPlanGuid = PowerPlanService.HighPerformanceSchemeGuid,
            IsRecoveryPending = true
        };
        var stateService = new SessionStateService(_stateFilePath);
        stateService.Save(state);

        var settingsService = new SessionOptimizationSettingsService(_settingsFilePath);
        var suspendService = new TestProcessSuspendService();
        var taskbarService = new TestTaskbarVisibilityService();
        using var coordinator = CreateCoordinator(powerService: powerService, settingsService: settingsService, suspendService: suspendService, taskbarService: taskbarService);

        // Act
        var result = await coordinator.StopSessionAsync();

        // Assert: Fail closed! Zero mutation, recovery remains pending
        Assert.IsFalse(result.Success);
        Assert.IsFalse(result.PowerPlanRestored);
        Assert.AreEqual(0, powerService.SetSchemeCalls, "No native mutation when scheme is unreadable");
        Assert.IsTrue(coordinator.ActiveSession?.IsRecoveryPending == true);
    }

    [TestMethod]
    public async Task Recovery_WhenOriginalOrTargetGuidMissing_FailsClosedWithoutMutation()
    {
        // Arrange
        var powerService = new TestPowerPlanService { ActiveScheme = PowerPlanService.HighPerformanceSchemeGuid };
        var state = new ActiveSessionState
        {
            IsActive = true,
            Trigger = "Manual",
            GameId = "test-game-1",
            PowerPlanChanged = true,
            OriginalPowerPlanGuid = null, // Missing original GUID
            AppliedPowerPlanGuid = PowerPlanService.HighPerformanceSchemeGuid,
            IsRecoveryPending = true
        };
        var stateService = new SessionStateService(_stateFilePath);
        stateService.Save(state);

        var settingsService = new SessionOptimizationSettingsService(_settingsFilePath);
        var suspendService = new TestProcessSuspendService();
        var taskbarService = new TestTaskbarVisibilityService();
        using var coordinator = CreateCoordinator(powerService: powerService, settingsService: settingsService, suspendService: suspendService, taskbarService: taskbarService);

        // Act
        var result = await coordinator.StopSessionAsync();

        // Assert: Fail closed! Zero mutation
        Assert.IsFalse(result.Success);
        Assert.IsFalse(result.PowerPlanRestored);
        Assert.AreEqual(0, powerService.SetSchemeCalls);
        Assert.IsTrue(coordinator.ActiveSession?.IsRecoveryPending == true);
    }

    [TestMethod]
    public async Task Recovery_WhenNativeRestoreFails_KeepsWalPendingForRetry()
    {
        // Arrange
        var powerService = new TestPowerPlanService
        {
            ActiveScheme = PowerPlanService.HighPerformanceSchemeGuid,
            SetActiveResult = false // Native set fails during recovery
        };
        var state = new ActiveSessionState
        {
            IsActive = true,
            Trigger = "Manual",
            GameId = "test-game-1",
            PowerPlanChanged = true,
            OriginalPowerPlanGuid = PowerPlanService.BalancedSchemeGuid,
            AppliedPowerPlanGuid = PowerPlanService.HighPerformanceSchemeGuid,
            IsRecoveryPending = true
        };
        var stateService = new SessionStateService(_stateFilePath);
        stateService.Save(state);

        var settingsService = new SessionOptimizationSettingsService(_settingsFilePath);
        var suspendService = new TestProcessSuspendService();
        var taskbarService = new TestTaskbarVisibilityService();
        using var coordinator = CreateCoordinator(powerService: powerService, settingsService: settingsService, suspendService: suspendService, taskbarService: taskbarService);

        // Act
        var result = await coordinator.StopSessionAsync();

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsFalse(result.PowerPlanRestored);
        Assert.IsTrue(coordinator.ActiveSession?.IsRecoveryPending == true);
        Assert.IsTrue(coordinator.ActiveSession?.PowerPlanChanged == true, "WAL PowerPlanChanged must remain true for subsequent retry");
        Assert.AreEqual(PowerPlanService.BalancedSchemeGuid, coordinator.ActiveSession?.OriginalPowerPlanGuid);
    }

    [TestMethod]
    public async Task Recovery_WhenPersistFailsAfterRestore_PreservesWalInMemoryAndDiskForRetry()
    {
        // Arrange
        var powerService = new TestPowerPlanService { ActiveScheme = PowerPlanService.HighPerformanceSchemeGuid };
        var state = new ActiveSessionState
        {
            IsActive = true,
            Trigger = "Manual",
            GameId = "test-game-1",
            PowerPlanChanged = true,
            OriginalPowerPlanGuid = PowerPlanService.BalancedSchemeGuid,
            AppliedPowerPlanGuid = PowerPlanService.HighPerformanceSchemeGuid,
            IsRecoveryPending = true
        };
        var initialSaveService = new SessionStateService(_stateFilePath);
        initialSaveService.Save(state);
        var stateService = new TestSessionStateService(_stateFilePath) { FailSaveCalls = [1] };

        var settingsService = new SessionOptimizationSettingsService(_settingsFilePath);
        var suspendService = new TestProcessSuspendService();
        var taskbarService = new TestTaskbarVisibilityService();
        using var coordinator = CreateCoordinator(
            powerService: powerService,
            settingsService: settingsService,
            suspendService: suspendService,
            taskbarService: taskbarService,
            stateService: stateService);

        // Act
        var result = await coordinator.StopSessionAsync();

        // Assert
        Assert.IsFalse(result.Success);
        Assert.AreEqual("state_persist_failed", result.ErrorCode);
        Assert.IsNotNull(coordinator.ActiveSession);
        Assert.IsTrue(coordinator.ActiveSession.PowerPlanChanged, "In-memory state must retain PowerPlanChanged when save fails so WAL is not lost");
        Assert.AreEqual(PowerPlanService.BalancedSchemeGuid, coordinator.ActiveSession.OriginalPowerPlanGuid);
    }

    [TestMethod]
    public async Task Recovery_RetrySucceeds_AfterPreviousNativeFailure()
    {
        // Arrange
        var powerService = new TestPowerPlanService
        {
            ActiveScheme = PowerPlanService.HighPerformanceSchemeGuid,
            SetActiveResult = false // Native set fails first time
        };
        var state = new ActiveSessionState
        {
            IsActive = true,
            Trigger = "Manual",
            GameId = "test-game-1",
            PowerPlanChanged = true,
            OriginalPowerPlanGuid = PowerPlanService.BalancedSchemeGuid,
            AppliedPowerPlanGuid = PowerPlanService.HighPerformanceSchemeGuid,
            IsRecoveryPending = true
        };
        var stateService = new SessionStateService(_stateFilePath);
        stateService.Save(state);

        var settingsService = new SessionOptimizationSettingsService(_settingsFilePath);
        var suspendService = new TestProcessSuspendService();
        var taskbarService = new TestTaskbarVisibilityService();
        using var coordinator = CreateCoordinator(powerService: powerService, settingsService: settingsService, suspendService: suspendService, taskbarService: taskbarService);

        // Act 1: First attempt fails
        var result1 = await coordinator.StopSessionAsync();
        Assert.IsFalse(result1.Success);

        // Fix condition for retry
        powerService.SetActiveResult = true;

        // Act 2: Retry
        var result2 = await coordinator.StopSessionAsync();

        // Assert
        Assert.IsTrue(result2.Success, result2.Message);
        Assert.IsTrue(result2.PowerPlanRestored);
        Assert.AreEqual(PowerPlanService.BalancedSchemeGuid, powerService.ActiveScheme);
        Assert.IsNull(coordinator.ActiveSession);
    }

    private SessionOptimizationCoordinator CreateCoordinator(
        PowerPlanService powerService,
        SessionOptimizationSettingsService settingsService,
        ProcessSuspendService suspendService,
        TaskbarVisibilityService taskbarService,
        SessionStateService? stateService = null)
    {
        return new SessionOptimizationCoordinator(
            stateService: stateService ?? new SessionStateService(_stateFilePath),
            settingsService: settingsService,
            suspendService: suspendService,
            taskbarService: taskbarService,
            powerPlanService: powerService,
            libraryService: new LibraryService(_libraryFilePath));
    }

    private sealed class TestSessionStateService(string filePath) : SessionStateService(filePath)
    {
        public HashSet<int> FailSaveCalls { get; init; } = [];
        public int SaveCalls { get; private set; }

        public void ResetSaveCalls() => SaveCalls = 0;

        public override bool Save(ActiveSessionState state)
        {
            SaveCalls++;
            return !FailSaveCalls.Contains(SaveCalls) && base.Save(state);
        }
    }

    private static LibraryItem CreateGame() => new()
    {
        Id = "test-game-1",
        DisplayName = "Test Game",
        ProcessName = "game.exe",
        Type = LibraryItemType.Game,
        IsEnabled = true
    };

    private sealed class TestPowerPlanService : PowerPlanService
    {
        public Guid? ActiveScheme { get; set; }
        public Guid? LastAppliedScheme { get; private set; }
        public int SetSchemeCalls { get; private set; }
        public bool SetActiveResult { get; set; } = true;
        public Action? OnBeforeSetActive { get; set; }

        public override Guid? GetActivePowerScheme() => ActiveScheme;

        public override bool SetActivePowerScheme(Guid schemeGuid)
        {
            SetSchemeCalls++;
            LastAppliedScheme = schemeGuid;
            OnBeforeSetActive?.Invoke();
            if (SetActiveResult)
            {
                ActiveScheme = schemeGuid;
                return true;
            }
            return false;
        }
    }

    private sealed class TestProcessSuspendService : ProcessSuspendService
    {
        private readonly SuspendedProcessRecord _record = new()
        {
            ProcessId = 4242,
            ProcessName = "bg.exe",
            ProcessStartTimeUtc = DateTime.UtcNow
        };

        public bool FailOnSuspend { get; set; }
        public Action? OnAfterSuspend { get; set; }
        public int SuspendCalls { get; private set; }
        public int ResumeCalls { get; private set; }

        public TestProcessSuspendService() { }

        public override Task<SessionProcessSnapshot> CaptureProcessSnapshotAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new SessionProcessSnapshot());
        }

        public override IReadOnlyList<SuspendCandidate> BuildCandidates(
            SessionProcessSnapshot snapshot,
            IEnumerable<BackgroundProcessRule> enabledRules,
            IEnumerable<string> customProcessNames,
            IEnumerable<string> protectedProcessNames)
        {
            return [new SuspendCandidate { ProcessId = _record.ProcessId, ProcessName = _record.ProcessName, ProcessStartTimeUtc = _record.ProcessStartTimeUtc }];
        }

        public override SessionActionResult SuspendProcesses(IEnumerable<SuspendCandidate> candidates)
        {
            SuspendCalls++;
            if (FailOnSuspend)
            {
                return new SessionActionResult { FailedCount = 1 };
            }
            var res = new SessionActionResult
            {
                SuccessCount = 1,
                Records = [_record]
            };
            OnAfterSuspend?.Invoke();
            return res;
        }

        public override SessionActionResult ResumeProcesses(IEnumerable<SuspendedProcessRecord> records)
        {
            ResumeCalls++;
            return new SessionActionResult
            {
                SuccessCount = records.Count(),
                Records = records.ToList()
            };
        }
    }

    private sealed class TestTaskbarVisibilityService : TaskbarVisibilityService
    {
        public override TaskbarVisibilityState? CaptureVisibilityState() => new() { PrimaryTaskbarFound = true, PrimaryTaskbarVisible = true };
        public override bool HideTaskbars() => true;
        public override bool RestoreVisibilityState(TaskbarVisibilityState state) => true;
    }
}
