using FrameHub.App.Helpers;
using FrameHub.App.Services;
using FrameHub.Core.Logging;
using FrameHub.Core.Models;
using FrameHub.Core.Services;
using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace FrameHub.App.ViewModels;

/// <summary>
/// First-run setup wizard (fresh installs and interrupted onboardings only;
/// upgrades never see it — see <see cref="AppRuntimeService.IsFirstRun"/>).
/// Works on a settings draft that is persisted only on Finish/Skip through the
/// normal runtime path, so cancelling the window can never corrupt settings.
/// Optional modules default OFF (light footprint); core process/CPU control
/// and the profile watcher are always on and are not toggleable here.
/// </summary>
public sealed class FirstRunSetupViewModel : ViewModelBase
{
    public const int StepCount = 6;

    private readonly LocalizationService _localization;
    private readonly AppRuntimeService _runtime;
    private readonly StartupApplyCoordinator _startupApplyCoordinator;
    private int _step;
    private bool _isApplying;
    private string _startupStatus = string.Empty;

    public event Action? CloseRequested;

    public AppSettings Draft { get; }

    public FirstRunSetupViewModel(LocalizationService localization, AppRuntimeService runtime)
    {
        _localization = localization;
        _runtime = runtime;
        _startupApplyCoordinator =
            new StartupApplyCoordinator(runtime.SettingsService, LoggerService.Instance);
        Draft = SettingsViewModel.Clone(runtime.Settings);
        if (_runtime.IsFirstRun)
        {
            // Fresh installs start from defaults, but force the light baseline
            // explicitly: every optional module OFF unless the user opts in.
            // When reopened later from Settings the draft keeps current values.
            Draft.HardwareMonitorEnabled = false;
            Draft.LiveTelemetryEnabled = false;
            Draft.CompanionEnabled = false;
            Draft.CompanionLanEnabled = false;
            Draft.StartWithWindows = false;
        }

        NextCommand = new RelayCommand(_ => Step++, _ => Step < StepCount - 1 && !_isApplying);
        BackCommand = new RelayCommand(_ => Step--, _ => Step > 0 && !_isApplying);
        FinishCommand = new RelayCommand(_ => _ = FinishAsync(), _ => !_isApplying);
        SkipCommand = new RelayCommand(_ => _ = SkipAsync(), _ => !_isApplying);
    }

    public ICommand NextCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand FinishCommand { get; }
    public ICommand SkipCommand { get; }

    public int Step
    {
        get => _step;
        set
        {
            int clamped = Math.Clamp(value, 0, StepCount - 1);
            if (!SetProperty(ref _step, clamped)) return;
            OnPropertyChanged(nameof(StepIndicator));
            OnPropertyChanged(nameof(IsWelcomeVisible));
            OnPropertyChanged(nameof(IsHardwareVisible));
            OnPropertyChanged(nameof(IsLiveFpsVisible));
            OnPropertyChanged(nameof(IsCompanionVisible));
            OnPropertyChanged(nameof(IsStartupVisible));
            OnPropertyChanged(nameof(IsSummaryVisible));
            OnPropertyChanged(nameof(IsFirstStep));
            OnPropertyChanged(nameof(IsLastStep));
        }
    }

    public bool IsWelcomeVisible => Step == 0;
    public bool IsHardwareVisible => Step == 1;
    public bool IsCompanionVisible => Step == 2;
    public bool IsLiveFpsVisible => Step == 3;
    public bool IsStartupVisible => Step == 4;
    public bool IsSummaryVisible => Step == 5;
    public bool IsFirstStep => Step == 0;
    public bool IsLastStep => Step == StepCount - 1;

    public bool IsApplying
    {
        get => _isApplying;
        private set => SetProperty(ref _isApplying, value);
    }

    public string StartupStatus
    {
        get => _startupStatus;
        private set => SetProperty(ref _startupStatus, value);
    }

    public string StepIndicator => string.Format(
        System.Globalization.CultureInfo.CurrentCulture,
        _localization.T("FirstRun.StepIndicator"), Step + 1, StepCount);

    public bool IsEnglish
    {
        get => Draft.Language == "en";
        set { if (value) SetDraftLanguage("en"); }
    }

    public bool IsPolish
    {
        get => Draft.Language == "pl";
        set { if (value) SetDraftLanguage("pl"); }
    }

