using System.Collections.Generic;
using System.Diagnostics;

namespace FrameHub.Core.Models
{
    /// <summary>
    /// Lightweight snapshot of grouped running processes used by the UI.
    /// Contains values only; no live Process handles are kept.
    /// </summary>
    public sealed class ProcessGroupSnapshot
    {
        public string Name { get; set; } = string.Empty;
        public int FirstProcessId { get; set; }
        public int InstanceCount { get; set; }
        public long TotalMemoryBytes { get; set; }
        public double CpuUsagePercent { get; set; }
        public ProcessPriorityClass Priority { get; set; } = ProcessPriorityClass.Normal;
        public List<ProcessInstanceKey> Instances { get; set; } = new();
    }
}
