using FrameHub.App.Services;
using FrameHub.Core.Logging;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Models.SessionOptimization;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Library;
using FrameHub.Core.Services.SessionOptimization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public sealed class SessionOptimizationCoordinatorTests
{
    private string _tempDir = null!;
    private string _stateFilePath = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FrameHub_CoordinatorTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _stateFilePath = Path.Combine(_tempDir, "active_session.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
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
    public async Task StartSessionAsync_NoGameItem_ReturnsNoGame()
    {
        using var coordinator = new SessionOptimizationCoordinator();
        var result = await coordinator.StartSessionAsync("Manual", null);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("no_game", result.ErrorCode);
    }

    [TestMethod]
    public async Task StartSessionAsync_WhenAlreadyActive_ReturnsAlreadyActive()
    {
        using var coordinator = new SessionOptimizationCoordinator();
        var game = new LibraryItem { Id = "g1", DisplayName = "Game 1", ProcessName = "game1.exe" };

        // Fake active session
        var field = typeof(SessionOptimizationCoordinator).GetField("_activeSession", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field!.SetValue(coordinator, new ActiveSessionState { IsActive = true, GameId = "g1", GameName = "Game 1" });

        var result = await coordinator.StartSessionAsync("Manual", game);
        Assert.IsFalse(result.Success);
        Assert.AreEqual("already_active", result.ErrorCode);
    }

    [TestMethod]
    public async Task StopSessionAsync_WhenNotActive_ReturnsNotActive()
    {
        using var coordinator = new SessionOptimizationCoordinator();
        var result = await coordinator.StopSessionAsync();

        Assert.IsFalse(result.Success);
        Assert.AreEqual("not_active", result.ErrorCode);
    }

    [TestMethod]
    public async Task ConcurrencyGate_RejectsConcurrentOperations()
    {
        using var coordinator = new SessionOptimizationCoordinator();
        var gateField = typeof(SessionOptimizationCoordinator).GetField("_mutationGate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var gate = (SemaphoreSlim)gateField!.GetValue(coordinator)!;

        // Acquire lock to simulate operation in progress
        await gate.WaitAsync();

        try
        {
            var game = new LibraryItem { Id = "g1", DisplayName = "Game 1" };
            var startResult = await coordinator.StartSessionAsync("Manual", game);
            Assert.IsFalse(startResult.Success);
            Assert.AreEqual("operation_in_progress", startResult.ErrorCode);

            var stopResult = await coordinator.StopSessionAsync();
            Assert.IsFalse(stopResult.Success);
            Assert.AreEqual("operation_in_progress", stopResult.ErrorCode);
        }
        finally
        {
            gate.Release();
        }
    }

    [TestMethod]
    public void LoadAndSaveSettings_DelegatesToSettingsService()
    {
        using var coordinator = new SessionOptimizationCoordinator();
        var settings = coordinator.LoadSettings();
        Assert.IsNotNull(settings);

        settings.AutoModeEnabled = !settings.AutoModeEnabled;
        coordinator.SaveSettings(settings);

        var reloaded = coordinator.LoadSettings();
        Assert.AreEqual(settings.AutoModeEnabled, reloaded.AutoModeEnabled);
    }
}
