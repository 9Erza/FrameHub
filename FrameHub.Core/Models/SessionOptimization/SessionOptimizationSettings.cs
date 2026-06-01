using System.Collections.Generic;

namespace FrameHub.Core.Models.SessionOptimization;

public sealed class SessionOptimizationSettings
{
    public bool AutoModeEnabled { get; set; }
    public string? SelectedGameId { get; set; }
    public bool HideTaskbarDuringSession { get; set; }
    public bool ShowManualProcessList { get; set; }

    // Kept for compatibility with earlier builds of the module.
    public bool ShowConfirmationBeforeManualStart { get; set; } = true;
    public List<string> AutoEnabledGameIds { get; set; } = new();
    public Dictionary<string, bool> RuleEnabledStates { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, SessionGameSuspendSettings> GameSettings { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);
}
