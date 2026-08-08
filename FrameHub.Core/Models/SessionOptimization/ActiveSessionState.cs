using System;
using System.Collections.Generic;

namespace FrameHub.Core.Models.SessionOptimization;

public sealed class ActiveSessionState
{
    public bool IsActive { get; set; }
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public string Trigger { get; set; } = "Manual";
    public string? GameId { get; set; }
    public string? GameName { get; set; }
    public string? GameProcessName { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public bool TaskbarHidden { get; set; }
    public bool IsRecoveryPending { get; set; }
    public List<SuspendedProcessRecord> SuspendedProcesses { get; set; } = new();
}
