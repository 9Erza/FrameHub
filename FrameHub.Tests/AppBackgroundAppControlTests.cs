using System.Diagnostics;
using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.Companion.Authentication;
using FrameHub.Companion.Persistence;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Benchmarking;
using FrameHub.Core.Services.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public sealed class AppBackgroundAppControlTests
{
    private string _tempDirectory = null!;
    private string _libraryPath = null!;
    private string _executablePath = null!;
    private LibraryService _library = null!;
    private ProcessScannerService _scanner = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.BackgroundControlTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _libraryPath = Path.Combine(_tempDirectory, "library.json");
        _executablePath = Path.Combine(_tempDirectory, "trusted.exe");
        File.WriteAllText(_executablePath, "test");
        _library = new LibraryService(_libraryPath);
        _scanner = new ProcessScannerService(new ProcessService());
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_tempDirectory, recursive: true); } catch { }
    }

    [TestMethod]
    public async Task List_ExposesOnlyExplicitEligibleBackgroundApps_AndSafeDto()
    {
        _library.SaveItems(new[]
        {
            Item("eligible", allow: true),
            Item("not-opted-in", allow: false),
            new LibraryItem { Id = "missing", DisplayName = "Missing", Type = LibraryItemType.BackgroundApp, IsEnabled = true, AllowRemoteControl = true, ExecutablePath = Path.Combine(_tempDirectory, "missing.exe"), ProcessName = "missing" },
            new LibraryItem { Id = "game", DisplayName = "Game", Type = LibraryItemType.Game, IsEnabled = true, AllowRemoteControl = true, ExecutablePath = _executablePath, ProcessName = "trusted" },
            new LibraryItem { Id = "protected", DisplayName = "Shell", Type = LibraryItemType.BackgroundApp, IsEnabled = true, AllowRemoteControl = true, ExecutablePath = Path.Combine(_tempDirectory, "explorer.exe"), ProcessName = "explorer" }
        });
        var provider = Provider();

        var result = await provider.GetBackgroundAppsAsync();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("eligible", result[0].Id);
        Assert.IsFalse(result[0].IsRunning);
        Assert.IsTrue(result[0].CanStart);
        foreach (string forbidden in new[] { "ProcessId", "Pid", "ExecutablePath", "ProcessName", "CommandLine", "Environment" })
        {
            Assert.IsNull(typeof(FrameHub.Companion.Models.CompanionBackgroundAppDto).GetProperty(forbidden));
        }
    }

    [TestMethod]
    public async Task Start_ResolvesTrustedServerItem_AndRejectsUntrustedOrAlreadyRunning()
    {
        _library.SaveItems(new[] { Item("eligible", allow: true), Item("hidden", allow: false) });
        var control = new FakeControlService();
        var provider = Provider(controlService: control);

        var started = await provider.StartBackgroundAppAsync("eligible");
        var hidden = await provider.StartBackgroundAppAsync("hidden");

        Assert.IsTrue(started.Success);
        Assert.AreEqual(Path.GetFullPath(_executablePath), Path.GetFullPath(control.StartedItem!.ExecutablePath!));
        Assert.AreEqual("not_found", hidden.ErrorCode);

        using Process current = Process.GetCurrentProcess();
        string currentPath = current.MainModule!.FileName;
        _library.SaveItems(new[]
        {
            new LibraryItem
            {
                Id = "running", DisplayName = "Running", Type = LibraryItemType.BackgroundApp,
                IsEnabled = true, AllowRemoteControl = true, ExecutablePath = currentPath, ProcessName = current.ProcessName
            }
        });
        var alreadyRunning = await provider.StartBackgroundAppAsync("running");
        Assert.AreEqual("already_running", alreadyRunning.ErrorCode);
    }

    [TestMethod]
    public async Task Stop_UsesScannerIdentity_AndNotRunningFailsClosed()
    {
        using Process current = Process.GetCurrentProcess();
        var item = new LibraryItem
        {
            Id = "running", DisplayName = "Running", Type = LibraryItemType.BackgroundApp,
            IsEnabled = true, AllowRemoteControl = true,
            ExecutablePath = current.MainModule!.FileName, ProcessName = current.ProcessName
        };
        var terminator = new RecordingTerminator();
        var service = new AppLibraryControlService(_scanner, new FakeLaunchService(), terminator);

        LibraryControlResult stopped = await service.StopAsync(item);
        LibraryControlResult absent = await service.StopAsync(Item("absent", allow: true));

        Assert.IsTrue(stopped.Success);
        Assert.IsTrue(terminator.Identities.Count >= 1);
        Assert.IsTrue(terminator.Identities.All(identity => identity.ProcessId > 4 && identity.StartTimeUtc != DateTime.MinValue));
        Assert.AreEqual("not_running", absent.ErrorCode);
    }

    [TestMethod]
    public void IdentityValidation_RejectsPidReuseNameAndPathMismatch()
    {
        var item = Item("trusted", allow: true);
        var expected = new LibraryProcessIdentity { ProcessId = 42, StartTimeUtc = DateTime.UtcNow, ProcessName = "trusted", ExecutablePath = _executablePath };

        Assert.IsTrue(SystemTrustedProcessTerminator.IdentityMatches(item, expected, expected));
        Assert.IsFalse(SystemTrustedProcessTerminator.IdentityMatches(item, expected, expected with { StartTimeUtc = expected.StartTimeUtc.AddSeconds(1) }));
        Assert.IsFalse(SystemTrustedProcessTerminator.IdentityMatches(item, expected, expected with { ProcessName = "other" }));
        Assert.IsFalse(SystemTrustedProcessTerminator.IdentityMatches(item, expected, expected with { ExecutablePath = Path.Combine(_tempDirectory, "other.exe") }));
    }

    [TestMethod]
    public async Task Operations_RejectBenchmarkAndConcurrentTap()
    {
        _library.SaveItems(new[] { Item("eligible", allow: true) });
        var activeBenchmark = new FakeBenchmarkCoordinator { IsActive = true };
        var blockedProvider = Provider(activeBenchmark);
        Assert.AreEqual("benchmark_active", (await blockedProvider.StartBackgroundAppAsync("eligible")).ErrorCode);

        var blockingControl = new FakeControlService { BlockStart = true };
        var provider = Provider(controlService: blockingControl);
        Task<FrameHub.Companion.Models.CompanionBackgroundAppOperationDto> first = Task.Run(() => provider.StartBackgroundAppAsync("eligible"));
        Assert.IsTrue(blockingControl.Entered.Wait(TimeSpan.FromSeconds(3)));
        var second = await provider.StartBackgroundAppAsync("eligible");
        Assert.AreEqual("operation_busy", second.ErrorCode);
        blockingControl.Release.Set();
        Assert.IsTrue((await first).Success);
    }

    [TestMethod]
    public async Task SequentialSuccessfulStart_UsesPerItemCooldownWithoutBlockingDifferentItem()
    {
        string secondExecutable = Path.Combine(_tempDirectory, "second.exe");
        File.WriteAllText(secondExecutable, "test");
        _library.SaveItems(new[]
        {
            Item("first", allow: true),
            new LibraryItem
            {
                Id = "second", DisplayName = "second", Type = LibraryItemType.BackgroundApp,
                IsEnabled = true, AllowRemoteControl = true, ExecutablePath = secondExecutable, ProcessName = "second"
            }
        });
        var control = new FakeControlService();
        var provider = Provider(controlService: control);

        var first = await provider.StartBackgroundAppAsync("first");
        var duplicate = await provider.StartBackgroundAppAsync("first");
        var different = await provider.StartBackgroundAppAsync("second");

        Assert.IsTrue(first.Success);
        Assert.AreEqual("operation_busy", duplicate.ErrorCode);
        Assert.IsTrue(different.Success, "A per-item reservation must not block a different trusted item.");
        CollectionAssert.AreEqual(new[] { "first", "second" }, control.StartedIds);
    }

    [TestMethod]
    public void ProtectedFrameHubAndSystemTargets_AreNeverEligible()
    {
        foreach (string name in new[] { "FrameHub", "FrameHub.App", "explorer", "svchost", "lsass" })
        {
            var item = new LibraryItem
            {
                Type = LibraryItemType.BackgroundApp, IsEnabled = true, AllowRemoteControl = true,
                ExecutablePath = Path.Combine(_tempDirectory, name + ".exe"), ProcessName = name
            };
            Assert.IsFalse(SystemTrustedProcessTerminator.IsEligibleTrustedItem(item), name);
        }

        string systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string systemExecutable = Path.Combine(systemDirectory, "cmd.exe");
        Assert.IsTrue(File.Exists(systemExecutable), "The Windows command processor is required for this system-path policy test.");
        Assert.IsFalse(SystemTrustedProcessTerminator.IsEligibleTrustedItem(new LibraryItem
        {
            Type = LibraryItemType.BackgroundApp, IsEnabled = true, AllowRemoteControl = true,
            ExecutablePath = systemExecutable, ProcessName = "cmd"
        }));
    }

    [TestMethod]
    public void ProtectedPathBoundary_DoesNotRejectWindowsLikeSibling()
    {
        string windowsRoot = Path.Combine(_tempDirectory, "Windows");
        string siblingRoot = Path.Combine(_tempDirectory, "WindowsApps");
        string siblingExecutable = Path.Combine(siblingRoot, "normal.exe");

        Assert.IsTrue(SystemTrustedProcessTerminator.IsPathWithinDirectory(Path.Combine(windowsRoot, "System32", "tool.exe"), windowsRoot));
        Assert.IsFalse(SystemTrustedProcessTerminator.IsPathWithinDirectory(siblingExecutable, windowsRoot));
    }

    [TestMethod]
    public async Task AllowRemoteControl_DefaultsFalseAndOptInPersistsAcrossReload()
    {
        var item = new LibraryItem
        {
            Id = "persisted", DisplayName = "Persisted", Type = LibraryItemType.BackgroundApp,
            IsEnabled = true, ExecutablePath = _executablePath, ProcessName = "trusted"
        };
        _library.SaveItems(new[] { item });

        LibraryItem defaultLoaded = _library.LoadItems().Single();
        Assert.IsFalse(defaultLoaded.AllowRemoteControl);
        Assert.AreEqual(0, (await Provider().GetBackgroundAppsAsync()).Count);

        defaultLoaded.AllowRemoteControl = true;
        _library.SaveItems(new[] { defaultLoaded });
        LibraryItem optedInLoaded = _library.LoadItems().Single();
        Assert.IsTrue(optedInLoaded.AllowRemoteControl);
        var exposed = await Provider().GetBackgroundAppsAsync();
        Assert.AreEqual(1, exposed.Count);
        Assert.AreEqual("persisted", exposed[0].Id);
    }

    [TestMethod]
    public void BackgroundAppPermission_WriteReadDependency_IsOneWayAndExplicit()
    {
        var changes = new List<(string Scope, bool Enabled)>();
        var vm = new PairedDeviceItemViewModel(
            new PairedDeviceRecord { Id = Guid.NewGuid(), DisplayName = "Phone", Scopes = new List<string>() },
            _ => { },
            (_, scope, enabled) => changes.Add((scope, enabled)));

        vm.WriteBackgroundAppsEnabled = true;
        Assert.IsTrue(vm.ReadBackgroundAppsEnabled);
        Assert.IsTrue(changes.Contains((CompanionScopes.WriteBackgroundApps, true)));
        Assert.IsTrue(changes.Contains((CompanionScopes.ReadBackgroundApps, true)));

        vm.WriteBackgroundAppsEnabled = false;
        Assert.IsTrue(vm.ReadBackgroundAppsEnabled, "Disabling write alone must retain read permission.");

        vm.WriteBackgroundAppsEnabled = true;
        vm.ReadBackgroundAppsEnabled = false;
        Assert.IsFalse(vm.WriteBackgroundAppsEnabled, "Disabling read must also disable write permission.");
    }

    private LibraryItem Item(string id, bool allow) => new()
    {
        Id = id, DisplayName = id, Type = LibraryItemType.BackgroundApp, IsEnabled = true,
        AllowRemoteControl = allow, ExecutablePath = _executablePath, ProcessName = "trusted"
    };

    private AppLibraryProvider Provider(FakeBenchmarkCoordinator? benchmark = null, IAppLibraryControlService? controlService = null) =>
        new(_scanner, benchmark ?? new FakeBenchmarkCoordinator(), new FakeLaunchService(), _library, controlService: controlService);

    private sealed class FakeLaunchService : IAppLibraryLaunchService
    {
        public LibraryLaunchResult Launch(LibraryItem? item) => LibraryLaunchResult.Ok();
    }

    private sealed class RecordingTerminator : ITrustedProcessTerminator
    {
        public List<LibraryProcessIdentity> Identities { get; } = new();
        public Task<bool> StopAsync(LibraryItem item, LibraryProcessIdentity expectedIdentity, CancellationToken cancellationToken)
        {
            Identities.Add(expectedIdentity);
            return Task.FromResult(true);
        }
    }

    private sealed class FakeControlService : IAppLibraryControlService
    {
        public LibraryItem? StartedItem { get; private set; }
        public List<string> StartedIds { get; } = new();
        public bool BlockStart { get; init; }
        public ManualResetEventSlim Entered { get; } = new(false);
        public ManualResetEventSlim Release { get; } = new(false);
        public LibraryControlResult Start(LibraryItem item)
        {
            StartedItem = item;
            StartedIds.Add(item.Id);
            Entered.Set();
            if (BlockStart) Release.Wait(TimeSpan.FromSeconds(5));
            return LibraryControlResult.Ok("started");
        }
        public Task<LibraryControlResult> StopAsync(LibraryItem item, CancellationToken cancellationToken = default) =>
            Task.FromResult(LibraryControlResult.Ok("stop_succeeded"));
    }

    private sealed class FakeBenchmarkCoordinator : IBenchmarkCaptureCoordinator, IBenchmarkOperationArbiter
    {
        public bool IsActive { get; set; }
        public BenchmarkCaptureStateSnapshot CurrentState => new() { IsActive = IsActive, State = IsActive ? CoordinatorState.Capturing : CoordinatorState.Idle };
        public event EventHandler<BenchmarkCaptureStateSnapshot>? StateChanged { add { } remove { } }
        public bool TryAcquireExternalMutation(out IDisposable? lease)
        {
            lease = IsActive ? null : new Lease();
            return !IsActive;
        }
        public BenchmarkCaptureStartHandle TryStartCapture(BenchmarkCaptureRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BenchmarkCaptureOutcome> StartCaptureAsync(BenchmarkCaptureRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
        private sealed class Lease : IDisposable { public void Dispose() { } }
    }
}
