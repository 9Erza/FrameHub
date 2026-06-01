namespace FrameHub.Core.Models.SessionOptimization;

public sealed class SuspendCandidate
{
    public string RuleId { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public string? ExecutablePath { get; set; }
    public bool IsExplorer { get; set; }
}
