using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.Companion.Authentication;
using FrameHub.Companion.Models;
using FrameHub.Companion.Providers;
using FrameHub.Core.Models;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Models.SessionOptimization;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Benchmarking;
using FrameHub.Core.Services.Library;
using FrameHub.Core.Services.SessionOptimization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;
using System.Text.Json;

namespace FrameHub.Tests;

// ---------------------------------------------------------------------------
// Real backend, synthetic delegates only — IsValidSelection/GetTopology never
// touch real processes, affinity, or CPU Sets.
// ---------------------------------------------------------------------------
[TestClass]
public sealed class SessionCpuBackendSelectionTests
{
    private static readonly IReadOnlyList<CoreInfo> Topology = Enumerable.Range(0, 8)
        .Select(index => new CoreInfo { Index = index, CoreIndex = index / 2, EfficiencyClass = 0, TypeTag = index % 2 == 0 ? "[P]" : "[T]", IsThread = index % 2 == 1 })
        .ToList();

    private static Dictionary<int, uint> CpuSetMap => Enumerable.Range(0, 8).ToDictionary(index => index, index => (uint)(100 + index));

    private static SessionCpuControlBackend CreateBackend(Dictionary<int, uint>? cpuSetMap = null) =>
        new(() => Topology, () => cpuSetMap ?? CpuSetMap);

    [TestMethod]
    public void GetTopology_MapsProviderCoreTopology()
    {
        SessionCpuTopology topology = CreateBackend().GetTopology();
        Assert.AreEqual(8, topology.Processors.Count);
        Assert.AreEqual(0, topology.Processors[0].Index);
        Assert.AreEqual("[P]", topology.Processors[0].TypeTag);
        Assert.IsTrue(topology.Processors[1].IsThread);
    }

    [TestMethod]
    public void ValidSelection_RejectsZeroAndUnsupportedModes()
    {
        SessionCpuControlBackend backend = CreateBackend();
        Assert.IsFalse(backend.IsValidSelection(OptimizationMode.Affinity, 0), "An empty processor selection must be rejected.");
#pragma warning disable CS0618
        Assert.IsFalse(backend.IsValidSelection(OptimizationMode.Exclusive, 0x0F), "The legacy Exclusive mode must not be remotely selectable.");
#pragma warning restore CS0618
        Assert.IsFalse(backend.IsValidSelection((OptimizationMode)99, 0x0F), "Unknown modes must be rejected.");
        Assert.IsTrue(backend.IsValidSelection(OptimizationMode.Affinity, 0x0F));
        Assert.IsTrue(backend.IsValidSelection(OptimizationMode.CpuSets, 0x0F));
    }

    [TestMethod]
    public void ValidSelection_RejectsBitsOutsideKnownTopology()
    {
        SessionCpuControlBackend backend = CreateBackend();
        Assert.IsFalse(backend.IsValidSelection(OptimizationMode.Affinity, 1L << 40), "Topology has 8 processors; a bit beyond it must never be silently truncated away.");
        Assert.IsFalse(backend.IsValidSelection(OptimizationMode.Affinity, 0x0F | (1L << 8)));
    }

    [TestMethod]
    public void ValidSelection_CpuSetsRequireAtLeastOneMappedSet()
    {
        SessionCpuControlBackend backend = CreateBackend(cpuSetMap: new Dictionary<int, uint>());
        Assert.IsFalse(backend.IsValidSelection(OptimizationMode.CpuSets, 0x0F), "Mirrors ProcessService: CPU Sets need at least one mapped logical processor.");
        Assert.IsTrue(CreateBackend().IsValidSelection(OptimizationMode.CpuSets, 0x03));
    }

    [TestMethod]
    public void FullSixtyFourProcessorTopology_SupportsFullMaskWithoutTruncation()
    {
        var fullTopology = Enumerable.Range(0, 64)
            .Select(index => new CoreInfo { Index = index, CoreIndex = index, TypeTag = "[P]" })
            .ToList();
        var fullMap = Enumerable.Range(0, 64).ToDictionary(index => index, index => (uint)index);
        var backend = new SessionCpuControlBackend(() => fullTopology, () => fullMap);

        Assert.IsTrue(backend.IsValidSelection(OptimizationMode.Affinity, unchecked((long)0xFFFFFFFFFFFFFFFF)));
        Assert.IsFalse(backend.IsValidSelection(OptimizationMode.Affinity, 0), "Even on a 64-processor system, an empty selection is invalid.");
    }
}

// ---------------------------------------------------------------------------
// Coordinator session CPU control with a fake backend and fake active game
// monitor. No real process, affinity, or CPU Set is ever touched.
// ---------------------------------------------------------------------------
[TestClass]
public sealed class SessionCpuCoordinatorTests
{
    private string _tempDir = null!;
    private FakeActiveGameMonitor _monitor = null!;
    private FakeSessionCpuBackend _backend = null!;
    private SessionStateService _stateService = null!;

