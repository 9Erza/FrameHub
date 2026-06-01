namespace FrameHub.Core.Models.SessionOptimization;

public sealed class RunningProcessGroup
{
    public string NormalizedProcessName { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public int InstanceCount { get; set; }
    public string? ExamplePath { get; set; }
}
