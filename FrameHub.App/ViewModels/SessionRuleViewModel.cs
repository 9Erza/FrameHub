using FrameHub.App.Helpers;
using FrameHub.App.Services;
using FrameHub.Core.Models.SessionOptimization;

namespace FrameHub.App.ViewModels;

public sealed class SessionRuleViewModel : ViewModelBase
{
    private readonly Action _onChanged;
    private readonly LocalizationService _localization;
    private bool _isEnabled;

    public BackgroundProcessRule Rule { get; }
    public string Id => Rule.Id;
    public string DisplayName => SessionDisplayText.RuleName(Rule.Id, Rule.DisplayName, _localization);
    public string Description => SessionDisplayText.RuleDescription(Rule.Id, Rule.Description, _localization);
    public string Category => Rule.Category;
    public bool IsAdvanced => Rule.IsAdvanced;
    public string ProcessNames => string.Join(", ", Rule.ProcessNames);
    public string Badge => SessionDisplayText.Badge(Rule.Id, Rule.DefaultEnabled, Rule.IsAdvanced, _localization);

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                Rule.IsEnabled = value;
                _onChanged();
            }
        }
    }

    public SessionRuleViewModel(BackgroundProcessRule rule, LocalizationService localization, Action onChanged)
    {
        Rule = rule;
        _localization = localization;
        _isEnabled = rule.IsEnabled;
        _onChanged = onChanged;
    }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(Badge));
    }
}