    [TestInitialize]
    public void Initialize()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FrameHubSessionCpuTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _monitor = new FakeActiveGameMonitor();
        _backend = new FakeSessionCpuBackend();
        _stateService = new SessionStateService(Path.Combine(_tempDir, "session-state.json"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    private SessionOptimizationCoordinator CreateCoordinator(
        IBenchmarkOperationArbiter? arbiter = null,
        SessionOptimizationSettingsService? settingsService = null,
        ProcessSuspendService? suspendService = null) => new(
        stateService: _stateService,
        settingsService: settingsService ?? new SessionOptimizationSettingsService(Path.Combine(_tempDir, "opt-settings.json")),
        suspendService: suspendService ?? new FakeProcessSuspendService(),
        libraryService: new LibraryService(Path.Combine(_tempDir, "library.json")),
        benchmarkArbiter: arbiter,
        activeGameMonitor: _monitor,
        sessionCpuBackend: _backend);

    private static ActiveGameSnapshot Snapshot(string id = "game-1", string name = "Game One", int pid = 4242, string processName = "game") => new(
        new LibraryItem { Id = id, DisplayName = name, ProcessName = processName, Type = LibraryItemType.Game, IsEnabled = true },
        new BenchmarkProcessIdentity { ProcessId = pid, ProcessName = processName, StartTimeUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) });

    private static ActiveGameSnapshot RiotSnapshot() => Snapshot(id: "valorant", name: "VALORANT", pid: 5151, processName: "valorant-win64-shipping");

    [TestMethod]
    public void GetState_NoActiveGame_ReportsUnavailableWithoutToken()
    {
        SessionCpuStateResult state = CreateCoordinator().GetSessionCpuState();
        Assert.IsFalse(state.Available);
        Assert.AreEqual("no_game", state.UnavailableReason);
        Assert.IsNull(state.SessionToken);
    }

    [TestMethod]
    public void GetState_TopologyAndEffectiveSelectionAreExposed()
    {
        _monitor.Snapshot = Snapshot();
        _backend.CurrentSelection = new SessionCpuSelection(OptimizationMode.Affinity, 0x0F);

        SessionCpuStateResult state = CreateCoordinator().GetSessionCpuState();

        Assert.IsTrue(state.Available);
        Assert.IsNotNull(state.SessionToken);
        Assert.AreEqual("Game One", state.GameDisplayName);
        Assert.AreEqual(8, state.Topology!.Processors.Count);
        Assert.AreEqual(0x0F, state.CurrentSelection!.Mask);
    }

    [TestMethod]
    public void Apply_TargetsOnlyTheAuthoritativeActiveGameProcess()
    {
        _monitor.Snapshot = Snapshot(pid: 4242);
        SessionOptimizationCoordinator coordinator = CreateCoordinator();
        Guid token = coordinator.GetSessionCpuState().SessionToken!.Value;

        SessionCpuMutationResult result = coordinator.ApplySessionCpuOverride(token, OptimizationMode.Affinity, 0x03);

        Assert.IsTrue(result.Success, result.ErrorCode);
        Assert.AreEqual(4242, _backend.AppliedCalls.Single().ProcessId, "The mutation must target the server-resolved active game process.");
    }

    [TestMethod]
    public void Apply_WithoutActiveGame_IsRejectedSafely()
    {
        SessionCpuMutationResult result = CreateCoordinator().ApplySessionCpuOverride(Guid.NewGuid(), OptimizationMode.Affinity, 0x03);
        Assert.IsFalse(result.Success);
        Assert.AreEqual("no_game", result.ErrorCode);
        Assert.AreEqual(0, _backend.AppliedCalls.Count);
    }

    [TestMethod]
    public void Apply_WithStaleSessionToken_IsRejected()
    {
        _monitor.Snapshot = Snapshot();
        SessionOptimizationCoordinator coordinator = CreateCoordinator();
        _ = coordinator.GetSessionCpuState().SessionToken;

        SessionCpuMutationResult result = coordinator.ApplySessionCpuOverride(Guid.NewGuid(), OptimizationMode.Affinity, 0x03);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("stale_session", result.ErrorCode);
        Assert.AreEqual(0, _backend.AppliedCalls.Count);
    }

    [TestMethod]
    public void Apply_AfterSessionChangedBeforeApply_OldRequestIsRejected()
    {
        _monitor.Snapshot = Snapshot(pid: 100);
        SessionOptimizationCoordinator coordinator = CreateCoordinator();
        Guid oldToken = coordinator.GetSessionCpuState().SessionToken!.Value;

        _monitor.Snapshot = Snapshot(pid: 200);
        SessionCpuMutationResult result = coordinator.ApplySessionCpuOverride(oldToken, OptimizationMode.Affinity, 0x03);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("stale_session", result.ErrorCode);
        Assert.AreEqual(0, _backend.AppliedCalls.Count, "A request for the previous game session must never mutate the new session.");
    }

    [TestMethod]
    public void Apply_WhenProcessExitedOrReused_FailsClosedAfterFreshValidation()
    {
        _monitor.Snapshot = Snapshot(pid: 4242);
        SessionOptimizationCoordinator coordinator = CreateCoordinator();
        Guid token = coordinator.GetSessionCpuState().SessionToken!.Value;
        _backend.FreshIdentityFails = true; // exited, or PID reused by a different instance

        SessionCpuMutationResult result = coordinator.ApplySessionCpuOverride(token, OptimizationMode.Affinity, 0x03);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("target_lost", result.ErrorCode);
        Assert.AreEqual(1, _backend.FreshIdentityCalls.Count, "Fresh identity validation must run before any mutation.");
        Assert.AreEqual(0, _backend.AppliedCalls.Count);
    }

    [TestMethod]
    public void Apply_FreshIdentityValidationOccursImmediatelyBeforeMutation()
    {
        _monitor.Snapshot = Snapshot();
        SessionOptimizationCoordinator coordinator = CreateCoordinator();
        Guid token = coordinator.GetSessionCpuState().SessionToken!.Value;

        Assert.IsTrue(coordinator.ApplySessionCpuOverride(token, OptimizationMode.CpuSets, 0x03).Success);

        Assert.AreEqual(1, _backend.FreshIdentityCalls.Count);
        Assert.AreEqual(1, _backend.AppliedCalls.Count);
        Assert.AreEqual("validate", _backend.OperationOrder[0]);
        Assert.AreEqual("apply", _backend.OperationOrder[1]);
    }

    [TestMethod]
    public void Apply_InvalidSelectionNeverReachesTheBackend()
    {
        _monitor.Snapshot = Snapshot();
        SessionOptimizationCoordinator coordinator = CreateCoordinator();
        Guid token = coordinator.GetSessionCpuState().SessionToken!.Value;

        SessionCpuMutationResult zero = coordinator.ApplySessionCpuOverride(token, OptimizationMode.Affinity, 0);
        SessionCpuMutationResult outside = coordinator.ApplySessionCpuOverride(token, OptimizationMode.Affinity, 1L << 40);
#pragma warning disable CS0618
        SessionCpuMutationResult legacy = coordinator.ApplySessionCpuOverride(token, OptimizationMode.Exclusive, 0x0F);
#pragma warning restore CS0618

        Assert.AreEqual("invalid_selection", zero.ErrorCode);
        Assert.AreEqual("invalid_selection", outside.ErrorCode);
        Assert.AreEqual("invalid_selection", legacy.ErrorCode);
        Assert.AreEqual(0, _backend.AppliedCalls.Count);
        Assert.AreEqual(0, _backend.FreshIdentityCalls.Count, "Invalid input must be rejected before any process access.");
    }

    [TestMethod]
    public void Apply_ProtectedRiotGame_IsNeverMutated()
    {
        _monitor.Snapshot = RiotSnapshot();
        SessionOptimizationCoordinator coordinator = CreateCoordinator();
        SessionCpuStateResult state = coordinator.GetSessionCpuState();

        Assert.IsFalse(state.Available);
        Assert.IsTrue(state.ProtectedGame);
        Assert.AreEqual("protected_game", state.UnavailableReason);

        SessionCpuMutationResult result = coordinator.ApplySessionCpuOverride(state.SessionToken!.Value, OptimizationMode.Affinity, 0x03);
        Assert.IsFalse(result.Success);
        Assert.AreEqual("protected_game", result.ErrorCode);
        Assert.AreEqual(0, _backend.AppliedCalls.Count);
    }

    [TestMethod]
    public void Apply_FirstOverrideCapturesBaseline_UnreadableBaselineFailsClosed()
    {
        _monitor.Snapshot = Snapshot();
        SessionOptimizationCoordinator coordinator = CreateCoordinator();
        Guid token = coordinator.GetSessionCpuState().SessionToken!.Value;
        _backend.CurrentSelection = null; // baseline unreadable

        SessionCpuMutationResult result = coordinator.ApplySessionCpuOverride(token, OptimizationMode.Affinity, 0x03);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("baseline_unavailable", result.ErrorCode, "Without a captured baseline a safe restore is impossible.");
        Assert.AreEqual(0, _backend.AppliedCalls.Count);
    }

    [TestMethod]
    public void BenchmarkArbiter_BlocksApplyAndResetButNotExistingOverride()
    {
        _monitor.Snapshot = Snapshot();
        SessionOptimizationCoordinator coordinator = CreateCoordinator(arbiter: new DenyingArbiter());
        Guid token = coordinator.GetSessionCpuState().SessionToken!.Value;

        Assert.AreEqual("benchmark_active", coordinator.ApplySessionCpuOverride(token, OptimizationMode.Affinity, 0x03).ErrorCode);
        Assert.AreEqual("benchmark_active", coordinator.ResetSessionCpuOverride(token).ErrorCode);
        Assert.AreEqual(0, _backend.AppliedCalls.Count);
    }

    [TestMethod]
    public void Reset_RestoresCapturedProfileBaseline_NotGuessedAllCpus()
    {
        _monitor.Snapshot = Snapshot();
        SessionOptimizationCoordinator coordinator = CreateCoordinator();
        Guid token = coordinator.GetSessionCpuState().SessionToken!.Value;
        _backend.CurrentSelection = new SessionCpuSelection(OptimizationMode.Affinity, 0x0F); // e.g. profile-applied state

        Assert.IsTrue(coordinator.ApplySessionCpuOverride(token, OptimizationMode.CpuSets, 0x03).Success);
        Assert.AreEqual(0x0F, coordinator.GetSessionCpuState().BaselineSelection!.Mask);

        SessionCpuMutationResult reset = coordinator.ResetSessionCpuOverride(token);

        Assert.IsTrue(reset.Success, reset.ErrorCode);
        Assert.AreEqual(0x0F, _backend.AppliedCalls[^1].Selection.Mask, "Restore must reapply the captured pre-override state, never a guessed all-CPU mask.");
        Assert.AreEqual(OptimizationMode.Affinity, _backend.AppliedCalls[^1].Selection.Mode);
        Assert.IsFalse(coordinator.GetSessionCpuState().TemporaryOverrideActive);
    }

    [TestMethod]
    public void Reset_RepeatedRestoreIsIdempotent()
    {
        _monitor.Snapshot = Snapshot();
        SessionOptimizationCoordinator coordinator = CreateCoordinator();
        Guid token = coordinator.GetSessionCpuState().SessionToken!.Value;

        Assert.IsTrue(coordinator.ApplySessionCpuOverride(token, OptimizationMode.Affinity, 0x03).Success);
        Assert.IsTrue(coordinator.ResetSessionCpuOverride(token).Success);
        int applyCount = _backend.AppliedCalls.Count;

        SessionCpuMutationResult second = coordinator.ResetSessionCpuOverride(token);

        Assert.IsTrue(second.Success, "Repeated restore must stay safe.");
        Assert.AreEqual(applyCount, _backend.AppliedCalls.Count, "An idempotent restore must not mutate again.");
    }

    [TestMethod]
    public void OverrideState_DisappearsWhenSessionEnds()
    {
        _monitor.Snapshot = Snapshot();
        SessionOptimizationCoordinator coordinator = CreateCoordinator();
        Guid token = coordinator.GetSessionCpuState().SessionToken!.Value;
        Assert.IsTrue(coordinator.ApplySessionCpuOverride(token, OptimizationMode.Affinity, 0x03).Success);

        _monitor.Snapshot = null;
        SessionCpuStateResult state = coordinator.GetSessionCpuState();

        Assert.IsFalse(state.TemporaryOverrideActive);
        Assert.AreEqual("no_game", state.UnavailableReason);
        Assert.AreEqual("no_game", coordinator.ResetSessionCpuOverride(token).ErrorCode, "No destructive restore may run against a vanished session.");
    }

    [TestMethod]
    public void OverrideState_DoesNotCarryOverToAnotherGame()
    {
        _monitor.Snapshot = Snapshot(id: "game-1", pid: 100);
        SessionOptimizationCoordinator coordinator = CreateCoordinator();
        Guid firstToken = coordinator.GetSessionCpuState().SessionToken!.Value;
        Assert.IsTrue(coordinator.ApplySessionCpuOverride(firstToken, OptimizationMode.Affinity, 0x03).Success);

        _monitor.Snapshot = Snapshot(id: "game-2", name: "Game Two", pid: 200);
        SessionCpuStateResult state = coordinator.GetSessionCpuState();

        Assert.IsFalse(state.TemporaryOverrideActive, "A new game session must start without the previous override.");
        Assert.AreNotEqual(firstToken, state.SessionToken);
        Assert.AreEqual("stale_session", coordinator.ApplySessionCpuOverride(firstToken, OptimizationMode.Affinity, 0x05).ErrorCode);
    }

    [TestMethod]
    public void OverrideState_IsNeverPersistedToTheSessionJournal()
    {
        _monitor.Snapshot = Snapshot();
        SessionOptimizationCoordinator coordinator = CreateCoordinator();
        Guid token = coordinator.GetSessionCpuState().SessionToken!.Value;
        Assert.IsTrue(coordinator.ApplySessionCpuOverride(token, OptimizationMode.Affinity, 0x03).Success);

        string journalPath = Path.Combine(_tempDir, "session-state.json");
        Assert.IsFalse(File.Exists(journalPath), "Temporary CPU override state must not be journaled; restart discards it.");
        Assert.IsNull(_stateService.Load(), "No durable session state may be created by CPU override operations.");
    }

    [TestMethod]
    public void CpuOperations_RequireConfiguredMonitorAndBackend()
    {
        SessionOptimizationCoordinator coordinator = new(stateService: _stateService);
        SessionCpuStateResult state = coordinator.GetSessionCpuState();
        Assert.IsFalse(state.Available);
        Assert.AreEqual("session_cpu_unavailable", state.UnavailableReason);
        Assert.AreEqual("session_cpu_unavailable", coordinator.ApplySessionCpuOverride(Guid.NewGuid(), OptimizationMode.Affinity, 0x03).ErrorCode);
    }

    [TestMethod]
    public void ApplySessionPriorityOverride_CapturesBaselineAndAppliesPriority()
    {
        _monitor.Snapshot = Snapshot(pid: 4242);
        _backend.CurrentPriority = ProcessPriorityClass.Normal;
        SessionOptimizationCoordinator coordinator = CreateCoordinator();
        Guid token = coordinator.GetSessionCpuState().SessionToken!.Value;

        SessionCpuMutationResult result = coordinator.ApplySessionPriorityOverride(token, ProcessPriorityClass.AboveNormal);

        Assert.IsTrue(result.Success, result.ErrorCode);
        Assert.AreEqual(1, _backend.AppliedPriorityCalls.Count);
        Assert.AreEqual(4242, _backend.AppliedPriorityCalls[0].ProcessId);
        Assert.AreEqual(ProcessPriorityClass.AboveNormal, _backend.AppliedPriorityCalls[0].Priority);

        SessionCpuStateResult state = coordinator.GetSessionCpuState();
        Assert.IsTrue(state.TemporaryOverrideActive);
        Assert.AreEqual(ProcessPriorityClass.AboveNormal, state.CurrentPriority);
        Assert.AreEqual(ProcessPriorityClass.AboveNormal, state.OverridePriority);
        Assert.AreEqual(ProcessPriorityClass.Normal, state.BaselinePriority);

        // Repeated apply keeps the original baseline
        _backend.CurrentPriority = ProcessPriorityClass.AboveNormal;
        SessionCpuMutationResult result2 = coordinator.ApplySessionPriorityOverride(token, ProcessPriorityClass.Normal);
        Assert.IsTrue(result2.Success);
        Assert.AreEqual(ProcessPriorityClass.Normal, result2.State?.BaselinePriority);
    }

    [TestMethod]
    public void ApplySessionPriorityOverride_RejectsWhenBenchmarkActive()
    {
        _monitor.Snapshot = Snapshot();
        SessionOptimizationCoordinator coordinator = CreateCoordinator(arbiter: new DenyingArbiter());
        Guid token = coordinator.GetSessionCpuState().SessionToken!.Value;

        SessionCpuMutationResult result = coordinator.ApplySessionPriorityOverride(token, ProcessPriorityClass.AboveNormal);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("benchmark_active", result.ErrorCode);
        Assert.AreEqual(0, _backend.AppliedPriorityCalls.Count);
    }

    [TestMethod]
    public void ApplySessionPriorityOverride_FailsClosedOnTargetLossOrIdentityMismatch()
    {
        _monitor.Snapshot = Snapshot();
        _backend.FreshIdentityFails = true;
        SessionOptimizationCoordinator coordinator = CreateCoordinator();
        Guid token = coordinator.GetSessionCpuState().SessionToken!.Value;

        SessionCpuMutationResult result = coordinator.ApplySessionPriorityOverride(token, ProcessPriorityClass.AboveNormal);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("target_lost", result.ErrorCode);
        Assert.AreEqual(0, _backend.AppliedPriorityCalls.Count);
    }

    [TestMethod]
    public void ApplySessionPriorityOverride_RejectsProtectedGames()
    {
        _monitor.Snapshot = RiotSnapshot();
        SessionOptimizationCoordinator coordinator = CreateCoordinator();
        Guid token = coordinator.GetSessionCpuState().SessionToken!.Value;

        SessionCpuMutationResult result = coordinator.ApplySessionPriorityOverride(token, ProcessPriorityClass.AboveNormal);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("protected_game", result.ErrorCode);
        Assert.AreEqual(0, _backend.AppliedPriorityCalls.Count);
    }

    [TestMethod]
    public void ApplySessionPriorityOverride_RejectsUnsupportedPriorities()
    {
        _monitor.Snapshot = Snapshot();
        SessionOptimizationCoordinator coordinator = CreateCoordinator();
        Guid token = coordinator.GetSessionCpuState().SessionToken!.Value;

        Assert.AreEqual("invalid_priority", coordinator.ApplySessionPriorityOverride(token, ProcessPriorityClass.High).ErrorCode);
        Assert.AreEqual("invalid_priority", coordinator.ApplySessionPriorityOverride(token, ProcessPriorityClass.RealTime).ErrorCode);
        Assert.AreEqual("invalid_priority", coordinator.ApplySessionPriorityOverride(token, ProcessPriorityClass.BelowNormal).ErrorCode);
        Assert.AreEqual("invalid_priority", coordinator.ApplySessionPriorityOverride(token, ProcessPriorityClass.Idle).ErrorCode);
        Assert.AreEqual(0, _backend.AppliedPriorityCalls.Count);
    }

    [TestMethod]
    public void ResetSessionPriorityOverride_RestoresBaselinePriority()
    {
        _monitor.Snapshot = Snapshot(pid: 4242);
        _backend.CurrentPriority = ProcessPriorityClass.Normal;
        SessionOptimizationCoordinator coordinator = CreateCoordinator();
        Guid token = coordinator.GetSessionCpuState().SessionToken!.Value;

        Assert.IsTrue(coordinator.ApplySessionPriorityOverride(token, ProcessPriorityClass.AboveNormal).Success);
        Assert.AreEqual(ProcessPriorityClass.AboveNormal, _backend.CurrentPriority);

        SessionCpuMutationResult resetResult = coordinator.ResetSessionPriorityOverride(token);
        Assert.IsTrue(resetResult.Success);
        Assert.AreEqual("restored", resetResult.ErrorCode);
        Assert.AreEqual(ProcessPriorityClass.Normal, _backend.CurrentPriority);

        SessionCpuStateResult state = coordinator.GetSessionCpuState();
        Assert.IsNull(state.OverridePriority);

        // Idempotent second reset
        SessionCpuMutationResult resetResult2 = coordinator.ResetSessionPriorityOverride(token);
        Assert.IsTrue(resetResult2.Success);
        Assert.AreEqual("restored", resetResult2.ErrorCode);
    }

    [TestMethod]
    public void ResetSessionPriorityOverride_DoesNotRestoreIfPriorityChangedExternally()
    {
        _monitor.Snapshot = Snapshot(pid: 4242);
        _backend.CurrentPriority = ProcessPriorityClass.Normal;
        SessionOptimizationCoordinator coordinator = CreateCoordinator();
        Guid token = coordinator.GetSessionCpuState().SessionToken!.Value;

        Assert.IsTrue(coordinator.ApplySessionPriorityOverride(token, ProcessPriorityClass.AboveNormal).Success);

        // External tool / user changed priority to Idle
        _backend.CurrentPriority = ProcessPriorityClass.Idle;

        int callsBeforeReset = _backend.AppliedPriorityCalls.Count;
        SessionCpuMutationResult resetResult = coordinator.ResetSessionPriorityOverride(token);
        Assert.IsTrue(resetResult.Success);
        // Did not overwrite the external priority change
        Assert.AreEqual(callsBeforeReset, _backend.AppliedPriorityCalls.Count);
        Assert.AreEqual(ProcessPriorityClass.Idle, _backend.CurrentPriority);
    }

    [TestMethod]
    public void ReconcileSessionCpuState_DiscardsStalePriorityStateWhenGameExits()
    {
        _monitor.Snapshot = Snapshot(pid: 4242);
        _backend.CurrentPriority = ProcessPriorityClass.Normal;
        SessionOptimizationCoordinator coordinator = CreateCoordinator();
        Guid token = coordinator.GetSessionCpuState().SessionToken!.Value;

        Assert.IsTrue(coordinator.ApplySessionPriorityOverride(token, ProcessPriorityClass.AboveNormal).Success);

        // Game exits
        _monitor.Snapshot = null;
        SessionCpuStateResult state = coordinator.GetSessionCpuState();

        Assert.IsFalse(state.Available);
        Assert.IsFalse(state.TemporaryOverrideActive);
        Assert.IsNull(state.SessionToken);
    }

    [TestMethod]
    public async Task StopSessionAsync_RestoresBaselinePriority()
    {
        _monitor.Snapshot = Snapshot(pid: 4242);
        _backend.CurrentPriority = ProcessPriorityClass.Normal;
        SessionOptimizationCoordinator coordinator = CreateCoordinator();
        Guid token = coordinator.GetSessionCpuState().SessionToken!.Value;

        Assert.IsTrue(coordinator.ApplySessionPriorityOverride(token, ProcessPriorityClass.AboveNormal).Success);
        Assert.AreEqual(ProcessPriorityClass.AboveNormal, _backend.CurrentPriority);

        var restoreResult = await coordinator.StopSessionAsync();
        Assert.AreEqual(ProcessPriorityClass.Normal, _backend.CurrentPriority);
    }

    [TestMethod]
    public void ApplySessionPriorityOverride_UsesIdentityAwareBackendMethods()
    {
        _monitor.Snapshot = Snapshot(pid: 4242);
        _backend.CurrentPriority = ProcessPriorityClass.Normal;
        SessionOptimizationCoordinator coordinator = CreateCoordinator();
        Guid token = coordinator.GetSessionCpuState().SessionToken!.Value;

        SessionCpuMutationResult result = coordinator.ApplySessionPriorityOverride(token, ProcessPriorityClass.AboveNormal);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, _backend.AppliedIdentityPriorityCalls.Count);
        Assert.AreEqual(4242, _backend.AppliedIdentityPriorityCalls[0].Identity.ProcessId);
        Assert.AreEqual(ProcessPriorityClass.AboveNormal, _backend.AppliedIdentityPriorityCalls[0].Priority);

        SessionCpuMutationResult resetResult = coordinator.ResetSessionPriorityOverride(token);
        Assert.IsTrue(resetResult.Success);
        Assert.AreEqual(2, _backend.AppliedIdentityPriorityCalls.Count);
        Assert.AreEqual(4242, _backend.AppliedIdentityPriorityCalls[1].Identity.ProcessId);
        Assert.AreEqual(ProcessPriorityClass.Normal, _backend.AppliedIdentityPriorityCalls[1].Priority);
    }

