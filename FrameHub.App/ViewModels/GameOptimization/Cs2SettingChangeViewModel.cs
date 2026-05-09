using FrameHub.App.Helpers;
using FrameHub.App.Services;
using FrameHub.Core.Models.GameOptimization;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace FrameHub.App.ViewModels.GameOptimization;

public sealed class Cs2SettingOptionViewModel
{
    public string Value { get; }
    public string DisplayName { get; }

    public Cs2SettingOptionViewModel(string value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public override string ToString() => DisplayName;
}

public sealed class Cs2SettingChangeViewModel : ViewModelBase
{
    private readonly LocalizationService _localization;
    private Cs2SettingOptionViewModel? _selectedOption;

    public event EventHandler? TargetValueChangedByUser;

    public GameSettingChange Change { get; }

    public string DisplayName => _localization.T($"CS2.Setting.{Change.Key}");
    public string Key => Change.Key;
    public string CurrentValue => Change.CurrentValue;
    public string RecommendedValue => Change.RecommendedValue;
    public string TargetValue => string.IsNullOrWhiteSpace(Change.TargetValue) ? Change.RecommendedValue : Change.TargetValue;
    public string CurrentDisplayValue => FormatValue(Change.Key, Change.CurrentValue);
    public string RecommendedDisplayValue => FormatValue(Change.Key, Change.RecommendedValue);
    public string Description => _localization.T($"CS2.Description.{Change.Key}");
    public string RiskText => Change.RiskLevel.ToString();
    public string StatusText => _localization.T($"CS2.Status.{Change.Status}");
    public string ApplyHint => CanApply ? _localization.T("CS2.ApplyThisSetting") : _localization.T("CS2.ReadOnlySetting");
    public bool IsOptional => Change.IsOptional;
    public bool CanApply => Change.CanApply && Change.Status != GameOptimizationSettingStatus.NotDetected && Change.Status != GameOptimizationSettingStatus.ReadOnly;
    public bool HasOptions => Options.Count > 0;

    public ObservableCollection<Cs2SettingOptionViewModel> Options { get; } = new();

    public Cs2SettingOptionViewModel? SelectedOption
    {
        get => _selectedOption;
        set
        {
            if (!SetProperty(ref _selectedOption, value) || value == null) return;
            Change.TargetValue = value.Value;
            RefreshStatusAfterTargetChange();
            OnPropertyChanged(nameof(TargetValue));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IsSelected));
            TargetValueChangedByUser?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsSelected
    {
        get => Change.IsSelected;
        set
        {
            if (!CanApply) value = false;
            if (Change.IsSelected == value) return;
            Change.IsSelected = value;
            OnPropertyChanged();
        }
    }

    public Cs2SettingChangeViewModel(GameSettingChange change, LocalizationService localization)
    {
        Change = change;
        _localization = localization;

        foreach (var option in change.Options)
        {
            string display = !string.IsNullOrWhiteSpace(option.DisplayOverride)
                ? LocalizeDisplayOverride(option.DisplayOverride)
                : FormatValue(change.Key, option.Value);
            Options.Add(new Cs2SettingOptionViewModel(option.Value, display));
        }

        if (string.IsNullOrWhiteSpace(change.TargetValue))
        {
            change.TargetValue = change.RecommendedValue;
        }

        _selectedOption = Options.FirstOrDefault(x => x.Value.Equals(change.TargetValue, StringComparison.OrdinalIgnoreCase))
            ?? Options.FirstOrDefault(x => x.Value.Equals(change.RecommendedValue, StringComparison.OrdinalIgnoreCase));
    }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(CurrentDisplayValue));
        OnPropertyChanged(nameof(RecommendedDisplayValue));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(ApplyHint));
    }

    private void RefreshStatusAfterTargetChange()
    {
        if (string.Equals(Change.CurrentValue, "not found", StringComparison.OrdinalIgnoreCase))
        {
            Change.Status = GameOptimizationSettingStatus.NotDetected;
            Change.IsSelected = false;
            return;
        }

        string target = string.IsNullOrWhiteSpace(Change.TargetValue) ? Change.RecommendedValue : Change.TargetValue;
        bool matches = string.Equals(Change.CurrentValue, target, StringComparison.OrdinalIgnoreCase);
        if (Change.IsOptional)
        {
            Change.Status = matches ? GameOptimizationSettingStatus.MatchesBaseline : GameOptimizationSettingStatus.OptionalPreference;
            Change.IsSelected = !matches;
            return;
        }

        Change.Status = matches ? GameOptimizationSettingStatus.MatchesBaseline : GameOptimizationSettingStatus.DifferentFromBaseline;
        Change.IsSelected = !matches;
    }

    private string FormatValue(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("not found", StringComparison.OrdinalIgnoreCase))
        {
            return _localization.T("CS2.Value.NotDetected");
        }

        return key switch
        {
            "cs2.resolution" => FormatResolution(value),
            "cs2.display_mode" => _localization.T($"CS2.Value.Display.{value}"),
            "cs2.msaa_mode" => _localization.T($"CS2.Value.MSAA.Mode.{value}"),
            "r_player_visibility_mode" => FormatOnOff(value),
            "setting.mat_vsync" => FormatOnOff(value),
            "setting.r_low_latency" => FormatOnOff(value),
            "setting.videocfg_shadow_quality" => _localization.T($"CS2.Value.Quality.{value}"),
            "setting.videocfg_dynamic_shadows" => _localization.T($"CS2.Value.DynamicShadows.{value}"),
            "setting.videocfg_texture_detail" => _localization.T($"CS2.Value.Quality.{value}"),
            "setting.r_texturefilteringquality" => _localization.T($"CS2.Value.TextureFiltering.{value}"),
            "setting.shaderquality" => _localization.T($"CS2.Value.Quality.{value}"),
            "setting.videocfg_particle_detail" => _localization.T($"CS2.Value.Quality.{value}"),
            "setting.videocfg_ao_detail" => _localization.T($"CS2.Value.AmbientOcclusion.{value}"),
            "setting.videocfg_hdr_detail" => _localization.T($"CS2.Value.HDR.{value}"),
            "setting.videocfg_fsr_detail" => _localization.T($"CS2.Value.FSR.{value}"),
            _ => value
        };
    }

    private string FormatResolution(string value)
    {
        string resolution = value.Split('|')[0];
        return resolution switch
        {
            "1920x1080" => "1920x1080 (16:9)",
            "1440x1080" => "1440x1080 (4:3)",
            "1280x960" => "1280x960 (4:3)",
            "1680x1050" => "1680x1050 (16:10)",
            "1024x768" => "1024x768 (4:3)",
            "1280x1024" => _localization.T("CS2.Value.Resolution.1280x1024"),
            _ => resolution
        };
    }

    private string FormatOnOff(string value)
    {
        return value == "0" ? _localization.T("CS2.Value.Off") : value == "1" ? _localization.T("CS2.Value.On") : value;
    }

    private string LocalizeDisplayOverride(string value)
    {
        if (value.Contains("CS2 shows", StringComparison.OrdinalIgnoreCase) && _localization.CurrentLanguage == "pl")
        {
            return "1280x1024 (5:4; CS2 pokazuje jako 4:3)";
        }

        return value;
    }
}
