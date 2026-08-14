using System.Diagnostics;
using System.IO;
using FrameHub.App.Services;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Benchmarking;
using FrameHub.Core.Services.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public sealed class AppLibraryProviderTests
{
    private string _tempDirectory = null!;
    private string _tempLibraryFilePath = null!;
    private LibraryService _libraryService = null!;
    private ProcessService _processService = null!;
    private ProcessScannerService _processScanner = null!;
    private string _fakeGameExe = null!;
    private string _fakeAppExe = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.ProviderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _tempLibraryFilePath = Path.Combine(_tempDirectory, "library.json");

        _fakeGameExe = Path.Combine(_tempDirectory, "game.exe");
        _fakeAppExe = Path.Combine(_tempDirectory, "app.exe");
        File.WriteAllText(_fakeGameExe, "game binary");
        File.WriteAllText(_fakeAppExe, "app binary");

        _libraryService = new LibraryService(_tempLibraryFilePath);
        _processService = new ProcessService();
        _processScanner = new ProcessScannerService(_processService);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { }
        }
    }

    private sealed class FakeBenchmarkCoordinator : IBenchmarkCaptureCoordinator
    {
        public bool IsActive { get; set; }
        public BenchmarkCaptureStateSnapshot CurrentState => new()
        {
            State = IsActive ? CoordinatorState.Capturing : CoordinatorState.Idle,
            IsActive = IsActive
        };
        public event EventHandler<BenchmarkCaptureStateSnapshot>? StateChanged
        {
            add { }
            remove { }
        }

        public BenchmarkCaptureStartHandle TryStartCapture(BenchmarkCaptureRequest request, CancellationToken cancellationToken = default)
        {
            return new BenchmarkCaptureStartHandle
            {
                Accepted = true,
                CompletionTask = Task.FromResult(new BenchmarkCaptureOutcome { Status = CoordinatorStatus.Completed })
            };
        }

        public Task<BenchmarkCaptureOutcome> StartCaptureAsync(BenchmarkCaptureRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new BenchmarkCaptureOutcome { Status = CoordinatorStatus.Completed });
        }

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }

    private sealed class FakeLibraryLaunchService : IAppLibraryLaunchService
    {
        public LibraryItem? LastLaunchedItem { get; private set; }
        public LibraryLaunchResult ResultToReturn { get; set; } = LibraryLaunchResult.Ok();
        public Action? BeforeLaunchCallback { get; set; }

        public LibraryLaunchResult Launch(LibraryItem? item)
        {
            BeforeLaunchCallback?.Invoke();
            LastLaunchedItem = item;
            return ResultToReturn;
        }
    }

    [TestMethod]
    public async Task GetLibraryItemsAsync_FiltersCorrectlyAndMapsSafeDtos()
    {
        var items = new List<LibraryItem>
        {
            new() { Id = "item-game", DisplayName = "My Game", Type = LibraryItemType.Game, IsEnabled = true, ExecutablePath = _fakeGameExe },
            new() { Id = "item-app", DisplayName = "My App", Type = LibraryItemType.App, IsEnabled = true, ExecutablePath = _fakeAppExe },
            new() { Id = "item-bg", DisplayName = "Background Tool", Type = LibraryItemType.BackgroundApp, IsEnabled = true, ExecutablePath = _fakeGameExe },
            new() { Id = "item-launcher", DisplayName = "Launcher", Type = LibraryItemType.Launcher, IsEnabled = true, ExecutablePath = _fakeGameExe },
            new() { Id = "item-disabled", DisplayName = "Disabled Game", Type = LibraryItemType.Game, IsEnabled = false, ExecutablePath = _fakeGameExe }
        };

        _libraryService.SaveItems(items);

        var benchmarkCoordinator = new FakeBenchmarkCoordinator();
        var launchService = new FakeLibraryLaunchService();
        var provider = new AppLibraryProvider(_processScanner, benchmarkCoordinator, launchService, _libraryService);

        var dtos = await provider.GetLibraryItemsAsync();

        Assert.AreEqual(2, dtos.Count, "Only enabled Game and App items must be returned.");
        Assert.IsTrue(dtos.Any(d => d.Id == "item-game" && d.DisplayName == "My Game" && d.Type == "Game"));
        Assert.IsTrue(dtos.Any(d => d.Id == "item-app" && d.DisplayName == "My App" && d.Type == "App"));
        Assert.IsFalse(dtos.Any(d => d.Id == "item-bg"));
        Assert.IsFalse(dtos.Any(d => d.Id == "item-launcher"));
        Assert.IsFalse(dtos.Any(d => d.Id == "item-disabled"));

        // Privacy check on DTO
        var dto = dtos.First(d => d.Id == "item-game");
        var dtoType = dto.GetType();
        Assert.IsNull(dtoType.GetProperty("ExecutablePath"));
        Assert.IsNull(dtoType.GetProperty("InstallPath"));
        Assert.IsNull(dtoType.GetProperty("IconPath"));
        Assert.IsNull(dtoType.GetProperty("ProcessName"));
        Assert.IsNull(dtoType.GetProperty("AppId"));
        Assert.IsNull(dtoType.GetProperty("LinkedProfileId"));
    }

    [TestMethod]
    public async Task LaunchItemAsync_ValidGame_DelegatesToLaunchServiceAndReturnsLaunched()
    {
        var item = new LibraryItem
        {
            Id = "game-launch-1",
            DisplayName = "Awesome Game",
            Type = LibraryItemType.Game,
            IsEnabled = true,
            ExecutablePath = _fakeGameExe
        };
        _libraryService.SaveItems(new[] { item });

        var benchmarkCoordinator = new FakeBenchmarkCoordinator();
        var launchService = new FakeLibraryLaunchService();
        var provider = new AppLibraryProvider(_processScanner, benchmarkCoordinator, launchService, _libraryService);

        var result = await provider.LaunchItemAsync("game-launch-1");

        Assert.IsTrue(result.Success);
        Assert.AreEqual("launched", result.ErrorCode);
        Assert.IsNotNull(launchService.LastLaunchedItem);
        Assert.AreEqual("game-launch-1", launchService.LastLaunchedItem.Id);
    }

    [TestMethod]
    public async Task LaunchItemAsync_UnknownOrHiddenItem_ReturnsNotFound()
    {
        var hiddenBg = new LibraryItem
        {
            Id = "bg-item",
            DisplayName = "Bg App",
            Type = LibraryItemType.BackgroundApp,
            IsEnabled = true,
            ExecutablePath = _fakeGameExe
        };
        _libraryService.SaveItems(new[] { hiddenBg });

        var benchmarkCoordinator = new FakeBenchmarkCoordinator();
        var launchService = new FakeLibraryLaunchService();
        var provider = new AppLibraryProvider(_processScanner, benchmarkCoordinator, launchService, _libraryService);

        var unknownResult = await provider.LaunchItemAsync("does-not-exist");
        Assert.IsFalse(unknownResult.Success);
        Assert.AreEqual("not_found", unknownResult.ErrorCode);

        var hiddenResult = await provider.LaunchItemAsync("bg-item");
        Assert.IsFalse(hiddenResult.Success);
        Assert.AreEqual("not_found", hiddenResult.ErrorCode);
    }

    [TestMethod]
    public async Task LaunchItemAsync_WhenBenchmarkActive_ReturnsBenchmarkActive()
    {
        var item = new LibraryItem
        {
            Id = "game-bm",
            DisplayName = "Bench Game",
            Type = LibraryItemType.Game,
            IsEnabled = true,
            ExecutablePath = _fakeGameExe
        };
        _libraryService.SaveItems(new[] { item });

        var benchmarkCoordinator = new FakeBenchmarkCoordinator { IsActive = true };
        var launchService = new FakeLibraryLaunchService();
        var provider = new AppLibraryProvider(_processScanner, benchmarkCoordinator, launchService, _libraryService);

        var result = await provider.LaunchItemAsync("game-bm");

        Assert.IsFalse(result.Success);
        Assert.AreEqual("benchmark_active", result.ErrorCode);
        Assert.IsNull(launchService.LastLaunchedItem, "Launch service must not be invoked when benchmark is active.");
    }

    [TestMethod]
    public async Task LaunchItemAsync_WhenBenchmarkBecomesActiveMidFlight_RechecksAndAborts()
    {
        var item = new LibraryItem
        {
            Id = "game-bm-race",
            DisplayName = "Race Game",
            Type = LibraryItemType.Game,
            IsEnabled = true,
            ExecutablePath = _fakeGameExe
        };
        _libraryService.SaveItems(new[] { item });

        var benchmarkCoordinator = new FakeBenchmarkCoordinator { IsActive = false };
        var launchService = new FakeLibraryLaunchService();

        // Inject clock that mutates benchmark state before launch
        var provider = new AppLibraryProvider(
            _processScanner,
            benchmarkCoordinator,
            launchService,
            _libraryService,
            clock: () =>
            {
                benchmarkCoordinator.IsActive = true;
                return DateTimeOffset.UtcNow;
            });

        var result = await provider.LaunchItemAsync("game-bm-race");

        Assert.IsFalse(result.Success);
        Assert.AreEqual("benchmark_active", result.ErrorCode);
        Assert.IsNull(launchService.LastLaunchedItem);
    }

    [TestMethod]
    public async Task LaunchItemAsync_WithinCooldownWindow_ReturnsLaunchInProgress()
    {
        var item = new LibraryItem
        {
            Id = "game-cooldown",
            DisplayName = "Cooldown Game",
            Type = LibraryItemType.Game,
            IsEnabled = true,
            ExecutablePath = _fakeGameExe
        };
        _libraryService.SaveItems(new[] { item });

        var benchmarkCoordinator = new FakeBenchmarkCoordinator();
        var launchService = new FakeLibraryLaunchService();

        var time = DateTimeOffset.UtcNow;
        var provider = new AppLibraryProvider(
            _processScanner,
            benchmarkCoordinator,
            launchService,
            _libraryService,
            clock: () => time);

        // First launch
        var result1 = await provider.LaunchItemAsync("game-cooldown");
        Assert.IsTrue(result1.Success);
        Assert.AreEqual("launched", result1.ErrorCode);

        // Second launch 1 second later (inside 3-second cooldown)
        time = time.AddSeconds(1);
        var result2 = await provider.LaunchItemAsync("game-cooldown");
        Assert.IsFalse(result2.Success);
        Assert.AreEqual("launch_in_progress", result2.ErrorCode);

        // Third launch 4 seconds later (after cooldown)
        time = time.AddSeconds(4);
        var result3 = await provider.LaunchItemAsync("game-cooldown");
        Assert.IsTrue(result3.Success);
        Assert.AreEqual("launched", result3.ErrorCode);
    }

    [TestMethod]
    public async Task LaunchItemAsync_WhenAlreadyRunningProcessMatched_ReturnsAlreadyRunning()
    {
        // Use current process as fake running target
        var currentProcess = Process.GetCurrentProcess();
        string currentExe = currentProcess.MainModule?.FileName ?? _fakeGameExe;

        var item = new LibraryItem
        {
            Id = "game-running-test",
            DisplayName = "Running Game",
            Type = LibraryItemType.Game,
            IsEnabled = true,
            ExecutablePath = currentExe,
            ProcessName = currentProcess.ProcessName
        };
        _libraryService.SaveItems(new[] { item });

        var benchmarkCoordinator = new FakeBenchmarkCoordinator();
        var launchService = new FakeLibraryLaunchService();
        var provider = new AppLibraryProvider(_processScanner, benchmarkCoordinator, launchService, _libraryService);

        var result = await provider.LaunchItemAsync("game-running-test");

        Assert.IsFalse(result.Success);
        Assert.AreEqual("already_running", result.ErrorCode);
        Assert.IsNull(launchService.LastLaunchedItem);
    }
}
