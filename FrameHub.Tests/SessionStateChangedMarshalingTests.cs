using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.Core.Models.SessionOptimization;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Library;
using FrameHub.Core.Services.SessionOptimization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Data;
using System.Windows.Threading;

namespace FrameHub.Tests;

/// <summary>
/// REA F1 (P1): <c>SessionOptimizationCoordinator.SessionStateChanged</c> fires
/// synchronously on the caller's thread — after <c>ConfigureAwait(false)</c>
/// continuations and on Companion Kestrel threads that is a ThreadPool thread.
/// The Session/Dashboard handlers must marshal to the creating (UI) dispatcher
/// instead of mutating WPF-bound state inline.
/// <para>
/// RED/GREEN design (deterministic, no sleeps, no timing): the whole case runs
/// on a dedicated STA thread whose dispatcher is deliberately NOT pumped while
/// a ThreadPool thread raises the event. Post-fix the mutation is deferred
/// (observable state unchanged, no exception) and applied only after the test
/// pumps the dispatcher. Pre-fix the mutation happens synchronously on the pool
/// thread, which additionally throws <c>NotSupportedException</c> out of the
/// bound <c>CollectionView</c> — exactly the production crash. The dispatcher
/// is captured at view-model construction, never via the process-global
/// <c>Application.Current</c> (proven one-shot per AppDomain), so the tests are
/// independent of test execution order.
/// </para>
/// </summary>
[TestClass]
public sealed class SessionStateChangedMarshalingTests
{
    private string _tempDir = null!;
    private string? _previousDataDir;
    private string? _previousLocalDataDir;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FrameHub_MarshalTests_" + Guid.NewGuid().ToString("N"));
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
    public void SessionHandler_RaisedFromThreadPool_MarshalsToCreatingDispatcher()
    {
        RunOnSta(() =>
        {
            using var coordinator = CreateCoordinator();
            var localization = new LocalizationService(new SettingsService(Path.Combine(_tempDir, "settings.json")));
            using var runtime = new AppRuntimeService(Path.Combine(_tempDir, "runtime_settings.json"));
            using var vm = new SessionOptimizationViewModel(localization, runtime, coordinator);

            // Bind a real CollectionView on the STA thread: any inline mutation from
            // the pool thread throws deterministically (the production F1 crash).
            var view = CollectionViewSource.GetDefaultView(vm.SuspendedProcesses);
            Assert.IsNotNull(view);
            Assert.AreEqual(0, vm.SuspendedProcesses.Count);

            ActiveSessionState state = CreateActiveSessionWithOneSuspended();
            Exception? poolError = RaiseActiveSessionFromThreadPool(coordinator, state);
            Assert.IsNull(poolError, "Handler must not throw when raised off the UI thread: " + poolError);

            // Dispatcher was never pumped: a marshaled update must still be pending.
            Assert.AreEqual(0, vm.SuspendedProcesses.Count, "Session update must be deferred to the creating dispatcher, not applied inline on the pool thread.");
            Assert.IsFalse(vm.IsSessionActive, "Session flag must be deferred to the creating dispatcher.");

            PumpDispatcher();

            Assert.IsTrue(vm.IsSessionActive, "Session flag must be applied after pumping the creating dispatcher.");
            Assert.AreEqual(1, vm.SuspendedProcesses.Count, "Suspended list must be rebuilt after pumping the creating dispatcher.");
        });
    }

    [TestMethod]
    public void DashboardHandler_RaisedFromThreadPool_MarshalsToCreatingDispatcher()
    {
        RunOnSta(() =>
        {
            using var coordinator = CreateCoordinator();
            var localization = new LocalizationService(new SettingsService(Path.Combine(_tempDir, "settings.json")));
            using var runtime = new AppRuntimeService(Path.Combine(_tempDir, "runtime_settings.json"));
            using var vm = new DashboardViewModel(localization, runtime);
            using var sessionVm = new SessionOptimizationViewModel(localization, runtime, coordinator);

            ActiveSessionState state = CreateActiveSessionWithOneSuspended();
            Exception? poolError = RaiseActiveSessionFromThreadPool(runtime.SessionOptimizationCoordinator, state);
            Assert.IsNull(poolError, "Handler must not throw when raised off the UI thread: " + poolError);

            Assert.IsFalse(vm.IsGamingModeActive, "Gaming flag must be deferred to the creating dispatcher.");

            PumpDispatcher();

            Assert.IsTrue(vm.IsGamingModeActive, "Gaming flag must be applied after pumping the creating dispatcher.");
        });
    }

    private SessionOptimizationCoordinator CreateCoordinator() => new(
        stateService: new SessionStateService(Path.Combine(_tempDir, "active_session.json")),
        settingsService: new SessionOptimizationSettingsService(Path.Combine(_tempDir, "session_optimization.json")),
        libraryService: new LibraryService(Path.Combine(_tempDir, "library.json")));

    private static ActiveSessionState CreateActiveSessionWithOneSuspended() => new()
    {
        IsActive = true,
        Trigger = "Manual",
        GameId = "game-1",
        GameName = "Game One",
        SuspendedProcesses =
        [
            new SuspendedProcessRecord
            {
                ProcessId = 4242,
                ProcessName = "background.exe",
                ProcessStartTimeUtc = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc),
                RuleId = "test-rule",
                RuleName = "Test rule"
            }
        ]
    };

    /// <summary>
    /// Invokes the coordinator's private ActiveSession setter from a ThreadPool
    /// thread, reproducing the post-ConfigureAwait(false) / Companion Kestrel path.
    /// </summary>
    private static Exception? RaiseActiveSessionFromThreadPool(SessionOptimizationCoordinator coordinator, ActiveSessionState state)
    {
        MethodInfo setter = typeof(SessionOptimizationCoordinator)
            .GetProperty("ActiveSession", BindingFlags.Public | BindingFlags.Instance)!
            .GetSetMethod(true)!;

        Exception? poolError = null;
        var raised = new ManualResetEventSlim(false);
        Task.Run(() =>
        {
            try
            {
                setter.Invoke(coordinator, [state]);
            }
            catch (Exception ex)
            {
                poolError = ex;
            }
            finally
            {
                raised.Set();
            }
        });

        Assert.IsTrue(raised.Wait(TimeSpan.FromSeconds(10)), "ThreadPool raise timed out.");
        return poolError;
    }

    private static void PumpDispatcher()
    {
        // Processes everything queued above Background priority, including the
        // Normal-priority BeginInvoke posted by the marshaling fix.
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }

    private static void RunOnSta(Action testBody)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                testBody();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "STA marshaling case timed out.");
        if (failure is not null)
        {
            Assert.Fail(failure.ToString());
        }
    }
}
