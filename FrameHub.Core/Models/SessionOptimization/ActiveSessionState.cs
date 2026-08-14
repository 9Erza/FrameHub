using System;
using System.Collections.Generic;

namespace FrameHub.Core.Models.SessionOptimization;

public enum SessionRecoveryPhase
{
    Active = 0,
    Prepared = 1,
    Applying = 2,
    Restoring = 3,
    RollingBack = 4
}

public sealed class TaskbarVisibilityState
{
    public bool PrimaryTaskbarFound { get; set; }
    public bool PrimaryTaskbarVisible { get; set; }
    public List<bool> SecondaryTaskbarsVisible { get; set; } = new();
}

public sealed class ActiveSessionState
{
    public bool IsActive { get; set; }
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public string Trigger { get; set; } = "Manual";
    public string? GameId { get; set; }
    public string? GameName { get; set; }
    public string? GameProcessName { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public SessionRecoveryPhase RecoveryPhase { get; set; } = SessionRecoveryPhase.Active;
    public bool TaskbarHideRequested { get; set; }
    public bool TaskbarMutationPending { get; set; }
    public bool TaskbarHidden { get; set; }
    public TaskbarVisibilityState? OriginalTaskbarVisibility { get; set; }
    public bool IsRecoveryPending { get; set; }
    public List<SuspendedProcessRecord> PlannedProcesses { get; set; } = new();
    public SuspendedProcessRecord? PendingSuspension { get; set; }
    public SuspendedProcessRecord? PendingResume { get; set; }
    public List<SuspendedProcessRecord> AmbiguousProcesses { get; set; } = new();
    public List<SuspendedProcessRecord> SuspendedProcesses { get; set; } = new();
}