    private void SetDraftLanguage(string language)
    {
        if (Draft.Language == language) return;
        Draft.Language = language;
        _localization.PreviewLanguage(language);
        RefreshTexts();
        OnPropertyChanged(nameof(IsEnglish));
        OnPropertyChanged(nameof(IsPolish));
    }

    public bool HardwareMonitorEnabled
    {
        get => Draft.HardwareMonitorEnabled;
        set { if (Draft.HardwareMonitorEnabled != value) { Draft.HardwareMonitorEnabled = value; OnPropertyChanged(); } }
    }

    public bool LiveTelemetryEnabled
    {
        get => Draft.LiveTelemetryEnabled;
        set { if (Draft.LiveTelemetryEnabled != value) { Draft.LiveTelemetryEnabled = value; OnPropertyChanged(); } }
    }

    public bool CompanionEnabled
    {
        get => Draft.CompanionEnabled;
        set { if (Draft.CompanionEnabled != value) { Draft.CompanionEnabled = value; OnPropertyChanged(); } }
    }

    public bool StartWithWindows
    {
        get => Draft.StartWithWindows;
        set { if (Draft.StartWithWindows != value) { Draft.StartWithWindows = value; OnPropertyChanged(); } }
    }

    public string SummaryText => string.Join(
        Environment.NewLine,
        _localization.T("FirstRun.Summary.Core"),
        FormatChoice("FirstRun.Summary.Hardware", Draft.HardwareMonitorEnabled),
        FormatChoice("FirstRun.Summary.LiveFps", Draft.LiveTelemetryEnabled),
        FormatChoice("FirstRun.Summary.Companion", Draft.CompanionEnabled),
        FormatChoice("FirstRun.Summary.Startup", Draft.StartWithWindows));

    private string FormatChoice(string labelKey, bool enabled) =>
        $"{_localization.T(labelKey)}: {_localization.T(enabled ? "FirstRun.Summary.On" : "FirstRun.Summary.Off")}";

    /// <summary>Finish with the user's choices.</summary>
    public async Task FinishAsync() => await ApplyAndCloseAsync(lightDefaults: false);

    /// <summary>
    /// Skip with the recommended light configuration: optional modules OFF,
    /// chosen language preserved.
    /// </summary>
    public async Task SkipAsync() => await ApplyAndCloseAsync(lightDefaults: true);

    private async Task ApplyAndCloseAsync(bool lightDefaults)
    {
        if (IsApplying) return;
        IsApplying = true;
        try
        {
            if (lightDefaults)
            {
                Draft.HardwareMonitorEnabled = false;
                Draft.LiveTelemetryEnabled = false;
                Draft.CompanionEnabled = false;
                Draft.CompanionLanEnabled = false;
                Draft.StartWithWindows = false;
            }

            _runtime.SaveSettings(SettingsViewModel.Clone(Draft));
            _runtime.CompleteOnboarding();

            // Always reconcile Windows startup registration in both directions:
            // enabling registers, disabling (including ON→OFF when reopened
            // from Settings) removes. Fresh installs reconcile to a no-op when
            // nothing is registered. Failures surface as status text, never
            // silent, and never block finishing.
            try
            {
                var desired = new DesiredStartupConfiguration(
                    Draft.StartWithWindows, Draft.StartupWindowMode, Draft.StartupRunElevated);
                var result = await _startupApplyCoordinator.ApplyLatestAsync(desired).ConfigureAwait(true);
                StartupStatus = result.Success
                    ? string.Empty
                    : _localization.T("FirstRun.StartupApplyFailed");
            }
            catch
            {
                StartupStatus = _localization.T("FirstRun.StartupApplyFailed");
            }

            RefreshSettingsUi();
            CloseRequested?.Invoke();
        }
        finally
        {
            IsApplying = false;
        }
    }

