using FrameHub.App.Helpers;
using FrameHub.App.Services;
using System.Windows;

namespace FrameHub.App.ViewModels;

public sealed class LanguageOptionViewModel : ViewModelBase
{
    private readonly LocalizationService _localization;
    private bool _isSelected;

    public string Code { get; }
    public string NameKey { get; }
    public string FlagSource { get; }

    public string DisplayName => _localization.T(NameKey);
    public Visibility SelectedVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!SetProperty(ref _isSelected, value)) return;
            OnPropertyChanged(nameof(SelectedVisibility));
        }
    }

    public LanguageOptionViewModel(string code, string nameKey, string flagSource, LocalizationService localization)
    {
        Code = code;
        NameKey = nameKey;
        FlagSource = flagSource;
        _localization = localization;
    }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(DisplayName));
    }
}
