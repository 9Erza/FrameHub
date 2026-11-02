using FrameHub.App.Helpers;
using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

/// <summary>
/// Audit 2026-11 (RAM/retention): view-models subscribed to longer-lived
/// runtime/coordinator/collection events without ever unsubscribing, so the
/// VMs were pinned for the process lifetime. These tests pin the retention
/// contract: after Dispose the VMs must be collectable while the runtime
/// stays alive, Dispose must be idempotent, and the shell must detach every
/// subscription it creates.
/// </summary>
[TestClass]
public sealed class LifecycleDetachTests
{
    private string _tempDirectory = null!;
    private string? _previousDataDir;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.LifecycleDetach", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _previousDataDir = Environment.GetEnvironmentVariable("FRAMEHUB_DATA_DIR");
        Environment.SetEnvironmentVariable("FRAMEHUB_DATA_DIR", _tempDirectory);
    }

    [TestCleanup]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("FRAMEHUB_DATA_DIR", _previousDataDir);
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { }
        }
    }

    [TestMethod]
    public void LogsViewModel_IsCollectableAfterDisposeWhileRuntimeAlive()
    {
        RunOnSta(() =>
        {
            using var runtime = CreateRuntime();
            var localization = new LocalizationService(runtime.SettingsService);
            WeakReference weak = CreateAndDisposeLogsVm(localization, runtime);
            Collect();
            Assert.IsFalse(weak.IsAlive, "LogsViewModel must detach Activity.CollectionChanged on Dispose so it can be collected while the runtime lives on.");
        });
    }

    [TestMethod]
    public void ProfilesViewModel_IsCollectableAfterDisposeWhileRuntimeAlive()
    {
        RunOnSta(() =>
        {
            using var runtime = CreateRuntime();
            var localization = new LocalizationService(runtime.SettingsService);
            WeakReference weak = CreateAndDisposeProfilesVm(localization, runtime);
            Collect();
            Assert.IsFalse(weak.IsAlive, "ProfilesViewModel must detach ProfilesChanged on Dispose so it can be collected while the runtime lives on.");
        });
    }

    [TestMethod]
    public void ShellViewModel_DisposeIsIdempotent()
    {
        RunOnSta(() =>
        {
            var shell = new ShellViewModel();
            shell.Dispose();
            shell.Dispose();
        });
    }

    [TestMethod]
    public void ShellViewModel_DetachesEverySubscriptionItCreates()
    {
        string? repoRoot = FindRepoRoot();
        Assert.IsNotNull(repoRoot, "Repository root not found.");
        string code = File.ReadAllText(Path.Combine(repoRoot!, "FrameHub.App", "ViewModels", "ShellViewModel.cs"));
        string[] handlers =
        [
            "OnLanguageChanged",
            "OnWatcherStateChanged",
            "OnProfilesChanged",
            "OnSessionStateChanged",
            "OnBenchmarkPropertyChanged",
            "OnLibraryItemsChanged",
            "OnLibraryBenchmarkRequested",
            "OnCreateFirstProfileRequested",
            "OnRuntimeStateChanged",
            "OnBenchmarkUserNotification",
        ];
        foreach (string handler in handlers)
        {
            StringAssert.Contains(code, "-= " + handler, $"ShellViewModel must unsubscribe {handler} on Dispose.");
        }

        StringAssert.Contains(code, "_profilesViewModel.Dispose()", "ShellViewModel must dispose the Profiles VM.");
        StringAssert.Contains(code, "_logsViewModel.Dispose()", "ShellViewModel must dispose the Logs VM.");
        StringAssert.Contains(code, "_settingsViewModel.Dispose()", "ShellViewModel must dispose the Settings VM.");
    }

    [TestMethod]
    public void DnsDiagnosticsPanel_DisposeIsIdempotent()
    {
        RunOnSta(() =>
        {
            using var runtime = CreateRuntime();
            var localization = new LocalizationService(runtime.SettingsService);
            var panel = new DnsDiagnosticsPanelViewModel(localization);
            panel.Dispose();
            panel.Dispose();
        });
    }

    [TestMethod]
    public void GameIconCache_MissingPathReturnsNullRepeatedly()
    {
        string missing = Path.Combine(_tempDirectory, "definitely-missing-icon.exe");
        Assert.IsNull(GameIconCache.GetOrLoad(missing, 28));
        Assert.IsNull(GameIconCache.GetOrLoad(missing, 28));
        Assert.IsNull(GameIconCache.GetOrLoad(null, 28));
        Assert.IsNull(GameIconCache.GetOrLoad(string.Empty, 32));
    }

    [TestMethod]
    public void GameIconCache_ReturnsSharedFrozenInstanceForSameFile()
    {
        RunOnSta(() =>
        {
            string? systemExe = FindSystemExe();
            if (systemExe is null) Assert.Inconclusive("No system executable available for icon decoding on this host.");
            var first = GameIconCache.GetOrLoad(systemExe, 28);
            var second = GameIconCache.GetOrLoad(systemExe, 28);
            if (first is null || second is null) Assert.Inconclusive("Icon decoding unavailable on this host.");
            Assert.AreSame(first, second, "Same path/size must return the cached instance, not re-decode.");
            Assert.IsTrue(first.IsFrozen, "Cached icons must be frozen so bindings can share them across threads.");
        });
    }

    private static WeakReference CreateAndDisposeLogsVm(LocalizationService localization, AppRuntimeService runtime)
    {
        var vm = new LogsViewModel(localization, runtime);
        var weak = new WeakReference(vm);
        vm.Dispose();
        return weak;
    }

    private static WeakReference CreateAndDisposeProfilesVm(LocalizationService localization, AppRuntimeService runtime)
    {
        var vm = new ProfilesViewModel(localization, runtime);
        var weak = new WeakReference(vm);
        vm.Dispose();
        return weak;
    }

    private static void Collect()
    {
        GC.Collect(2, GCCollectionMode.Forced, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true);
    }

    private static string? FindSystemExe()
    {
        string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        foreach (string name in new[] { "notepad.exe", "calc.exe", "mspaint.exe" })
        {
            string candidate = Path.Combine(system, name);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    private static string? FindRepoRoot()
    {
        string? directory = Path.GetDirectoryName(typeof(LifecycleDetachTests).Assembly.Location);
        while (directory != null && !Directory.Exists(Path.Combine(directory, "FrameHub.App")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        return directory;
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(60)), "STA test host timed out.");
        if (failure is not null) Assert.Fail(failure.ToString());
    }

    private AppRuntimeService CreateRuntime()
    {
        string settingsPath = Path.Combine(_tempDirectory, $"settings-{Guid.NewGuid():N}.json");
        return new AppRuntimeService(settingsPath);
    }
}
