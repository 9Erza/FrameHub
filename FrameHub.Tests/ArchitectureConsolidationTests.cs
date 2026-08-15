using System.Diagnostics;
using FrameHub.App.Services;
using FrameHub.Core.Models;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Benchmarking;
using FrameHub.Core.Services.Library;
using FrameHub.App.ViewModels;
using FrameHub.Core.Services.SessionOptimization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public sealed class ProcessObservationSnapshotProviderTests
{
    [TestMethod]
    public async Task ConcurrentRequests_ShareSingleEnumeration()
    {
        int enumerations = 0;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var provider = new ProcessObservationSnapshotProvider(
            TimeSpan.FromSeconds(1),
            enumerate: () =>
            {
                Interlocked.Increment(ref enumerations);
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(5));
                return [new ProcessObservation(10, "game", null, DateTime.UtcNow)];
            });

        Task<ProcessObservationSnapshot> first = provider.GetSnapshotAsync();
        Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(3)));
        Task<ProcessObservationSnapshot> second = provider.GetSnapshotAsync();
        release.Set();

        ProcessObservationSnapshot[] snapshots = await Task.WhenAll(first, second);
        Assert.AreEqual(1, enumerations);
        Assert.AreSame(snapshots[0], snapshots[1]);
    }

    [TestMethod]
    public async Task ExpiredSnapshot_RefreshesGeneration()
    {
        int enumerations = 0;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var provider = new ProcessObservationSnapshotProvider(
            TimeSpan.FromMilliseconds(250),
            enumerate: () =>
            {
                Interlocked.Increment(ref enumerations);
                return Array.Empty<ProcessObservation>();
            },
            clock: () => now);

        ProcessObservationSnapshot first = await provider.GetSnapshotAsync();
        now = now.AddMilliseconds(251);
        ProcessObservationSnapshot second = await provider.GetSnapshotAsync();

        Assert.AreEqual(2, enumerations);
        Assert.IsTrue(second.Generation > first.Generation);
    }

    [TestMethod]
    public async Task FailedRefresh_DoesNotPoisonSubsequentRequests()
    {
        int enumerations = 0;
        var provider = new ProcessObservationSnapshotProvider(
            enumerate: () => Interlocked.Increment(ref enumerations) == 1
                ? throw new InvalidOperationException("transient")
                : [new ProcessObservation(10, "recovered", null, DateTime.UtcNow)]);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => provider.GetSnapshotAsync());
        ProcessObservationSnapshot recovered = await provider.GetSnapshotAsync();

        Assert.AreEqual(2, enumerations);
        Assert.AreEqual(1, recovered.Generation);
        Assert.AreEqual("recovered", recovered.Processes.Single().ProcessName);
    }

    [TestMethod]
    public async Task ImmediatelyCompletedRefresh_DoesNotRemainInstalledAfterExpiry()
    {
        int enumerations = 0;
        long ticks = DateTimeOffset.UtcNow.Ticks;
        var provider = new ProcessObservationSnapshotProvider(
            TimeSpan.Zero,
            enumerate: () =>
            {
                Interlocked.Increment(ref enumerations);
                return Array.Empty<ProcessObservation>();
            },
            clock: () => new DateTimeOffset(Interlocked.Increment(ref ticks), TimeSpan.Zero));

        for (int index = 0; index < 25; index++)
        {
            _ = await provider.GetSnapshotAsync();
        }

        Assert.AreEqual(25, enumerations);
    }

    [TestMethod]
    public async Task LibraryProjection_MapsMultipleItemsFromOneEnumeration()
    {
        int enumerations = 0;
        string firstPath = Path.GetFullPath("first.exe");
        string secondPath = Path.GetFullPath("second.exe");
        var provider = new ProcessObservationSnapshotProvider(
            enumerate: () =>
            {
                Interlocked.Increment(ref enumerations);
                return
                [
                    new ProcessObservation(10, "first", firstPath, DateTime.UtcNow),
                    new ProcessObservation(11, "second", secondPath, DateTime.UtcNow)
                ];
            });
        var scanner = new ProcessScannerService(new ProcessService(), provider);
        LibraryItem[] items =
        [
            new() { Id = "one", ProcessName = "first", ExecutablePath = firstPath },
            new() { Id = "two", ProcessName = "second", ExecutablePath = secondPath }
        ];

        IReadOnlySet<string> running = await scanner.FindRunningLibraryItemIdsAsync(items);

        Assert.AreEqual(1, enumerations);
        CollectionAssert.AreEquivalent(new[] { "one", "two" }, running.ToArray());
    }

    [TestMethod]
    public async Task CachedObservation_CannotAuthorizeDestructiveStop()
    {
        string fakePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "not-running.exe");
        var provider = new ProcessObservationSnapshotProvider(
            enumerate: () => [new ProcessObservation(424242, "not-running", fakePath, DateTime.UtcNow)]);
        var scanner = new ProcessScannerService(new ProcessService(), provider);
        var terminator = new RecordingTerminator();
        var service = new AppLibraryControlService(scanner, new NoOpLaunchService(), terminator);
        var item = new LibraryItem { Id = "cached", ProcessName = "not-running", ExecutablePath = fakePath };

        _ = await scanner.FindRunningLibraryItemIdsAsync([item]);
        LibraryControlResult result = await service.StopAsync(item);

        Assert.AreEqual("not_running", result.ErrorCode);
        Assert.AreEqual(0, terminator.Calls);
    }

    private sealed class RecordingTerminator : ITrustedProcessTerminator
    {
        public int Calls { get; private set; }
        public Task<bool> StopAsync(LibraryItem item, LibraryProcessIdentity expectedIdentity, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(true);
        }
    }

    private sealed class NoOpLaunchService : IAppLibraryLaunchService
    {
        public LibraryLaunchResult Launch(LibraryItem? item) => LibraryLaunchResult.Ok();
    }
}

