using System.Collections.Generic;

namespace FrameHub.Core.Models
{
    public sealed class OptimizationResult
    {
        public bool Success { get; set; }
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public OptimizationMode Mode { get; set; } = OptimizationMode.Affinity;
        public string Priority { get; set; } = "Normal";
        public string Message { get; set; } = string.Empty;
        public bool CoreApplied { get; set; }
        public bool PriorityApplied { get; set; }
        public bool RequiresAdmin { get; set; }
        public int? Win32Error { get; set; }
    }

    public sealed class OptimizationBatchResult
    {
        public int Total { get; set; }
        public int Successful { get; set; }
        public int Failed { get; set; }
        public List<OptimizationResult> Results { get; set; } = new();

        public bool HasAnySuccess => Successful > 0;
    }
}
