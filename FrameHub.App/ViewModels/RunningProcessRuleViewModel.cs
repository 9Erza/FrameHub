using FrameHub.App.Helpers;
using FrameHub.Core.Models.SessionOptimization;

namespace FrameHub.App.ViewModels;

public sealed class RunningProcessRuleViewModel : ViewModelBase
{
    private readonly Action _onChanged;
    private bool _isEnabled;

    public RunningProcessGroup ProcessGroup { get; }
    public string NormalizedProcessName => ProcessGroup.NormalizedProcessName;
    public string ProcessName => ProcessGroup.ProcessName;
    public int InstanceCount => ProcessGroup.InstanceCount;
    public string ExamplePath => ProcessGroup.ExamplePath ?? "—";

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                _onChanged();
            }
        }
    }

    public RunningProcessRuleViewModel(RunningProcessGroup processGroup, bool isEnabled, Action onChanged)
    {
        ProcessGroup = processGroup;
        _isEnabled = isEnabled;
        _onChanged = onChanged;
    }
}