    [TestMethod]
    public async Task StartSessionAsync_AutomaticallyAppliesPriorityOverride_WhenGameSettingsOptIn()
    {
        _monitor.Snapshot = Snapshot(id: "game-1", pid: 4242);
        _backend.CurrentPriority = ProcessPriorityClass.Normal;
        var settingsService = new SessionOptimizationSettingsService(Path.Combine(_tempDir, "opt-settings.json"));
        var settings = new SessionOptimizationSettings();
        settings.GameSettings["game-1"] = new SessionGameSuspendSettings { ApplyPriorityOverride = true };
        settingsService.Save(settings);

        using SessionOptimizationCoordinator coordinator = CreateCoordinator(settingsService: settingsService);

        var gameItem = _monitor.Snapshot.LibraryItem;
        var startResult = await coordinator.StartSessionAsync("Manual", gameItem);
        Assert.IsTrue(startResult.Success);

        Assert.AreEqual(ProcessPriorityClass.AboveNormal, _backend.CurrentPriority);
        Assert.AreEqual(1, _backend.AppliedIdentityPriorityCalls.Count);

        var stopResult = await coordinator.StopSessionAsync();
        Assert.IsTrue(stopResult.Success);
        Assert.AreEqual(ProcessPriorityClass.Normal, _backend.CurrentPriority);
    }

