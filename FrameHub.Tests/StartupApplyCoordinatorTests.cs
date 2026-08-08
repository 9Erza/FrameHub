using FrameHub.Core.Logging;
using FrameHub.Core.Models;
using FrameHub.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public class StartupApplyCoordinatorTests
{
    [TestMethod]
    public async Task NonElevatedMinimizedStartup_ExecutesRegistryWriteAndVerifiesRegistry()
    {
        var backend = new StatefulBackend();
        var coordinator = new StartupApplyCoordinator(backend, new TestLogger());
        var desired = new DesiredStartupConfiguration(true, StartupWindowMode.Minimized, false);

        var result = await coordinator.ApplyLatestAsync(desired);

        Assert.IsTrue(result.Success);
        CollectionAssert.AreEqual(new[] { StartupOperation.CreateOrUpdateRegistry }, backend.Operations);
        Assert.AreEqual("--minimized", backend.LastRegistryArguments);
        Assert.AreEqual(StartupConfigurationState.Registry, result.FinalEvaluation.State);
        Assert.IsTrue(backend.TaskAbsentReadCount >= 2);
    }

    [TestMethod]
    public async Task DesiredChangedWhileBusy_LatestDesiredIsAppliedAfterCurrentRun()
    {
        var backend = new StatefulBackend { BlockFirstRead = true };
        var coordinator = new StartupApplyCoordinator(backend, new TestLogger());
        var first = coordinator.ApplyLatestAsync(new(true, StartupWindowMode.Normal, false));
        await backend.FirstReadStarted.Task;
        var latest = coordinator.ApplyLatestAsync(new(true, StartupWindowMode.Minimized, false));
        backend.AllowFirstRead.TrySetResult();

        await Task.WhenAll(first, latest);

        Assert.AreEqual("--minimized", backend.LastRegistryArguments);
        Assert.AreEqual(StartupWindowMode.Minimized, backend.AppliedDesired.Last().WindowMode);
    }

    private sealed class StatefulBackend : IStartupConfigurationBackend
    {
        private bool _registryExists;
        private int _reads;
        public bool BlockFirstRead { get; init; }
        public TaskCompletionSource FirstReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowFirstRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<StartupOperation> Operations { get; } = [];
        public List<DesiredStartupConfiguration> AppliedDesired { get; } = [];
        public string? LastRegistryArguments { get; private set; }
        public int TaskAbsentReadCount { get; private set; }

        public async Task<ActualStartupConfiguration> ReadActualAsync(DesiredStartupConfiguration desired, CancellationToken cancellationToken = default)
        {
            if (BlockFirstRead && _reads++ == 0)
            {
                FirstReadStarted.TrySetResult();
                await AllowFirstRead.Task.WaitAsync(cancellationToken);
            }
            TaskAbsentReadCount++;
            return new(
                new(_registryExists, "C:\\FrameHub.App.exe", LastRegistryArguments ?? string.Empty, null, true, true, _registryExists && string.Equals(LastRegistryArguments, desired.Arguments, StringComparison.Ordinal), true),
                new(false, false, null, string.Empty, false, false, false, false, false, true));
        }

        public Task<StartupOperationResult> ExecuteAsync(StartupOperation operation, DesiredStartupConfiguration desired, CancellationToken cancellationToken = default)
        {
            Operations.Add(operation);
            AppliedDesired.Add(desired);
            if (operation == StartupOperation.CreateOrUpdateRegistry)
            {
                _registryExists = true;
                LastRegistryArguments = desired.Arguments;
            }
            return Task.FromResult(new StartupOperationResult(true));
        }
    }

    private sealed class TestLogger : ILogger
    {
        public ILogLevel LogLevel { get; set; } = FrameHub.Core.Logging.LogLevel.Info;
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? ex = null) { }
        public void Fatal(string message, Exception? ex = null) { }
        public void LogException(Exception ex) { }
        public void Dispose() { }
    }
}
