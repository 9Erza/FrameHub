using FrameHub.Core.Models;

namespace FrameHub.Core.Services;

public static class StartupConfigurationPlanner
{
    public static StartupConfigurationEvaluation Evaluate(DesiredStartupConfiguration desired, ActualStartupConfiguration actual)
    {
        var reasons = new List<StartupConfigurationReason>();
        var operations = new List<StartupOperation>();
        if (!actual.Registry.ReadSucceeded || !actual.Task.ReadSucceeded)
            return new(StartupConfigurationState.Broken, new[] { StartupConfigurationReason.ReadFailed }, operations);
        if (!desired.StartWithWindows)
        {
            if (actual.Registry.Exists) { reasons.Add(StartupConfigurationReason.UnexpectedRegistryEntry); operations.Add(StartupOperation.RemoveRegistry); }
            if (actual.Task.Exists) { reasons.Add(StartupConfigurationReason.UnexpectedScheduledTask); operations.Add(StartupOperation.RemoveScheduledTask); }
            var state = operations.Count == 0 ? StartupConfigurationState.Disabled
                : actual.Registry.Exists && actual.Task.Exists ? StartupConfigurationState.Conflict
                : StartupConfigurationState.Broken;
            return new(state, reasons, operations);
        }
        if (actual.Registry.Exists && actual.Task.Exists)
        {
            return new(StartupConfigurationState.Conflict, Array.Empty<StartupConfigurationReason>(), desired.RunElevated
                ? new[] { StartupOperation.CreateOrUpdateScheduledTask, StartupOperation.RemoveRegistry }
                : new[] { StartupOperation.RemoveScheduledTask, StartupOperation.CreateOrUpdateRegistry });
        }
        if (desired.RunElevated)
        {
            if (actual.Registry.Exists) { reasons.Add(StartupConfigurationReason.UnexpectedRegistryEntry); }
            if (!IsValidTask(actual.Task)) reasons.AddRange(TaskReasons(actual.Task));
            if (reasons.Count == 0) return new(StartupConfigurationState.ElevatedScheduledTask, reasons, operations);
            operations.Add(StartupOperation.CreateOrUpdateScheduledTask);
            if (actual.Registry.Exists) operations.Add(StartupOperation.RemoveRegistry);
            return new(StartupConfigurationState.Broken, reasons, operations);
        }
        if (actual.Task.Exists) reasons.Add(StartupConfigurationReason.UnexpectedScheduledTask);
        if (!IsValidRegistry(actual.Registry)) reasons.AddRange(RegistryReasons(actual.Registry));
        if (reasons.Count == 0) return new(StartupConfigurationState.Registry, reasons, operations);
        if (actual.Task.Exists) operations.Add(StartupOperation.RemoveScheduledTask);
        operations.Add(StartupOperation.CreateOrUpdateRegistry);
        return new(StartupConfigurationState.Broken, reasons, operations);
    }

    private static bool IsValidRegistry(RegistryStartupSnapshot item) => item.Exists && item.ExecutableExists && item.IsExpectedExecutable && item.IsExpectedArguments;
    private static bool IsValidTask(ScheduledTaskStartupSnapshot item) => item.Exists && item.Enabled && item.ExecutableExists && item.HasLogonTrigger && item.IsElevated && item.IsExpectedExecutable && item.IsExpectedArguments;
    private static IEnumerable<StartupConfigurationReason> RegistryReasons(RegistryStartupSnapshot item)
    {
        if (!item.Exists) yield return StartupConfigurationReason.MissingRegistry;
        else { if (!item.ExecutableExists) yield return StartupConfigurationReason.ExecutableMissing; if (!item.IsExpectedExecutable) yield return StartupConfigurationReason.WrongExecutable; if (!item.IsExpectedArguments) yield return StartupConfigurationReason.WrongArguments; }
    }
    private static IEnumerable<StartupConfigurationReason> TaskReasons(ScheduledTaskStartupSnapshot item)
    {
        if (!item.Exists) { yield return StartupConfigurationReason.MissingScheduledTask; yield break; }
        if (!item.ExecutableExists) yield return StartupConfigurationReason.ExecutableMissing; if (!item.Enabled) yield return StartupConfigurationReason.TaskDisabled; if (!item.HasLogonTrigger) yield return StartupConfigurationReason.MissingLogonTrigger; if (!item.IsElevated) yield return StartupConfigurationReason.MissingElevatedRunLevel; if (!item.IsExpectedExecutable) yield return StartupConfigurationReason.WrongExecutable; if (!item.IsExpectedArguments) yield return StartupConfigurationReason.WrongArguments;
    }
}
