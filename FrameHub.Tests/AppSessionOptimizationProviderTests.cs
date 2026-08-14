using FrameHub.App.Services;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Models.SessionOptimization;
using FrameHub.Core.Services.Benchmarking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public sealed class AppSessionOptimizationProviderTests
{
    [TestMethod]
    public async Task GetStateAsync_ReturnsSanitizedDtoWithoutPidsOrPaths()
    {
        using var coordinator = new SessionOptimizationCoordinator();
        var fakeMonitor = new FakeActiveGameMonitor(null);
        var fakeBenchmarkCoordinator = new FakeBenchmarkCaptureCoordinator(isActive: false);

        var provider = new AppSessionOptimizationProvider(coordinator, fakeMonitor, fakeBenchmarkCoordinator);
        var dto = await provider.GetStateAsync();

        Assert.IsNotNull(dto);
        Assert.IsFalse(dto.IsSessionActive);

        // Verify with reflection that the DTO type does not expose unsafe process properties
        var properties = typeof(FrameHub.Companion.Models.CompanionSessionOptimizationStateDto).GetProperties();
        var propNames = properties.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.IsFalse(propNames.Contains("ExecutablePath"));
        Assert.IsFalse(propNames.Contains("InstallPath"));
        Assert.IsFalse(propNames.Contains("ProcessId"));
        Assert.IsFalse(propNames.Contains("ProcessIds"));
        Assert.IsFalse(propNames.Contains("AffinityMask"));
        Assert.IsFalse(propNames.Contains("CpuSets"));
        Assert.IsFalse(propNames.Contains("SuspendedProcesses"));
    }

    [TestMethod]
    public async Task ApplyOptimizationAsync_WhenBenchmarkActive_ReturnsBenchmarkActive()
    {
        using var coordinator = new SessionOptimizationCoordinator();
        var fakeMonitor = new FakeActiveGameMonitor(CreateSnapshot("g1", "Game 1"));
        var fakeBenchmarkCoordinator = new FakeBenchmarkCaptureCoordinator(isActive: true);

        var provider = new AppSessionOptimizationProvider(coordinator, fakeMonitor, fakeBenchmarkCoordinator);
        var result = await provider.ApplyOptimizationAsync();

        Assert.IsFalse(result.Success);
        Assert.AreEqual("benchmark_active", result.ErrorCode);
    }

    [TestMethod]
    public async Task ApplyOptimizationAsync_WhenNoActiveGame_ReturnsNoGame()
    {
        using var coordinator = new SessionOptimizationCoordinator();
        var fakeMonitor = new FakeActiveGameMonitor(null);
        var fakeBenchmarkCoordinator = new FakeBenchmarkCaptureCoordinator(isActive: false);

        var provider = new AppSessionOptimizationProvider(coordinator, fakeMonitor, fakeBenchmarkCoordinator);
        var result = await provider.ApplyOptimizationAsync();

        Assert.IsFalse(result.Success);
        Assert.AreEqual("no_game", result.ErrorCode);
    }

    [TestMethod]
    public async Task RestoreSessionAsync_WhenBenchmarkActive_ReturnsBenchmarkActive()
    {
        using var coordinator = new SessionOptimizationCoordinator();
        var fakeMonitor = new FakeActiveGameMonitor(null);
        var fakeBenchmarkCoordinator = new FakeBenchmarkCaptureCoordinator(isActive: true);

        var provider = new AppSessionOptimizationProvider(coordinator, fakeMonitor, fakeBenchmarkCoordinator);
        var result = await provider.RestoreSessionAsync();

        Assert.IsFalse(result.Success);
        Assert.AreEqual("benchmark_active", result.ErrorCode);
    }

    [TestMethod]
    public async Task RestoreSessionAsync_WhenNoActiveSession_ReturnsNotActive()
    {
        using var coordinator = new SessionOptimizationCoordinator();
        var fakeMonitor = new FakeActiveGameMonitor(null);
        var fakeBenchmarkCoordinator = new FakeBenchmarkCaptureCoordinator(isActive: false);

        var provider = new AppSessionOptimizationProvider(coordinator, fakeMonitor, fakeBenchmarkCoordinator);
        var result = await provider.RestoreSessionAsync();

        Assert.IsFalse(result.Success);
        Assert.AreEqual("not_active", result.ErrorCode);
    }

    private static ActiveGameSnapshot CreateSnapshot(string id, string name)
    {
        var item = new LibraryItem
        {
            Id = id,
            DisplayName = name,
            ProcessName = name.ToLowerInvariant().Replace(" ", "") + ".exe",
            Type = LibraryItemType.Game
        };
        var proc = new BenchmarkProcessIdentity
        {
            ProcessId = 1234,
            ProcessName = item.ProcessName,
            ExecutablePath = @"C:\Games\" + item.ProcessName,
            StartTimeUtc = DateTime.UtcNow
        };
        return new ActiveGameSnapshot(item, proc);
    }

    private sealed class FakeActiveGameMonitor : IActiveGameMonitor
    {
        public ActiveGameSnapshot? CurrentSnapshot { get; set; }

        public FakeActiveGameMonitor(ActiveGameSnapshot? initial)
        {
            CurrentSnapshot = initial;
        }

        public void Start() { }
        public Task StopAsync() => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class FakeBenchmarkCaptureCoordinator : IBenchmarkCaptureCoordinator
    {
        public bool IsActive { get; set; }
        public BenchmarkCaptureStateSnapshot CurrentState => new()
        {
            State = IsActive ? CoordinatorState.Capturing : CoordinatorState.Idle,
            IsActive = IsActive
        };

        public FakeBenchmarkCaptureCoordinator(bool isActive)
        {
            IsActive = isActive;
        }

        public event EventHandler<BenchmarkCaptureStateSnapshot>? StateChanged
        {
            add { }
            remove { }
        }
        public BenchmarkCaptureStartHandle TryStartCapture(BenchmarkCaptureRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<BenchmarkCaptureOutcome> StartCaptureAsync(BenchmarkCaptureRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
