using FrameHub.Core.Helpers;

namespace FrameHub.Core.Models
{
    /// <summary>
    /// Represents a grouped process entry in the UI.
    /// Several running PIDs can share the same process name.
    /// </summary>
    public class ProcessItem : ViewModelBase
    {
        private string _cpuUsage = "0%";
        private string _ramUsageMB = "0 MB";
        private bool _isOptimized;
        private string _priority = "Normal";
        private string _modeTag = string.Empty;
        private int _instanceCount = 1;

        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string DisplayName => string.IsNullOrEmpty(ModeTag) ? Name : $"{Name} [{ModeTag}]";

        public int InstanceCount
        {
            get => _instanceCount;
            set => SetProperty(ref _instanceCount, value);
        }

        public string ModeTag
        {
            get => _modeTag;
            set
            {
                if (SetProperty(ref _modeTag, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public string CpuUsage
        {
            get => _cpuUsage;
            set => SetProperty(ref _cpuUsage, value);
        }

        public string RamUsageMB
        {
            get => _ramUsageMB;
            set => SetProperty(ref _ramUsageMB, value);
        }

        public string Priority
        {
            get => _priority;
            set => SetProperty(ref _priority, value);
        }

        public bool IsOptimized
        {
            get => _isOptimized;
            set => SetProperty(ref _isOptimized, value);
        }
    }
}
