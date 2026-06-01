using System.Collections.Generic;

namespace FrameHub.Core.Models.SessionOptimization;

public sealed class SessionGameSuspendSettings
{
    public bool AutoEnabled { get; set; }
    public Dictionary<string, bool> RuleEnabledStates { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, bool> CustomProcessEnabledStates { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);
}
