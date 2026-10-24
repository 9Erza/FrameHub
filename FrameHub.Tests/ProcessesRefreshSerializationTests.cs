using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;

namespace FrameHub.Tests;

/// <summary>
/// REA F3 (P2): <c>ProcessesViewModel.RefreshAsync(force: true)</c> used to bypass
/// the <c>_isRefreshing</c> guard, so a manual refresh could overlap the timer
/// tick and corrupt the WPF-bound <c>Processes</c> collection with two interleaved
/// <c>UpdateRows</c> passes. The fix serializes refreshes through a gate and
/// coalesces contenders into one follow-up pass (force = priority, not bypass).
/// <para>
/// Deterministic red/green without timing: the test holds the gate externally
/// (same white-box reflection precedent as
/// <c>SessionOptimizationCoordinatorTests.ConcurrencyGate_RejectsConcurrentOperations</c>),
/// issues a forced refresh, and asserts it coalesced (returned without scanning,
/// follow-up pending) instead of overlapping. Pre-fix the gate field does not
/// exist, so the negative control fails explicitly.
/// </para>
/// </summary>
[TestClass]
public sealed class ProcessesRefreshSerializationTests
{
    private string _tempDir = null!;
    private string? _previousDataDir;
    private string? _previousLocalDataDir;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FrameHub_ProcRefreshTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _previousDataDir = Environment.GetEnvironmentVariable(AppPaths.DataDirectoryVariable);
        _previousLocalDataDir = Environment.GetEnvironmentVariable(AppPaths.LocalDataDirectoryVariable);
        Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, _tempDir);
        Environment.SetEnvironmentVariable(AppPaths.LocalDataDirectoryVariable, _tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, _previousDataDir);
        Environment.SetEnvironmentVariable(AppPaths.LocalDataDirectoryVariable, _previousLocalDataDir);
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    [TestMethod]
    public async Task RefreshAsync_ForceWhileRefreshRunning_CoalescesInsteadOfOverlapping()
    {
        var localization = new LocalizationService(new SettingsService(Path.Combine(_tempDir, "settings.json")));
        using var runtime = new AppRuntimeService(Path.Combine(_tempDir, "runtime_settings.json"));
        using var vm = new ProcessesViewModel(localization, runtime);

        FieldInfo? gateField = typeof(ProcessesViewModel).GetField("_refreshGate", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(gateField, "RED: no serialization gate — force refreshes can still overlap the timer tick (REA F3).");
        var gate = (SemaphoreSlim)gateField.GetValue(vm)!;
        FieldInfo pendingField = typeof(ProcessesViewModel).GetField("_refreshPending", BindingFlags.NonPublic | BindingFlags.Instance)!;

        // Simulate a refresh already in progress by holding the gate externally.
        await gate.WaitAsync();
        try
        {
            Task forced = vm.RefreshAsync(force: true);
            Task timeout = Task.Delay(TimeSpan.FromSeconds(10));
            Assert.AreEqual(Task.WhenAny(forced, timeout).GetAwaiter().GetResult(), forced,
                "A coalesced refresh must return promptly instead of blocking on the running one.");

            Assert.IsTrue((bool)pendingField.GetValue(vm)!, "A refresh request during a running pass must schedule one follow-up pass.");
        }
        finally
        {
            gate.Release();
        }

        // The scheduled follow-up pass runs on the next call and clears the flag.
        await vm.RefreshAsync(force: true);
        Assert.IsFalse((bool)pendingField.GetValue(vm)!, "The follow-up pass must clear the pending flag.");
    }

    [TestMethod]
    public async Task RefreshAsync_ConcurrentForceCalls_DoNotThrow()
    {
        var localization = new LocalizationService(new SettingsService(Path.Combine(_tempDir, "settings.json")));
        using var runtime = new AppRuntimeService(Path.Combine(_tempDir, "runtime_settings.json"));
        using var vm = new ProcessesViewModel(localization, runtime);

        var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            try
            {
                await vm.RefreshAsync(force: true);
            }
            catch (Exception ex)
            {
                errors.Enqueue(ex);
            }
        })).ToArray();
        await Task.WhenAll(tasks);

        Assert.AreEqual(0, errors.Count, "Concurrent forced refreshes must serialize without exceptions: " + errors.FirstOrDefault());
    }
}
