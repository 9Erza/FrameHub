using System;

namespace FrameHub.Core.Models.Library;

/// <summary>
/// A point-in-time identity for one process matched to a trusted Library item.
/// PID is deliberately paired with process start time, name, and executable path.
/// </summary>
public sealed record LibraryProcessIdentity
{
    public int ProcessId { get; init; }
    public DateTime StartTimeUtc { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string? ExecutablePath { get; init; }
}
