using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Library;
using FrameHub.Core.Services.SessionOptimization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;

namespace FrameHub.Tests;

/// <summary>
/// REA F4 (P3): rotating <c>CancellationTokenSource</c> instances were canceled
/// but never disposed, abandoning a source on every process-list refresh and
/// every hardware poll. The fix disposes the superseded source before/after
/// replacement. Token polling on the old instance stays valid after Dispose;
/// only the source object itself becomes unusable, which is exactly what these
/// tests assert (deterministic, no timing: rotation is synchronous, observation
/// is <c>ObjectDisposedException</c> vs silent success).
/// <para>Pre-fix the superseded source is merely canceled, so canceling it again
/// succeeds and the negative control fails explicitly.</para>
/// </summary>
[TestClass]
public sealed class CancellationSourceHygieneTests
{
    private string _tempDir = null!;
    private string? _previousDataDir;
    private string? _previousLocalDataDir;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FrameHub_CtsHygieneTests_" + Guid.NewGuid().ToString("N"));
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
    public void SessionRefreshRotation_DisposesSupersededCancellationSource()
    {
        var localization = new LocalizationService(new SettingsService(Path.Combine(_tempDir, "settings.json")));
        using var runtime = new AppRuntimeService(Path.Combine(_tempDir, "runtime_settings.json"));
        using var coordinator = new SessionOptimizationCoordinator(
            stateService: new SessionStateService(Path.Combine(_tempDir, "active_session.json")),
            settingsService: new SessionOptimizationSettingsService(Path.Combine(_tempDir, "session_optimization.json")),
            libraryService: new LibraryService(Path.Combine(_tempDir, "library.json")));
        using var vm = new SessionOptimizationViewModel(localization, runtime, coordinator);

        MethodInfo startRefresh = typeof(SessionOptimizationViewModel).GetMethod(
            "StartProcessRefresh", BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo field = typeof(SessionOptimizationViewModel).GetField(
            "_processRefreshCancellation", BindingFlags.NonPublic | BindingFlags.Instance)!;

        startRefresh.Invoke(vm, [false]);
        var first = (CancellationTokenSource)field.GetValue(vm)!;
        startRefresh.Invoke(vm, [false]);
        var second = (CancellationTokenSource)field.GetValue(vm)!;

        Assert.AreNotSame(first, second, "Each rotation must install a fresh source.");
        Assert.ThrowsException<ObjectDisposedException>(
            () => first.Cancel(),
            "RED: the superseded refresh source was abandoned instead of disposed (REA F4).");
        second.Cancel();
    }

    [TestMethod]
    public async Task HardwarePollRotation_DisposesSupersededCancellationSource()
    {
        var localization = new LocalizationService(new SettingsService(Path.Combine(_tempDir, "settings.json")));
        using var runtime = new AppRuntimeService(Path.Combine(_tempDir, "runtime_settings.json"));
        runtime.SetHardwareMonitorEnabled(true);
        using var vm = new HardwareViewModel(localization, runtime);

        vm.IsMonitorEnabled = true;
        typeof(HardwareViewModel).GetField("_pageActive", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(vm, true);
        typeof(HardwareViewModel).GetField("_hardwareLease", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(vm, runtime.AcquireHardwareLease());

        MethodInfo poll = typeof(HardwareViewModel).GetMethod(
            "UpdateMetricsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo field = typeof(HardwareViewModel).GetField(
            "_pollCancellation", BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo inProgress = typeof(HardwareViewModel).GetField(
            "_pollInProgress", BindingFlags.NonPublic | BindingFlags.Instance)!;

        await ((Task)poll.Invoke(vm, null)!);
        var first = (CancellationTokenSource)field.GetValue(vm)!;
        await ((Task)poll.Invoke(vm, null)!);
        var second = (CancellationTokenSource)field.GetValue(vm)!;

        Assert.AreNotSame(first, second, "Each completed poll must rotate to a fresh source.");
        Assert.ThrowsException<ObjectDisposedException>(
            () => first.Cancel(),
            "RED: the superseded poll source was abandoned instead of disposed (REA F4).");
        second.Cancel();
        vm.Deactivate();
    }
}
