using System.Collections.Generic;

namespace FrameHub.Core.Models.SessionOptimization;

public sealed class SessionGameSuspendSettings
{
    public bool AutoEnabled { get; set; }

    /// <summary>
    /// True after the user explicitly changes base suspend rules for this game.
    /// This prevents legacy/global rule defaults from overriding per-game choices.
    /// </summary>
    public bool RulesConfigured { get; set; }

    /// <summary>
    /// Enables additional manually selected process rules for this specific game.
    /// When false, saved custom process names are remembered but ignored.
    /// </summary>
    public bool ManualProcessRulesEnabled { get; set; }

    public Dictionary<string, bool> RuleEnabledStates { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, bool> CustomProcessEnabledStates { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);
}
