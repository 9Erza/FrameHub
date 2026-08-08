namespace FrameHub.Core.Models;

public enum StartupConfigurationState { Disabled, Registry, ElevatedScheduledTask, Conflict, Broken }
public enum StartupConfigurationReason { MissingRegistry, MissingScheduledTask, WrongExecutable, WrongArguments, ExecutableMissing, TaskDisabled, MissingLogonTrigger, MissingElevatedRunLevel, UnexpectedRegistryEntry, UnexpectedScheduledTask, ReadFailed }
public enum StartupOperation { RemoveRegistry, RemoveScheduledTask, CreateOrUpdateRegistry, CreateOrUpdateScheduledTask }

public sealed record DesiredStartupConfiguration(bool StartWithWindows, StartupWindowMode WindowMode, bool RunElevated)
{
    public string Arguments => GetArguments(WindowMode);

    public static string GetArguments(StartupWindowMode mode) => mode switch
    {
        StartupWindowMode.Minimized => "--minimized",
        StartupWindowMode.Tray => "--tray",
        _ => string.Empty
    };
}

public sealed record RegistryStartupSnapshot(bool Exists, string? ExecutablePath, string Arguments, string? RawCommand, bool ExecutableExists, bool IsExpectedExecutable, bool IsExpectedArguments, bool ReadSucceeded = true, string? Error = null);
public sealed record ScheduledTaskStartupSnapshot(bool Exists, bool Enabled, string? ExecutablePath, string Arguments, bool ExecutableExists, bool HasLogonTrigger, bool IsElevated, bool IsExpectedExecutable, bool IsExpectedArguments, bool ReadSucceeded = true, string? Error = null);
public sealed record ActualStartupConfiguration(RegistryStartupSnapshot Registry, ScheduledTaskStartupSnapshot Task);
public sealed record StartupConfigurationEvaluation(StartupConfigurationState State, IReadOnlyList<StartupConfigurationReason> Reasons, IReadOnlyList<StartupOperation> RequiredOperations);
