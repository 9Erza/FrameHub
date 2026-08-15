namespace FrameHub.Companion.Authentication;

public static class CompanionScopes
{
    public const string ReadStatus = "read:status";
    public const string ReadTelemetry = "read:telemetry";
    public const string ReadBenchmarks = "read:benchmarks";
    public const string WriteBenchmarks = "write:benchmarks";
    public const string ReadLibrary = "read:library";
    public const string WriteLaunch = "write:launch";
    public const string ReadBackgroundApps = "read:background-apps";
    public const string WriteBackgroundApps = "write:background-apps";
    public const string ReadOptimization = "read:optimization";
    public const string WriteOptimization = "write:optimization";

    public static readonly IReadOnlySet<string> KnownScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ReadStatus,
        ReadTelemetry,
        ReadBenchmarks,
        WriteBenchmarks,
        ReadLibrary,
        WriteLaunch,
        ReadBackgroundApps,
        WriteBackgroundApps,
        ReadOptimization,
        WriteOptimization
    };


    public static bool IsValidScope(string? scope)
    {
        return !string.IsNullOrWhiteSpace(scope) && KnownScopes.Contains(scope.Trim());
    }
}
