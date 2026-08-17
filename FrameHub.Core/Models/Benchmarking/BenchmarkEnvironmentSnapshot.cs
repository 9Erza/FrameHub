namespace FrameHub.Core.Models.Benchmarking;

/// <summary>
/// Best-effort, point-in-time machine/OS/display context captured once per benchmark session.
/// Every field is optional; unavailable values stay null and are never fabricated.
/// Contains no personally identifying information (no hostnames, serials, credentials, or identifiers).
/// </summary>
public sealed class BenchmarkEnvironmentSnapshot
{
    public string? OsDescription { get; init; }
    public string? OsBuild { get; init; }
    public string? CpuName { get; init; }
    public string? GpuName { get; init; }
    public string? GpuDriverVersion { get; init; }
    public ulong? TotalMemoryBytes { get; init; }
    public int? DisplayWidth { get; init; }
    public int? DisplayHeight { get; init; }
    public int? DisplayRefreshRateHz { get; init; }

    public bool HasAnyValue =>
        !string.IsNullOrWhiteSpace(OsDescription)
        || !string.IsNullOrWhiteSpace(OsBuild)
        || !string.IsNullOrWhiteSpace(CpuName)
        || !string.IsNullOrWhiteSpace(GpuName)
        || !string.IsNullOrWhiteSpace(GpuDriverVersion)
        || TotalMemoryBytes is > 0
        || DisplayWidth is > 0
        || DisplayHeight is > 0
        || DisplayRefreshRateHz is > 0;
}