    [TestMethod]
    public async Task StartSessionAsync_DoesNotApplyPriorityOverride_WhenDefaultOff()
    {
        _monitor.Snapshot = Snapshot(id: "game-1", pid: 4242);
        _backend.CurrentPriority = ProcessPriorityClass.Normal;
        var settingsService = new SessionOptimizationSettingsService(Path.Combine(_tempDir, "opt-settings.json"));
        var settings = new SessionOptimizationSettings();
        settingsService.Save(settings);

        using SessionOptimizationCoordinator coordinator = CreateCoordinator(settingsService: settingsService);

        var gameItem = _monitor.Snapshot.LibraryItem;
        var startResult = await coordinator.StartSessionAsync("Manual", gameItem);
        Assert.IsTrue(startResult.Success);

        Assert.AreEqual(ProcessPriorityClass.Normal, _backend.CurrentPriority);
        Assert.AreEqual(0, _backend.AppliedIdentityPriorityCalls.Count);
    }

    private sealed class FakeProcessSuspendService : ProcessSuspendService
    {
        private readonly SuspendedProcessRecord _record = new()
        {
            ProcessId = 9999,
            ProcessName = "mock-background",
            ProcessStartTimeUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        public int SuspendCalls { get; private set; }
        public int ResumeCalls { get; private set; }

        public override Task<SessionProcessSnapshot> CaptureProcessSnapshotAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SessionProcessSnapshot());

        public override IReadOnlyList<SuspendCandidate> BuildCandidates(
            SessionProcessSnapshot snapshot,
            IEnumerable<BackgroundProcessRule> enabledRules,
            IEnumerable<string> customProcessNames,
            IEnumerable<string> protectedProcessNames) =>
            [new SuspendCandidate { ProcessId = _record.ProcessId, ProcessName = _record.ProcessName, ProcessStartTimeUtc = _record.ProcessStartTimeUtc }];

