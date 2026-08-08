using System;

namespace FrameHub.Core.Models.SessionOptimization;

public sealed class SuspendedProcessRecord
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public DateTime ProcessStartTimeUtc { get; set; }
    public string RuleId { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string? ExecutablePath { get; set; }
    public DateTime SuspendedAtUtc { get; set; } = DateTime.UtcNow;
}
