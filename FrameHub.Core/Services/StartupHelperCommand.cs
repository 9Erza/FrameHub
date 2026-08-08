using FrameHub.Core.Models;

namespace FrameHub.Core.Services;

public enum StartupHelperAction { CreateTask, RemoveTask }
public sealed record StartupHelperCommand(StartupHelperAction Action, StartupWindowMode Mode)
{
    public static bool TryParse(IReadOnlyList<string> args, out StartupHelperCommand? command)
    {
        command = null;
        if (args.Count < 2 || !string.Equals(args[0], "--startup-helper", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(args[1], "remove-task", StringComparison.OrdinalIgnoreCase) && args.Count == 2)
        {
            command = new(StartupHelperAction.RemoveTask, StartupWindowMode.Normal);
            return true;
        }
        if (!string.Equals(args[1], "create-task", StringComparison.OrdinalIgnoreCase) || args.Count != 4 || !string.Equals(args[2], "--mode", StringComparison.OrdinalIgnoreCase)) return false;
        if (!Enum.TryParse<StartupWindowMode>(args[3], true, out var mode) || !Enum.IsDefined(mode)) return false;
        command = new(StartupHelperAction.CreateTask, mode);
        return true;
    }

    public string ToArguments() => Action == StartupHelperAction.RemoveTask
        ? "--startup-helper remove-task"
        : $"--startup-helper create-task --mode {Mode.ToString().ToLowerInvariant()}";
}