        public override SessionActionResult SuspendProcesses(IEnumerable<SuspendCandidate> candidates)
        {
            SuspendCalls++;
            return new SessionActionResult
            {
                SuccessCount = 1,
                Records = [_record]
            };
        }

        public override SessionActionResult ResumeProcesses(IEnumerable<SuspendedProcessRecord> records)
        {
            ResumeCalls++;
            return new SessionActionResult
            {
                SuccessCount = 1,
                Records = [_record]
            };
        }
    }

    private sealed class FakeActiveGameMonitor : IActiveGameMonitor
    {
        public ActiveGameSnapshot? Snapshot { get; set; }
        public ActiveGameSnapshot? CurrentSnapshot => Snapshot;
        public void Start() { }
        public Task StopAsync() => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class DenyingArbiter : IBenchmarkOperationArbiter
    {
        public bool TryAcquireExternalMutation(out IDisposable? lease)
        {
            lease = null;
            return false;
        }
    }

    private sealed class FakeSessionCpuBackend : ISessionCpuControlBackend
    {
        public SessionCpuSelection? CurrentSelection { get; set; } = new(OptimizationMode.Affinity, 0xFF);
        public ProcessPriorityClass? CurrentPriority { get; set; } = ProcessPriorityClass.Normal;
        public bool FreshIdentityFails { get; set; }
        public bool ApplyPrioritySucceeds { get; set; } = true;

        public List<(int ProcessId, SessionCpuSelection Selection)> AppliedCalls { get; } = new();
        public List<(int ProcessId, ProcessPriorityClass Priority)> AppliedPriorityCalls { get; } = new();
        public List<(BenchmarkProcessIdentity Identity, ProcessPriorityClass Priority)> AppliedIdentityPriorityCalls { get; } = new();
        public List<BenchmarkProcessIdentity> FreshIdentityCalls { get; } = new();
        public List<string> OperationOrder { get; } = new();

        public SessionCpuTopology GetTopology() => new(Enumerable.Range(0, 8)
            .Select(index => new SessionCpuLogicalProcessor(index, index / 2, 0, false, index % 2 == 1, index % 2 == 0 ? "[P]" : "[T]"))
            .ToList());

        public bool IsValidSelection(OptimizationMode mode, long mask) =>
            mask != 0 && mode is OptimizationMode.Affinity or OptimizationMode.CpuSets && (mask & ~(1L << 0 | 1L << 1 | 1L << 2 | 1L << 3 | 1L << 4 | 1L << 5 | 1L << 6 | 1L << 7)) == 0;

        public BenchmarkProcessIdentity? ResolveFreshIdentity(BenchmarkProcessIdentity expected)
        {
            FreshIdentityCalls.Add(expected);
            OperationOrder.Add("validate");
            return FreshIdentityFails ? null : expected;
        }

        public SessionCpuSelection? GetCurrentSelection(int processId) => CurrentSelection;

        public string ApplySelection(int processId, SessionCpuSelection selection)
        {
            AppliedCalls.Add((processId, selection));
            OperationOrder.Add("apply");
            return "OK_AFFINITY";
        }

        public ProcessPriorityClass? GetCurrentPriority(int processId) => CurrentPriority;

        public ProcessPriorityClass? GetCurrentPriority(BenchmarkProcessIdentity expectedIdentity) =>
            FreshIdentityFails ? null : CurrentPriority;

        public bool ApplyPriority(int processId, ProcessPriorityClass priority) =>
            ApplyPriority(new BenchmarkProcessIdentity { ProcessId = processId, ProcessName = "mock" }, priority);

        public bool ApplyPriority(BenchmarkProcessIdentity expectedIdentity, ProcessPriorityClass priority)
        {
            AppliedPriorityCalls.Add((expectedIdentity.ProcessId, priority));
            AppliedIdentityPriorityCalls.Add((expectedIdentity, priority));
            OperationOrder.Add("apply_priority");
            if (ApplyPrioritySucceeds && !FreshIdentityFails)
            {
                CurrentPriority = priority;
                return true;
            }
            return false;
        }
    }
}

// ---------------------------------------------------------------------------
// Authorization surface, permission matrix, provider mapping, and localization.
// ---------------------------------------------------------------------------
[TestClass]
public sealed class SessionCpuPermissionAndPresentationTests
{
    private static string? FindRepoRoot()
    {
        string? directory = Path.GetDirectoryName(typeof(SessionCpuPermissionAndPresentationTests).Assembly.Location);
        while (directory != null && !Directory.Exists(Path.Combine(directory, "FrameHub.App")))
        {
            directory = Path.GetDirectoryName(directory);
        }
        return directory;
    }

