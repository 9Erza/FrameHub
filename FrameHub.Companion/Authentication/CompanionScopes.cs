namespace FrameHub.Companion.Authentication;

public static class CompanionScopes
{
    public const string ReadStatus = "read:status";
    public const string ReadTelemetry = "read:telemetry";
    public const string ReadBenchmarks = "read:benchmarks";
    public const string WriteBenchmarks = "write:benchmarks";

    public static readonly IReadOnlySet<string> KnownScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ReadStatus,
        ReadTelemetry,
        ReadBenchmarks,
        WriteBenchmarks
    };


    public static bool IsValidScope(string? scope)
    {
        return !string.IsNullOrWhiteSpace(scope) && KnownScopes.Contains(scope.Trim());
    }
}
