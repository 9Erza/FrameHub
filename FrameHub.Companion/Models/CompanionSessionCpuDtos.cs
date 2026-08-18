namespace FrameHub.Companion.Models;

/// <summary>
/// CPU scheduling request for the active game session. Deliberately contains ONLY the
/// opaque session token and the requested scheduling selection (logical processor
/// indices) — never a PID, process name, executable path, or priority. The server
/// resolves the target from the authoritative active-game session.
/// </summary>
public sealed record CompanionSessionCpuApplyRequestDto
{
    public string? SessionToken { get; init; }
    public string? Mode { get; init; }
    public IReadOnlyList<int>? Indices { get; init; }
}

public sealed record CompanionSessionCpuResetRequestDto
{
    public string? SessionToken { get; init; }
}

public sealed record CompanionSessionCpuSelectionDto
{
    /// <summary>"affinity" or "cpu-sets".</summary>
    public string Mode { get; init; } = string.Empty;
    /// <summary>Exact logical processor mask (server-side Int64; may exceed JavaScript safe integers).</summary>
    public long Mask { get; init; }
    /// <summary>Exact logical processor indices for clients.</summary>
    public IReadOnlyList<int> Indices { get; init; } = Array.Empty<int>();
}

public sealed record CompanionSessionCpuProcessorDto
{
    public int Index { get; init; }
    public int CoreIndex { get; init; }
    public string Type { get; init; } = string.Empty;
    public bool IsECore { get; init; }
    public bool IsThread { get; init; }
}

public sealed record CompanionSessionCpuTopologyDto
{
    public IReadOnlyList<CompanionSessionCpuProcessorDto> Processors { get; init; } = Array.Empty<CompanionSessionCpuProcessorDto>();
}

public sealed record CompanionSessionCpuStateDto
{
    public bool Available { get; init; }
    /// <summary>"", "no_game", "protected_game", or "session_cpu_unavailable".</summary>
    public string UnavailableReason { get; init; } = string.Empty;
    public bool ProtectedGame { get; init; }
    public string? SessionToken { get; init; }
    public string? GameDisplayName { get; init; }
    /// <summary>"system", "profile", or "temporary-override".</summary>
    public string Source { get; init; } = "system";
    public string? ProfileName { get; init; }
    public bool TemporaryOverrideActive { get; init; }
    public CompanionSessionCpuSelectionDto? CurrentSelection { get; init; }
    public CompanionSessionCpuSelectionDto? OverrideSelection { get; init; }
    public CompanionSessionCpuTopologyDto? Topology { get; init; }
}

public sealed record CompanionSessionCpuResultDto
{
    public bool Success { get; init; }
    public string ErrorCode { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public CompanionSessionCpuStateDto? State { get; init; }
}