    [TestMethod]
    public async Task SessionCpuScopes_ExistButAreNeverGrantedByDefaultOrLegacyRecords()
    {
        Assert.IsTrue(CompanionScopes.IsValidScope(CompanionScopes.ReadOptimizationCpu));
        Assert.IsTrue(CompanionScopes.IsValidScope(CompanionScopes.WriteOptimizationCpu));

        string tempDir = Path.Combine(Path.GetTempPath(), "FrameHubSessionCpuScopeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string storePath = Path.Combine(tempDir, "devices.json");
        try
        {
            var store = new FrameHub.Companion.Persistence.DeviceRecordStore(storePath);
            var pairingEngine = new FrameHub.Companion.Pairing.PairingEngine(store);

            var status = pairingEngine.StartPairingSession("127.0.0.1", 5000);
            var clientTask = pairingEngine.SubmitPairingRequestAsync(status.PairingToken!, "Test Phone", "127.0.0.1");
            bool allowed = pairingEngine.AllowPendingRequest(out _, out var pairedRecord);
            Assert.IsTrue(allowed);
            var result = await clientTask;
            Assert.AreEqual(FrameHub.Companion.Models.PairingResultStatus.Approved, result.Status);

            Assert.IsNotNull(pairedRecord);
            CollectionAssert.AreEqual(new[] { CompanionScopes.ReadStatus }, pairedRecord.Scopes.ToList(),
                "Default pairing must grant only read:status.");
            Assert.IsFalse(pairedRecord.Scopes.Contains(CompanionScopes.ReadOptimizationCpu));
            Assert.IsFalse(pairedRecord.Scopes.Contains(CompanionScopes.WriteOptimizationCpu));

            // A legacy device record loaded from disk also never synthesizes the new scopes
            var legacyRecord = new FrameHub.Companion.Persistence.PairedDeviceRecord
            {
                Id = Guid.NewGuid(),
                DisplayName = "Legacy Phone",
                CredentialHash = "hash",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Scopes = new List<string> { CompanionScopes.ReadStatus, CompanionScopes.WriteOptimization }
            };
            File.WriteAllText(storePath, JsonSerializer.Serialize(new[] { legacyRecord }));
            var reloadedStore = new FrameHub.Companion.Persistence.DeviceRecordStore(storePath);
            var device = reloadedStore.Devices.Single();
            Assert.IsFalse(device.Scopes.Contains(CompanionScopes.ReadOptimizationCpu));
            Assert.IsFalse(device.Scopes.Contains(CompanionScopes.WriteOptimizationCpu));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [TestMethod]
    public void PermissionMatrix_WriteCpuToggleForcesReadOn_AndReadOffForcesWriteOff()
    {
        var record = new FrameHub.Companion.Persistence.PairedDeviceRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = "Phone",
            CredentialHash = "hash",
            Scopes = new List<string>()
        };
        var toggles = new List<(Guid Id, string Scope, bool Enable)>();

        var viewModel = new PairedDeviceItemViewModel(record, _ => { }, (id, scope, enable) => toggles.Add((id, scope, enable)));

        viewModel.WriteOptimizationCpuEnabled = true;
        CollectionAssert.Contains(toggles.Select(t => t.Scope).ToList(), CompanionScopes.ReadOptimizationCpu, "write ON must force read ON.");
        CollectionAssert.Contains(toggles.Select(t => t.Scope).ToList(), CompanionScopes.WriteOptimizationCpu);

        toggles.Clear();
        viewModel.ReadOptimizationCpuEnabled = false;
        Assert.IsFalse(viewModel.WriteOptimizationCpuEnabled, "read OFF must force write OFF.");
        CollectionAssert.Contains(toggles.Select(t => t.Scope).ToList(), CompanionScopes.WriteOptimizationCpu, "The write revoke must be recorded.");
        Assert.IsFalse(toggles.Any(t => t.Scope == CompanionScopes.WriteOptimizationCpu && t.Enable));
    }

    [TestMethod]
    public void SettingsPermissionMatrix_XamlExposesSessionCpuRow()
    {
        string? repoRoot = FindRepoRoot();
        if (repoRoot == null)
        {
            Assert.Inconclusive("Repository source root was not discoverable from the test assembly location.");
        }
        string xaml = File.ReadAllText(Path.Combine(repoRoot!, "FrameHub.App", "Views", "SettingsView.xaml"));
        StringAssert.Contains(xaml, "IsChecked=\"{Binding ReadOptimizationCpuEnabled, Mode=TwoWay}\"");
        StringAssert.Contains(xaml, "IsChecked=\"{Binding WriteOptimizationCpuEnabled, Mode=TwoWay}\"");
        StringAssert.Contains(xaml, "Text=\"{Binding AreaSessionCpuLabel}\"");
    }

    [TestMethod]
    public void SessionCpuLocalization_HasEnglishAndPolishParity()
    {
        string[] keys =
        [
            "Settings.CompanionAreaSessionCpu",
            "Settings.CompanionScopeReadOptimizationCpu",
            "Settings.CompanionScopeWriteOptimizationCpu"
        ];

        foreach (string key in keys)
        {
            string english = FrameHub.App.Services.LocalizationService.Translate(key, "en");
            string polish = FrameHub.App.Services.LocalizationService.Translate(key, "pl");
            Assert.AreNotEqual(key, english, $"Missing English localization for '{key}'.");
            Assert.AreNotEqual(key, polish, $"Missing Polish localization for '{key}'.");
        }

        Assert.AreEqual("Game CPU Assignment", FrameHub.App.Services.LocalizationService.Translate("Settings.CompanionAreaSessionCpu", "en"));
        Assert.AreEqual("Przydział CPU dla gry", FrameHub.App.Services.LocalizationService.Translate("Settings.CompanionAreaSessionCpu", "pl"));
        Assert.AreEqual("Game CPU data", FrameHub.App.Services.LocalizationService.Translate("Settings.CompanionScopeReadOptimizationCpu", "en"));
        Assert.AreEqual("Dane CPU gry", FrameHub.App.Services.LocalizationService.Translate("Settings.CompanionScopeReadOptimizationCpu", "pl"));
        Assert.AreEqual("Game CPU assignment", FrameHub.App.Services.LocalizationService.Translate("Settings.CompanionScopeWriteOptimizationCpu", "en"));
        Assert.AreEqual("Przydział CPU dla gry", FrameHub.App.Services.LocalizationService.Translate("Settings.CompanionScopeWriteOptimizationCpu", "pl"));
    }

    [TestMethod]
    public void PairedDeviceItemViewModel_UsesGameCpuControlTerminologyInFallbacks()
    {
        var record = new FrameHub.Companion.Persistence.PairedDeviceRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test Phone",
            CredentialHash = "hash",
            Scopes = new List<string>()
        };

        var vm = new PairedDeviceItemViewModel(record, _ => { }, (_, _, _) => { });
        Assert.AreEqual("Game CPU Assignment", vm.AreaSessionCpuLabel);
        Assert.AreEqual("Game CPU Data", vm.ScopeReadOptimizationCpuLabel);
        Assert.AreEqual("Game CPU Assignment", vm.ScopeWriteOptimizationCpuLabel);
    }

    [TestMethod]
    public void Topology_PreservesPhysicalCoreAndThreadMetadata_WithoutOddEvenAssumption()
    {
        // Asymmetric/hybrid layout: 2 P-cores with SMT (indices 0, 1T, 2, 3T) and 2 E-cores without SMT (indices 4, 5)
        var core0 = new CoreInfo { Index = 0, CoreIndex = 0, IsThread = false, IsECore = false, TypeTag = "[P]" };
        var core0T = new CoreInfo { Index = 1, CoreIndex = 0, IsThread = true, IsECore = false, TypeTag = "[P]" };
        var core1 = new CoreInfo { Index = 2, CoreIndex = 1, IsThread = false, IsECore = false, TypeTag = "[P]" };
        var core1T = new CoreInfo { Index = 3, CoreIndex = 1, IsThread = true, IsECore = false, TypeTag = "[P]" };
        var core2 = new CoreInfo { Index = 4, CoreIndex = 2, IsThread = false, IsECore = true, TypeTag = "[E]" };
        var core3 = new CoreInfo { Index = 5, CoreIndex = 3, IsThread = false, IsECore = true, TypeTag = "[E]" };

        var backend = new SessionCpuControlBackend(
            () => [core0, core0T, core1, core1T, core2, core3],
            () => new Dictionary<int, uint> { [0] = 1, [1] = 2, [2] = 3, [3] = 4, [4] = 5, [5] = 6 });

        var topology = backend.GetTopology();
        Assert.AreEqual(6, topology.Processors.Count);

        var physicalProcessors = topology.Processors.Where(p => !p.IsThread).ToList();
        Assert.AreEqual(4, physicalProcessors.Count, "4 physical cores exist across the 6 logical processors.");
        CollectionAssert.AreEqual(new[] { 0, 2, 4, 5 }, physicalProcessors.Select(p => p.Index).ToList(),
            "Physical cores must be selected strictly via !IsThread/topology, correctly including E-core 5 even though 5 is odd.");
    }

    [TestMethod]
    public void NativeBackend_ContainsNoProfileOrLibraryPolicy()
    {
        string? repoRoot = FindRepoRoot();
        if (repoRoot == null)
        {
            Assert.Inconclusive("Repository source root was not discoverable from the test assembly location.");
        }
        string source = File.ReadAllText(Path.Combine(repoRoot!, "FrameHub.Core", "Services", "SessionOptimization", "SessionCpuControlBackend.cs"));
        Assert.IsFalse(source.Contains("ProcessProfile", StringComparison.Ordinal), "Profile objects and source detection must stay outside the native backend.");
        Assert.IsFalse(source.Contains("LibraryService", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("MatchesIdentity", StringComparison.Ordinal), "Profile identity matching must stay outside the native backend.");
        Assert.IsFalse(source.Contains("LoadProfiles", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AppProvider_ReportsSystemProfileAndOverrideSources()
    {
        var monitor = new StubActiveGameMonitor();
        var backend = new StubCpuBackend();
        var coordinator = new SessionOptimizationCoordinator(
            stateService: new SessionStateService(Path.Combine(Path.GetTempPath(), "FrameHubCpuProvTests", Guid.NewGuid().ToString("N"), "state.json")),
            activeGameMonitor: monitor,
            sessionCpuBackend: backend);
        var provider = new AppSessionOptimizationProvider(
            coordinator,
            monitor,
            new StubBenchmarkCoordinator(),
            gameProfileResolver: libraryItemId => libraryItemId == "profiled-game"
                ? new ProcessProfile { Id = "p1", DisplayName = "Competitive", ProcessName = "game", IsEnabled = true, ApplyCoreOptimization = true }
                : null);

        monitor.Snapshot = SessionCpuCoordinatorTests_SnapshotHelper.Snapshot("plain-game");
        Assert.AreEqual("system", provider.GetCpuStateAsync().Result.Source);

        monitor.Snapshot = SessionCpuCoordinatorTests_SnapshotHelper.Snapshot("profiled-game");
        Assert.AreEqual("profile", provider.GetCpuStateAsync().Result.Source);
        Assert.AreEqual("Competitive", provider.GetCpuStateAsync().Result.ProfileName);

        Guid token = coordinator.GetSessionCpuState().SessionToken!.Value;
        Assert.IsTrue(coordinator.ApplySessionCpuOverride(token, OptimizationMode.Affinity, 0x03).Success);
        Assert.AreEqual("temporary-override", provider.GetCpuStateAsync().Result.Source);
        Assert.AreEqual("Competitive", provider.GetCpuStateAsync().Result.ProfileName,
            "Profile identity stays visible during an override so Restore can be labeled correctly.");
    }

    [TestMethod]
    public void AppProvider_RejectsRequestsMissingTokenModeOrProcessors()
    {
        var monitor = new StubActiveGameMonitor { Snapshot = SessionCpuCoordinatorTests_SnapshotHelper.Snapshot("game-1") };
        var coordinator = new SessionOptimizationCoordinator(
            stateService: new SessionStateService(Path.Combine(Path.GetTempPath(), "FrameHubCpuProvTests", Guid.NewGuid().ToString("N"), "state.json")),
            activeGameMonitor: monitor,
            sessionCpuBackend: new StubCpuBackend());
        var provider = new AppSessionOptimizationProvider(coordinator, monitor, new StubBenchmarkCoordinator());

        Assert.AreEqual("invalid_request", provider.ApplyCpuOverrideAsync(new CompanionSessionCpuApplyRequestDto()).Result.ErrorCode);
        Assert.AreEqual("invalid_request", provider.ApplyCpuOverrideAsync(new CompanionSessionCpuApplyRequestDto { SessionToken = "not-a-guid", Mode = "affinity", Indices = [0] }).Result.ErrorCode);
        Assert.AreEqual("invalid_request", provider.ApplyCpuOverrideAsync(new CompanionSessionCpuApplyRequestDto { SessionToken = Guid.NewGuid().ToString("N"), Mode = "exclusive", Indices = [0] }).Result.ErrorCode);
        Assert.AreEqual("invalid_request", provider.ApplyCpuOverrideAsync(new CompanionSessionCpuApplyRequestDto { SessionToken = Guid.NewGuid().ToString("N"), Mode = "affinity", Indices = [] }).Result.ErrorCode);
        Assert.AreEqual("invalid_selection", provider.ApplyCpuOverrideAsync(new CompanionSessionCpuApplyRequestDto { SessionToken = Guid.NewGuid().ToString("N"), Mode = "affinity", Indices = [99] }).Result.ErrorCode);
        Assert.AreEqual("invalid_request", provider.ResetCpuOverrideAsync(new CompanionSessionCpuResetRequestDto()).Result.ErrorCode);
    }

    private sealed class SessionCpuCoordinatorTests_SnapshotHelper
    {
        public static ActiveGameSnapshot Snapshot(string id) => new(
            new LibraryItem { Id = id, DisplayName = "Game " + id, ProcessName = "game", Type = LibraryItemType.Game, IsEnabled = true },
            new BenchmarkProcessIdentity { ProcessId = 4242, ProcessName = "game", StartTimeUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
    }

    private sealed class StubActiveGameMonitor : IActiveGameMonitor
    {
        public ActiveGameSnapshot? Snapshot { get; set; }
        public ActiveGameSnapshot? CurrentSnapshot => Snapshot;
        public void Start() { }
        public Task StopAsync() => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class StubCpuBackend : ISessionCpuControlBackend
    {
        public SessionCpuSelection? CurrentSelection { get; set; } = new(OptimizationMode.Affinity, 0xFF);
        public ProcessPriorityClass? CurrentPriority { get; set; } = ProcessPriorityClass.Normal;

        public SessionCpuTopology GetTopology() => new(new[] { new SessionCpuLogicalProcessor(0, 0, 0, false, false, "[P]") });

        public bool IsValidSelection(OptimizationMode mode, long mask) => mask != 0;

        public BenchmarkProcessIdentity? ResolveFreshIdentity(BenchmarkProcessIdentity expected) => expected;

        public SessionCpuSelection? GetCurrentSelection(int processId) => CurrentSelection;

        public string ApplySelection(int processId, SessionCpuSelection selection) => "OK_AFFINITY";

        public ProcessPriorityClass? GetCurrentPriority(int processId) => CurrentPriority;

        public ProcessPriorityClass? GetCurrentPriority(BenchmarkProcessIdentity expectedIdentity) => CurrentPriority;

        public bool ApplyPriority(int processId, ProcessPriorityClass priority) => true;

        public bool ApplyPriority(BenchmarkProcessIdentity expectedIdentity, ProcessPriorityClass priority) => true;
    }

    private sealed class StubBenchmarkCoordinator : IBenchmarkCaptureCoordinator
    {
        public event EventHandler<BenchmarkCaptureStateSnapshot>? StateChanged { add { } remove { } }
        public BenchmarkCaptureStateSnapshot CurrentState { get; } = new() { State = CoordinatorState.Idle, IsActive = false };
        public bool IsActive => false;
        public BenchmarkCaptureStartHandle TryStartCapture(BenchmarkCaptureRequest request, CancellationToken cancellationToken = default) =>
            new() { Accepted = false, ErrorCode = "not_supported" };
        public Task<BenchmarkCaptureOutcome> StartCaptureAsync(BenchmarkCaptureRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BenchmarkCaptureOutcome { Status = CoordinatorStatus.Failed, ErrorCode = "not_supported" });
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }
}

[TestClass]
public sealed class ProcessServicePriorityIdentityTests
{
    private sealed class FakeProcessPriorityAccessor : IProcessPriorityAccessor
    {
        public ProcessPriorityClass? Priority { get; set; } = ProcessPriorityClass.Normal;
        public int ProcessId { get; set; }
        public DateTime StartTimeUtc { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string? ExecutablePath { get; set; }

        public bool MatchesIdentity(BenchmarkProcessIdentity expected) =>
            ProcessService.MatchesIdentityCore(expected, ProcessId, ProcessName, StartTimeUtc, ExecutablePath);
    }

    [TestMethod]
    public void ProcessService_Priority_MatchesExpectedIdentity_AllowsGetAndSet()
    {
        var service = new ProcessService();
        var fake = new FakeProcessPriorityAccessor
        {
            ProcessId = 1234,
            StartTimeUtc = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc),
            ProcessName = "game.exe",
            ExecutablePath = @"C:\Games\game.exe",
            Priority = ProcessPriorityClass.Normal
        };
        service.ProcessPriorityAccessorForTesting = pid => pid == 1234 ? fake : null;

        var matchingIdentity = new BenchmarkProcessIdentity
        {
            ProcessId = 1234,
            StartTimeUtc = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc),
            ProcessName = "game.exe",
            ExecutablePath = @"C:\Games\game.exe"
        };

        Assert.AreEqual(ProcessPriorityClass.Normal, service.GetPriority(matchingIdentity));
        Assert.IsTrue(service.SetPriority(matchingIdentity, ProcessPriorityClass.AboveNormal));
        Assert.AreEqual(ProcessPriorityClass.AboveNormal, fake.Priority);
    }

    [TestMethod]
    public void ProcessService_Priority_RejectsOnRecycledPid_StartTimeMismatch()
    {
        var service = new ProcessService();
        var fake = new FakeProcessPriorityAccessor
        {
            ProcessId = 1234,
            StartTimeUtc = new DateTime(2026, 10, 18, 13, 0, 0, DateTimeKind.Utc),
            ProcessName = "game.exe",
            ExecutablePath = @"C:\Games\game.exe",
            Priority = ProcessPriorityClass.Normal
        };
        service.ProcessPriorityAccessorForTesting = pid => pid == 1234 ? fake : null;

        var oldIdentity = new BenchmarkProcessIdentity
        {
            ProcessId = 1234,
            StartTimeUtc = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc),
            ProcessName = "game.exe",
            ExecutablePath = @"C:\Games\game.exe"
        };

        Assert.IsNull(service.GetPriority(oldIdentity));
        Assert.IsFalse(service.SetPriority(oldIdentity, ProcessPriorityClass.AboveNormal));
        Assert.AreEqual(ProcessPriorityClass.Normal, fake.Priority);
    }

    [TestMethod]
    public void ProcessService_Priority_RejectsOnProcessNameMismatch()
    {
        var service = new ProcessService();
        var fake = new FakeProcessPriorityAccessor
        {
            ProcessId = 1234,
            StartTimeUtc = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc),
            ProcessName = "svchost.exe",
            ExecutablePath = @"C:\Windows\System32\svchost.exe",
            Priority = ProcessPriorityClass.Normal
        };
        service.ProcessPriorityAccessorForTesting = pid => pid == 1234 ? fake : null;

        var expectedGameIdentity = new BenchmarkProcessIdentity
        {
            ProcessId = 1234,
            StartTimeUtc = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc),
            ProcessName = "game.exe",
            ExecutablePath = @"C:\Games\game.exe"
        };

        Assert.IsNull(service.GetPriority(expectedGameIdentity));
        Assert.IsFalse(service.SetPriority(expectedGameIdentity, ProcessPriorityClass.AboveNormal));
        Assert.AreEqual(ProcessPriorityClass.Normal, fake.Priority);
    }

    [TestMethod]
    public void ProcessService_Priority_RejectsOnExecutablePathMismatch()
    {
        var service = new ProcessService();
        var fake = new FakeProcessPriorityAccessor
        {
            ProcessId = 1234,
            StartTimeUtc = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc),
            ProcessName = "game.exe",
            ExecutablePath = @"C:\OtherDir\game.exe",
            Priority = ProcessPriorityClass.Normal
        };
        service.ProcessPriorityAccessorForTesting = pid => pid == 1234 ? fake : null;

        var expectedIdentity = new BenchmarkProcessIdentity
        {
            ProcessId = 1234,
            StartTimeUtc = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc),
            ProcessName = "game.exe",
            ExecutablePath = @"C:\Games\game.exe"
        };

        Assert.IsNull(service.GetPriority(expectedIdentity));
        Assert.IsFalse(service.SetPriority(expectedIdentity, ProcessPriorityClass.AboveNormal));
        Assert.AreEqual(ProcessPriorityClass.Normal, fake.Priority);
    }

    [TestMethod]
    public void ProcessService_Priority_RejectsWhenExpectedStartTimeIsDefault()
    {
        var service = new ProcessService();
        var fake = new FakeProcessPriorityAccessor
        {
            ProcessId = 1234,
            StartTimeUtc = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc),
            ProcessName = "game.exe",
            ExecutablePath = @"C:\Games\game.exe",
            Priority = ProcessPriorityClass.Normal
        };
        service.ProcessPriorityAccessorForTesting = pid => pid == 1234 ? fake : null;

        var invalidIdentity = new BenchmarkProcessIdentity
        {
            ProcessId = 1234,
            StartTimeUtc = default,
            ProcessName = "game.exe",
            ExecutablePath = @"C:\Games\game.exe"
        };

        Assert.IsNull(service.GetPriority(invalidIdentity));
        Assert.IsFalse(service.SetPriority(invalidIdentity, ProcessPriorityClass.AboveNormal));
        Assert.AreEqual(ProcessPriorityClass.Normal, fake.Priority);
    }

    [TestMethod]
    public void ProcessService_Priority_RejectsWhenActualStartTimeIsDefault()
    {
        var service = new ProcessService();
        var fake = new FakeProcessPriorityAccessor
        {
            ProcessId = 1234,
            StartTimeUtc = default,
            ProcessName = "game.exe",
            ExecutablePath = @"C:\Games\game.exe",
            Priority = ProcessPriorityClass.Normal
        };
        service.ProcessPriorityAccessorForTesting = pid => pid == 1234 ? fake : null;

        var expected = new BenchmarkProcessIdentity
        {
            ProcessId = 1234,
            StartTimeUtc = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc),
            ProcessName = "game.exe",
            ExecutablePath = @"C:\Games\game.exe"
        };

        Assert.IsNull(service.GetPriority(expected));
        Assert.IsFalse(service.SetPriority(expected, ProcessPriorityClass.AboveNormal));
        Assert.AreEqual(ProcessPriorityClass.Normal, fake.Priority);
    }

