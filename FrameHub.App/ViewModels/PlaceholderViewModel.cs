using FrameHub.App.Helpers;
using FrameHub.App.Services;
using System.Collections.ObjectModel;

namespace FrameHub.App.ViewModels;

public sealed class PlaceholderViewModel : ViewModelBase
{
    private readonly LocalizationService _localization;
    private readonly IReadOnlyList<string> _plannedItemKeys;

    public required string TitleKey { get; init; }
    public required string SubtitleKey { get; init; }
    public required string StatusKey { get; init; }
    public required string DescriptionKey { get; init; }
    public bool IsPlanned { get; init; }

    public string Title => _localization.T(TitleKey);
    public string Subtitle => _localization.T(SubtitleKey);
    public string Status => _localization.T(StatusKey);
    public string Description => _localization.T(DescriptionKey);
    public string CurrentStateTitle => _localization.T("Placeholder.CurrentState.Title");
    public string CurrentStateDescription => _localization.T("Placeholder.CurrentState.Description");
    public string PlannedCapabilitiesTitle => _localization.T("Placeholder.PlannedCapabilities.Title");
    public string SafetyRuleTitle => _localization.T("Placeholder.SafetyRule.Title");
    public string SafetyRuleDescription => _localization.T("Placeholder.SafetyRule.Description");

    public ObservableCollection<string> PlannedItems { get; } = new();

    public PlaceholderViewModel(LocalizationService localization, IReadOnlyList<string> plannedItemKeys)
    {
        _localization = localization;
        _plannedItemKeys = plannedItemKeys;
    }

    public void RefreshTexts()
    {
        PlannedItems.Clear();
        foreach (var key in _plannedItemKeys)
        {
            PlannedItems.Add(_localization.T(key));
        }

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(CurrentStateTitle));
        OnPropertyChanged(nameof(CurrentStateDescription));
        OnPropertyChanged(nameof(PlannedCapabilitiesTitle));
        OnPropertyChanged(nameof(SafetyRuleTitle));
        OnPropertyChanged(nameof(SafetyRuleDescription));
    }
}
