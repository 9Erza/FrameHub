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

    private string _countBadge = string.Empty;

    /// <summary>
    /// Live numeric badge (games in library, enabled profiles). Empty hides the badge.
    /// Takes precedence over the static localised <see cref="Badge"/> when set.
    /// </summary>
    public string CountBadge
    {
        get => _countBadge;
        set
        {
            if (SetProperty(ref _countBadge, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CountBadgeVisibility));
                OnPropertyChanged(nameof(EffectiveBadge));
                OnPropertyChanged(nameof(EffectiveBadgeVisibility));
            }
        }
    }

    public Visibility CountBadgeVisibility => string.IsNullOrWhiteSpace(_countBadge) ? Visibility.Collapsed : Visibility.Visible;
    public string EffectiveBadge => string.IsNullOrWhiteSpace(_countBadge) ? Badge : _countBadge;
    public Visibility EffectiveBadgeVisibility => string.IsNullOrWhiteSpace(EffectiveBadge) ? Visibility.Collapsed : Visibility.Visible;
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
        OnPropertyChanged(nameof(EffectiveBadge));
        OnPropertyChanged(nameof(EffectiveBadgeVisibility));
        OnPropertyChanged(nameof(CountBadgeVisibility));
    }
}