    [TestMethod]
    public void ProcessService_Priority_RejectsWhenExpectedPathSet_ButActualPathIsNull()
    {
        var service = new ProcessService();
        var fake = new FakeProcessPriorityAccessor
        {
            ProcessId = 1234,
            StartTimeUtc = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc),
            ProcessName = "game.exe",
            ExecutablePath = null,
            Priority = ProcessPriorityClass.Normal
        };
        service.ProcessPriorityAccessorForTesting = pid => pid == 1234 ? fake : null;

        var expected = new BenchmarkProcessIdentity
        {
            ProcessId = 1234,
            StartTimeUtc = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc),
            ProcessName = "game.exe",
            ExecutablePath = @"C:\Games\game.exe"
        };

        Assert.IsNull(service.GetPriority(expected));
        Assert.IsFalse(service.SetPriority(expected, ProcessPriorityClass.AboveNormal));
        Assert.AreEqual(ProcessPriorityClass.Normal, fake.Priority);
    }

    [TestMethod]
    public void ProcessService_Priority_RejectsWhenExpectedPathSet_ButActualPathIsEmpty()
    {
        var service = new ProcessService();
        var fake = new FakeProcessPriorityAccessor
        {
            ProcessId = 1234,
            StartTimeUtc = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc),
            ProcessName = "game.exe",
            ExecutablePath = "   ",
            Priority = ProcessPriorityClass.Normal
        };
        service.ProcessPriorityAccessorForTesting = pid => pid == 1234 ? fake : null;

        var expected = new BenchmarkProcessIdentity
        {
            ProcessId = 1234,
            StartTimeUtc = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc),
            ProcessName = "game.exe",
            ExecutablePath = @"C:\Games\game.exe"
        };

        Assert.IsNull(service.GetPriority(expected));
        Assert.IsFalse(service.SetPriority(expected, ProcessPriorityClass.AboveNormal));
        Assert.AreEqual(ProcessPriorityClass.Normal, fake.Priority);
    }

    [TestMethod]
    public void ProcessService_Priority_AllowsWhenExecutablePathNotExpected_AndRestMatches()
    {
        var service = new ProcessService();
        var fake = new FakeProcessPriorityAccessor
        {
            ProcessId = 1234,
            StartTimeUtc = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc),
            ProcessName = "game.exe",
            ExecutablePath = @"C:\Games\game.exe",
            Priority = ProcessPriorityClass.Normal
        };
        service.ProcessPriorityAccessorForTesting = pid => pid == 1234 ? fake : null;

        var expectedWithoutPath = new BenchmarkProcessIdentity
        {
            ProcessId = 1234,
            StartTimeUtc = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc),
            ProcessName = "game.exe",
            ExecutablePath = null
        };

        Assert.AreEqual(ProcessPriorityClass.Normal, service.GetPriority(expectedWithoutPath));
        Assert.IsTrue(service.SetPriority(expectedWithoutPath, ProcessPriorityClass.AboveNormal));
        Assert.AreEqual(ProcessPriorityClass.AboveNormal, fake.Priority);
    }

    [TestMethod]
    public void MatchesExpectedIdentity_WithRealProcess_AcceptsMatchingIdentity()
    {
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        var expected = new BenchmarkProcessIdentity
        {
            ProcessId = current.Id,
            StartTimeUtc = current.StartTime.ToUniversalTime(),
            ProcessName = current.ProcessName,
            ExecutablePath = current.MainModule?.FileName
        };

        Assert.IsTrue(ProcessService.MatchesExpectedIdentity(current, expected));
    }

    [TestMethod]
    public void MatchesExpectedIdentity_WithRealProcess_RejectsDefaultStartTime()
    {
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        var expected = new BenchmarkProcessIdentity
        {
            ProcessId = current.Id,
            StartTimeUtc = default,
            ProcessName = current.ProcessName,
            ExecutablePath = current.MainModule?.FileName
        };

        Assert.IsFalse(ProcessService.MatchesExpectedIdentity(current, expected));
    }

    [TestMethod]
    public void MatchesExpectedIdentity_WithRealProcess_RejectsMismatchedPath()
    {
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        var expected = new BenchmarkProcessIdentity
        {
            ProcessId = current.Id,
            StartTimeUtc = current.StartTime.ToUniversalTime(),
            ProcessName = current.ProcessName,
            ExecutablePath = @"C:\FrameHubTest\NonExistentPath\game.exe"
        };

        Assert.IsFalse(ProcessService.MatchesExpectedIdentity(current, expected));
    }

    [TestMethod]
    public void ProcessService_GetPriority_WithRealProcess_ReadsWithoutTestingHook()
    {
        var service = new ProcessService();
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        var expected = new BenchmarkProcessIdentity
        {
            ProcessId = current.Id,
            StartTimeUtc = current.StartTime.ToUniversalTime(),
            ProcessName = current.ProcessName,
            ExecutablePath = current.MainModule?.FileName
        };

        ProcessPriorityClass? prio = service.GetPriority(current.Id, expected);
        Assert.IsNotNull(prio);
        Assert.AreEqual(current.PriorityClass, prio.Value);
    }
}
