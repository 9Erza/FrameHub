namespace FrameHub.Core.Services;

public static class StartupCommandParser
{
    public static (string? ExecutablePath, string Arguments) Parse(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return (null, string.Empty);
        string value = command.Trim();
        if (value[0] == '"')
        {
            int closing = value.IndexOf('"', 1);
            return closing < 0 ? (null, string.Empty) : (value[1..closing], value[(closing + 1)..].Trim());
        }
        int argumentStart = value.IndexOf(" --", StringComparison.Ordinal);
        return argumentStart < 0 ? (value, string.Empty) : (value[..argumentStart].Trim(), value[(argumentStart + 1)..].Trim());
    }
}
