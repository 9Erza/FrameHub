using FrameHub.App.Helpers;
using FrameHub.App.Services;
using FrameHub.Core.Logging;
using FrameHub.Core.Models;
using FrameHub.Core.Services;
using System.Diagnostics;
using System.IO;
using System.Windows;
using WpfMessageBox = System.Windows.MessageBox;
using System.Windows.Input;

namespace FrameHub.App.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly LocalizationService _localization;
    private readonly AppRuntimeService _runtime;
    private readonly StartupApplyCoordinator _startupApplyCoordinator;
    private readonly UpdateService _updateService = new();
    private AppSettings _settings;
    private string _statusMessage = string.Empty;
    private string _realStartupStatus = string.Empty;
    private bool _startupRequiresAttention;
    private bool _isStartupBusy;
    private StartupConfigurationState _startupStatusState;
    private int _startupApplyRequests;

    public ICommand RestartAsAdminCommand { get; }
    public ICommand CheckUpdatesCommand { get; }
    public ICommand OpenAppDataCommand { get; }
    public ICommand OpenLogsCommand { get; }
    public ICommand RepairStartupCommand { get; }

    public string Title => _localization.T("Settings.Title");
    public string Subtitle => _localization.T("Settings.Subtitle");
    public string StartupTitle => _localization.T("Settings.StartupTitle");
    public string BehaviorTitle => _localization.T("Settings.BehaviorTitle");
    public string LoggingTitle => _localization.T("Settings.LoggingTitle");
    public string SafetyTitle => _localization.T("Settings.SafetyTitle");
    public string DiagnosticsTitle => _localization.T("Settings.DiagnosticsTitle");
    public string StartWithWindowsLabel => _localization.T("Settings.StartWithWindows");
    public string RunAsAdminLabel => _localization.T("Settings.RunAsAdmin");
    public string StartupBehaviorLabel => _localization.T("Settings.StartupBehavior");
    public string StartupNormalLabel => _localization.T("Settings.StartupNormal");
    public string StartupMinimizedLabel => _localization.T("Settings.StartupMinimized");
    public string StartupTrayLabel => _localization.T("Settings.StartupTray");
    public string ApplyStartupText => StartupRequiresAttention ? _localization.T("Settings.RepairStartup") : _localization.T("Settings.ApplyStartupAgain");
    public string LanguageDescription => _localization.T("Settings.LanguageDescription");
    public string StartupDescription => _localization.T("Settings.StartupDescription");
    public string StartupModeDescription => _localization.T("Settings.StartupModeDescription");
    public string StartupAdminDescription => _localization.T("Settings.StartupAdminDescription");
    public string TrayDescription => _localization.T("Settings.TrayDescription");
    public string RestartAdminDescription => _localization.T("Settings.RestartAdminDescription");
    public string UpdatesDescription => _localization.T("Settings.UpdatesDescription");
    public string DiagnosticsDescription => _localization.T("Settings.DiagnosticsDescription");
    public string StartMinimizedLabel => _localization.T("Settings.StartMinimized");
    public string MinimizeToTrayLabel => _localization.T("Settings.MinimizeToTray");
    public string CloseToTrayLabel => _localization.T("Settings.CloseToTray");
    public string LogEnabledLabel => _localization.T("Settings.LogEnabled");
    public string CheckUpdatesLabel => _localization.T("Settings.CheckUpdates");
    public string AllowRealtimeLabel => _localization.T("Settings.AllowRealtime");
    public string StorageSensorsLabel => _localization.T("Settings.StorageSensors");
    public string ProcessRefreshLabel => _localization.T("Settings.ProcessRefresh");
    public string WatcherRefreshLabel => _localization.T("Settings.WatcherRefresh");
    public string HardwareRefreshLabel => _localization.T("Settings.HardwareRefresh");
    public string LanguageTitle => _localization.T("Settings.LanguageTitle");
    public string EnglishLabel => _localization.T("Language.English");
    public string PolishLabel => _localization.T("Language.Polish");
    public string RestartAsAdminText => _localization.T("Settings.RestartAsAdminButton");
    public string CheckUpdatesText => _localization.T("Settings.CheckUpdatesButton");
    public string OpenAppDataText => _localization.T("Settings.OpenAppData");
    public string OpenLogsText => _localization.T("Settings.OpenLogs");
    public string AdminStatus => _runtime.SettingsService.IsRunAsAdmin() ? _localization.T("Settings.AdminYes") : _localization.T("Settings.AdminNo");
    public string StartupStatus => RealStartupStatus;
    public string RealStartupStatus { get => _realStartupStatus; private set => SetProperty(ref _realStartupStatus, value); }
    public bool StartupRequiresAttention { get => _startupRequiresAttention; private set => SetProperty(ref _startupRequiresAttention, value); }
    public bool IsStartupBusy { get => _isStartupBusy; private set => SetProperty(ref _isStartupBusy, value); }
    public StartupConfigurationState StartupStatusState { get => _startupStatusState; private set => SetProperty(ref _startupStatusState, value); }
    public bool StartupControlsEnabled => StartWithWindows && !IsStartupBusy;
    public bool StartupModeNormal { get => _settings.StartupWindowMode == StartupWindowMode.Normal; set { if (!value) return; _settings.StartupWindowMode = StartupWindowMode.Normal; _ = SaveStartupSettingsAsync(); } }
    public bool StartupModeMinimized { get => _settings.StartupWindowMode == StartupWindowMode.Minimized; set { if (!value) return; _settings.StartupWindowMode = StartupWindowMode.Minimized; _ = SaveStartupSettingsAsync(); } }
    public bool StartupModeTray { get => _settings.StartupWindowMode == StartupWindowMode.Tray; set { if (!value) return; _settings.StartupWindowMode = StartupWindowMode.Tray; _ = SaveStartupSettingsAsync(); } }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsEnglish
    {
        get => _settings.Language == "en";
        set
        {
            if (!value || _settings.Language == "en") return;
            _settings.Language = "en";
            SaveSettings();
            _localization.SetLanguage("en");
            RefreshTexts();
        }
    }

    public bool IsPolish
    {
        get => _settings.Language == "pl";
        set
        {
            if (!value || _settings.Language == "pl") return;
            _settings.Language = "pl";
            SaveSettings();
            _localization.SetLanguage("pl");
            RefreshTexts();
        }
    }

    public bool StartWithWindows
    {
        get => _settings.StartWithWindows;
        set { if (_settings.StartWithWindows == value) return; _settings.StartWithWindows = value; OnPropertyChanged(); OnPropertyChanged(nameof(StartupControlsEnabled)); _ = SaveStartupSettingsAsync(); }
    }

    public bool RunAsAdministrator
    {
        get => _settings.StartupRunElevated;
        set { if (_settings.StartupRunElevated == value) return; _settings.StartupRunElevated = value; _ = SaveStartupSettingsAsync(); }
    }

    public bool StartMinimized
    {
        get => _settings.StartupWindowMode == StartupWindowMode.Minimized;
        set { if (!value || _settings.StartupWindowMode == StartupWindowMode.Minimized) return; _settings.StartupWindowMode = StartupWindowMode.Minimized; _ = SaveStartupSettingsAsync(); }
    }

    public bool MinimizeToTray
    {
        get => _settings.MinimizeToTray;
        set { if (_settings.MinimizeToTray == value) return; _settings.MinimizeToTray = value; SaveSettings(); }
    }

    public bool CloseToTray
    {
        get => _settings.CloseToTray;
        set { if (_settings.CloseToTray == value) return; _settings.CloseToTray = value; SaveSettings(); }
    }

    public bool LogEnabled
    {
        get => _settings.LogEnabled;
        set { if (_settings.LogEnabled == value) return; _settings.LogEnabled = value; SaveSettings(); }
    }

    public bool CheckForUpdates
    {
        get => _settings.CheckForUpdates;
        set { if (_settings.CheckForUpdates == value) return; _settings.CheckForUpdates = value; SaveSettings(); }
    }

    public bool AllowRealtimePriority
    {
        get => _settings.AllowRealtimePriority;
        set { if (_settings.AllowRealtimePriority == value) return; _settings.AllowRealtimePriority = value; SaveSettings(); }
    }

    public bool EnableStorageSensors
    {
        get => _settings.EnableStorageSensors;
        set { if (_settings.EnableStorageSensors == value) return; _settings.EnableStorageSensors = value; SaveSettings(); }
    }

    public int ProcessListRefreshSeconds
    {
        get => _settings.ProcessListRefreshSeconds;
        set { value = Math.Clamp(value, 1, 10); if (_settings.ProcessListRefreshSeconds == value) return; _settings.ProcessListRefreshSeconds = value; SaveSettings(); OnPropertyChanged(); }
    }

    public int ProfileWatcherSeconds
    {
        get => _settings.ProfileWatcherSeconds;
        set { value = Math.Clamp(value, 1, 30); if (_settings.ProfileWatcherSeconds == value) return; _settings.ProfileWatcherSeconds = value; SaveSettings(); OnPropertyChanged(); }
    }

    public int HardwareRefreshSeconds
    {
        get => _settings.HardwareRefreshSeconds;
        set { value = Math.Clamp(value, 1, 10); if (_settings.HardwareRefreshSeconds == value) return; _settings.HardwareRefreshSeconds = value; SaveSettings(); OnPropertyChanged(); }
    }

    public SettingsViewModel(LocalizationService localization, AppRuntimeService runtime)
    {
        _localization = localization;
        _runtime = runtime;
        _startupApplyCoordinator = new StartupApplyCoordinator(runtime.SettingsService, LoggerService.Instance);
        _settings = Clone(runtime.Settings);
        StatusMessage = _localization.T("Settings.Saved");

        RestartAsAdminCommand = new RelayCommand(_ => _runtime.SettingsService.RestartAsAdmin());
        CheckUpdatesCommand = new RelayCommand(_ => _ = CheckUpdatesAsync());
        OpenAppDataCommand = new RelayCommand(_ => OpenFolder(AppPaths.UserDataDirectory));
        OpenLogsCommand = new RelayCommand(_ => OpenFolder(Path.GetDirectoryName(LoggerService.Shared.Configuration.LogFilePath) ?? AppPaths.UserDataDirectory));
        RepairStartupCommand = new RelayCommand(_ => _ = SaveStartupSettingsAsync());
        _ = RefreshStartupStatusAsync();
    }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(StartupTitle));
        OnPropertyChanged(nameof(BehaviorTitle));
        OnPropertyChanged(nameof(LoggingTitle));
        OnPropertyChanged(nameof(SafetyTitle));
        OnPropertyChanged(nameof(DiagnosticsTitle));
        OnPropertyChanged(nameof(StartWithWindowsLabel));
        OnPropertyChanged(nameof(RunAsAdminLabel));
        OnPropertyChanged(nameof(StartupBehaviorLabel));
        OnPropertyChanged(nameof(StartupNormalLabel));
        OnPropertyChanged(nameof(StartupMinimizedLabel));
        OnPropertyChanged(nameof(StartupTrayLabel));
        OnPropertyChanged(nameof(ApplyStartupText));
        OnPropertyChanged(nameof(LanguageDescription));
        OnPropertyChanged(nameof(StartupDescription));
        OnPropertyChanged(nameof(StartupModeDescription));
        OnPropertyChanged(nameof(StartupAdminDescription));
        OnPropertyChanged(nameof(TrayDescription));
        OnPropertyChanged(nameof(RestartAdminDescription));
        OnPropertyChanged(nameof(UpdatesDescription));
        OnPropertyChanged(nameof(DiagnosticsDescription));
        OnPropertyChanged(nameof(StartMinimizedLabel));
        OnPropertyChanged(nameof(MinimizeToTrayLabel));
        OnPropertyChanged(nameof(CloseToTrayLabel));
        OnPropertyChanged(nameof(LogEnabledLabel));
        OnPropertyChanged(nameof(CheckUpdatesLabel));
        OnPropertyChanged(nameof(AllowRealtimeLabel));
        OnPropertyChanged(nameof(StorageSensorsLabel));
        OnPropertyChanged(nameof(ProcessRefreshLabel));
        OnPropertyChanged(nameof(WatcherRefreshLabel));
        OnPropertyChanged(nameof(HardwareRefreshLabel));
        OnPropertyChanged(nameof(LanguageTitle));
        OnPropertyChanged(nameof(EnglishLabel));
        OnPropertyChanged(nameof(PolishLabel));
        OnPropertyChanged(nameof(RestartAsAdminText));
        OnPropertyChanged(nameof(CheckUpdatesText));
        OnPropertyChanged(nameof(OpenAppDataText));
        OnPropertyChanged(nameof(OpenLogsText));
        OnPropertyChanged(nameof(AdminStatus));
        OnPropertyChanged(nameof(IsEnglish));
        OnPropertyChanged(nameof(IsPolish));
    }

    public void ReloadFromRuntime()
    {
        _settings = Clone(_runtime.Settings);
        RefreshAllValues();
    }

    private void SaveSettings()
    {
        _runtime.SaveSettings(Clone(_settings));
        StatusMessage = _localization.T("Settings.Saved");
        OnPropertyChanged(nameof(AdminStatus));
    }

    private async Task SaveStartupSettingsAsync()
    {
        _runtime.SaveSettings(Clone(_settings));
        try
        {
            await ApplyStartupAsync();
        }
        catch (Exception ex)
        {
            LoggerService.Instance.Error("Startup apply failed unexpectedly.", ex);
            StartupRequiresAttention = true;
            RealStartupStatus = $"{_localization.T("Settings.StartupRequiresAttention")} {ex.Message}";
            StatusMessage = RealStartupStatus;
        }
    }

    private async Task ApplyStartupAsync()
    {
        _startupApplyRequests++;
        IsStartupBusy = true;
        OnPropertyChanged(nameof(StartupControlsEnabled));
        try
        {
            var desired = new DesiredStartupConfiguration(_settings.StartWithWindows, _settings.StartupWindowMode, _settings.StartupRunElevated);
            var result = await _startupApplyCoordinator.ApplyLatestAsync(desired);
            UpdateStartupStatus(result.FinalEvaluation, result.Error ?? (result.WasElevationCancelled ? _localization.T("Settings.StartupUacCancelled") : null));
            StatusMessage = result.Success ? _localization.T("Settings.Saved") : RealStartupStatus;
        }
        finally
        {
            _startupApplyRequests--;
            IsStartupBusy = _startupApplyRequests > 0;
            OnPropertyChanged(nameof(StartupControlsEnabled));
        }
    }

    private async Task RefreshStartupStatusAsync()
    {
        var desired = new DesiredStartupConfiguration(_settings.StartWithWindows, _settings.StartupWindowMode, _settings.StartupRunElevated);
        var actual = await _runtime.SettingsService.ReadActualAsync(desired);
        UpdateStartupStatus(StartupConfigurationPlanner.Evaluate(desired, actual), null);
    }

    private void UpdateStartupStatus(StartupConfigurationEvaluation evaluation, string? detail)
    {
        StartupStatusState = evaluation.State;
        StartupRequiresAttention = evaluation.State is StartupConfigurationState.Broken or StartupConfigurationState.Conflict;
        RealStartupStatus = evaluation.State switch
        {
            StartupConfigurationState.Disabled => _localization.T("Settings.StartupDisabled"),
            StartupConfigurationState.Registry => _localization.T("Settings.StartupConfiguredNormally"),
            StartupConfigurationState.ElevatedScheduledTask => _localization.T("Settings.StartupConfiguredElevated"),
            StartupConfigurationState.Conflict => _localization.T("Settings.StartupConflict"),
            _ => string.IsNullOrWhiteSpace(detail) ? _localization.T("Settings.StartupRequiresAttention") : $"{_localization.T("Settings.StartupRequiresAttention")} {detail}"
        };
        OnPropertyChanged(nameof(StartupStatus));
        OnPropertyChanged(nameof(ApplyStartupText));
    }

    private async Task CheckUpdatesAsync()
    {
        StatusMessage = _localization.T("Settings.CheckingUpdates");
        var result = await _updateService.CheckForUpdatesAsync(new AppInfo().Version);
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            StatusMessage = $"{_localization.T("Settings.UpdateFailed")} {result.Error}";
            return;
        }

        if (result.IsUpdateAvailable)
        {
            StatusMessage = string.Format(_localization.T("Settings.UpdateAvailable"), result.LatestVersion);
            var answer = WpfMessageBox.Show(StatusMessage + "\n\n" + _localization.T("Settings.OpenReleaseQuestion"), "FrameHub", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (answer == MessageBoxResult.Yes)
            {
                UpdateService.OpenReleasePage(result.ReleaseUrl);
            }
        }
        else
        {
            StatusMessage = _localization.T("Settings.UpToDate");
        }
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    private void RefreshAllValues()
    {
        OnPropertyChanged(nameof(IsEnglish));
        OnPropertyChanged(nameof(IsPolish));
        OnPropertyChanged(nameof(StartWithWindows));
        OnPropertyChanged(nameof(RunAsAdministrator));
        OnPropertyChanged(nameof(StartMinimized));
        OnPropertyChanged(nameof(MinimizeToTray));
        OnPropertyChanged(nameof(CloseToTray));
        OnPropertyChanged(nameof(LogEnabled));
        OnPropertyChanged(nameof(CheckForUpdates));
        OnPropertyChanged(nameof(AllowRealtimePriority));
        OnPropertyChanged(nameof(EnableStorageSensors));
        OnPropertyChanged(nameof(ProcessListRefreshSeconds));
        OnPropertyChanged(nameof(ProfileWatcherSeconds));
        OnPropertyChanged(nameof(HardwareRefreshSeconds));
        OnPropertyChanged(nameof(AdminStatus));
        OnPropertyChanged(nameof(RealStartupStatus));
        OnPropertyChanged(nameof(StartupStatus));
        OnPropertyChanged(nameof(StartupRequiresAttention));
        OnPropertyChanged(nameof(StartupStatusState));
        OnPropertyChanged(nameof(IsStartupBusy));
        OnPropertyChanged(nameof(StartupControlsEnabled));
    }

    private static AppSettings Clone(AppSettings source) => new()
    {
        StartWithWindows = source.StartWithWindows,
        StartupWindowMode = source.StartupWindowMode,
        StartupRunElevated = source.StartupRunElevated,
        StartupSettingsVersion = source.StartupSettingsVersion,
        MinimizeToTray = source.MinimizeToTray,
        CloseToTray = source.CloseToTray,
        Language = source.Language,
        AllowRealtimePriority = source.AllowRealtimePriority,
        LogEnabled = source.LogEnabled,
        LogLevelValue = source.LogLevelValue,
        LogFilePath = source.LogFilePath,
        EnableConsoleOutput = source.EnableConsoleOutput,
        LogSourceName = source.LogSourceName,
        ProcessListRefreshSeconds = source.ProcessListRefreshSeconds,
        ProfileWatcherSeconds = source.ProfileWatcherSeconds,
        HardwareRefreshSeconds = source.HardwareRefreshSeconds,
        HardwareMonitorEnabled = source.HardwareMonitorEnabled,
        Cs2SteamUserdataId = source.Cs2SteamUserdataId,
        Cs2SteamUserdataPath = source.Cs2SteamUserdataPath,
        EnableStorageSensors = source.EnableStorageSensors,
        CheckForUpdates = source.CheckForUpdates,
        CustomLibraryLocations = source.CustomLibraryLocations.ToList()
    };
}