    private void RefreshSettingsUi()
    {
        OnPropertyChanged(nameof(HardwareMonitorEnabled));
        OnPropertyChanged(nameof(LiveTelemetryEnabled));
        OnPropertyChanged(nameof(CompanionEnabled));
        OnPropertyChanged(nameof(StartWithWindows));
        OnPropertyChanged(nameof(SummaryText));
    }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(StepIndicator));
        OnPropertyChanged(nameof(WelcomeTitle));
        OnPropertyChanged(nameof(WelcomeText));
        OnPropertyChanged(nameof(CoreTitle));
        OnPropertyChanged(nameof(CoreText));
        OnPropertyChanged(nameof(LanguageTitle));
        OnPropertyChanged(nameof(EnglishLabel));
        OnPropertyChanged(nameof(PolishLabel));
        OnPropertyChanged(nameof(HardwareTitle));
        OnPropertyChanged(nameof(HardwareText));
        OnPropertyChanged(nameof(HardwareCostText));
        OnPropertyChanged(nameof(HardwareEnableLabel));
        OnPropertyChanged(nameof(LiveFpsTitle));
        OnPropertyChanged(nameof(LiveFpsText));
        OnPropertyChanged(nameof(LiveFpsCostText));
        OnPropertyChanged(nameof(LiveFpsEnableLabel));
        OnPropertyChanged(nameof(LiveFpsRequiresCompanionText));
        OnPropertyChanged(nameof(LiveFpsBenchmarkNote));
        OnPropertyChanged(nameof(CompanionTitle));
        OnPropertyChanged(nameof(CompanionText));
        OnPropertyChanged(nameof(CompanionCostText));
        OnPropertyChanged(nameof(CompanionEnableLabel));
        OnPropertyChanged(nameof(StartupTitle));
        OnPropertyChanged(nameof(StartupText));
        OnPropertyChanged(nameof(StartupEnableLabel));
        OnPropertyChanged(nameof(SummaryTitle));
        OnPropertyChanged(nameof(SummaryHint));
        OnPropertyChanged(nameof(BackText));
        OnPropertyChanged(nameof(NextText));
        OnPropertyChanged(nameof(FinishText));
        OnPropertyChanged(nameof(SkipText));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(StartupStatus));
    }

    public string Title => _localization.T("FirstRun.Title");
    public string WelcomeTitle => _localization.T("FirstRun.WelcomeTitle");
    public string WelcomeText => _localization.T("FirstRun.WelcomeText");
    public string CoreTitle => _localization.T("FirstRun.CoreTitle");
    public string CoreText => _localization.T("FirstRun.CoreText");
    public string LanguageTitle => _localization.T("FirstRun.LanguageTitle");
    public string EnglishLabel => _localization.T("FirstRun.English");
    public string PolishLabel => _localization.T("FirstRun.Polish");
    public string HardwareTitle => _localization.T("FirstRun.HardwareTitle");
    public string HardwareText => _localization.T("FirstRun.HardwareText");
    public string HardwareCostText => _localization.T("FirstRun.HardwareCost");
    public string HardwareEnableLabel => _localization.T("FirstRun.HardwareEnable");
    public string LiveFpsTitle => _localization.T("FirstRun.LiveFpsTitle");
    public string LiveFpsText => _localization.T("FirstRun.LiveFpsText");
    public string LiveFpsCostText => _localization.T("FirstRun.LiveFpsCost");
    public string LiveFpsEnableLabel => _localization.T("FirstRun.LiveFpsEnable");
    public string LiveFpsRequiresCompanionText => _localization.T("FirstRun.LiveFpsRequiresCompanion");
    public string LiveFpsBenchmarkNote => _localization.T("FirstRun.LiveFpsBenchmarkNote");
    public string CompanionTitle => _localization.T("FirstRun.CompanionTitle");
    public string CompanionText => _localization.T("FirstRun.CompanionText");
    public string CompanionCostText => _localization.T("FirstRun.CompanionCost");
    public string CompanionEnableLabel => _localization.T("FirstRun.CompanionEnable");
    public string StartupTitle => _localization.T("FirstRun.StartupTitle");
    public string StartupText => _localization.T("FirstRun.StartupText");
    public string StartupEnableLabel => _localization.T("FirstRun.StartupEnable");
    public string SummaryTitle => _localization.T("FirstRun.SummaryTitle");
    public string SummaryHint => _localization.T("FirstRun.SummaryHint");
    public string BackText => _localization.T("FirstRun.Back");
    public string NextText => _localization.T("FirstRun.Next");
    public string FinishText => _localization.T("FirstRun.Finish");
    public string SkipText => _localization.T("FirstRun.Skip");
}
