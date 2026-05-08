using System;

namespace FrameHub.Core.Models
{
    /// <summary>
    /// Stable identifier for a running process instance.
    /// PID alone is not enough because Windows can reuse PIDs after a process exits.
    /// </summary>
    public readonly record struct ProcessInstanceKey(int ProcessId, DateTime StartTimeUtc);
}