[TestClass]
public sealed class BackgroundAppBatchObservationTests
{
    private string _root = null!;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "FrameHub.BackgroundBatch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [TestMethod]
    public async Task ListingMultipleBackgroundApps_EnumeratesOnce()
    {
        int enumerations = 0;
        string firstPath = CreateExecutable("first");
        string secondPath = CreateExecutable("second");
        var observation = new ProcessObservationSnapshotProvider(enumerate: () =>
        {
            Interlocked.Increment(ref enumerations);
            return [new ProcessObservation(10, "first", firstPath, DateTime.UtcNow)];
        });
        var scanner = new ProcessScannerService(new ProcessService(), observation);
        var library = new LibraryService(Path.Combine(_root, "library.json"));
        library.SaveItems(
        [
            BackgroundItem("first", firstPath),
            BackgroundItem("second", secondPath)
        ]);
        var provider = new AppBackgroundAppProvider(scanner, new IdleBenchmark(), new NoOpLaunch(), library);

        IReadOnlyList<FrameHub.Companion.Models.CompanionBackgroundAppDto> result = await provider.GetBackgroundAppsAsync();

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(1, enumerations);
        Assert.IsTrue(result.Single(item => item.Id == "first").IsRunning);
        Assert.IsFalse(result.Single(item => item.Id == "second").IsRunning);
    }

