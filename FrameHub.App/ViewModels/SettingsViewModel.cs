using FrameHub.App.Helpers;
using FrameHub.App.Services;
using FrameHub.Companion;
using FrameHub.Companion.Models;
using FrameHub.Companion.Network;
using FrameHub.Companion.Pairing;
using FrameHub.Companion.Persistence;
using FrameHub.Core.Logging;
using FrameHub.Core.Models;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Benchmarking;
using System.Collections.ObjectModel;
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
    private bool _isRecordingBenchmarkHotkey;
    private string _benchmarkHotkeyStatus = string.Empty;

    public ICommand RestartAsAdminCommand { get; }
    public ICommand CheckUpdatesCommand { get; }
    public ICommand OpenAppDataCommand { get; }
    public ICommand OpenLogsCommand { get; }
    public ICommand RepairStartupCommand { get; }
    public ICommand OpenThirdPartyNoticesCommand { get; }
    public ICommand OpenPresentMonProjectCommand { get; }
    public ICommand ClearBenchmarkHotkeyCommand { get; }

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
    public string BenchmarkEngineTitle => _localization.T("Settings.BenchmarkEngine.Title");
    public string BenchmarkEngineDescription => _localization.T("Settings.BenchmarkEngine.Description");
    public string BenchmarkEngineStatus { get; private set; } = string.Empty;
    public string BenchmarkEngineVersion { get; private set; } = string.Empty;
    public string BenchmarkSafetyText => _localization.T("Settings.BenchmarkEngine.Safety");
    public string ThirdPartyLicense => _localization.T("Settings.BenchmarkEngine.License");
    public string OpenThirdPartyNoticesText => _localization.T("Settings.BenchmarkEngine.Notices");
    public string OpenPresentMonProjectText => _localization.T("Settings.BenchmarkEngine.Project");
    public string ThirdPartyComponents => _localization.T("Settings.ThirdParty.Components");
    public string BenchmarkHotkeyTitle => _localization.T("Settings.BenchmarkHotkey.Title");
    public string BenchmarkHotkeyDescription => _localization.T("Settings.BenchmarkHotkey.Description");
    public string BenchmarkHotkeyEnableLabel => _localization.T("Settings.BenchmarkHotkey.Enable");
    public string BenchmarkHotkeyCombinationLabel => _localization.T("Settings.BenchmarkHotkey.Combination");
    public string BenchmarkHotkeyRecordText => IsRecordingBenchmarkHotkey ? _localization.T("Settings.BenchmarkHotkey.Recording") : _localization.T("Settings.BenchmarkHotkey.Record");
    public string BenchmarkHotkeyClearText => _localization.T("Settings.BenchmarkHotkey.Clear");
    public string BenchmarkHotkeyDisplay => BenchmarkHotkeyGesture.FromSettings(true, _settings.BenchmarkHotkeyModifiers, _settings.BenchmarkHotkeyVirtualKey)?.ToString() ?? _localization.T("Benchmark.Hotkey.NotConfigured");
    public IReadOnlyList<string> BenchmarkHotkeyTokens => BenchmarkHotkeyGesture.FromSettings(true, _settings.BenchmarkHotkeyModifiers, _settings.BenchmarkHotkeyVirtualKey) is BenchmarkHotkeyGesture gesture
        ? gesture.ToString().Split(" + ", StringSplitOptions.RemoveEmptyEntries)
        : [_localization.T("Benchmark.Hotkey.NotConfigured")];
    public string BenchmarkHotkeyStatus { get => _benchmarkHotkeyStatus; private set => SetProperty(ref _benchmarkHotkeyStatus, value); }
    public bool IsRecordingBenchmarkHotkey { get => _isRecordingBenchmarkHotkey; private set { if (SetProperty(ref _isRecordingBenchmarkHotkey, value)) OnPropertyChanged(nameof(BenchmarkHotkeyRecordText)); } }
    public bool BenchmarkHotkeyEnabled
    {
        get => _settings.BenchmarkHotkeyEnabled;
        set
        {
            if (value && BenchmarkHotkeyGesture.FromSettings(true, _settings.BenchmarkHotkeyModifiers, _settings.BenchmarkHotkeyVirtualKey) is null)
            {
                BenchmarkHotkeyStatus = _localization.T("Settings.BenchmarkHotkey.ConfigureFirst");
                OnPropertyChanged();
                return;
            }
            if (_settings.BenchmarkHotkeyEnabled == value) return;
            _settings.BenchmarkHotkeyEnabled = value;
            BenchmarkHotkeyStatus = string.Empty;
            SaveSettings();
            OnPropertyChanged();
        }
    }
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

    private string _companionPortValidationError = string.Empty;
    public string CompanionPortValidationError
    {
        get => _companionPortValidationError;
        private set => SetProperty(ref _companionPortValidationError, value);
    }

    public bool HasCompanionPortValidationError => !string.IsNullOrEmpty(CompanionPortValidationError);

    public string CompanionTitle => _localization.T("Settings.CompanionTitle");
    public string CompanionDescription => _localization.T("Settings.CompanionDescription");
    public string CompanionEnableLabel => _localization.T("Settings.CompanionEnable");
    public string CompanionPortLabel => _localization.T("Settings.CompanionPort");
    public string CompanionStatusLabel => _localization.T("Settings.CompanionStatus");
    public string CompanionEndpointLabel => _localization.T("Settings.CompanionEndpoint");

    public bool CompanionEnabled
    {
        get => _settings.CompanionEnabled;
        set
        {
            if (_settings.CompanionEnabled == value) return;
            _settings.CompanionEnabled = value;
            SaveSettings();
            OnPropertyChanged();
            UpdateCompanionStatusProperties();
        }
    }

    private string _companionPortText = string.Empty;
    public string CompanionPortText
    {
        get => _companionPortText;
        set
        {
            _companionPortText = value ?? string.Empty;
            if (int.TryParse(_companionPortText, out int port) && port >= 1 && port <= 65535)
            {
                CompanionPortValidationError = string.Empty;
                if (_settings.CompanionPort != port)
                {
                    _settings.CompanionPort = port;
                    SaveSettings();
                    UpdateCompanionStatusProperties();
                }
            }
            else
            {
                CompanionPortValidationError = _localization.T("Settings.CompanionPortInvalid");
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCompanionPortValidationError));
            OnPropertyChanged(nameof(CompanionPort));
        }
    }

    public int CompanionPort
    {
        get => _settings.CompanionPort;
        set => CompanionPortText = value.ToString();
    }

    public string CompanionStatusText
    {
        get
        {
            var status = _runtime.CompanionServer.Status;
            return status.State switch
            {
                CompanionServiceState.Starting => _localization.T("Settings.CompanionStateStarting"),
                CompanionServiceState.Running => _localization.T("Settings.CompanionStateRunning"),
                CompanionServiceState.Failed => _localization.T("Settings.CompanionStateFailed"),
                _ => _localization.T("Settings.CompanionStateDisabled")
            };
        }
    }

    public bool IsCompanionRunning => _runtime.CompanionServer.Status.State == CompanionServiceState.Running;
    public bool IsCompanionFailed => _runtime.CompanionServer.Status.State == CompanionServiceState.Failed;

    public string CompanionEndpointText => IsCompanionRunning ? (_runtime.CompanionServer.Status.BoundAddress ?? string.Empty) : string.Empty;

    public string CompanionErrorMessage
    {
        get
        {
            if (!IsCompanionFailed) return string.Empty;
            return string.Format(_localization.T("Settings.CompanionFailedMessage"), _settings.CompanionPort);
        }
    }

    public ObservableCollection<LanCandidateIp> AvailableLanAddresses { get; } = new();
    public ObservableCollection<PairedDeviceItemViewModel> PairedDevices { get; } = new();

    public ICommand RefreshLanAddressesCommand { get; }
    public ICommand StartPairingCommand { get; }
    public ICommand CancelPairingCommand { get; }
    public ICommand CopyPairingUrlCommand { get; }
    public ICommand AllowPairingCommand { get; }
    public ICommand DenyPairingCommand { get; }
    public ICommand ResetDeviceStoreCommand { get; }

    public string CompanionLanEnableLabel => _localization.T("Settings.CompanionLanEnable");
    public string CompanionLanAddressLabel => _localization.T("Settings.CompanionLanAddress");
    public string CompanionLanRefreshLabel => _localization.T("Settings.CompanionLanRefresh");
    public string CompanionLanStatusLabel => _localization.T("Settings.CompanionLanStatus");
    public string CompanionPairButtonLabel => _localization.T("Settings.CompanionPairButton");
    public string CompanionCancelPairButtonLabel => _localization.T("Settings.CompanionCancelPairButton");
    public string CompanionPairingUrlLabel => _localization.T("Settings.CompanionPairingUrl");
    public string CompanionPairingTokenLabel => _localization.T("Settings.CompanionPairingToken");
    public string CompanionCopyUrlLabel => _localization.T("Settings.CompanionCopyUrl");
    public string CompanionPendingTitle => _localization.T("Settings.CompanionPendingTitle");
    public string CompanionAllowLabel => _localization.T("Settings.CompanionAllow");
    public string CompanionDenyLabel => _localization.T("Settings.CompanionDeny");
    public string CompanionPairedDevicesTitle => _localization.T("Settings.CompanionPairedDevicesTitle");
    public string CompanionNoPairedDevicesLabel => _localization.T("Settings.CompanionNoPairedDevices");
    public string CompanionStoreFaultTitle => _localization.T("Settings.CompanionStoreFaultTitle");
    public string CompanionStoreFaultMessage => _localization.T("Settings.CompanionStoreFaultMessage");
    public string CompanionResetStoreLabel => _localization.T("Settings.CompanionResetStore");

    public bool CompanionLanEnabled
    {
        get => _settings.CompanionLanEnabled;
        set
        {
            if (_settings.CompanionLanEnabled == value) return;
            _settings.CompanionLanEnabled = value;
            if (value && string.IsNullOrWhiteSpace(_settings.CompanionLanAddress))
            {
                RefreshLanAddresses();
            }
            SaveSettings();
            OnPropertyChanged();
            UpdateCompanionStatusProperties();
        }
    }

    public string? CompanionLanAddress
    {
        get => _settings.CompanionLanAddress;
        set
        {
            if (_settings.CompanionLanAddress == value) return;
            _settings.CompanionLanAddress = value;
            SaveSettings();
            OnPropertyChanged();
            UpdateCompanionStatusProperties();
        }
    }

    public string LanStatusText
    {
        get
        {
            if (!_settings.CompanionLanEnabled)
                return _localization.T("Settings.CompanionLanDisabled");

            var status = _runtime.CompanionServer.Status;
            if (status.LanFaulted)
                return string.Format(_localization.T("Settings.CompanionLanFault"), status.LanErrorMessage ?? "Unavailable");

            if (status.LanBoundAddress != null)
                return status.LanBoundAddress;

            return _localization.T("Settings.CompanionStateStarting");
        }
    }

    public bool IsPairingActive => _runtime.CompanionServer.PairingEngine.GetCurrentStatus().IsActive;
    public string PairingUrl => _runtime.CompanionServer.PairingEngine.GetCurrentStatus().PairingUrl ?? string.Empty;
    public string PairingToken => _runtime.CompanionServer.PairingEngine.GetCurrentStatus().PairingToken ?? string.Empty;

    public bool IsPairingSectionVisible => IsCompanionRunning;

    public string CompanionPairingActiveText
    {
        get
        {
            var status = _runtime.CompanionServer.PairingEngine.GetCurrentStatus();
            if (!status.IsActive || status.ExpiresAtUtc is not { } expiresAtUtc)
            {
                return string.Empty;
            }

            int remainingMinutes = (int)Math.Ceiling(Math.Max((expiresAtUtc - DateTimeOffset.Now).TotalMinutes, 1));
            return string.Format(_localization.T("Settings.CompanionPairingActive"), remainingMinutes);
        }
    }

    public string CompanionPairingLanHintText => _localization.T("Settings.CompanionPairingLanHint");

    public string PairingStatusMessage => _pairingStatusMessageKey == null ? string.Empty : _localization.T(_pairingStatusMessageKey);

    private string? _pairingStatusMessageKey;

    private void SetPairingStatusMessage(string? key)
    {
        _pairingStatusMessageKey = key;
        OnPropertyChanged(nameof(PairingStatusMessage));
    }

    public bool HasPendingPairingRequest => _runtime.CompanionServer.PairingEngine.GetCurrentStatus().PendingRequest != null;
    public string PendingDeviceName => _runtime.CompanionServer.PairingEngine.GetCurrentStatus().PendingRequest?.DisplayName ?? string.Empty;
    public string PendingSourceIp => _runtime.CompanionServer.PairingEngine.GetCurrentStatus().PendingRequest?.SourceIp ?? string.Empty;

    public bool HasPairedDevices => PairedDevices.Count > 0;
    public bool IsDeviceStoreFaulted => _runtime.CompanionServer.DeviceStore.IsFaulted;
    public string DeviceStoreFaultMessage => _runtime.CompanionServer.DeviceStore.FaultMessage ?? string.Empty;

    public SettingsViewModel(LocalizationService localization, AppRuntimeService runtime)
    {
        _localization = localization;
        _runtime = runtime;
        _startupApplyCoordinator = new StartupApplyCoordinator(runtime.SettingsService, LoggerService.Instance);
        _settings = Clone(runtime.Settings);
        _companionPortText = _settings.CompanionPort.ToString();
        StatusMessage = _localization.T("Settings.Saved");

        _runtime.CompanionServer.StatusChanged += OnCompanionStatusChanged;
        _runtime.CompanionServer.PairingEngine.SessionStatusChanged += OnPairingSessionStatusChanged;

        RestartAsAdminCommand = new RelayCommand(_ => _runtime.SettingsService.RestartAsAdmin());
        CheckUpdatesCommand = new RelayCommand(_ => _ = CheckUpdatesAsync());
        OpenAppDataCommand = new RelayCommand(_ => OpenFolder(AppPaths.UserDataDirectory));
        OpenLogsCommand = new RelayCommand(_ => OpenFolder(Path.GetDirectoryName(LoggerService.Shared.Configuration.LogFilePath) ?? AppPaths.UserDataDirectory));
        RepairStartupCommand = new RelayCommand(_ => _ = SaveStartupSettingsAsync());
        OpenThirdPartyNoticesCommand = new RelayCommand(_ => OpenThirdPartyNotices());
        OpenPresentMonProjectCommand = new RelayCommand(_ => ExternalLinkService.TryOpen(FrameHubExternalLink.PresentMon));
        ClearBenchmarkHotkeyCommand = new RelayCommand(_ => ClearBenchmarkHotkey());

        RefreshLanAddressesCommand = new RelayCommand(_ => RefreshLanAddresses());
        StartPairingCommand = new RelayCommand(_ => StartPairing());
        CancelPairingCommand = new RelayCommand(_ => CancelPairing());
        CopyPairingUrlCommand = new RelayCommand(_ => CopyPairingUrl());
        AllowPairingCommand = new RelayCommand(_ => AllowPairing());
        DenyPairingCommand = new RelayCommand(_ => DenyPairing());
        ResetDeviceStoreCommand = new RelayCommand(_ => ResetDeviceStore());

        RefreshLanAddresses();
        RefreshPairedDevices();

        ProbeBenchmarkEngine();
        _ = RefreshStartupStatusAsync();
    }

    public void RefreshLanAddresses()
    {
        AvailableLanAddresses.Clear();
        var candidates = LanAddressService.GetAvailableLanAddresses();
        foreach (var candidate in candidates)
        {
            AvailableLanAddresses.Add(candidate);
        }

        if (string.IsNullOrWhiteSpace(_settings.CompanionLanAddress) && AvailableLanAddresses.Count > 0)
        {
            CompanionLanAddress = AvailableLanAddresses[0].IpAddress;
        }
    }

    public void RefreshPairedDevices()
    {
        PairedDevices.Clear();
        var devices = _runtime.CompanionServer.DeviceStore.Devices;
        string scopeTelemetryLabel = _localization.T("Settings.CompanionScopeTelemetry");
        string scopeReadBenchmarksLabel = _localization.T("Settings.CompanionScopeReadBenchmarks");
        string scopeWriteBenchmarksLabel = _localization.T("Settings.CompanionScopeWriteBenchmarks");
        string scopeReadLibraryLabel = _localization.T("Settings.CompanionScopeReadLibrary");
        string scopeWriteLaunchLabel = _localization.T("Settings.CompanionScopeWriteLaunch");
        string scopeReadBackgroundAppsLabel = _localization.T("Settings.CompanionScopeReadBackgroundApps");
        string scopeWriteBackgroundAppsLabel = _localization.T("Settings.CompanionScopeWriteBackgroundApps");
        string scopeReadOptimizationLabel = _localization.T("Settings.CompanionScopeReadOptimization");
        string scopeWriteOptimizationLabel = _localization.T("Settings.CompanionScopeWriteOptimization");
        string revokeLabel = _localization.T("Settings.CompanionRevoke");
        string neverUsedText = _localization.T("Settings.CompanionNeverUsed");
        string pairedLabel = _localization.T("Settings.CompanionPaired");
        string lastUsedLabel = _localization.T("Settings.CompanionLastUsed");

        foreach (var dev in devices)
        {
            PairedDevices.Add(new PairedDeviceItemViewModel(
                dev,
                RevokeDevice,
                ToggleDeviceScope,
                scopeTelemetryLabel,
                scopeReadBenchmarksLabel,
                scopeWriteBenchmarksLabel,
                scopeReadLibraryLabel,
                scopeWriteLaunchLabel,
                scopeReadOptimizationLabel,
                scopeWriteOptimizationLabel,
                revokeLabel,
                neverUsedText,
                scopeReadBackgroundAppsLabel,
                scopeWriteBackgroundAppsLabel,
                pairedLabel,
                lastUsedLabel));
        }
        OnPropertyChanged(nameof(HasPairedDevices));
        OnPropertyChanged(nameof(IsDeviceStoreFaulted));
        OnPropertyChanged(nameof(DeviceStoreFaultMessage));
    }

    private void ToggleDeviceScope(Guid id, string scope, bool enable)
    {
        if (enable)
        {
            _runtime.CompanionServer.DeviceStore.GrantScope(id, scope);
        }
        else
        {
            _runtime.CompanionServer.DeviceStore.RevokeScope(id, scope);
        }
        RefreshPairedDevices();
    }

    private void StartPairing()
    {
        var status = _runtime.CompanionServer.Status;
        if (status.State != CompanionServiceState.Running)
        {
            SetPairingStatusMessage("Settings.CompanionPairingWaitRunning");
            return;
        }

        string host;
        int port = status.Port > 0 ? status.Port : (_settings.CompanionPort > 0 ? _settings.CompanionPort : 47821);

        if (_settings.CompanionLanEnabled)
        {
            if (string.IsNullOrWhiteSpace(status.LanBoundAddress) || status.LanFaulted)
            {
                SetPairingStatusMessage("Settings.CompanionPairingLanUnavailable");
                return;
            }

            if (Uri.TryCreate(status.LanBoundAddress, UriKind.Absolute, out var lanUri))
            {
                host = lanUri.Host;
                port = lanUri.Port;
            }
            else
            {
                SetPairingStatusMessage("Settings.CompanionPairingLanUnavailable");
                return;
            }
        }
        else
        {
            host = "127.0.0.1";
            if (!string.IsNullOrWhiteSpace(status.BoundAddress) && Uri.TryCreate(status.BoundAddress, UriKind.Absolute, out var localUri))
            {
                host = localUri.Host;
                port = localUri.Port;
            }
        }

        _runtime.CompanionServer.PairingEngine.StartPairingSession(host, port);
        SetPairingStatusMessage(null);
    }

    private void CancelPairing()
    {
        _runtime.CompanionServer.PairingEngine.CancelPairingSession();
    }

    private void CopyPairingUrl()
    {
        string url = PairingUrl;
        if (!string.IsNullOrWhiteSpace(url))
        {
            try { System.Windows.Clipboard.SetText(url); } catch { }
        }
    }

    private void AllowPairing()
    {
        _runtime.CompanionServer.PairingEngine.AllowPendingRequest(out _, out _);
        RefreshPairedDevices();
    }

    private void DenyPairing()
    {
        _runtime.CompanionServer.PairingEngine.DenyPendingRequest();
        RefreshPairedDevices();
    }

    private void RevokeDevice(Guid id)
    {
        RevokeDeviceWithConfirmation(_runtime.CompanionServer.DeviceStore, id, ConfirmRevokeDevice);
        RefreshPairedDevices();
    }

    /// <summary>Test seam substituting the WPF confirmation dialog; when null the real MessageBox is used.</summary>
    public Func<string, bool>? RevokeConfirmationPrompt { get; set; }

    private bool ConfirmRevokeDevice(string deviceName)
    {
        Func<string, bool>? prompt = RevokeConfirmationPrompt;
        if (prompt != null)
        {
            return prompt(deviceName);
        }

        return WpfMessageBox.Show(
            string.Format(_localization.T("Settings.CompanionRevokeConfirmMessage"), deviceName),
            _localization.T("Settings.CompanionRevokeConfirmTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    /// <summary>
    /// Revokes exactly one paired device after an explicit confirmation naming that device.
    /// Cancel (or a missing record) performs no mutation.
    /// </summary>
    public static bool RevokeDeviceWithConfirmation(DeviceRecordStore store, Guid id, Func<string, bool> confirmRemoval)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(confirmRemoval);

        PairedDeviceRecord? device = store.GetDeviceById(id);
        if (device == null) return false;
        if (!confirmRemoval(device.DisplayName)) return false;
        return store.RevokeDevice(id);
    }

    private void ResetDeviceStore()
    {
        _runtime.CompanionServer.DeviceStore.ResetStore();
        RefreshPairedDevices();
    }

    private void OnPairingSessionStatusChanged(object? sender, PairingSessionStatus e)
    {
        DispatchIfRequired(UpdatePairingProperties);
    }

    private void UpdatePairingProperties()
    {
        OnPropertyChanged(nameof(IsPairingActive));
        OnPropertyChanged(nameof(PairingUrl));
        OnPropertyChanged(nameof(PairingToken));
        OnPropertyChanged(nameof(CompanionPairingActiveText));
        OnPropertyChanged(nameof(HasPendingPairingRequest));
        OnPropertyChanged(nameof(PendingDeviceName));
        OnPropertyChanged(nameof(PendingSourceIp));
    }

    private void OnCompanionStatusChanged(object? sender, CompanionStatusInfo status)
    {
        DispatchIfRequired(UpdateCompanionStatusProperties);
    }

    private static void DispatchIfRequired(Action action)
    {
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher)
        {
            if (dispatcher.CheckAccess())
            {
                action();
            }
            else if (dispatcher.Thread.IsAlive && !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
            {
                try
                {
                    dispatcher.InvokeAsync(action);
                }
                catch (InvalidOperationException)
                {
                    // Dispatcher was shut down or unavailable during invoke
                }
            }
            return;
        }

        action();
    }

    public void UpdateCompanionStatusProperties()
    {
        OnPropertyChanged(nameof(CompanionStatusText));
        OnPropertyChanged(nameof(CompanionEndpointText));
        OnPropertyChanged(nameof(CompanionErrorMessage));
        OnPropertyChanged(nameof(IsCompanionRunning));
        OnPropertyChanged(nameof(IsCompanionFailed));
        OnPropertyChanged(nameof(LanStatusText));
        OnPropertyChanged(nameof(CompanionLanEnabled));
        OnPropertyChanged(nameof(CompanionLanAddress));
        OnPropertyChanged(nameof(IsPairingSectionVisible));
    }

    public void RefreshTexts()
    {
        ProbeBenchmarkEngine();
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
        OnPropertyChanged(nameof(BenchmarkEngineTitle));
        OnPropertyChanged(nameof(BenchmarkEngineDescription));
        OnPropertyChanged(nameof(BenchmarkEngineStatus));
        OnPropertyChanged(nameof(BenchmarkEngineVersion));
        OnPropertyChanged(nameof(BenchmarkSafetyText));
        OnPropertyChanged(nameof(ThirdPartyLicense));
        OnPropertyChanged(nameof(OpenThirdPartyNoticesText));
        OnPropertyChanged(nameof(OpenPresentMonProjectText));
        OnPropertyChanged(nameof(ThirdPartyComponents));
        OnPropertyChanged(nameof(BenchmarkHotkeyTitle));
        OnPropertyChanged(nameof(BenchmarkHotkeyDescription));
        OnPropertyChanged(nameof(BenchmarkHotkeyEnableLabel));
        OnPropertyChanged(nameof(BenchmarkHotkeyCombinationLabel));
        OnPropertyChanged(nameof(BenchmarkHotkeyRecordText));
        OnPropertyChanged(nameof(BenchmarkHotkeyClearText));
        OnPropertyChanged(nameof(BenchmarkHotkeyDisplay));
        OnPropertyChanged(nameof(BenchmarkHotkeyTokens));
        OnPropertyChanged(nameof(IsEnglish));
        OnPropertyChanged(nameof(IsPolish));
        OnPropertyChanged(nameof(CompanionTitle));
        OnPropertyChanged(nameof(CompanionDescription));
        OnPropertyChanged(nameof(CompanionEnableLabel));
        OnPropertyChanged(nameof(CompanionPortLabel));
        OnPropertyChanged(nameof(CompanionStatusLabel));
        OnPropertyChanged(nameof(CompanionEndpointLabel));
        OnPropertyChanged(nameof(CompanionPairButtonLabel));
        OnPropertyChanged(nameof(CompanionCancelPairButtonLabel));
        OnPropertyChanged(nameof(CompanionPairingUrlLabel));
        OnPropertyChanged(nameof(CompanionPairingTokenLabel));
        OnPropertyChanged(nameof(CompanionCopyUrlLabel));
        OnPropertyChanged(nameof(CompanionPairingActiveText));
        OnPropertyChanged(nameof(CompanionPairingLanHintText));
        OnPropertyChanged(nameof(PairingStatusMessage));
        UpdateCompanionStatusProperties();
    }

    public void ReloadFromRuntime()
    {
        _settings = Clone(_runtime.Settings);
        _companionPortText = _settings.CompanionPort.ToString();
        CompanionPortValidationError = string.Empty;
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

    public void BeginBenchmarkHotkeyRecording()
    {
        IsRecordingBenchmarkHotkey = true;
        BenchmarkHotkeyStatus = _localization.T("Settings.BenchmarkHotkey.RecordingPrompt");
    }

    public void CancelBenchmarkHotkeyRecording()
    {
        IsRecordingBenchmarkHotkey = false;
        BenchmarkHotkeyStatus = string.Empty;
    }

    public bool TryRecordBenchmarkHotkey(Key key, ModifierKeys modifiers)
    {
        if (!BenchmarkHotkeyGesture.TryCreate(key, modifiers, out BenchmarkHotkeyGesture gesture))
        {
            BenchmarkHotkeyStatus = _localization.T("Settings.BenchmarkHotkey.Invalid");
            return false;
        }

        _settings.BenchmarkHotkeyModifiers = (uint)gesture.Modifiers;
        _settings.BenchmarkHotkeyVirtualKey = gesture.VirtualKey;
        _settings.BenchmarkHotkeyEnabled = true;
        IsRecordingBenchmarkHotkey = false;
        BenchmarkHotkeyStatus = _localization.T("Settings.BenchmarkHotkey.Saved");
        SaveSettings();
        OnPropertyChanged(nameof(BenchmarkHotkeyEnabled));
        OnPropertyChanged(nameof(BenchmarkHotkeyDisplay));
        OnPropertyChanged(nameof(BenchmarkHotkeyTokens));
        return true;
    }

    public void ReportBenchmarkHotkeyRegistrationFailure()
    {
        _settings.BenchmarkHotkeyEnabled = false;
        BenchmarkHotkeyStatus = _localization.T("Settings.BenchmarkHotkey.Conflict");
        SaveSettings();
        OnPropertyChanged(nameof(BenchmarkHotkeyEnabled));
    }

    private void ClearBenchmarkHotkey()
    {
        _settings.BenchmarkHotkeyEnabled = false;
        _settings.BenchmarkHotkeyModifiers = 0;
        _settings.BenchmarkHotkeyVirtualKey = 0;
        IsRecordingBenchmarkHotkey = false;
        BenchmarkHotkeyStatus = string.Empty;
        SaveSettings();
        OnPropertyChanged(nameof(BenchmarkHotkeyEnabled));
        OnPropertyChanged(nameof(BenchmarkHotkeyDisplay));
        OnPropertyChanged(nameof(BenchmarkHotkeyTokens));
    }

    private void ProbeBenchmarkEngine()
    {
        try
        {
            string path = new PresentMonApiDllLocator().Locate();
            BenchmarkEngineStatus = _localization.T("Benchmark.Engine.Ready");
            BenchmarkEngineVersion = FileVersionInfo.GetVersionInfo(path).FileVersion ?? _localization.T("Benchmark.Unavailable");
        }
        catch
        {
            BenchmarkEngineStatus = _localization.T("Benchmark.Engine.Unavailable");
            BenchmarkEngineVersion = _localization.T("Benchmark.Unavailable");
        }
    }

    private void OpenThirdPartyNotices()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md");
        if (File.Exists(path)) Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        else StatusMessage = _localization.T("Settings.BenchmarkEngine.NoticesMissing");
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
        OnPropertyChanged(nameof(BenchmarkHotkeyEnabled));
        OnPropertyChanged(nameof(BenchmarkHotkeyDisplay));
        OnPropertyChanged(nameof(BenchmarkHotkeyTokens));
        OnPropertyChanged(nameof(CompanionEnabled));
        OnPropertyChanged(nameof(CompanionPortText));
        OnPropertyChanged(nameof(CompanionPort));
        UpdateCompanionStatusProperties();
    }

    internal static AppSettings Clone(AppSettings source) => new()
    {
        StartWithWindows = source.StartWithWindows,
        StartupWindowMode = source.StartupWindowMode,
        StartupRunElevated = source.StartupRunElevated,
        StartupSettingsVersion = source.StartupSettingsVersion,
        LegacyStartMinimized = source.LegacyStartMinimized,
        LegacyRunAsAdministrator = source.LegacyRunAsAdministrator,
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
        BenchmarkHotkeyEnabled = source.BenchmarkHotkeyEnabled,
        BenchmarkHotkeyModifiers = source.BenchmarkHotkeyModifiers,
        BenchmarkHotkeyVirtualKey = source.BenchmarkHotkeyVirtualKey,
        CompanionEnabled = source.CompanionEnabled,
        CompanionLanEnabled = source.CompanionLanEnabled,
        CompanionLanAddress = source.CompanionLanAddress,
        CompanionPort = source.CompanionPort,
        CustomLibraryLocations = source.CustomLibraryLocations?.ToList() ?? new()
    };
}
