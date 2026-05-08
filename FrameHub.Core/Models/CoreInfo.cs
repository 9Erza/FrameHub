using FrameHub.Core.Helpers;

namespace FrameHub.Core.Models
{
    /// <summary>
    /// Represents a logical processor shown in the core selection panel.
    /// </summary>
    public class CoreInfo : ViewModelBase
    {
        private bool _isChecked = true;
        private double _loadUsage;

        public int Index { get; set; }
        public int Group { get; set; }
        public int CoreIndex { get; set; }
        public byte EfficiencyClass { get; set; }
        public string TypeTag { get; set; } = "[P]";

        public string DisplayName => Group == 0
            ? $"Core {Index} {TypeTag}"
            : $"G{Group}: Core {Index} {TypeTag}";

        public bool IsThread { get; set; }
        public bool IsECore { get; set; }

        public bool IsChecked
        {
            get => _isChecked;
            set => SetProperty(ref _isChecked, value);
        }

        public double LoadUsage
        {
            get => _loadUsage;
            set => SetProperty(ref _loadUsage, value);
        }
    }
}