    [TestMethod]
    public void LibraryAndBackgroundProviders_CanShareOneLaunchReservationOwner()
    {
        var observation = new ProcessObservationSnapshotProvider(enumerate: Array.Empty<ProcessObservation>);
        var scanner = new ProcessScannerService(new ProcessService(), observation);
        var library = new LibraryService(Path.Combine(_root, "library.json"));
        var benchmark = new IdleBenchmark();
        var launch = new NoOpLaunch();
        var reservations = new LibraryLaunchReservationService();
        var regularProvider = new AppLibraryProvider(
            scanner, benchmark, launch, library, launchReservations: reservations);
        var backgroundProvider = new AppBackgroundAppProvider(
            scanner, benchmark, launch, library, launchReservations: reservations);

        object? regularOwner = typeof(AppLibraryProvider)
            .GetField("_launchReservations", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(regularProvider);
        object? backgroundOwner = typeof(AppBackgroundAppProvider)
            .GetField("_launchReservations", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(backgroundProvider);

        Assert.AreSame(reservations, regularOwner);
        Assert.AreSame(reservations, backgroundOwner);
    }

    private string CreateExecutable(string name)
    {
        string path = Path.Combine(_root, name + ".exe");
        File.WriteAllText(path, "test");
        return path;
    }

    private static LibraryItem BackgroundItem(string id, string path) => new()
    {
        Id = id,
        DisplayName = id,
        Type = LibraryItemType.BackgroundApp,
        IsEnabled = true,
        AllowRemoteControl = true,
        ProcessName = id,
        ExecutablePath = path
    };

    private sealed class NoOpLaunch : IAppLibraryLaunchService
    {
        public LibraryLaunchResult Launch(LibraryItem? item) => LibraryLaunchResult.Ok();
    }

    private sealed class IdleBenchmark : IBenchmarkCaptureCoordinator, IBenchmarkOperationArbiter
    {
        public bool IsActive => false;
        public BenchmarkCaptureStateSnapshot CurrentState => new() { State = CoordinatorState.Idle, IsActive = false };
        public event EventHandler<BenchmarkCaptureStateSnapshot>? StateChanged { add { } remove { } }
        public bool TryAcquireExternalMutation(out IDisposable? lease) { lease = new Lease(); return true; }
        public BenchmarkCaptureStartHandle TryStartCapture(BenchmarkCaptureRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BenchmarkCaptureOutcome> StartCaptureAsync(BenchmarkCaptureRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
        private sealed class Lease : IDisposable { public void Dispose() { } }
    }
}

[TestClass]
public sealed class ConsolidatedPolicyStructureTests
{
    [TestMethod]
    public void SessionViewModel_DoesNotOwnIndependentPolicyOrScannerServices()
    {
        Type[] fieldTypes = typeof(SessionOptimizationViewModel)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Select(field => field.FieldType)
            .ToArray();

        Assert.IsFalse(fieldTypes.Contains(typeof(ProcessSuspendService)));
        Assert.IsFalse(fieldTypes.Contains(typeof(SessionOptimizationSettingsService)));
        Assert.IsFalse(fieldTypes.Contains(typeof(LibraryService)));
        Assert.IsTrue(fieldTypes.Contains(typeof(SessionOptimizationCoordinator)));
    }

    [TestMethod]
    public async Task ManualProfileMutation_IsBlockedByAcceptedBenchmarkReservation_ThenReleased()
    {
        string root = Path.Combine(Path.GetTempPath(), "FrameHub.ProfileArbitration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var settingsService = new SettingsService(Path.Combine(root, "settings.json"));
            settingsService.SaveSettings(new AppSettings { CompanionEnabled = false, ProfileWatcherSeconds = 30 });
            using var runtime = new AppRuntimeService(settingsService);
            runtime.StopProfileWatcher();

            using Process current = Process.GetCurrentProcess();
            var request = new BenchmarkCaptureRequest
            {
                Target = new BenchmarkTarget { LibraryItemId = "test", DisplayName = "Test", LibrarySource = "Manual" },
                Process = new BenchmarkProcessIdentity
                {
                    ProcessId = current.Id,
                    ProcessName = current.ProcessName,
                    StartTimeUtc = current.StartTime.ToUniversalTime()
                },
                AppVersion = "test",
                DurationSeconds = 30
            };
            BenchmarkCaptureStartHandle reservation = runtime.BenchmarkCoordinator.TryStartCapture(request);
            Assert.IsTrue(reservation.Accepted);

            var profile = new ProcessProfile { ProcessName = "framehub-process-that-does-not-exist" };
            OptimizationBatchResult blocked = runtime.ApplyProfileNow(profile);
            Assert.AreEqual("SKIPPED_BENCHMARK_ACTIVE", blocked.Results.Single().Message);

            await runtime.BenchmarkCoordinator.StopAsync();
            OptimizationBatchResult allowed = runtime.ApplyProfileNow(profile);
            Assert.AreEqual(0, allowed.Total, "After the reservation ends the normal mutation path must run.");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
