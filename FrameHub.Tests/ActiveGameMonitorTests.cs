using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Models.SessionOptimization;
using FrameHub.Core.Services.Benchmarking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public sealed class ActiveGameMonitorTests
{
    [TestMethod]
    public void ResolveActiveGame_ZeroDetectedGames_ReturnsNull()
    {
        var snapshotProvider = new FakeProcessSnapshotProvider([]);
        var gameDetector = new BenchmarkGameDetectionService(snapshotProvider);
        var libraryItems = new List<LibraryItem> { CreateGameItem("g1", "Game One", "C:\\Games\\game1.exe") };

        var monitor = new ActiveGameMonitor(gameDetector, libraryLoader: () => libraryItems);
        monitor.UpdateSnapshotOnce();

        Assert.IsNull(monitor.CurrentSnapshot);
    }

    [TestMethod]
    public void ResolveActiveGame_SingleDetectedGame_ReturnsFullIdentity()
    {
        var startTime = new DateTime(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc);
        var snapshotProvider = new FakeProcessSnapshotProvider([
            new BenchmarkProcessSnapshot(1234, "game1", "C:\\Games\\game1.exe", startTime)
        ]);
        var gameDetector = new BenchmarkGameDetectionService(snapshotProvider);
        var item = CreateGameItem("g1", "Game One", "C:\\Games\\game1.exe");

        var monitor = new ActiveGameMonitor(gameDetector, libraryLoader: () => [item]);
        monitor.UpdateSnapshotOnce();

        var snapshot = monitor.CurrentSnapshot;
        Assert.IsNotNull(snapshot);
        Assert.AreEqual("g1", snapshot.LibraryItem.Id);
        Assert.AreEqual(1234, snapshot.Process.ProcessId);
        Assert.AreEqual("game1", snapshot.Process.ProcessName);
        Assert.AreEqual("C:\\Games\\game1.exe", snapshot.Process.ExecutablePath);
        Assert.AreEqual(startTime, snapshot.Process.StartTimeUtc);
    }

    [TestMethod]
    public async Task StopAsync_ClearsPreviouslyPublishedGameSnapshot()
    {
        var snapshotProvider = new FakeProcessSnapshotProvider([
            new BenchmarkProcessSnapshot(4321, "game1", "C:\\Games\\game1.exe", DateTime.UtcNow)
        ]);
        var monitor = new ActiveGameMonitor(
            new BenchmarkGameDetectionService(snapshotProvider),
            libraryLoader: () => [CreateGameItem("g1", "Game One", "C:\\Games\\game1.exe")]);
        monitor.UpdateSnapshotOnce();
        Assert.IsNotNull(monitor.CurrentSnapshot);

        await monitor.StopAsync();

        Assert.IsNull(monitor.CurrentSnapshot);
    }

    [TestMethod]
    public async Task StopAsync_InvalidatesBlockedLoopSoLateScanCannotRepublish()
    {
        var scanEntered = new ManualResetEventSlim();
        var releaseScan = new ManualResetEventSlim();
        var snapshotProvider = new FakeProcessSnapshotProvider([
            new BenchmarkProcessSnapshot(4321, "game1", "C:\\Games\\game1.exe", DateTime.UtcNow)
        ]);
        using var monitor = new ActiveGameMonitor(
            new BenchmarkGameDetectionService(snapshotProvider),
            libraryLoader: () =>
            {
                scanEntered.Set();
                releaseScan.Wait();
                return [CreateGameItem("g1", "Game One", "C:\\Games\\game1.exe")];
            });

        monitor.Start();
        Assert.IsTrue(scanEntered.Wait(TimeSpan.FromSeconds(2)));

        await monitor.StopAsync();
        Assert.IsNull(monitor.CurrentSnapshot);

        releaseScan.Set();
        await Task.Delay(100);
        Assert.IsNull(monitor.CurrentSnapshot, "A scan owned by a stopped generation must never publish after StopAsync returns.");

        monitor.Start();
        for (int i = 0; i < 50 && monitor.CurrentSnapshot == null; i++) await Task.Delay(10);
        Assert.IsNotNull(monitor.CurrentSnapshot, "A fresh generation must be able to publish after restart.");
        await monitor.StopAsync();
    }

    [TestMethod]
    public void ResolveActiveGame_MultipleDetectedGames_WithoutDisambiguation_ReturnsNull()
    {
        var startTime = DateTime.UtcNow;
        var snapshotProvider = new FakeProcessSnapshotProvider([
            new BenchmarkProcessSnapshot(1001, "game1", "C:\\Games\\game1.exe", startTime),
            new BenchmarkProcessSnapshot(1002, "game2", "C:\\Games\\game2.exe", startTime)
        ]);
        var gameDetector = new BenchmarkGameDetectionService(snapshotProvider);
        var items = new List<LibraryItem>
        {
            CreateGameItem("g1", "Game One", "C:\\Games\\game1.exe"),
            CreateGameItem("g2", "Game Two", "C:\\Games\\game2.exe")
        };

        var monitor = new ActiveGameMonitor(gameDetector, libraryLoader: () => items);
        monitor.UpdateSnapshotOnce();

        Assert.IsNull(monitor.CurrentSnapshot);
    }

    [TestMethod]
    public void ResolveActiveGame_MultipleDetectedGames_ActiveSessionDisambiguates_ReturnsDetectedProcessIdentity()
    {
        var osStartTime1 = new DateTime(2026, 8, 13, 10, 0, 0, DateTimeKind.Utc);
        var osStartTime2 = new DateTime(2026, 8, 13, 11, 0, 0, DateTimeKind.Utc);
        var snapshotProvider = new FakeProcessSnapshotProvider([
            new BenchmarkProcessSnapshot(1001, "game1", "C:\\Games\\game1.exe", osStartTime1),
            new BenchmarkProcessSnapshot(1002, "game2", "C:\\Games\\game2.exe", osStartTime2)
        ]);
        var gameDetector = new BenchmarkGameDetectionService(snapshotProvider);
        var items = new List<LibraryItem>
        {
            CreateGameItem("g1", "Game One", "C:\\Games\\game1.exe"),
            CreateGameItem("g2", "Game Two", "C:\\Games\\game2.exe")
        };

        var sessionState = new ActiveSessionState
        {
            IsActive = true,
            GameId = "g2",
            GameName = "Game Two",
            StartedAtUtc = new DateTime(2026, 8, 13, 14, 0, 0, DateTimeKind.Utc) // Different from OS StartTimeUtc!
        };

        var monitor = new ActiveGameMonitor(
            gameDetector,
            libraryLoader: () => items,
            sessionStateLoader: () => sessionState);

        monitor.UpdateSnapshotOnce();

        var snapshot = monitor.CurrentSnapshot;
        Assert.IsNotNull(snapshot);
        Assert.AreEqual("g2", snapshot.LibraryItem.Id);
        Assert.AreEqual(1002, snapshot.Process.ProcessId);
        // Verify OS StartTimeUtc was preserved, NOT fabricated from SessionState
        Assert.AreEqual(osStartTime2, snapshot.Process.StartTimeUtc);
    }

    private static LibraryItem CreateGameItem(string id, string name, string executable) => new()
    {
        Id = id,
        DisplayName = name,
        ExecutablePath = executable,
        Type = LibraryItemType.Game,
        IsEnabled = true
    };

    private sealed class FakeProcessSnapshotProvider : IBenchmarkProcessSnapshotProvider
    {
        private readonly IReadOnlyList<BenchmarkProcessSnapshot> _snapshots;
        public FakeProcessSnapshotProvider(IReadOnlyList<BenchmarkProcessSnapshot> snapshots) => _snapshots = snapshots;
        public IReadOnlyList<BenchmarkProcessSnapshot> GetProcesses() => _snapshots;
    }
}
