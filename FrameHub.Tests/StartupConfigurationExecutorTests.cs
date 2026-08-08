using FrameHub.Core.Models;
using FrameHub.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public class StartupConfigurationExecutorTests
{
    private static readonly DesiredStartupConfiguration Normal = new(true, StartupWindowMode.Normal, false);
    private static readonly DesiredStartupConfiguration Elevated = new(true, StartupWindowMode.Normal, true);

    [TestMethod] public async Task HealthyState_PerformsNothing()
    {
        var backend = new FakeBackend(Actual(registry: true));
        var result = await new StartupConfigurationExecutor(backend).ApplyAsync(Normal);
        Assert.IsTrue(result.Success); Assert.AreEqual(0, backend.Operations.Count);
    }

    [TestMethod] public async Task OffRegistry_RemovesAndVerifiesDisabled()
    {
        var backend = new FakeBackend(Actual(registry: true)) { AfterOperation = Actual() };
        var result = await new StartupConfigurationExecutor(backend).ApplyAsync(new(false, StartupWindowMode.Tray, true));
        Assert.IsTrue(result.Success); CollectionAssert.AreEqual(new[] { StartupOperation.RemoveRegistry }, backend.Operations);
    }

    [TestMethod] public async Task RegistryToElevated_PreservesPlannerOrder()
    {
        var backend = new FakeBackend(Actual(registry: true)) { AfterOperation = Actual(task: true) };
        var result = await new StartupConfigurationExecutor(backend).ApplyAsync(Elevated);
        Assert.IsTrue(result.Success); CollectionAssert.AreEqual(new[] { StartupOperation.CreateOrUpdateScheduledTask, StartupOperation.RemoveRegistry }, backend.Operations);
    }

    [TestMethod] public async Task ElevatedToRegistry_CreatesRegistryBeforeRemovingTask()
    {
        var backend = new FakeBackend(Actual(task: true)) { AfterOperation = Actual(registry: true) };
        var result = await new StartupConfigurationExecutor(backend).ApplyAsync(Normal);
        Assert.IsTrue(result.Success); CollectionAssert.AreEqual(new[] { StartupOperation.CreateOrUpdateRegistry, StartupOperation.RemoveScheduledTask }, backend.Operations);
    }

    [TestMethod] public async Task CancelledTaskCreate_DoesNotRemoveRegistry()
    {
        var backend = new FakeBackend(Actual(registry: true)) { NextResult = new(false, true, true, "cancelled") };
        var result = await new StartupConfigurationExecutor(backend).ApplyAsync(Elevated);
        Assert.IsFalse(result.Success); Assert.IsTrue(result.WasElevationCancelled); CollectionAssert.AreEqual(new[] { StartupOperation.CreateOrUpdateScheduledTask }, backend.Operations);
    }

    [TestMethod] public async Task CancelledTaskRemove_LeavesTaskDetected()
    {
        var backend = new FakeBackend(Actual(registry: true, task: true)) { NextResult = new(false, true, true, "cancelled") };
        var result = await new StartupConfigurationExecutor(backend).ApplyAsync(Normal);
        Assert.IsFalse(result.Success); Assert.AreEqual(StartupConfigurationState.Conflict, result.FinalEvaluation.State); CollectionAssert.AreEqual(new[] { StartupOperation.RemoveScheduledTask }, backend.Operations);
    }

    [TestMethod] public async Task RegistryCreateFailure_DoesNotRemoveTask()
    {
        var backend = new FakeBackend(Actual(task: true)) { NextResult = new(false, Error: "registry failed") };
        var result = await new StartupConfigurationExecutor(backend).ApplyAsync(Normal);
        Assert.IsFalse(result.Success); CollectionAssert.AreEqual(new[] { StartupOperation.CreateOrUpdateRegistry }, backend.Operations);
        Assert.AreEqual(StartupConfigurationState.Broken, result.FinalEvaluation.State);
    }

    [TestMethod] public async Task AnyToOff_RemovesBothMechanismsAndVerifiesDisabled()
    {
        var backend = new FakeBackend(Actual(registry: true, task: true)) { AfterOperation = Actual() };
        var result = await new StartupConfigurationExecutor(backend).ApplyAsync(new(false, StartupWindowMode.Normal, false));
        Assert.IsTrue(result.Success); CollectionAssert.AreEqual(new[] { StartupOperation.RemoveRegistry, StartupOperation.RemoveScheduledTask }, backend.Operations);
        Assert.AreEqual(StartupConfigurationState.Disabled, result.FinalEvaluation.State);
    }

    [TestMethod] public async Task BackendFailure_IsNotFalseSuccess()
    {
        var backend = new FakeBackend(Actual(registry: true)) { NextResult = new(false, Error: "failed") };
        var result = await new StartupConfigurationExecutor(backend).ApplyAsync(Elevated);
        Assert.IsFalse(result.Success); Assert.AreEqual("failed", result.Error);
    }

    [TestMethod] public async Task FinalMismatch_IsNotFalseSuccess()
    {
        var backend = new FakeBackend(Actual()) { AfterOperation = Actual() };
        var result = await new StartupConfigurationExecutor(backend).ApplyAsync(Normal);
        Assert.IsFalse(result.Success); Assert.AreEqual(StartupConfigurationState.Broken, result.FinalEvaluation.State);
    }

    [TestMethod] public async Task InitialReadFailure_PerformsNoOperations()
    {
        var backend = new FakeBackend(Actual(readSucceeded: false));
        var result = await new StartupConfigurationExecutor(backend).ApplyAsync(Normal);
        Assert.IsFalse(result.Success); Assert.AreEqual(0, backend.Operations.Count);
    }

    [TestMethod] public async Task PostOperationReadFailure_IsNotSuccess()
    {
        var backend = new FakeBackend(Actual()) { AfterOperation = Actual(readSucceeded: false) };
        var result = await new StartupConfigurationExecutor(backend).ApplyAsync(Normal);
        Assert.IsFalse(result.Success); CollectionAssert.Contains(result.FinalEvaluation.Reasons.ToList(), StartupConfigurationReason.ReadFailed);
    }

    [TestMethod] public void UnknownHelperCommand_IsRejected() => Assert.IsFalse(StartupHelperCommand.TryParse(new[] { "--startup-helper", "shell" }, out _));
    [TestMethod] public void UnknownHelperMode_IsRejected() => Assert.IsFalse(StartupHelperCommand.TryParse(new[] { "--startup-helper", "create-task", "--mode", "anything" }, out _));
    [TestMethod] public void WhitelistedHelperCommand_UsesOnlyKnownArguments() { Assert.IsTrue(StartupHelperCommand.TryParse(new[] { "--startup-helper", "create-task", "--mode", "tray" }, out var command)); Assert.AreEqual("--startup-helper create-task --mode tray", command!.ToArguments()); }

    private static ActualStartupConfiguration Actual(bool registry = false, bool task = false, bool readSucceeded = true) => new(
        new(registry, "C:\\FrameHub.exe", string.Empty, null, true, true, true, readSucceeded),
        new(task, true, "C:\\FrameHub.exe", string.Empty, true, true, true, true, true, readSucceeded));

    private sealed class FakeBackend(ActualStartupConfiguration initial) : IStartupConfigurationBackend
    {
        private int _reads;
        public List<StartupOperation> Operations { get; } = [];
        public ActualStartupConfiguration? AfterOperation { get; set; }
        public StartupOperationResult? NextResult { get; set; }
        public Task<ActualStartupConfiguration> ReadActualAsync(DesiredStartupConfiguration desired, CancellationToken cancellationToken = default) => Task.FromResult(_reads++ == 0 ? initial : AfterOperation ?? initial);
        public Task<StartupOperationResult> ExecuteAsync(StartupOperation operation, DesiredStartupConfiguration desired, CancellationToken cancellationToken = default)
        {
            Operations.Add(operation);
            return Task.FromResult(NextResult ?? new StartupOperationResult(true));
        }
    }
}
