using System.Collections.Generic;

namespace FrameHub.Core.Models.SessionOptimization;

public sealed class SessionActionResult
{
    public int SuccessCount { get; set; }
    public int ResolvedCount { get; set; }
    public int StaleProcessCount { get; set; }
    public int FailedCount { get; set; }
    public List<SuspendedProcessRecord> Records { get; set; } = new();
    public List<string> Messages { get; set; } = new();
}
