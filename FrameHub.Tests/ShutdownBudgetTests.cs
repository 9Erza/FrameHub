using FrameHub.App.Services;
using FrameHub.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;
using System.Reflection;

namespace FrameHub.Tests;

/// <summary>
/// REA F2 (P2): the 8 s window shutdown deadline was not enforced end-to-end —
/// <c>AppRuntimeService.Dispose</c> blocked on the session coordinator drain with
/// its 10 s default plus companion stops, so background disposal could outlive
/// the deadline unboundedly (fail-safe, but the "8 s" was not real). The fix
/// threads the remaining budget through <c>Shell/MainWindow → AppRuntimeService
/// → SessionOptimizationCoordinator.ShutdownAsync(timeout:)</c>.
/// <para>
/// Deterministic red/green without timing races: an operation is held open via
/// the white-box <c>_activeOperations</c> counter (same reflection precedent as
/// <c>SessionOptimizationCoordinatorTests.ConcurrencyGate_RejectsConcurrentOperations</c>),
/// so the drain can only complete by timeout. Post-fix the 500 ms budget is
/// honored (returns <c>false</c> far below the old 10 s default); pre-fix the
/// new <c>ShutdownAsync(budget)</c> entry point does not exist at all (CS error),
/// and the old <c>Dispose</c> path blocks ~10 s+ on the coordinator default.
/// </para>
/// </summary>
[TestClass]
public sealed class ShutdownBudgetTests
{
    private string _tempDir = null!;
    private string? _previousDataDir;
    private string? _previousLocalDataDir;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FrameHub_ShutdownTests_" + Guid.NewGuid().ToString("N"));
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
    public async Task ShutdownAsync_WithHeldSessionOperation_ReturnsWithinBudget()
    {
        using var runtime = new AppRuntimeService(Path.Combine(_tempDir, "runtime_settings.json"));
        HoldCoordinatorOperation(runtime.SessionOptimizationCoordinator);

        var stopwatch = Stopwatch.StartNew();
        bool drained = await runtime.ShutdownAsync(TimeSpan.FromMilliseconds(500));
        stopwatch.Stop();

        Assert.IsFalse(drained, "A stuck session operation must surface as an explicit timeout, not a silent hang.");
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(8),
            $"The 500 ms budget must be honored (took {stopwatch.Elapsed.TotalSeconds:F1} s; the old default alone was 10 s).");

        ReleaseCoordinatorOperation(runtime.SessionOptimizationCoordinator);
    }

    [TestMethod]
    public async Task ShutdownAsync_WithoutContention_DrainsCleanly()
    {
        using var runtime = new AppRuntimeService(Path.Combine(_tempDir, "runtime_settings.json"));

        var stopwatch = Stopwatch.StartNew();
        bool drained = await runtime.ShutdownAsync(TimeSpan.FromSeconds(30));
        stopwatch.Stop();

        Assert.IsTrue(drained, "An idle runtime must shut down cleanly.");
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"Idle shutdown must be fast (took {stopwatch.Elapsed.TotalSeconds:F1} s).");
    }

    [TestMethod]
    public void Dispose_WithHeldSessionOperation_CompletesWithinDefaultEnvelope()
    {
        using var runtime = new AppRuntimeService(Path.Combine(_tempDir, "runtime_settings.json"));
        HoldCoordinatorOperation(runtime.SessionOptimizationCoordinator);

        var stopwatch = Stopwatch.StartNew();
        runtime.Dispose();
        stopwatch.Stop();

        // Hang-guard: Dispose must always return (fail-safe drain), governed by
        // the 8 s default envelope instead of the old ~12 s unbounded chain.
        // QA P3.7: bound (10 s) distinguishes the 8 s envelope from the old
        // ~12 s behavior; the previous 20 s could not tell them apart.
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"Dispose must return promptly even with a stuck operation (took {stopwatch.Elapsed.TotalSeconds:F1} s).");

        ReleaseCoordinatorOperation(runtime.SessionOptimizationCoordinator);
    }

    [TestMethod]
    public async Task CoordinatorShutdownAsync_TimeoutParameter_IsHonoredUnderHeldOperation()
    {
        using var coordinator = new SessionOptimizationCoordinator(
            stateService: new Core.Services.SessionOptimization.SessionStateService(Path.Combine(_tempDir, "active_session.json")),
            settingsService: new Core.Services.SessionOptimization.SessionOptimizationSettingsService(Path.Combine(_tempDir, "session_optimization.json")));
        HoldCoordinatorOperation(coordinator);

        var stopwatch = Stopwatch.StartNew();
        bool drained = await coordinator.ShutdownAsync(TimeSpan.FromMilliseconds(300));
        stopwatch.Stop();

        Assert.IsFalse(drained);
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Coordinator timeout must be honored (took {stopwatch.Elapsed.TotalSeconds:F1} s).");

        ReleaseCoordinatorOperation(coordinator);
        Assert.IsTrue(await coordinator.ShutdownAsync(TimeSpan.FromSeconds(5)));
    }

    private static void HoldCoordinatorOperation(SessionOptimizationCoordinator coordinator)
    {
        FieldInfo activeOperations = typeof(SessionOptimizationCoordinator).GetField(
            "_activeOperations", BindingFlags.NonPublic | BindingFlags.Instance)!;
        activeOperations.SetValue(coordinator, 1);
    }

    private static void ReleaseCoordinatorOperation(SessionOptimizationCoordinator coordinator)
    {
        MethodInfo exitOperation = typeof(SessionOptimizationCoordinator).GetMethod(
            "ExitOperation", BindingFlags.NonPublic | BindingFlags.Instance)!;
        exitOperation.Invoke(coordinator, null);
    }
}
