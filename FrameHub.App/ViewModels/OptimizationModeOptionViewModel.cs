using FrameHub.App.Helpers;
using FrameHub.App.Services;
using FrameHub.Core.Models;

namespace FrameHub.App.ViewModels;

public sealed class OptimizationModeOptionViewModel : ViewModelBase
{
    private readonly LocalizationService _localization;

    public OptimizationModeOptionViewModel(OptimizationMode mode, LocalizationService localization)
    {
        Mode = mode;
        _localization = localization;
    }

    public OptimizationMode Mode { get; }

    public string DisplayName => Mode switch
    {
        OptimizationMode.CpuSets => _localization.T("OptimizationMode.CpuSets"),
        _ => _localization.T("OptimizationMode.Affinity")
    };

    public void RefreshTexts() => OnPropertyChanged(nameof(DisplayName));

    public override string ToString() => DisplayName;
}
