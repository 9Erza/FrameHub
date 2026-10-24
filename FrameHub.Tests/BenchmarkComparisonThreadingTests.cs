using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.App.ViewModels.Benchmark;
using FrameHub.Core.Models;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Benchmarking;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace FrameHub.Tests;

/// <summary>
/// P2-A (ComparisonRebuild race) + P2-B (raw-frames.json Cleanup flake) gates.
/// All stores and settings live under an isolated temp directory
/// (<c>FRAMEHUB_DATA_DIR</c> override + explicit storage paths); nothing touches
/// the real %APPDATA%\FrameHub. No sleeps, no retries, no loosened assertions:
/// synchronization is barrier/event/task-handshake based only.
/// </summary>
[TestClass]
public sealed class BenchmarkComparisonThreadingTests
{
    private string _tempDir = null!;
    private LocalizationService _localization = null!;

    [TestInitialize]
    public void Setup()
    {
        // No FRAMEHUB_DATA_DIR override here on purpose: the process-wide
        // LoggerService singleton opens (and holds) its log file in whatever
        // directory is current at first touch, which would collide with the
        // isolated store delete. All paths below are explicit temp paths, so
        // this class neither reads nor writes the real %APPDATA%\FrameHub.
        _tempDir = Path.Combine(Path.GetTempPath(), "FrameHub_ComparisonThreadingTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _localization = new LocalizationService(new SettingsService(Path.Combine(_tempDir, "settings.json")));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }

    /// <summary>
    /// P2-A gate. Forces genuine overlap of comparison rebuilds: every round all
    /// workers enter the rebuild window at the same barrier tick, half through
    /// the public ComparisonA/B setters and half through RefreshHistoryAsync
    /// (the exact CI interleaving: ThreadPool refresh vs test/UI-thread
    /// selection). Without the <c>_comparisonGate</c> serialization this
    /// corrupts the ObservableCollection lists (ArgumentOutOfRangeException out
    /// of List.Insert, the CI PR #6 signature); with the fix it is clean and
    /// the final rebuild converges to the exact expected rows.
    /// RED on the narrow lock-removal revert, GREEN with the fix.
    /// </summary>
    [TestMethod]
    public void ComparisonRebuild_BarrierInterleavedSettersAndRefresh_DoNotCorruptRows()
    {
        var storage = new BenchmarkStorageService(Path.Combine(_tempDir, "bench"));
        using var vm = new BenchmarkViewModel(
            _localization,
            new ComparisonRuntime(),
            storage: storage,
            engineProbe: () => (false, null, null));
        List<BenchmarkHistoryEntry> entries = Enumerable.Range(0, 8).Select(i => CompletedEntry(i % 2)).ToList();
        vm.ComparisonA = Item(entries[0]);
        vm.ComparisonB = Item(entries[1]);

        int workers = Math.Max(8, Environment.ProcessorCount);
        using var barrier = new Barrier(workers);
        var errors = new ConcurrentQueue<Exception>();
        Parallel.For(0, workers, worker =>
        {
            try
            {
                for (int round = 0; round < 200; round++)
                {
                    if (!barrier.SignalAndWait(TimeSpan.FromSeconds(30)))
                        throw new TimeoutException($"Barrier round {round} timed out on worker {worker}.");
                    if (worker % 2 == 0)
                    {
                        vm.ComparisonA = Item(entries[(round + worker) % entries.Count]);
                        vm.ComparisonB = Item(entries[(round + worker + 1) % entries.Count]);
                    }
                    else
                    {
                        vm.RefreshHistoryAsync().GetAwaiter().GetResult();
                    }
                }
            }
            catch (Exception ex) { errors.Enqueue(ex); }
        });

        Assert.AreEqual(0, errors.Count,
            "Concurrent comparison rebuilds must not throw. First: " + errors.FirstOrDefault()?.ToString());

        vm.ComparisonA = Item(entries[0]);
        vm.ComparisonB = Item(entries[1]);
        Assert.AreEqual(BenchmarkComparisonService.Compare(entries[0], entries[1]).Count, vm.ComparisonRows.Count,
            "Rebuilds must converge to the exact expected metric rows.");
        Assert.AreEqual(BenchmarkComparisonService.CompareEnvironments(entries[0], entries[1]).Count, vm.EnvironmentDifferenceRows.Count,
            "Rebuilds must converge to the exact expected environment rows.");
    }

    /// <summary>
    /// P2-B gate. An explicitly awaited refresh auto-selects the completed
    /// session (deterministic on both variants — no handshake involved), then
    /// the fire-and-forget result load behind the <c>SelectedHistory</c> setter
    /// is registered synchronously on the calling thread, so pending work is
    /// observable deterministically (no timing): right after the setter returns
    /// the load cannot have finished yet. The handshake then drains it, the
    /// chart actually loads, and deleting the store afterwards is clean — the
    /// exact ordering the old <c>TestCleanup</c> flake was missing.
    /// RED on the narrow tracking-removal revert (nothing is ever pending),
    /// GREEN with the fix.
    /// </summary>
    [TestMethod]
    public async Task BackgroundWork_SetterTriggeredResultLoad_IsTrackedAndDrainsBeforeDelete()
    {
        var storage = new BenchmarkStorageService(Path.Combine(_tempDir, "bench"));
        SaveCompleted(storage, "game");
        using var vm = new BenchmarkViewModel(
            _localization,
            new ComparisonRuntime(),
            storage: storage,
            engineProbe: () => (false, null, null));

        // Awaited refresh: deterministic on both variants (does not depend on
        // the handshake), so the RED below cannot be a scheduling accident.
        await vm.RefreshHistoryAsync();
        Assert.IsNotNull(vm.SelectedHistory, "Refresh must auto-select the completed session.");

        // Act: re-trigger the fire-and-forget result load. Registration happens
        // synchronously on this thread, so a missing load here cannot have
        // "just finished already" — HasPending is a deterministic observation.
        vm.SelectedHistory = new BenchmarkHistoryItemViewModel(
            vm.SelectedHistory.Entry, _localization);
        Assert.IsTrue(vm.HasPendingBackgroundWork,
            "RED: setter-triggered result load is not tracked — a store delete could race the live raw-frames.json reader (P2-B).");

        await vm.WaitForBackgroundWorkAsync(TimeSpan.FromSeconds(30));
        Assert.IsFalse(vm.HasPendingBackgroundWork, "Handshake must drain the result load.");
        Assert.IsNotNull(vm.CurrentResult, "Drained result load must have produced chart data (anti-vacuous).");

        Directory.Delete(storage.RootDirectory, true);
        Assert.IsFalse(Directory.Exists(storage.RootDirectory), "Store delete after the handshake must be clean.");
    }

    /// <summary>
    /// P2-B stress proof: a storm of concurrent refreshes (each auto-selecting
    /// and cascading a raw-frames.json result load) drains through the
    /// handshake and the store deletes cleanly. Companion to the deterministic
    /// gate above; GREEN with the fix.
    /// </summary>
    [TestMethod]
    public async Task BackgroundWork_RefreshStorm_DrainsBeforeStoreDelete()
    {
        var storage = new BenchmarkStorageService(Path.Combine(_tempDir, "bench"));
        SaveCompleted(storage, "game");
        using var vm = new BenchmarkViewModel(
            _localization,
            new ComparisonRuntime(),
            storage: storage,
            engineProbe: () => (false, null, null));
        await vm.WaitForBackgroundWorkAsync(TimeSpan.FromSeconds(30));

        var errors = new ConcurrentQueue<Exception>();
        Parallel.For(0, 8, _ =>
        {
            try
            {
                for (int i = 0; i < 25; i++) vm.RefreshHistoryAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex) { errors.Enqueue(ex); }
        });
        Assert.AreEqual(0, errors.Count, "Refresh storm must not throw. First: " + errors.FirstOrDefault()?.ToString());

        await vm.WaitForBackgroundWorkAsync(TimeSpan.FromSeconds(30));
        Assert.IsFalse(vm.HasPendingBackgroundWork);
        Directory.Delete(storage.RootDirectory, true);
        Assert.IsFalse(Directory.Exists(storage.RootDirectory));
    }

    /// <summary>
    /// P2-A Dispatcher/binding verification (characterization, not a red/green
    /// gate): the production scenario — every collection mutation dispatched
    /// onto the single STA UI thread through the dispatcher queue, with live
    /// WPF CollectionViews bound — stays consistent under interleaved
    /// selection + history refresh traffic. Documents that the UI-thread
    /// origination contract (all production callers) is what keeps bindings
    /// safe; the lock additionally serializes non-UI contexts (CI/tests).
    /// </summary>
    [TestMethod]
    public async Task ComparisonUiBinding_DispatcherDrivenInterleaving_StaysConsistent()
    {
        var rig = StaRig.Create(Path.Combine(_tempDir, "bench"), _localization);
        try
        {
            await rig.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await rig.InvokeAsync(() => rig.Vm.WaitForBackgroundWorkAsync(TimeSpan.FromSeconds(30)));

            List<BenchmarkHistoryEntry> entries = Enumerable.Range(0, 8).Select(i => CompletedEntry(i % 2)).ToList();
            for (int i = 0; i < 25; i++)
            {
                int capture = i;
                await rig.InvokeAsync(() =>
                {
                    rig.Vm.ComparisonA = Item(entries[capture % entries.Count]);
                    rig.Vm.ComparisonB = Item(entries[(capture + 1) % entries.Count]);
                });
                if (capture % 5 == 0) await rig.InvokeAsync(() => rig.Vm.RefreshHistoryAsync());
            }
            await rig.InvokeAsync(() => rig.Vm.WaitForBackgroundWorkAsync(TimeSpan.FromSeconds(30)));
            await rig.InvokeAsync(() =>
            {
                rig.Vm.ComparisonA = Item(entries[0]);
                rig.Vm.ComparisonB = Item(entries[1]);
            });

            (int rows, int envRows) = await rig.InvokeAsync(() => (rig.Vm.ComparisonRows.Count, rig.Vm.EnvironmentDifferenceRows.Count));
            Assert.AreEqual(BenchmarkComparisonService.Compare(entries[0], entries[1]).Count, rows);
            Assert.AreEqual(BenchmarkComparisonService.CompareEnvironments(entries[0], entries[1]).Count, envRows);
        }
        finally { rig.Dispose(); }
    }

    /// <summary>
    /// P2-A boundary tripwire (characterization): proves this rig CAN observe
    /// thread-affinity violations (anti-vacuous control for the test above) and
    /// documents the verified boundary — an off-UI-thread mutation throws from
    /// the live CollectionView even with the <c>_comparisonGate</c> lock held
    /// correctly, because a lock is not a Dispatcher marshal. Production keeps
    /// this safe by originating all mutations on the UI thread (verified by
    /// inspection of every RebuildComparison/History caller); any future
    /// marshaling change must revisit this contract.
    /// </summary>
    [TestMethod]
    public async Task ComparisonUiBinding_BackgroundThreadMutation_ThrowsDespiteGate()
    {
        var rig = StaRig.Create(Path.Combine(_tempDir, "bench"), _localization);
        try
        {
            await rig.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await rig.InvokeAsync(() => rig.Vm.WaitForBackgroundWorkAsync(TimeSpan.FromSeconds(30)));

            List<BenchmarkHistoryEntry> entries = Enumerable.Range(0, 8).Select(i => CompletedEntry(i % 2)).ToList();
            await rig.InvokeAsync(() =>
            {
                rig.Vm.ComparisonA = Item(entries[0]);
                rig.Vm.ComparisonB = Item(entries[1]);
            });

            Exception? observed = null;
            try { rig.Vm.ComparisonB = Item(entries[2]); }
            catch (Exception ex) { observed = ex; }
            Assert.IsNotNull(observed, "Off-UI-thread mutation against a live view must throw (rig sensitivity control).");
            Assert.IsTrue(observed is NotSupportedException || observed is InvalidOperationException,
                $"Expected a WPF thread-affinity violation, got {observed.GetType().FullName}: {observed.Message}");

            await rig.InvokeAsync(() =>
            {
                rig.Vm.ComparisonA = Item(entries[0]);
                rig.Vm.ComparisonB = Item(entries[1]);
            });
            (int rows, int envRows) = await rig.InvokeAsync(() => (rig.Vm.ComparisonRows.Count, rig.Vm.EnvironmentDifferenceRows.Count));
            Assert.AreEqual(BenchmarkComparisonService.Compare(entries[0], entries[1]).Count, rows,
                "UI-thread rebuild after the rejected mutation must restore exact rows.");
            Assert.AreEqual(BenchmarkComparisonService.CompareEnvironments(entries[0], entries[1]).Count, envRows);
        }
        finally { rig.Dispose(); }
    }

    private static void SaveCompleted(BenchmarkStorageService storage, string gameId)
    {
        DateTime startUtc = DateTime.UtcNow;
        BenchmarkSession session = storage.CreateSession(
            new BenchmarkTarget { LibraryItemId = gameId, DisplayName = "Game", LibrarySource = "Manual" },
            new BenchmarkProcessIdentity { ProcessId = 123, ProcessName = "game", StartTimeUtc = startUtc },
            "0.7.0",
            startUtc);
        session.Metadata.Status = BenchmarkSessionStatus.Completed;
        session.Metadata.CaptureDurationSeconds = 30;
        session.Metadata.AnalyzedDurationSeconds = 30;
        storage.SaveSession(session);
        storage.SaveSummary(session, new BenchmarkSummary
        {
            SessionId = session.Metadata.SessionId,
            CaptureDurationSeconds = 30,
            AnalyzedDurationSeconds = 30,
            SelectedSwapChainAddress = "0x1",
            PrimaryPresentedMetrics = new BenchmarkMetricSet { ValidFrameCount = 1, AverageFps = 120 },
            Quality = new BenchmarkQualityResult { Level = BenchmarkQualityLevel.Valid }
        });
        File.WriteAllText(session.RawDataPath, JsonSerializer.Serialize(
            new[] { new BenchmarkFrameSample { ProcessId = 123, SwapChainAddress = "0x1", MsBetweenPresents = 10 } }));
    }

    private BenchmarkHistoryItemViewModel Item(BenchmarkHistoryEntry entry) => new(entry, _localization);

    private static BenchmarkHistoryEntry CompletedEntry(int variant) => new()
    {
        SessionDirectory = Guid.NewGuid().ToString("N"),
        Metadata = new BenchmarkSessionMetadata
        {
            SessionId = Guid.NewGuid(),
            FrameHubVersion = "0.7.0",
            Game = new BenchmarkTarget { LibraryItemId = "game", DisplayName = "Game", LibrarySource = "Manual" },
            Status = BenchmarkSessionStatus.Completed,
            Environment = new BenchmarkEnvironmentSnapshot
            {
                OsDescription = "Microsoft Windows 10.0.26100",
                OsBuild = "26100",
                CpuName = "AMD Ryzen 9 7950X",
                GpuName = "NVIDIA GeForce RTX 4080",
                GpuDriverVersion = variant == 0 ? "32.0.15.6094" : "33.0.15.5222",
                TotalMemoryBytes = 34_359_738_368UL,
                DisplayWidth = 2560,
                DisplayHeight = 1440,
                DisplayRefreshRateHz = 165
            }
        },
        Summary = new BenchmarkSummary { PrimaryPresentedMetrics = new BenchmarkMetricSet { AverageFps = 120 } }
    };

    private sealed class ComparisonRuntime : IBenchmarkRuntimeContext
    {
        public AppSettings Settings { get; } = new();
        public List<ProcessProfile> Profiles { get; } = [];
        public string? LastAppliedProfile => null;
        public IBenchmarkCaptureCoordinator BenchmarkCoordinator { get; } = new BenchmarkCaptureCoordinator();
        public void AddActivity(string message, string level = "Info") { }
    }

    /// <summary>
    /// Minimal STA rig: one thread with a pumped dispatcher (the "UI thread"),
    /// a VM created on it, and live WPF CollectionViews over the bound
    /// collections so thread-affinity violations become visible.
    /// </summary>
    private sealed class StaRig : IDisposable
    {
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Dispatcher Dispatcher { get; private set; } = null!;
        public BenchmarkViewModel Vm { get; private set; } = null!;
        private readonly Thread _thread;
        private readonly List<ListBox> _views = [];
        private bool _disposed;

        private StaRig(Thread thread) => _thread = thread;

        public static StaRig Create(string storeDir, LocalizationService localization)
        {
            StaRig? rig = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var r = rig!;
                    r.Dispatcher = Dispatcher.CurrentDispatcher;
                    SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(r.Dispatcher));
                    var storage = new BenchmarkStorageService(storeDir);
                    r.Vm = new BenchmarkViewModel(
                        localization,
                        new ComparisonRuntime(),
                        storage: storage,
                        engineProbe: () => (false, null, null));
                    var historyBox = new ListBox { ItemsSource = r.Vm.History };
                    var rowsBox = new ListBox { ItemsSource = r.Vm.ComparisonRows };
                    var envBox = new ListBox { ItemsSource = r.Vm.EnvironmentDifferenceRows };
                    _ = CollectionViewSource.GetDefaultView(r.Vm.History);
                    _ = CollectionViewSource.GetDefaultView(r.Vm.ComparisonRows);
                    _ = CollectionViewSource.GetDefaultView(r.Vm.EnvironmentDifferenceRows);
                    r._views.Add(historyBox); r._views.Add(rowsBox); r._views.Add(envBox);
                    r.Ready.TrySetResult();
                    Dispatcher.Run();
                }
                catch (Exception ex) { rig?.Ready.TrySetException(ex); }
            });
            rig = new StaRig(thread);
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return rig;
        }

        public Task InvokeAsync(Action action) => Dispatcher.InvokeAsync(action).Task;
        public Task InvokeAsync(Func<Task> func) => Dispatcher.InvokeAsync(func).Task.Unwrap();
        public Task<T> InvokeAsync<T>(Func<T> func) => Dispatcher.InvokeAsync(func).Task;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { Dispatcher.InvokeAsync(() => Vm.Dispose()); } catch { }
            try { Dispatcher.InvokeShutdown(); } catch { }
            _thread.Join(TimeSpan.FromSeconds(30));
        }
    }
}
