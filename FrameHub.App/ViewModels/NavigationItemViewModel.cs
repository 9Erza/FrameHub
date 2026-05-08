using FrameHub.App.Helpers;
using FrameHub.App.Services;
using System.Windows;

namespace FrameHub.App.ViewModels;

public sealed class NavigationItemViewModel : ViewModelBase
{
    private readonly LocalizationService _localization;
    private bool _isSelected;

    public required string Key { get; init; }
    public required string TitleKey { get; init; }
    public required string Icon { get; init; }
    public string BadgeKey { get; init; } = string.Empty;
    public bool IsPlanned { get; init; }

    public string Title => _localization.T(TitleKey);
    public string Badge => string.IsNullOrWhiteSpace(BadgeKey) ? string.Empty : _localization.T(BadgeKey);
    public Visibility BadgeVisibility => string.IsNullOrWhiteSpace(Badge) ? Visibility.Collapsed : Visibility.Visible;
    public double ItemOpacity => IsPlanned ? 0.55 : 1.0;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public NavigationItemViewModel(LocalizationService localization)
    {
        _localization = localization;
    }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Badge));
        OnPropertyChanged(nameof(BadgeVisibility));
    }
}
