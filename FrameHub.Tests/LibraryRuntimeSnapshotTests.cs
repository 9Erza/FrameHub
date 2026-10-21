using System.Collections.Concurrent;
using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.App.ViewModels.Library;
using FrameHub.Core.Models;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

/// <summary>
/// Regression for Library runtime-state snapshot race:
/// RefreshRuntimeState must materialize Items on the UI thread before awaiting
/// the process snapshot. Otherwise items added while the snapshot is in flight
/// leak into the background enumeration (snapshot-isolation violation).
/// </summary>
[TestClass]
public sealed class LibraryRuntimeSnapshotTests
{
    private string _tempDirectory = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.LibrarySnapshotTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { }
        }
    }

    [TestMethod]
    public async Task Activate_ItemAddedDuringSnapshot_IsNotMarkedUntilNextActivate()
    {
        using var ui = new UiThread();
        var gate = new GateProvider();

        string libraryPath = Path.Combine(_tempDirectory, $"library-{Guid.NewGuid():N}.json");
        var libraryService = new LibraryService(libraryPath);
        var baseItem = new LibraryItem
        {
            Id = "base-1",
            DisplayName = "Base Game",
            Type = LibraryItemType.Game,
            ProcessName = "basegame",
            ExecutablePath = @"C:\Games\Base\game.exe"
        };
        libraryService.SaveItems(new[] { baseItem });

        string settingsPath = Path.Combine(_tempDirectory, $"settings-{Guid.NewGuid():N}.json");

        // Create runtime + VM on the single UI thread (WPF dispatcher equivalent).
        var host = await ui.RunAsync(() =>
        {
            var runtime = new AppRuntimeService(settingsPath);
            var localization = new LocalizationService(runtime.SettingsService);
            var scanner = new ProcessScannerService(new ProcessService(), gate);
            var launcher = new FakeLaunchService();
            var viewModel = new LibraryViewModel(localization, runtime, libraryService, scanner, launcher);
            return new Host(viewModel, runtime);
        });

        try
        {
            // 1. Let the constructor's initial refresh complete with no processes running.
            Assert.IsTrue(gate.Entered.Wait(TimeSpan.FromSeconds(5)), "Initial snapshot was not requested.");
            gate.CompleteWith(new List<ProcessObservation>());
            await SpinUntilAsync(
                async () => await ui.RunAsync(() => host.ViewModel.Items.Count == 1 && !host.ViewModel.Items[0].IsRunning),
                TimeSpan.FromSeconds(5),
                "Initial refresh did not settle.");

            // 2. Start a refresh that blocks inside GetSnapshotAsync, then add a running game.
            gate.Reset();
            await ui.RunAsync(() => host.ViewModel.Activate());
            Assert.IsTrue(gate.Entered.Wait(TimeSpan.FromSeconds(5)), "Refresh snapshot was not requested.");

            var lateItem = new LibraryItem
            {
                Id = "late-1",
                DisplayName = "Late Game",
                Type = LibraryItemType.Game,
                ProcessName = "lategame",
                ExecutablePath = @"C:\Games\Late\game.exe"
            };
            await ui.RunAsync(() =>
            {
                var lateVm = new LibraryItemViewModel(
                    lateItem,
                    GetLocalization(host),
                    () => Array.Empty<ProcessProfile>());
                host.ViewModel.Items.Add(lateVm);
            });

            // Snapshot observes the late game process as running.
            gate.CompleteWith(new List<ProcessObservation>
            {
                new ProcessObservation(7777, "lategame", @"C:\Games\Late\game.exe", DateTime.UtcNow)
            });

            // Give the in-flight refresh a bounded window to (incorrectly) mark the late item.
            await Task.Delay(500);

            bool lateRunningAfterFirstRefresh = await ui.RunAsync(() =>
                host.ViewModel.Items.First(x => x.Id == "late-1").IsRunning);

            Assert.IsFalse(
                lateRunningAfterFirstRefresh,
                "Snapshot isolation violated: item added while the snapshot was in flight must not be marked running by that same refresh; it becomes visible on the next Activate().");

            // 3. Next bounded refresh must pick the late game up (proves the process really matches).
            gate.Reset();
            await ui.RunAsync(() => host.ViewModel.Activate());
            Assert.IsTrue(gate.Entered.Wait(TimeSpan.FromSeconds(5)), "Second refresh snapshot was not requested.");
            gate.CompleteWith(new List<ProcessObservation>
            {
                new ProcessObservation(7777, "lategame", @"C:\Games\Late\game.exe", DateTime.UtcNow)
            });

            await SpinUntilAsync(
                async () => await ui.RunAsync(() =>
                    host.ViewModel.Items.First(x => x.Id == "late-1").IsRunning),
                TimeSpan.FromSeconds(5),
                "Second Activate() must mark the late game running.");
        }
        finally
        {
            await ui.RunAsync(() =>
            {
                host.ViewModel.Dispose();
                host.Runtime.Dispose();
            });
        }
    }

    private static LocalizationService GetLocalization(Host host)
    {
        // LibraryItemViewModel only needs localization for texts; reuse the VM's runtime settings.
        return new LocalizationService(host.Runtime.SettingsService);
    }

    private static async Task SpinUntilAsync(Func<Task<bool>> condition, TimeSpan timeout, string message)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition().ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(20).ConfigureAwait(false);
        }

        Assert.IsTrue(await condition().ConfigureAwait(false), message);
    }

    private sealed record Host(LibraryViewModel ViewModel, AppRuntimeService Runtime);

    private sealed class FakeLaunchService : IAppLibraryLaunchService
    {
        public LibraryLaunchResult Launch(LibraryItem? item) => LibraryLaunchResult.Ok();
    }

    private sealed class GateProvider : IProcessObservationSnapshotProvider
    {
        public TimeSpan TimeToLive => TimeSpan.Zero;

        public ManualResetEventSlim Entered { get; } = new(false);

        private volatile TaskCompletionSource<ProcessObservationSnapshot> _gate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ProcessObservationSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            Entered.Set();
            return _gate.Task;
        }

        public void CompleteWith(IReadOnlyList<ProcessObservation> processes)
        {
            var snapshot = new ProcessObservationSnapshot(1, DateTimeOffset.UtcNow, processes);
            _gate.TrySetResult(snapshot);
        }

        public void Reset()
        {
            Entered.Reset();
            Interlocked.Exchange(
                ref _gate,
                new TaskCompletionSource<ProcessObservationSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously));
        }
    }

    /// <summary>Minimal single-threaded UI equivalent: one STA pump thread + SynchronizationContext.</summary>
    private sealed class UiThread : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new();
        private readonly Thread _thread;
        private readonly SingleThreadContext _context;
        private bool _disposed;

        public UiThread()
        {
            _context = new SingleThreadContext(_queue);
            _thread = new Thread(() =>
            {
                SynchronizationContext.SetSynchronizationContext(_context);
                foreach (Action action in _queue.GetConsumingEnumerable())
                {
                    try
                    {
                        SynchronizationContext.SetSynchronizationContext(_context);
                        action();
                    }
                    catch
                    {
                        // Queued work reports via its own TaskCompletionSource.
                    }
                }
            });
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.IsBackground = true;
            _thread.Start();
        }

        public Task<T> RunAsync<T>(Func<T> func)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add(() =>
            {
                try
                {
                    SynchronizationContext.SetSynchronizationContext(_context);
                    tcs.SetResult(func());
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            return tcs.Task;
        }

        public Task RunAsync(Action action) =>
            RunAsync<object?>(() =>
            {
                action();
                return null;
            });

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _queue.CompleteAdding();
            _thread.Join(TimeSpan.FromSeconds(5));
            _queue.Dispose();
        }

        private sealed class SingleThreadContext : SynchronizationContext
        {
            private readonly BlockingCollection<Action> _queue;

            public SingleThreadContext(BlockingCollection<Action> queue)
            {
                _queue = queue;
            }

            public override void Post(SendOrPostCallback d, object? state)
            {
                _queue.Add(() => d(state));
            }

            public override void Send(SendOrPostCallback d, object? state)
            {
                if (SynchronizationContext.Current == this)
                {
                    d(state);
                    return;
                }

                using var done = new ManualResetEventSlim(false);
                Exception? error = null;
                _queue.Add(() =>
                {
                    try
                    {
                        d(state);
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                    finally
                    {
                        done.Set();
                    }
                });
                done.Wait();
                if (error != null)
                {
                    throw error;
                }
            }
        }
    }
}
