using System.Diagnostics;
using FrameHub.Core.Models;
using FrameHub.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public sealed class OptimizationRetryTests
{
    [DataTestMethod]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(false, false)]
    public void IncompleteApplication_IsRetriedUntilAllRequestedOperationsSucceed(bool coreSucceeds, bool prioritySucceeds)
    {
        using Process process = Process.GetCurrentProcess();
        var native = new RecordingProcessService { CoreSucceeds = coreSucceeds, PrioritySucceeds = prioritySucceeds };
        var optimization = new OptimizationService(native, () => new Dictionary<int, uint>());
        ProcessProfile profile = Profile(process);
        ProcessGroupSnapshot snapshot = Snapshot(process);

        OptimizationResult first = Apply(optimization, profile, snapshot);
        Assert.AreEqual(coreSucceeds || prioritySucceeds, first.Success,
            "The outcome must continue to report any completed operation as partial success.");
        Assert.AreEqual(coreSucceeds, first.CoreApplied);
        Assert.AreEqual(prioritySucceeds, first.PriorityApplied);

        native.CoreSucceeds = true;
        native.PrioritySucceeds = true;
        OptimizationResult retry = Apply(optimization, profile, snapshot);

        Assert.IsTrue(retry.CoreApplied, "Incomplete profiles must retry the core operation rather than hit the full-profile cache.");
        Assert.IsTrue(retry.PriorityApplied, "Incomplete profiles must retry the priority operation rather than hit the full-profile cache.");
        Assert.AreEqual(2, native.CoreCalls);
        Assert.AreEqual(2, native.PriorityCalls);
        Assert.AreEqual("SKIPPED_ALREADY_APPLIED", Apply(optimization, profile, snapshot).Message);
        Assert.AreEqual(2, native.CoreCalls, "Fully applied profiles must still be deduplicated.");
        Assert.AreEqual(2, native.PriorityCalls);
    }

    [DataTestMethod]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(false, false)]
    public void FailedForcedApplication_InvalidatesPreviousCompleteSignature(bool coreSucceeds, bool prioritySucceeds)
    {
        using Process process = Process.GetCurrentProcess();
        var native = new RecordingProcessService();
        var optimization = new OptimizationService(native, () => new Dictionary<int, uint>());
        ProcessProfile profile = Profile(process);
        ProcessGroupSnapshot snapshot = Snapshot(process);
        Assert.IsTrue(Apply(optimization, profile, snapshot).Success);

        native.CoreSucceeds = coreSucceeds;
        native.PrioritySucceeds = prioritySucceeds;
        OptimizationResult forced = Apply(optimization, profile, snapshot, force: true);
        Assert.AreEqual(coreSucceeds, forced.CoreApplied);
        Assert.AreEqual(prioritySucceeds, forced.PriorityApplied);

        native.CoreSucceeds = true;
        native.PrioritySucceeds = true;
        OptimizationResult retry = Apply(optimization, profile, snapshot);
        Assert.IsTrue(retry.CoreApplied, "An incomplete forced attempt must invalidate the earlier complete cache entry.");
        Assert.IsTrue(retry.PriorityApplied);
        Assert.AreEqual(3, native.CoreCalls);
        Assert.AreEqual(3, native.PriorityCalls);
    }

    [DataTestMethod]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public void CompleteSingleOperation_IsCachedWithoutRequiringDisabledOperation(bool applyCore, bool applyPriority)
    {
        using Process process = Process.GetCurrentProcess();
        var native = new RecordingProcessService();
        var optimization = new OptimizationService(native, () => new Dictionary<int, uint>());
        ProcessProfile profile = Profile(process);
        profile.ApplyCoreOptimization = applyCore;
        profile.ApplyPriority = applyPriority;
        ProcessGroupSnapshot snapshot = Snapshot(process);

        Assert.IsTrue(Apply(optimization, profile, snapshot).Success);
        Assert.AreEqual("SKIPPED_ALREADY_APPLIED", Apply(optimization, profile, snapshot).Message);
        Assert.AreEqual(applyCore ? 1 : 0, native.CoreCalls);
        Assert.AreEqual(applyPriority ? 1 : 0, native.PriorityCalls);
    }

    private static ProcessProfile Profile(Process process) => new()
    {
        ProcessName = process.ProcessName,
        IsEnabled = true,
        ApplyCoreOptimization = true,
        ApplyPriority = true,
        AffinityMask = 1,
        OptimizationMode = OptimizationMode.Affinity,
        Priority = "Normal"
    };

    private static ProcessGroupSnapshot Snapshot(Process process) => new()
    {
        Name = process.ProcessName,
        Instances = [new ProcessInstanceKey(process.Id, process.StartTime.ToUniversalTime())]
    };

    private static OptimizationResult Apply(OptimizationService service, ProcessProfile profile, ProcessGroupSnapshot snapshot, bool force = false) =>
        service.ApplyProfilesForSnapshots([profile], [snapshot], allowRealtimePriority: false, force).Results.Single();

    private sealed class RecordingProcessService : ProcessService
    {
        public bool CoreSucceeds { get; set; } = true;
        public bool PrioritySucceeds { get; set; } = true;
        public int CoreCalls { get; private set; }
        public int PriorityCalls { get; private set; }

        public override string ApplyCoreOptimization(int pid, long mask, OptimizationMode mode, Dictionary<int, uint> cpuSetMap)
        {
            CoreCalls++;
            return CoreSucceeds ? "OK_AFFINITY" : "ERR_SYNTHETIC";
        }

        public override bool SetPriority(int pid, ProcessPriorityClass priority)
        {
            PriorityCalls++;
            return PrioritySucceeds;
        }
    }
}
