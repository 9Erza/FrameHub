using System.Globalization;

namespace FrameHub.BenchmarkHarness;

public sealed class BenchmarkHarnessOptions
{
    public int ProcessId { get; init; }
    public int DurationSeconds { get; init; } = 30;
    public string? GameId { get; init; }
    public string? OutputRoot { get; init; }
    public string? PresentMonApiDllPath { get; init; }

    public static bool TryParse(IReadOnlyList<string> arguments, out BenchmarkHarnessOptions? options, out string? error)
    {
        options = null;
        error = null;
        int? processId = null;
        int durationSeconds = 30;
        string? gameId = null;
        string? outputRoot = null;
        string? presentMonApiDllPath = null;

        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            if (argument is "--help" or "-h")
            {
                error = Usage;
                return false;
            }

            if (index + 1 >= arguments.Count)
            {
                error = $"Missing value for '{argument}'.";
                return false;
            }
            string value = arguments[++index];
            switch (argument)
            {
                case "--pid":
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedPid) || parsedPid <= 0)
                    {
                        error = "--pid must be a positive integer.";
                        return false;
                    }
                    processId = parsedPid;
                    break;
                case "--seconds":
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out durationSeconds) || durationSeconds is < 10 or > 600)
                    {
                        error = "--seconds must be an integer from 10 through 600.";
                        return false;
                    }
                    break;
                case "--game-id":
                    gameId = value;
                    break;
                case "--presentmon-api-dll":
                    presentMonApiDllPath = value;
                    break;
                case "--output":
                    outputRoot = value;
                    break;
                case "--backend":
                    if (value != "api") { error = "--backend only accepts 'api'; the console/CSV backend has been retired."; return false; }
                    break;
                default:
                    error = $"Unknown argument '{argument}'.";
                    return false;
            }
        }

        if (!processId.HasValue)
        {
            error = "--pid is required.";
            return false;
        }

        options = new BenchmarkHarnessOptions
        {
            ProcessId = processId.Value,
            DurationSeconds = durationSeconds,
            GameId = NullIfWhiteSpace(gameId),
            PresentMonApiDllPath = NullIfWhiteSpace(presentMonApiDllPath),
            OutputRoot = NullIfWhiteSpace(outputRoot)
        };
        return true;
    }

    public const string Usage = "Usage: dotnet run --project .\\FrameHub.BenchmarkHarness -- --pid <PID> [--backend api] [--seconds 10..600] [--game-id <FrameHubLibraryItemId>] [--presentmon-api-dll <absolute path>] [--output <benchmark-root>]";

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
