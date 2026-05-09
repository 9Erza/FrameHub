using FrameHub.App.Helpers;
using FrameHub.App.Services;
using FrameHub.App.ViewModels.GameOptimization;
using FrameHub.App.ViewModels.Library;
using FrameHub.Core.Models;
using FrameHub.Core.Models.GameOptimization;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Services;
using FrameHub.Core.Services.GameOptimization;
using FrameHub.Core.Services.Library;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Windows.Threading;
using Media = System.Windows.Media;
using WinFormsFolderDialog = System.Windows.Forms.FolderBrowserDialog;
using WinFormsOpenFileDialog = System.Windows.Forms.OpenFileDialog;

namespace FrameHub.App.ViewModels;

public sealed class LibraryViewModel : ViewModelBase
{
    public event Action<string, string>? InfoDialogRequested;

    private readonly LocalizationService _localization;
    private readonly AppRuntimeService _runtime;
    private readonly LibraryService _libraryService = new();
    private readonly SteamLibraryScanner _steamScanner = new();
    private readonly EpicLibraryScanner _epicScanner = new();
    private readonly CustomFolderScanner _customScanner = new();
    private readonly Cs2OptimizationService _cs2Service = new();
    private string _newDisplayName = string.Empty;
    private string _newExecutablePath = string.Empty;
    private LibraryItemType _newItemType = LibraryItemType.Game;
    private string _statusMessage = string.Empty;
    private bool _isBusy;
    private LibraryItemViewModel? _selectedItem;
    private ProcessProfile? _selectedOptimizationProfile;
    private string _selectedPriority = string.Empty;
    private OptimizationModeOptionViewModel? _selectedOptimizationModeOption;
    private Cs2ConfigAnalysis? _cs2Analysis;
    private GameOptimizationPreset? _selectedCs2Preset;
    private bool _suppressCs2PresetRefresh;
    private string _cs2StatusMessage = string.Empty;
    private string _cs2AutoexecText = string.Empty;
    private string _cs2AutoexecStatusMessage = string.Empty;
    private bool _isCs2Running;
    private readonly DispatcherTimer _cs2ProcessTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private string _selectedCs2FpsMax = string.Empty;
    private string _cs2MouseSensitivity = string.Empty;
    private string _cs2Volume = string.Empty;
    private string _cs2SmokeKey = string.Empty;
    private string _cs2FlashKey = string.Empty;
    private string _cs2MolotovKey = string.Empty;
    private string _cs2HeKey = string.Empty;
    private string _cs2VoiceKey = string.Empty;
    private string _selectedCs2JumpBindMode = "Oba";
    private string _cs2CustomBindKey = string.Empty;
    private string _cs2CustomBindCommandText = string.Empty;
    private string _cs2CrosshairStyle = "4";
    private double _cs2CrosshairSize = 4.3;
    private double _cs2CrosshairThickness = 0.2;
    private double _cs2CrosshairGap = 1.4;
    private double _cs2CrosshairAlpha = 255;
    private double _cs2CrosshairRed = 0;
    private double _cs2CrosshairGreen = 255;
    private double _cs2CrosshairBlue = 0;
    private bool _cs2CrosshairDot;
    private bool _cs2CrosshairOutline;
    private double _cs2CrosshairOutlineThickness = 1;
    private bool _cs2CrosshairTStyle;
    private bool _cs2CrosshairUseAlpha = true;
    private bool _cs2CrosshairFollowRecoil;
    private double _cs2CrosshairSniperWidth = 1;
    private bool _cs2CrosshairGapUseWeaponValue;
    private string _cs2CrosshairColor = "5";

    public string Title => _localization.T("Library.Title");
    public string Subtitle => _localization.T("Library.Subtitle");
    public string ScanTitle => _localization.T("Library.ScanTitle");
    public string ManualTitle => _localization.T("Library.ManualTitle");
    public string ItemsTitle => _localization.T("Library.ItemsTitle");
    public string CustomFoldersTitle => _localization.T("Library.CustomFoldersTitle");
    public string EmptyText => _localization.T("Library.Empty");
    public string DisplayNameLabel => _localization.T("Library.DisplayName");
    public string ExecutableLabel => _localization.T("Library.Executable");
    public string TypeLabel => _localization.T("Library.Type");
    public string BrowseText => _localization.T("Library.Browse");
    public string ReadyBadgeText => _localization.T("Badge.Ready");
    public string AddManualText => _localization.T("Library.AddManual");
    public string ScanSteamText => _localization.T("Library.ScanSteam");
    public string ScanEpicText => _localization.T("Library.ScanEpic");
    public string ScanAllText => _localization.T("Library.ScanAll");
    public string AddFolderText => _localization.T("Library.AddFolder");
    public string ScanCustomText => _localization.T("Library.ScanCustom");
    public string RefreshStatusText => _localization.T("Library.RefreshStatus");
    public string SafetyNoteTitle => _localization.T("Library.SafetyTitle");
    public string SafetyNoteDescription => _localization.T("Library.SafetyDescription");
    public string RemoveText => _localization.T("Library.Remove");
    public string ReadyCountText => string.Format(_localization.T("Library.ReadyCount"), Items.Count(x => x.IsReady), Items.Count);
    public string DetailsTitle => _localization.T("Library.DetailsTitle");
    public string NoSelectionText => _localization.T("Library.NoSelection");
    public string LaunchText => _localization.T("Library.Launch");
    public string OpenFolderText => _localization.T("Library.OpenFolder");
    public string ProfileEditorTitle => _localization.T("Library.ProfileEditorTitle");
    public string ProfileEditorHintText => _localization.T("Library.ProfileEditorHint");
    public string CpuCoresTitle => _localization.T("Library.CpuCores");
    public string CpuThreadsTitle => _localization.T("Library.CpuThreads");
    public string CreateUpdateProfileText => HasSelectedOptimizationProfile
        ? _localization.T("Library.UpdateOptimizationProfile")
        : _localization.T("Library.CreateOptimizationProfile");
    public string ApplyProfileText => _localization.T("Library.ApplyProfileNow");
    public string DetailsOptimizeText => _localization.T("Library.DetailsOptimize");
    public string Cs2OptimizerTitle => _localization.T("CS2.Title");
    public string Cs2OptimizerSubtitle => _localization.T("CS2.Subtitle");
    public string ScanCs2Text => _localization.T("CS2.Scan");
    public string BackupCs2Text => _localization.T("CS2.Backup");
    public string ApplyCs2Text => _localization.T("CS2.ApplyPreset");
    public string RestoreCs2Text => _localization.T("CS2.RestoreLatest");
    public string Cs2CurrentSettingsText => _localization.T("CS2.CurrentSettings");
    public string Cs2PresetText => _localization.T("CS2.Preset");
    public string Cs2OverviewTitle => _localization.T("CS2.OverviewTitle");
    public string Cs2ResolutionTitle => _localization.T("CS2.Card.Resolution");
    public string Cs2DisplayModeTitle => _localization.T("CS2.Card.DisplayMode");
    public string Cs2VsyncTitle => _localization.T("CS2.Card.VSync");
    public string Cs2BaselineTitle => _localization.T("CS2.Card.Baseline");
    public string Cs2ConfigPathLabel => _localization.T("CS2.ConfigPath");
    public string Cs2PresetDescription
    {
        get
        {
            if (SelectedCs2Preset == null) return _localization.T("CS2.PresetDescriptionFallback");
            string localized = _localization.T($"CS2.Preset.{SelectedCs2Preset.Id}.Description");
            return localized.StartsWith("CS2.Preset.", StringComparison.OrdinalIgnoreCase)
                ? SelectedCs2Preset.Description
                : localized;
        }
    }
    public string Cs2TableSettingHeader => _localization.T("CS2.Table.Setting");
    public string Cs2TableCurrentHeader => _localization.T("CS2.Table.Current");
    public string Cs2TableRecommendedHeader => _localization.T("CS2.Table.Recommended");
    public string Cs2TableSetHeader => _localization.T("CS2.Table.SetTo");
    public string Cs2TableStatusHeader => _localization.T("CS2.Table.Status");
    public string Cs2TableApplyHeader => _localization.T("CS2.Table.Apply");
    public string Cs2SafetyTitle => _localization.T("CS2.SafetyTitle");
    public string Cs2SafetyDescription => _localization.T("CS2.SafetyDescription");
    public string Cs2SettingsListTitle => _localization.T("CS2.SettingsListTitle");
    public string Cs2SettingsListHint => _localization.T("CS2.SettingsListHint");
    public string Cs2AutoexecTitle => _localization.T("CS2.Autoexec.Title");
    public string Cs2AutoexecSubtitle => _localization.T("CS2.Autoexec.Subtitle");
    public string Cs2AutoexecPathLabel => _localization.T("CS2.Autoexec.Path");
    public string Cs2AutoexecOpenCreateText => _localization.T("CS2.Autoexec.OpenCreate");
    public string Cs2AutoexecSaveText => _localization.T("CS2.Autoexec.Save");
    public string Cs2AutoexecOpenFolderText => _localization.T("CS2.Autoexec.OpenFolder");
    public string Cs2AutoexecGrenadesText => _localization.T("CS2.Autoexec.AddGrenades");
    public string Cs2AutoexecRadarText => _localization.T("CS2.Autoexec.AddRadar");
    public string Cs2AutoexecFpsText => _localization.T("CS2.Autoexec.AddFps");
    public string Cs2AutoexecFpsLabel => _localization.T("CS2.Autoexec.FpsLabel");
    public string BackToLibraryText => IsPolish ? "← Wróć do biblioteki" : "← Back to Library";
    public string CpuTabTitle => IsPolish ? "Optymalizacja procesora" : "CPU optimization";
    public string GraphicsTabTitle => IsPolish ? "Ustawienia graficzne" : "Graphics settings";
    public string ConfigTabTitle => "Config";
    public string CrosshairEditorTitle => IsPolish ? "Edytor celownika" : "Crosshair editor";
    public string CrosshairEditorHint => IsPolish
        ? "Aktualny celownik jest wczytywany z configu CS2. Zmieniony celownik jest zapisywany jako komendy w autoexec.cfg."
        : "The current crosshair is loaded from the CS2 config. Modified crosshair settings are saved as commands in autoexec.cfg.";
    public string Cs2SteamCloudWarningText => IsPolish ? "Zalecenie: wyłącz Steam Cloud we właściwościach Counter-Strike 2 na Steam. Synchronizacja chmury może nadpisać albo zepsuć zmiany w configu po zamknięciu gry." : "Recommendation: disable Steam Cloud in Counter-Strike 2 properties on Steam. Cloud sync can overwrite or break config changes after closing the game.";
    public string Cs2RunningLockText => IsPolish ? "CS2 jest uruchomiony — zamknij grę, zanim zmienisz ustawienia graficzne, celownik albo autoexec.cfg." : "CS2 is running — close the game before changing graphics settings, crosshair or autoexec.cfg.";
    public string Cs2StoppedEditText => IsPolish ? "CS2 jest zamknięty — edycja configu jest odblokowana." : "CS2 is closed — config editing is unlocked.";
    public string LoadCrosshairText => IsPolish ? "Wczytaj celownik" : "Load crosshair";
    public string SaveCrosshairText => IsPolish ? "Zapisz celownik" : "Save crosshair";
    public string CrosshairLivePreviewText => IsPolish ? "Podgląd na żywo" : "Live preview";
    public string AutoexecClearText => IsPolish ? "Wyczyść zawartość autoexec.cfg" : "Clear autoexec.cfg content";
    public string AutoexecGeneralSectionTitle => IsPolish ? "Ogólne ustawienia" : "General settings";
    public string AutoexecQolSectionTitle => IsPolish ? "Ułatwienia" : "Quality-of-life";
    public string AutoexecBindsSectionTitle => IsPolish ? "Bindy" : "Binds";
    public string AutoexecCustomBindTitle => IsPolish ? "Własny bind" : "Custom bind";
    public string AddText => IsPolish ? "Dodaj" : "Add";
    public string FpsMaxPlaceholder => "500";
    public string MouseSensitivityPlaceholder => "0.5";
    public string VolumePlaceholder => "0.1";
    public string SmokeKeyPlaceholder => "z";
    public string FlashKeyPlaceholder => "x";
    public string MolotovKeyPlaceholder => "c";
    public string HeKeyPlaceholder => "v";
    public string VoiceKeyPlaceholder => "k";
    public string KeyText => IsPolish ? "Klawisz" : "Key";
    public string CommandText => IsPolish ? "Komenda" : "Command";
    public string MouseSensitivityText => IsPolish ? "Czułość myszy" : "Mouse sensitivity";
    public string VolumeText => IsPolish ? "Głośność" : "Volume";
    public string AutoexecConsoleText => IsPolish ? "Włącz konsolę" : "Enable console";
    public string SmokeBindText => IsPolish ? "Dymny" : "Smoke";
    public string FlashBindText => IsPolish ? "Błyskowy" : "Flash";
    public string MolotovBindText => IsPolish ? "Molotov" : "Molotov";
    public string HeBindText => IsPolish ? "HE" : "HE";
    public string VoiceBindText => IsPolish ? "Mikrofon" : "Voice";
    public string JumpBindText => IsPolish ? "Skok" : "Jump";
    public string SmokeKeyTooltip => IsPolish ? "Klawisz granatu dymnego" : "Smoke key";
    public string FlashKeyTooltip => IsPolish ? "Klawisz granatu błyskowego" : "Flash key";
    public string MolotovKeyTooltip => IsPolish ? "Klawisz mołotowa" : "Molotov key";
    public string HeKeyTooltip => IsPolish ? "Klawisz granatu HE" : "HE key";
    public string VoiceKeyTooltip => IsPolish ? "Klawisz mikrofonu" : "Voice key";
    public string AutoexecPreviewTitle => IsPolish ? "Podgląd autoexec.cfg na żywo" : "Live autoexec.cfg preview";
    public string SelectAllText => _localization.T("Processes.SelectAll");
    public string ClearAllText => _localization.T("Processes.ClearAll");
    public string DisableSmtText => _localization.T("Processes.DisableSmt");
    public string DisableECoresText => _localization.T("Processes.DisableECores");
    public string PriorityLabel => _localization.T("Processes.Priority");
    public string ModeLabel => _localization.T("Processes.Mode");
    public string CpuInfoTitle => _localization.T("Library.CpuInfo.Title");
    public string DetectedProcessorLabel => _localization.T("Library.CpuInfo.DetectedProcessor");
    public string DetectedProcessorText => BuildDetectedProcessorText();
    public string RecommendedPresetTitle => _localization.T("Library.CpuInfo.RecommendedPresetTitle");
    public string RecommendedPresetText => BuildRecommendedPresetText();
    public string CpuModeExplanationTitle => _localization.T("Library.CpuInfo.ModeExplanationTitle");
    public string CpuSetsExplanation => _localization.T("Library.CpuInfo.CpuSetsExplanation");
    public string AffinityExplanation => _localization.T("Library.CpuInfo.AffinityExplanation");
    public string TestWarningText => _localization.T("Library.CpuInfo.TestWarning");
    public string CpuTipsTitle => _localization.T("Library.CpuInfo.TipsTitle");
    public string CoreZeroTitle => _localization.T("Library.CpuInfo.CoreZeroTitle");
    public string CoreZeroExplanation => _localization.T("Library.CpuInfo.CoreZeroExplanation");
    public string SmtTitle => _localization.T("Library.CpuInfo.SmtTitle");
    public string SmtExplanation => _localization.T("Library.CpuInfo.SmtExplanation");
    public string IntelECoreTitle => _localization.T("Library.CpuInfo.IntelECoreTitle");
    public string IntelECoreExplanation => _localization.T("Library.CpuInfo.IntelECoreExplanation");
    public string CurrentOptimizationProfileTitle => HasSelectedOptimizationProfile
        ? _localization.T("Library.CurrentProfile.Title")
        : _localization.T("Library.NoProfile.Title");
    public string CurrentOptimizationProfileText => BuildCurrentOptimizationProfileText();
    public bool HasSelectedOptimizationProfile => SelectedOptimizationProfile != null;


    private bool IsPolish => _localization.CurrentLanguage == "pl";

    public ObservableCollection<LibraryItemViewModel> Items { get; } = new();
    public ObservableCollection<string> CustomFolders { get; } = new();
    public ObservableCollection<LibraryItemType> AvailableTypes { get; } = new()
    {
        LibraryItemType.Game,
        LibraryItemType.App,
        LibraryItemType.BackgroundApp,
        LibraryItemType.Launcher
    };

    public ObservableCollection<CoreInfo> Cores { get; } = new();
    public IEnumerable<CoreInfo> PhysicalCores => Cores.Where(core => !core.IsThread);
    public IEnumerable<CoreInfo> ThreadCores => Cores.Where(core => core.IsThread);
    public ObservableCollection<string> AvailablePriorities { get; } = new();
    public ObservableCollection<OptimizationModeOptionViewModel> AvailableOptimizationModes { get; } = new();
    public ObservableCollection<GameOptimizationPreset> Cs2Presets { get; } = new();
    public ObservableCollection<Cs2SettingChangeViewModel> Cs2SettingChanges { get; } = new();
    public ObservableCollection<string> Cs2FpsMaxOptions { get; } = new() { "0", "300", "400", "500", "501" };
    public ObservableCollection<string> Cs2CrosshairStyleOptions { get; } = new() { "0", "1", "2", "3", "4", "5" };
    public ObservableCollection<string> Cs2CrosshairColorOptions { get; } = new() { "0", "1", "2", "3", "4", "5" };
    public ObservableCollection<string> Cs2JumpBindModes { get; } = new() { "Oba", "Scroll w dół", "Scroll w górę" };

    public ICommand ScanSteamCommand { get; }
    public ICommand ScanEpicCommand { get; }
    public ICommand ScanAllCommand { get; }
    public ICommand BrowseExecutableCommand { get; }
    public ICommand AddManualCommand { get; }
    public ICommand AddFolderCommand { get; }
    public ICommand ScanCustomCommand { get; }
    public ICommand RefreshStatusCommand { get; }
    public ICommand RemoveItemCommand { get; }
    public ICommand SelectItemCommand { get; }
    public ICommand LaunchSelectedCommand { get; }
    public ICommand OpenSelectedFolderCommand { get; }
    public ICommand CreateUpdateProfileCommand { get; }
    public ICommand ApplyLinkedProfileCommand { get; }
    public ICommand SelectAllCoresCommand { get; }
    public ICommand ClearAllCoresCommand { get; }
    public ICommand DisableSmtCommand { get; }
    public ICommand DisableECoresCommand { get; }
    public ICommand ScanCs2Command { get; }
    public ICommand BackupCs2Command { get; }
    public ICommand ApplyCs2PresetCommand { get; }
    public ICommand RestoreCs2BackupCommand { get; }
    public ICommand LoadCs2AutoexecCommand { get; }
    public ICommand SaveCs2AutoexecCommand { get; }
    public ICommand OpenCs2AutoexecFolderCommand { get; }
    public ICommand InsertCs2GrenadeBindsCommand { get; }
    public ICommand InsertCs2RadarCommand { get; }
    public ICommand InsertCs2FpsMaxCommand { get; }
    public ICommand BackToLibraryCommand { get; }
    public ICommand ClearCs2AutoexecCommand { get; }
    public ICommand LoadCs2CrosshairCommand { get; }
    public ICommand SaveCs2CrosshairCommand { get; }
    public ICommand InsertCs2SensitivityCommand { get; }
    public ICommand InsertCs2VolumeCommand { get; }
    public ICommand InsertCs2ConsoleCommand { get; }
    public ICommand InsertCs2SmokeBindCommand { get; }
    public ICommand InsertCs2FlashBindCommand { get; }
    public ICommand InsertCs2MolotovBindCommand { get; }
    public ICommand InsertCs2HeBindCommand { get; }
    public ICommand InsertCs2VoiceBindCommand { get; }
    public ICommand InsertCs2JumpBindCommand { get; }
    public ICommand InsertCs2CustomBindCommand { get; }

    public string NewDisplayName
    {
        get => _newDisplayName;
        set => SetProperty(ref _newDisplayName, value);
    }

    public string NewExecutablePath
    {
        get => _newExecutablePath;
        set
        {
            if (!SetProperty(ref _newExecutablePath, value)) return;
            if (File.Exists(value) && string.IsNullOrWhiteSpace(NewDisplayName))
            {
                NewDisplayName = Path.GetFileNameWithoutExtension(value);
            }
        }
    }

    public LibraryItemType NewItemType
    {
        get => _newItemType;
        set => SetProperty(ref _newItemType, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public LibraryItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!SetProperty(ref _selectedItem, value)) return;
            LoadProfileForSelectedItem();
            AnalyzeCs2IfSelected();
            OnPropertyChanged(nameof(HasSelectedItem));
            OnPropertyChanged(nameof(IsCs2Selected));
            RefreshCs2ProcessState();
            OnPropertyChanged(nameof(SelectedItemTitle));
            OnPropertyChanged(nameof(ShowLibraryHome));
            OnPropertyChanged(nameof(ShowSelectedDetail));
        }
    }

    public ProcessProfile? SelectedOptimizationProfile
    {
        get => _selectedOptimizationProfile;
        private set
        {
            if (!SetProperty(ref _selectedOptimizationProfile, value)) return;
            OnPropertyChanged(nameof(HasSelectedOptimizationProfile));
            OnPropertyChanged(nameof(CurrentOptimizationProfileTitle));
            OnPropertyChanged(nameof(CurrentOptimizationProfileText));
            OnPropertyChanged(nameof(CreateUpdateProfileText));
        }
    }

    public bool HasSelectedItem => SelectedItem != null;
    public bool ShowLibraryHome => SelectedItem == null;
    public bool ShowSelectedDetail => SelectedItem != null;
    public bool IsCs2Selected => _cs2Service.IsCs2LibraryItem(SelectedItem?.Item);
    public bool IsCs2Running
    {
        get => _isCs2Running;
        private set
        {
            if (!SetProperty(ref _isCs2Running, value)) return;
            OnPropertyChanged(nameof(CanEditCs2Config));
            OnPropertyChanged(nameof(Cs2EditLockMessage));
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public bool CanEditCs2Config => !IsCs2Selected || !IsCs2Running;
    public string Cs2EditLockMessage => IsCs2Running ? Cs2RunningLockText : Cs2StoppedEditText;
    public string SelectedItemTitle => SelectedItem?.DisplayName ?? NoSelectionText;

    public string SelectedPriority
    {
        get => _selectedPriority;
        set => SetProperty(ref _selectedPriority, value);
    }

    public OptimizationMode? SelectedOptimizationMode
    {
        get => _selectedOptimizationModeOption?.Mode;
        set
        {
            var normalized = value.HasValue && (int)value.Value == 2 ? OptimizationMode.Affinity : value;
            SelectedOptimizationModeOption = normalized.HasValue
                ? AvailableOptimizationModes.FirstOrDefault(x => x.Mode == normalized.Value)
                : null;
        }
    }

    public OptimizationModeOptionViewModel? SelectedOptimizationModeOption
    {
        get => _selectedOptimizationModeOption;
        set => SetProperty(ref _selectedOptimizationModeOption, value);
    }

    public GameOptimizationPreset? SelectedCs2Preset
    {
        get => _selectedCs2Preset;
        set
        {
            if (!SetProperty(ref _selectedCs2Preset, value)) return;
            if (!_suppressCs2PresetRefresh)
            {
                RefreshCs2Changes();
            }
            OnPropertyChanged(nameof(Cs2PresetDescription));
            OnPropertyChanged(nameof(Cs2AutoexecPath));
        }
    }

    public string Cs2StatusMessage
    {
        get => _cs2StatusMessage;
        set => SetProperty(ref _cs2StatusMessage, value);
    }

    public string Cs2AutoexecText
    {
        get => _cs2AutoexecText;
        set => SetProperty(ref _cs2AutoexecText, value);
    }

    public string Cs2AutoexecStatusMessage
    {
        get => _cs2AutoexecStatusMessage;
        set => SetProperty(ref _cs2AutoexecStatusMessage, value);
    }

    public string SelectedCs2FpsMax
    {
        get => _selectedCs2FpsMax;
        set => SetProperty(ref _selectedCs2FpsMax, value);
    }

    public string Cs2MouseSensitivity { get => _cs2MouseSensitivity; set => SetProperty(ref _cs2MouseSensitivity, value); }
    public string Cs2Volume { get => _cs2Volume; set => SetProperty(ref _cs2Volume, value); }
    public string Cs2SmokeKey { get => _cs2SmokeKey; set => SetProperty(ref _cs2SmokeKey, value); }
    public string Cs2FlashKey { get => _cs2FlashKey; set => SetProperty(ref _cs2FlashKey, value); }
    public string Cs2MolotovKey { get => _cs2MolotovKey; set => SetProperty(ref _cs2MolotovKey, value); }
    public string Cs2HeKey { get => _cs2HeKey; set => SetProperty(ref _cs2HeKey, value); }
    public string Cs2VoiceKey { get => _cs2VoiceKey; set => SetProperty(ref _cs2VoiceKey, value); }
    public string SelectedCs2JumpBindMode { get => _selectedCs2JumpBindMode; set => SetProperty(ref _selectedCs2JumpBindMode, value); }
    public string Cs2CustomBindKey { get => _cs2CustomBindKey; set => SetProperty(ref _cs2CustomBindKey, value); }
    public string Cs2CustomBindCommandText { get => _cs2CustomBindCommandText; set => SetProperty(ref _cs2CustomBindCommandText, value); }

    public string Cs2CrosshairStyle { get => _cs2CrosshairStyle; set { if (SetProperty(ref _cs2CrosshairStyle, value)) NotifyCrosshairPreviewChanged(); } }
    public double Cs2CrosshairSize { get => _cs2CrosshairSize; set { if (SetProperty(ref _cs2CrosshairSize, value)) NotifyCrosshairPreviewChanged(); } }
    public double Cs2CrosshairThickness { get => _cs2CrosshairThickness; set { if (SetProperty(ref _cs2CrosshairThickness, value)) NotifyCrosshairPreviewChanged(); } }
    public double Cs2CrosshairGap { get => _cs2CrosshairGap; set { if (SetProperty(ref _cs2CrosshairGap, value)) NotifyCrosshairPreviewChanged(); } }
    public double Cs2CrosshairAlpha { get => _cs2CrosshairAlpha; set { if (SetProperty(ref _cs2CrosshairAlpha, value)) NotifyCrosshairPreviewChanged(); } }
    public double Cs2CrosshairRed { get => _cs2CrosshairRed; set { if (SetProperty(ref _cs2CrosshairRed, value)) NotifyCrosshairPreviewChanged(); } }
    public double Cs2CrosshairGreen { get => _cs2CrosshairGreen; set { if (SetProperty(ref _cs2CrosshairGreen, value)) NotifyCrosshairPreviewChanged(); } }
    public double Cs2CrosshairBlue { get => _cs2CrosshairBlue; set { if (SetProperty(ref _cs2CrosshairBlue, value)) NotifyCrosshairPreviewChanged(); } }
    public bool Cs2CrosshairDot { get => _cs2CrosshairDot; set { if (SetProperty(ref _cs2CrosshairDot, value)) NotifyCrosshairPreviewChanged(); } }
    public bool Cs2CrosshairOutline { get => _cs2CrosshairOutline; set { if (SetProperty(ref _cs2CrosshairOutline, value)) NotifyCrosshairPreviewChanged(); } }
    public double Cs2CrosshairOutlineThickness { get => _cs2CrosshairOutlineThickness; set { if (SetProperty(ref _cs2CrosshairOutlineThickness, value)) NotifyCrosshairPreviewChanged(); } }
    public bool Cs2CrosshairTStyle { get => _cs2CrosshairTStyle; set { if (SetProperty(ref _cs2CrosshairTStyle, value)) NotifyCrosshairPreviewChanged(); } }
    public bool Cs2CrosshairUseAlpha { get => _cs2CrosshairUseAlpha; set { if (SetProperty(ref _cs2CrosshairUseAlpha, value)) NotifyCrosshairPreviewChanged(); } }
    public bool Cs2CrosshairFollowRecoil { get => _cs2CrosshairFollowRecoil; set { if (SetProperty(ref _cs2CrosshairFollowRecoil, value)) NotifyCrosshairPreviewChanged(); } }
    public double Cs2CrosshairSniperWidth { get => _cs2CrosshairSniperWidth; set { if (SetProperty(ref _cs2CrosshairSniperWidth, value)) NotifyCrosshairPreviewChanged(); } }
    public bool Cs2CrosshairGapUseWeaponValue { get => _cs2CrosshairGapUseWeaponValue; set => SetProperty(ref _cs2CrosshairGapUseWeaponValue, value); }
    public string Cs2CrosshairColor { get => _cs2CrosshairColor; set { if (SetProperty(ref _cs2CrosshairColor, value)) NotifyCrosshairPreviewChanged(); } }

    public Media.Brush Cs2CrosshairPreviewBrush
    {
        get
        {
            byte alpha = (byte)(Cs2CrosshairUseAlpha ? Clamp(Cs2CrosshairAlpha, 10, 255) : 255);
            string colorMode = NormalizeCrosshairColorMode(Cs2CrosshairColor);

            return colorMode switch
            {
                "0" => new Media.SolidColorBrush(Media.Color.FromArgb(alpha, 255, 0, 0)),
                "1" => new Media.SolidColorBrush(Media.Color.FromArgb(alpha, 0, 255, 0)),
                "2" => new Media.SolidColorBrush(Media.Color.FromArgb(alpha, 255, 255, 0)),
                "3" => new Media.SolidColorBrush(Media.Color.FromArgb(alpha, 0, 96, 255)),
                "4" => new Media.SolidColorBrush(Media.Color.FromArgb(alpha, 0, 190, 255)),
                _ => new Media.SolidColorBrush(Media.Color.FromArgb(
                    alpha,
                    (byte)Clamp(Cs2CrosshairRed, 0, 255),
                    (byte)Clamp(Cs2CrosshairGreen, 0, 255),
                    (byte)Clamp(Cs2CrosshairBlue, 0, 255)))
            };
        }
    }
    public double CrosshairPreviewCenterX => 290;
    public double CrosshairPreviewCenterY => 82;
    public double CrosshairPreviewArmLength => Math.Max(8, Math.Min(82, Cs2CrosshairSize * 9));
    public double CrosshairPreviewThickness => Math.Max(1, Math.Min(8, Cs2CrosshairThickness * 5));
    public double CrosshairOutlinePreviewThickness => CrosshairPreviewThickness + Math.Max(2, Cs2CrosshairOutlineThickness * 2);
    public double CrosshairPreviewGap => Math.Max(2, Math.Min(58, 11 + Cs2CrosshairGap * 5));
    public double CrosshairLeftX1 => CrosshairPreviewCenterX - CrosshairPreviewGap - CrosshairPreviewArmLength;
    public double CrosshairLeftX2 => CrosshairPreviewCenterX - CrosshairPreviewGap;
    public double CrosshairRightX1 => CrosshairPreviewCenterX + CrosshairPreviewGap;
    public double CrosshairRightX2 => CrosshairPreviewCenterX + CrosshairPreviewGap + CrosshairPreviewArmLength;
    public double CrosshairTopY1 => CrosshairPreviewCenterY - CrosshairPreviewGap - CrosshairPreviewArmLength;
    public double CrosshairTopY2 => CrosshairPreviewCenterY - CrosshairPreviewGap;
    public double CrosshairBottomY1 => CrosshairPreviewCenterY + CrosshairPreviewGap;
    public double CrosshairBottomY2 => CrosshairPreviewCenterY + CrosshairPreviewGap + CrosshairPreviewArmLength;
    public double CrosshairDotSize => Math.Max(4, CrosshairPreviewThickness + 4);
    public double CrosshairDotLeft => CrosshairPreviewCenterX - (CrosshairDotSize / 2);
    public double CrosshairDotTop => CrosshairPreviewCenterY - (CrosshairDotSize / 2);
    public double CrosshairRecoilY => CrosshairPreviewCenterY + 36;
    public System.Windows.Visibility CrosshairTopVisibility => Cs2CrosshairTStyle ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    public System.Windows.Visibility CrosshairOutlineVisibility => Cs2CrosshairOutline ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public System.Windows.Visibility CrosshairTopOutlineVisibility => Cs2CrosshairOutline && !Cs2CrosshairTStyle ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public System.Windows.Visibility CrosshairRecoilVisibility => Cs2CrosshairFollowRecoil ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public string Cs2Summary => _cs2Analysis?.Summary ?? _localization.T("CS2.NotScanned");
    public string Cs2ConfigPath => _cs2Analysis?.Paths.VideoConfigPath ?? _localization.T("CS2.NotDetected");
    public string Cs2AutoexecPath => _cs2Service.GetAutoexecPath(_cs2Analysis) ?? _localization.T("CS2.Autoexec.NotDetected");
    public string Cs2ResolutionDisplay => GetCs2DisplayValue("cs2.resolution");
    public string Cs2DisplayModeDisplay => GetCs2DisplayValue("cs2.display_mode");
    public string Cs2VsyncDisplay => GetCs2DisplayValue("setting.mat_vsync");
    public string Cs2BaselineMatchDisplay => _cs2Analysis == null || _cs2Analysis.BaselineTotalSettings == 0
        ? _localization.T("CS2.NotScannedShort")
        : string.Format(_localization.T("CS2.BaselineMatch"), _cs2Analysis.BaselineMatchedSettings, _cs2Analysis.BaselineTotalSettings);

    public LibraryViewModel(LocalizationService localization, AppRuntimeService runtime)
    {
        _localization = localization;
        _runtime = runtime;

        foreach (var core in _runtime.Cores) Cores.Add(core);
        OnPropertyChanged(nameof(PhysicalCores));
        OnPropertyChanged(nameof(ThreadCores));
        RefreshOptimizationModes();
        RefreshPriorities();
        ApplyUnoptimizedCpuEditorSelection();

        ScanSteamCommand = new RelayCommand(_ => ScanSteam(), _ => !IsBusy);
        ScanEpicCommand = new RelayCommand(_ => ScanEpic(), _ => !IsBusy);
        ScanAllCommand = new RelayCommand(_ => ScanAll(), _ => !IsBusy);
        BrowseExecutableCommand = new RelayCommand(_ => BrowseExecutable());
        AddManualCommand = new RelayCommand(_ => AddManual(), _ => File.Exists(NewExecutablePath));
        AddFolderCommand = new RelayCommand(_ => AddCustomFolder());
        ScanCustomCommand = new RelayCommand(_ => ScanCustom(), _ => !IsBusy);
        RefreshStatusCommand = new RelayCommand(_ => RefreshRuntimeState());
        RemoveItemCommand = new RelayCommand(parameter => RemoveItem(parameter as LibraryItemViewModel));
        SelectItemCommand = new RelayCommand(parameter => SelectedItem = parameter as LibraryItemViewModel);
        LaunchSelectedCommand = new RelayCommand(_ => LaunchSelected(), _ => SelectedItem != null && File.Exists(SelectedItem.Item.ExecutablePath));
        OpenSelectedFolderCommand = new RelayCommand(_ => OpenSelectedFolder(), _ => SelectedItem != null);
        CreateUpdateProfileCommand = new RelayCommand(_ => CreateOrUpdateProfileForSelected(), _ => SelectedItem != null && !string.IsNullOrWhiteSpace(SelectedItem.Item.ProcessName));
        ApplyLinkedProfileCommand = new RelayCommand(_ => ApplyLinkedProfile(), _ => SelectedItem != null && !string.IsNullOrWhiteSpace(SelectedItem.Item.ProcessName));
        SelectAllCoresCommand = new RelayCommand(_ => SetAllCores(true));
        ClearAllCoresCommand = new RelayCommand(_ => SetAllCores(false));
        DisableSmtCommand = new RelayCommand(_ => DisableSmtThreads());
        DisableECoresCommand = new RelayCommand(_ => DisableEfficiencyCores());
        ScanCs2Command = new RelayCommand(_ => AnalyzeCs2IfSelected(), _ => IsCs2Selected && CanEditCs2Config);
        BackupCs2Command = new RelayCommand(_ => BackupCs2(), _ => IsCs2Selected && _cs2Analysis?.Paths.IsComplete == true && CanEditCs2Config);
        ApplyCs2PresetCommand = new RelayCommand(_ => ApplyCs2Preset(), _ => IsCs2Selected && SelectedCs2Preset != null && _cs2Analysis?.Paths.IsComplete == true && CanEditCs2Config);
        RestoreCs2BackupCommand = new RelayCommand(_ => RestoreLatestCs2Backup(), _ => IsCs2Selected && CanEditCs2Config);
        LoadCs2AutoexecCommand = new RelayCommand(_ => LoadCs2Autoexec(createIfMissing: true), _ => IsCs2Selected && CanEditCs2Config);
        SaveCs2AutoexecCommand = new RelayCommand(_ => SaveCs2Autoexec(), _ => IsCs2Selected && CanEditCs2Config);
        OpenCs2AutoexecFolderCommand = new RelayCommand(_ => OpenCs2AutoexecFolder(), _ => IsCs2Selected);
        InsertCs2GrenadeBindsCommand = new RelayCommand(_ => InsertCs2GrenadeBinds(), _ => IsCs2Selected && CanEditCs2Config);
        InsertCs2RadarCommand = new RelayCommand(_ => InsertCs2RadarSettings(), _ => IsCs2Selected && CanEditCs2Config);
        InsertCs2FpsMaxCommand = new RelayCommand(_ => InsertCs2FpsMax(), _ => IsCs2Selected && CanEditCs2Config);
        BackToLibraryCommand = new RelayCommand(_ => BackToLibrary());
        ClearCs2AutoexecCommand = new RelayCommand(_ => ClearCs2Autoexec(), _ => IsCs2Selected && CanEditCs2Config);
        LoadCs2CrosshairCommand = new RelayCommand(_ => LoadCs2CrosshairSettings(), _ => IsCs2Selected && CanEditCs2Config);
        SaveCs2CrosshairCommand = new RelayCommand(_ => SaveCs2CrosshairSettings(), _ => IsCs2Selected && CanEditCs2Config);
        InsertCs2SensitivityCommand = new RelayCommand(_ => InsertCs2Sensitivity(), _ => IsCs2Selected && CanEditCs2Config);
        InsertCs2VolumeCommand = new RelayCommand(_ => InsertCs2Volume(), _ => IsCs2Selected && CanEditCs2Config);
        InsertCs2ConsoleCommand = new RelayCommand(_ => InsertCs2Console(), _ => IsCs2Selected && CanEditCs2Config);
        InsertCs2SmokeBindCommand = new RelayCommand(_ => InsertCs2Bind(Cs2SmokeKey, SmokeKeyPlaceholder, "slot8", "smoke"), _ => IsCs2Selected && CanEditCs2Config);
        InsertCs2FlashBindCommand = new RelayCommand(_ => InsertCs2Bind(Cs2FlashKey, FlashKeyPlaceholder, "slot7", "flash"), _ => IsCs2Selected && CanEditCs2Config);
        InsertCs2MolotovBindCommand = new RelayCommand(_ => InsertCs2Bind(Cs2MolotovKey, MolotovKeyPlaceholder, "slot10", "molotov"), _ => IsCs2Selected && CanEditCs2Config);
        InsertCs2HeBindCommand = new RelayCommand(_ => InsertCs2Bind(Cs2HeKey, HeKeyPlaceholder, "slot6", "he"), _ => IsCs2Selected && CanEditCs2Config);
        InsertCs2VoiceBindCommand = new RelayCommand(_ => InsertCs2Bind(Cs2VoiceKey, VoiceKeyPlaceholder, "+voicerecord", "voice"), _ => IsCs2Selected && CanEditCs2Config);
        InsertCs2JumpBindCommand = new RelayCommand(_ => InsertCs2JumpBind(), _ => IsCs2Selected && CanEditCs2Config);
        InsertCs2CustomBindCommand = new RelayCommand(_ => InsertCs2CustomBind(), _ => IsCs2Selected && CanEditCs2Config);

        _cs2ProcessTimer.Tick += (_, _) => RefreshCs2ProcessState();
        _cs2ProcessTimer.Start();

        Reload();
    }

    public void RefreshTexts()
    {
        foreach (var item in Items) item.RefreshTexts();
        foreach (var change in Cs2SettingChanges) change.RefreshTexts();
        foreach (var mode in AvailableOptimizationModes) mode.RefreshTexts();
        RefreshOptimizationModes();
        RefreshPriorities();
        OnPropertyChanged(string.Empty);
    }

    public void Reload()
    {
        Items.Clear();
        foreach (var item in _libraryService.LoadItems()) Items.Add(CreateItemViewModel(item));

        CustomFolders.Clear();
        foreach (string folder in _runtime.Settings.CustomLibraryLocations) CustomFolders.Add(folder);

        RefreshRuntimeState();
        StatusMessage = _localization.T("Library.Ready");
        OnPropertyChanged(nameof(ReadyCountText));
    }

    private void BrowseExecutable()
    {
        using var dialog = new WinFormsOpenFileDialog
        {
            Filter = IsPolish ? "Pliki wykonywalne (*.exe)|*.exe|Wszystkie pliki (*.*)|*.*" : "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
            Title = IsPolish ? "Wybierz plik wykonywalny" : "Select executable"
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            NewExecutablePath = dialog.FileName;
            if (string.IsNullOrWhiteSpace(NewDisplayName)) NewDisplayName = Path.GetFileNameWithoutExtension(dialog.FileName);
        }
    }

    private void AddManual()
    {
        if (!File.Exists(NewExecutablePath))
        {
            StatusMessage = _localization.T("Library.ExecutableMissing");
            return;
        }

        var item = ExecutableResolver.CreateManualItemFromExecutable(NewExecutablePath, NewItemType);
        if (!string.IsNullOrWhiteSpace(NewDisplayName)) item.DisplayName = NewDisplayName.Trim();
        MergeAndSave(new[] { item }, _localization.T("Library.ManualAdded"));
        SelectedItem = Items.FirstOrDefault(x => x.Item.ExecutablePath?.Equals(item.ExecutablePath, StringComparison.OrdinalIgnoreCase) == true);

        NewDisplayName = string.Empty;
        NewExecutablePath = string.Empty;
        NewItemType = LibraryItemType.Game;
    }

    private void ScanSteam() => ScanWith(() => _steamScanner.Scan(), "Steam");
    private void ScanEpic() => ScanWith(() => _epicScanner.Scan(), "Epic");

    private void ScanAll()
    {
        IsBusy = true;
        try
        {
            var items = new List<LibraryItem>();
            var warnings = new List<string>();
            var steam = _steamScanner.Scan();
            var epic = _epicScanner.Scan();
            items.AddRange(steam.Items);
            items.AddRange(epic.Items);
            warnings.AddRange(steam.Warnings);
            warnings.AddRange(epic.Warnings);
            MergeAndSave(items, string.Format(_localization.T("Library.ScanResult"), items.Count));
            AppendWarnings(warnings);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AddCustomFolder()
    {
        using var dialog = new WinFormsFolderDialog
        {
            Description = IsPolish ? "Wybierz folder z grami albo aplikacjami przenośnymi" : "Select folder with games or portable apps",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        string folder = dialog.SelectedPath;
        if (_runtime.Settings.CustomLibraryLocations.Any(x => x.Equals(folder, StringComparison.OrdinalIgnoreCase))) return;

        _runtime.Settings.CustomLibraryLocations.Add(folder);
        _runtime.SaveSettings(_runtime.Settings);
        CustomFolders.Add(folder);
        StatusMessage = string.Format(_localization.T("Library.FolderAdded"), folder);
    }

    private void ScanCustom() => ScanWith(() => _customScanner.Scan(_runtime.Settings.CustomLibraryLocations), "Custom");

    private void ScanWith(Func<FrameHub.Core.Models.Library.LibraryScanResult> scan, string sourceName)
    {
        IsBusy = true;
        try
        {
            var result = scan();
            MergeAndSave(result.Items, string.Format(_localization.T("Library.SourceScanResult"), sourceName, result.Items.Count));
            AppendWarnings(result.Warnings);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void MergeAndSave(IEnumerable<LibraryItem> newItems, string message)
    {
        var merged = _libraryService.MergeItems(Items.Select(x => x.Item), newItems);
        _libraryService.SaveItems(merged);
        Items.Clear();
        foreach (var item in merged) Items.Add(CreateItemViewModel(item));

        RefreshRuntimeState();
        StatusMessage = message;
        _runtime.AddActivity(message);
        OnPropertyChanged(nameof(ReadyCountText));
    }

    private void RemoveItem(LibraryItemViewModel? item)
    {
        if (item == null) return;
        if (SelectedItem == item) SelectedItem = null;
        Items.Remove(item);
        _libraryService.SaveItems(Items.Select(x => x.Item));
        StatusMessage = string.Format(_localization.T("Library.ItemRemoved"), item.DisplayName);
        OnPropertyChanged(nameof(ReadyCountText));
    }

    private void RefreshRuntimeState()
    {
        foreach (var item in Items) item.RefreshRuntimeState();
        OnPropertyChanged(nameof(ReadyCountText));
    }

    private void AppendWarnings(IEnumerable<string> warnings)
    {
        var list = warnings.Take(3).ToList();
        if (list.Count > 0) StatusMessage += " " + string.Join(" ", list);
    }

    private LibraryItemViewModel CreateItemViewModel(LibraryItem item) => new(item, _localization, () => _runtime.Profiles);

    private void RefreshOptimizationModes()
    {
        var current = SelectedOptimizationMode ?? OptimizationMode.CpuSets;
        AvailableOptimizationModes.Clear();
        AvailableOptimizationModes.Add(new OptimizationModeOptionViewModel(OptimizationMode.CpuSets, _localization));
        AvailableOptimizationModes.Add(new OptimizationModeOptionViewModel(OptimizationMode.Affinity, _localization));
        SelectedOptimizationMode = current;
    }

    private void RefreshPriorities()
    {
        string current = _selectedPriority;
        string currentRaw = string.IsNullOrWhiteSpace(current) ? string.Empty : PriorityService.Normalize(current, allowRealtime: true);
        AvailablePriorities.Clear();
        foreach (var priority in PriorityService.GetDisplayPriorities(_localization.CurrentLanguage, _runtime.Settings.AllowRealtimePriority))
        {
            AvailablePriorities.Add(priority);
        }
        SelectedPriority = string.IsNullOrWhiteSpace(currentRaw) ? string.Empty : PriorityService.Translate(currentRaw, _localization.CurrentLanguage);
    }

    private string BuildDetectedProcessorText()
    {
        int logical = Cores.Count;
        int performance = Cores.Count(core => !core.IsThread && !core.IsECore);
        int threads = Cores.Count(core => core.IsThread);
        int efficiency = Cores.Count(core => core.IsECore);
        string topology = string.Format(_localization.T("Library.CpuInfo.Topology"), logical, performance, threads, efficiency);
        return $"{_runtime.CpuName} · {topology}";
    }

    private string BuildCurrentOptimizationProfileText()
    {
        if (SelectedItem == null)
        {
            return _localization.T("Library.NoProfile.NoSelection");
        }

        if (SelectedOptimizationProfile == null)
        {
            return _localization.T("Library.NoProfile.Description");
        }

        string mode = new OptimizationModeOptionViewModel(SelectedOptimizationProfile.OptimizationMode, _localization).DisplayName;
        string priority = PriorityService.Translate(SelectedOptimizationProfile.Priority, _localization.CurrentLanguage);
        int selectedLogicalProcessors = CountSelectedLogicalProcessors(SelectedOptimizationProfile.AffinityMask);
        return string.Format(
            _localization.T("Library.CurrentProfile.Description"),
            SelectedOptimizationProfile.DisplayName,
            ProfileService.NormalizeProcessName(SelectedOptimizationProfile.ProcessName),
            mode,
            priority,
            selectedLogicalProcessors);
    }

    private int CountSelectedLogicalProcessors(long affinityMask)
    {
        if (affinityMask == 0) return 0;
        int count = 0;
        for (int i = 0; i < Cores.Count && i < 64; i++)
        {
            if ((affinityMask & (1L << i)) != 0) count++;
        }
        return count;
    }

    private string BuildRecommendedPresetText()
    {
        var recommendation = BuildStandardCpuOptimizationRecommendation();
        string mode = _localization.T("OptimizationMode.CpuSets");
        string priority = PriorityService.Translate("Normal", _localization.CurrentLanguage);
        string details = string.Format(_localization.T("Library.CpuInfo.RecommendedPreset"), mode, priority, recommendation.SelectedLogicalProcessors);
        return details + " " + recommendation.Description;
    }

    private void LoadProfileForSelectedItem()
    {
        if (SelectedItem == null)
        {
            SelectedOptimizationProfile = null;
            ApplyUnoptimizedCpuEditorSelection();
            return;
        }

        var profile = FindProfileForSelectedItem();
        SelectedOptimizationProfile = profile;
        if (profile != null)
        {
            SelectedPriority = PriorityService.Translate(profile.Priority, _localization.CurrentLanguage);
            SelectedOptimizationMode = profile.OptimizationMode;
            ApplyMaskToCores(profile.AffinityMask);
            return;
        }

        ApplyUnoptimizedCpuEditorSelection();
    }

    private ProcessProfile? FindProfileForSelectedItem()
    {
        if (SelectedItem == null) return null;
        string? profileId = SelectedItem.Item.LinkedProfileId;
        if (!string.IsNullOrWhiteSpace(profileId))
        {
            var byId = _runtime.Profiles.FirstOrDefault(p => p.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
            if (byId != null) return byId;
        }

        var byLibraryItemId = _runtime.Profiles.FirstOrDefault(p => p.LibraryItemId?.Equals(SelectedItem.Item.Id, StringComparison.OrdinalIgnoreCase) == true);
        if (byLibraryItemId != null) return byLibraryItemId;

        if (!string.IsNullOrWhiteSpace(SelectedItem.Item.ExecutablePath))
        {
            var byExecutablePath = _runtime.Profiles.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.ExecutablePath) && p.ExecutablePath.Equals(SelectedItem.Item.ExecutablePath, StringComparison.OrdinalIgnoreCase));
            if (byExecutablePath != null) return byExecutablePath;
        }

        string selectedProcessName = ProfileService.NormalizeProcessName(SelectedItem.Item.ProcessName);
        if (!string.IsNullOrWhiteSpace(selectedProcessName))
        {
            var byProcessName = _runtime.Profiles.FirstOrDefault(p => ProfileService.NormalizeProcessName(p.ProcessName).Equals(selectedProcessName, StringComparison.OrdinalIgnoreCase));
            if (byProcessName != null) return byProcessName;
        }

        return null;
    }

    private void CreateOrUpdateProfileForSelected()
    {
        if (SelectedItem == null || string.IsNullOrWhiteSpace(SelectedItem.Item.ProcessName)) return;
        long mask = BuildSelectedCoreMask();
        if (mask == 0) mask = BuildAllCoreMask();

        var existing = FindProfileForSelectedItem();
        var profile = existing ?? new ProcessProfile();
        profile.ProcessName = ProfileService.NormalizeProcessName(SelectedItem.Item.ProcessName);
        profile.DisplayName = SelectedItem.Item.DisplayName;
        profile.ExecutablePath = SelectedItem.Item.ExecutablePath;
        profile.LibraryItemId = SelectedItem.Item.Id;
        profile.AffinityMask = mask;
        profile.Priority = string.IsNullOrWhiteSpace(SelectedPriority) ? "Normal" : PriorityService.Normalize(SelectedPriority, _runtime.Settings.AllowRealtimePriority);
        profile.OptimizationMode = SelectedOptimizationMode ?? OptimizationMode.CpuSets;
        profile.IsEnabled = true;
        profile.ApplyCoreOptimization = true;
        profile.ApplyPriority = true;
        profile.Notes = $"Linked from FrameHub Library item: {SelectedItem.Item.DisplayName}.";
        profile.UpdatedAt = DateTime.UtcNow;

        _runtime.UpsertProfile(profile, replaceSameProcessName: false);
        var saved = _runtime.Profiles.FirstOrDefault(p => p.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase));
        if (saved != null)
        {
            SelectedItem.Item.LinkedProfileId = saved.Id;
            SelectedOptimizationProfile = saved;
        }
        else
        {
            SelectedOptimizationProfile = profile;
        }
        _libraryService.SaveItems(Items.Select(x => x.Item));
        SelectedItem.RefreshRuntimeState();
        _runtime.ApplyProfileNow(saved ?? profile, force: true);
        StatusMessage = string.Format(_localization.T("Library.ProfileSaved"), SelectedItem.Item.DisplayName);
        _runtime.AddActivity(StatusMessage);
    }

    private void ApplyLinkedProfile()
    {
        if (SelectedItem == null || string.IsNullOrWhiteSpace(SelectedItem.Item.ProcessName))
        {
            StatusMessage = _localization.T("Library.ProfileMissing");
            return;
        }

        // The Library-level Optimize button is intentionally destructive for the CPU profile:
        // it rebuilds the standard FrameHub preset and overwrites the current saved core-control
        // profile for this game/app instead of merely applying the old profile.
        ApplyRecommendedCpuPresetSelection();
        CreateOrUpdateProfileForSelected();

        var profile = FindProfileForSelectedItem();
        if (profile == null)
        {
            StatusMessage = _localization.T("Library.ProfileMissing");
            return;
        }

        bool cs2GraphicsApplied = false;
        bool cs2GraphicsSkippedBecauseRunning = false;
        string cs2GraphicsMessage = string.Empty;

        if (IsCs2Selected)
        {
            RefreshCs2ProcessState();
            if (IsCs2Running)
            {
                cs2GraphicsSkippedBecauseRunning = true;
                cs2GraphicsMessage = IsPolish
                    ? "UWAGA: CS2 był uruchomiony, więc ustawienia graficzne NIE zostały zoptymalizowane. Zamknij CS2 i kliknij Optymalizuj ponownie, żeby zastosować Preset turniejowy."
                    : "WARNING: CS2 was running, so graphics settings were NOT optimized. Close CS2 and click Optimize again to apply the Competitive preset.";
                Cs2StatusMessage = cs2GraphicsMessage;
                _runtime.AddActivity(cs2GraphicsMessage, "Warn");
            }
            else
            {
                cs2GraphicsApplied = ApplyCompetitiveCs2GraphicsPresetFromOptimize(out cs2GraphicsMessage);
            }
        }

        StatusMessage = string.Format(_localization.T("Library.ProfileApplied"), profile.ProcessName);

        string title = IsPolish ? "Optymalizacja zakończona" : "Optimization complete";
        string message = IsCs2Selected
            ? BuildCs2OptimizeDialogMessage(cs2GraphicsApplied, cs2GraphicsSkippedBecauseRunning, cs2GraphicsMessage)
            : (IsPolish
                ? "Standardowy profil procesora został ponownie wygenerowany, zapisany i zastosowany dla wybranej gry lub aplikacji. Jeżeli profil już istniał, został nadpisany."
                : "The standard CPU profile was regenerated, saved, and applied for the selected game or app. If a profile already existed, it was overwritten.");

        InfoDialogRequested?.Invoke(title, cs2GraphicsSkippedBecauseRunning ? "[WARN]" + message : message);
    }

    private string BuildCs2OptimizeDialogMessage(bool graphicsApplied, bool graphicsSkippedBecauseRunning, string graphicsMessage)
    {
        string cpuMessage = IsPolish
            ? "Standardowy profil procesora został ponownie wygenerowany, zapisany i zastosowany dla CS2. Jeżeli profil kontroli rdzeni już istniał, został nadpisany."
            : "The standard CPU profile was regenerated, saved, and applied for CS2. If a core-control profile already existed, it was overwritten.";

        if (graphicsSkippedBecauseRunning)
        {
            return cpuMessage + Environment.NewLine + Environment.NewLine + graphicsMessage;
        }

        if (graphicsApplied)
        {
            string graphicsAppliedMessage = IsPolish
                ? "Ustawienia graficzne CS2 zostały zoptymalizowane według Presetu turniejowego. Rozdzielczość i tryb wyświetlania nie zostały zmienione — zostają preferencją użytkownika."
                : "CS2 graphics settings were optimized with the Competitive preset. Resolution and display mode were not changed — they remain the user's preference.";
            return cpuMessage + Environment.NewLine + Environment.NewLine + graphicsAppliedMessage;
        }

        string graphicsFailedMessage = IsPolish
            ? "Nie udało się zastosować ustawień graficznych CS2: " + graphicsMessage
            : "CS2 graphics settings could not be applied: " + graphicsMessage;
        return cpuMessage + Environment.NewLine + Environment.NewLine + graphicsFailedMessage;
    }

    private bool ApplyCompetitiveCs2GraphicsPresetFromOptimize(out string message)
    {
        message = string.Empty;

        if (!EnsureCs2Analysis() || _cs2Analysis == null || !_cs2Analysis.Paths.IsComplete)
        {
            message = IsPolish ? "Nie wykryto kompletnej konfiguracji CS2." : "Complete CS2 config was not detected.";
            Cs2StatusMessage = message;
            return false;
        }

        var competitivePreset = _cs2Analysis.Presets.FirstOrDefault(preset => preset.Id.Equals("cs2_competitive_baseline", StringComparison.OrdinalIgnoreCase))
            ?? _cs2Service.Analyze(SelectedItem!.Item).Presets.FirstOrDefault(preset => preset.Id.Equals("cs2_competitive_baseline", StringComparison.OrdinalIgnoreCase));

        if (competitivePreset == null)
        {
            message = IsPolish ? "Nie znaleziono Presetu turniejowego." : "Competitive preset was not found.";
            Cs2StatusMessage = message;
            return false;
        }

        foreach (var change in competitivePreset.Changes)
        {
            if (change.Key.Equals("cs2.resolution", StringComparison.OrdinalIgnoreCase)
                || change.Key.Equals("cs2.display_mode", StringComparison.OrdinalIgnoreCase))
            {
                change.IsSelected = false;
                change.TargetValue = change.CurrentValue;
            }
        }

        var result = _cs2Service.ApplyPreset(_cs2Analysis, competitivePreset);
        Cs2StatusMessage = result.Message;
        _runtime.AddActivity(result.Message, result.Success ? "Info" : "Warn");
        AnalyzeCs2IfSelected();
        message = result.Message;
        return result.Success;
    }

    private void LaunchSelected()
    {
        if (SelectedItem?.Item.ExecutablePath == null || !File.Exists(SelectedItem.Item.ExecutablePath)) return;
        Process.Start(new ProcessStartInfo
        {
            FileName = SelectedItem.Item.ExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(SelectedItem.Item.ExecutablePath),
            UseShellExecute = true
        });
    }

    private void OpenSelectedFolder()
    {
        string? folder = SelectedItem?.Item.InstallPath;
        if (string.IsNullOrWhiteSpace(folder) && !string.IsNullOrWhiteSpace(SelectedItem?.Item.ExecutablePath)) folder = Path.GetDirectoryName(SelectedItem.Item.ExecutablePath);
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder)) Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
    }

    private void SetAllCores(bool isChecked)
    {
        foreach (var core in Cores) core.IsChecked = isChecked;
    }

    private void DisableSmtThreads()
    {
        foreach (var core in Cores.Where(c => c.IsThread)) core.IsChecked = false;
    }

    private void DisableEfficiencyCores()
    {
        foreach (var core in Cores.Where(c => c.IsECore)) core.IsChecked = false;
    }


    private void ApplyUnoptimizedCpuEditorSelection()
    {
        ApplyMaskToCores(BuildAllCoreMask());
        SelectedOptimizationMode = OptimizationMode.CpuSets;
        SelectedPriority = PriorityService.Translate("Normal", _localization.CurrentLanguage);
    }

    private void ApplyRecommendedCpuPresetSelection()
    {
        var recommendation = BuildStandardCpuOptimizationRecommendation();
        ApplyMaskToCores(recommendation.AffinityMask);
        SelectedOptimizationMode = OptimizationMode.CpuSets;
        SelectedPriority = PriorityService.Translate("Normal", _localization.CurrentLanguage);
    }

    private CpuOptimizationRecommendation BuildStandardCpuOptimizationRecommendation()
    {
        long mask = BuildAllCoreMask();
        bool hasECores = Cores.Any(core => core.IsECore);
        bool hasSmtOrHt = Cores.Any(core => core.IsThread);
        bool isAmd = _runtime.CpuVendor.Contains("AMD", StringComparison.OrdinalIgnoreCase)
            || _runtime.CpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase)
            || _runtime.CpuName.Contains("Ryzen", StringComparison.OrdinalIgnoreCase);
        bool isIntel = _runtime.CpuVendor.Contains("Intel", StringComparison.OrdinalIgnoreCase)
            || _runtime.CpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase)
            || _runtime.CpuName.Contains("Core", StringComparison.OrdinalIgnoreCase);

        if (isAmd)
        {
            mask = RemoveSmtOrHtThreads(mask);
            mask = RemovePrimaryCoreZero(mask);
            return new CpuOptimizationRecommendation(
                mask,
                IsPolish
                    ? "Dla AMD domyślnie odznacza SMT oraz główny Core 0, a priorytet procesu zostaje Normalny."
                    : "For AMD, the default preset disables SMT and the primary Core 0 while keeping process priority Normal.");
        }

        if (isIntel && hasECores)
        {
            mask = RemoveEfficiencyCores(mask);
            string description;
            if (hasSmtOrHt)
            {
                mask = RemoveSmtOrHtThreads(mask);
                description = IsPolish
                    ? "Dla hybrydowych Intel z P/E-core i HT domyślnie odznacza rdzenie E oraz wątki HT, a priorytet procesu zostaje Normalny."
                    : "For hybrid Intel CPUs with P/E-cores and HT, the default preset disables E-cores and HT threads while keeping process priority Normal.";
            }
            else
            {
                description = IsPolish
                    ? "Dla najnowszych hybrydowych Intel bez HT domyślnie odznacza rdzenie E, a priorytet procesu zostaje Normalny."
                    : "For newer hybrid Intel CPUs without HT, the default preset disables E-cores while keeping process priority Normal.";
            }

            return new CpuOptimizationRecommendation(mask, description);
        }

        if (isIntel)
        {
            mask = RemovePrimaryCoreZero(mask);
            if (hasSmtOrHt) mask = RemoveSmtOrHtThreads(mask);

            return new CpuOptimizationRecommendation(
                mask,
                hasSmtOrHt
                    ? (IsPolish
                        ? "Dla klasycznych Intel bez podziału P/E domyślnie odznacza Core 0 oraz wątki HT, a priorytet procesu zostaje Normalny."
                        : "For non-hybrid Intel CPUs, the default preset disables Core 0 and HT threads while keeping process priority Normal.")
                    : (IsPolish
                        ? "Dla klasycznych Intel bez podziału P/E domyślnie odznacza Core 0, a priorytet procesu zostaje Normalny."
                        : "For non-hybrid Intel CPUs, the default preset disables Core 0 while keeping process priority Normal."));
        }

        return new CpuOptimizationRecommendation(
            mask,
            IsPolish
                ? "Nie rozpoznano jednoznacznie rodziny CPU, więc domyślny preset zostawia wszystkie procesory logiczne zaznaczone i priorytet Normalny."
                : "The CPU family was not detected clearly, so the default preset keeps all logical processors selected and priority Normal.");
    }

    private long RemoveSmtOrHtThreads(long mask)
    {
        foreach (var core in Cores.Where(core => core.IsThread && core.Index is >= 0 and < 64))
        {
            mask &= ~(1L << core.Index);
        }

        return mask;
    }

    private long RemoveEfficiencyCores(long mask)
    {
        foreach (var core in Cores.Where(core => core.IsECore && core.Index is >= 0 and < 64))
        {
            mask &= ~(1L << core.Index);
        }

        return mask;
    }

    private long RemovePrimaryCoreZero(long mask)
    {
        var coreZero = Cores.FirstOrDefault(core => core.Index == 0)
            ?? Cores.Where(core => !core.IsThread).OrderBy(core => core.Index).FirstOrDefault()
            ?? Cores.OrderBy(core => core.Index).FirstOrDefault();

        if (coreZero is { Index: >= 0 and < 64 })
        {
            mask &= ~(1L << coreZero.Index);
        }

        return mask;
    }

    private sealed record CpuOptimizationRecommendation(long AffinityMask, string Description)
    {
        public int SelectedLogicalProcessors
        {
            get
            {
                if (AffinityMask == 0) return 0;
                int count = 0;
                for (int i = 0; i < 64; i++)
                {
                    if ((AffinityMask & (1L << i)) != 0) count++;
                }

                return count;
            }
        }
    }

    private long BuildSelectedCoreMask()
    {
        long mask = 0;
        for (int i = 0; i < Cores.Count && i < 64; i++)
        {
            if (Cores[i].IsChecked) mask |= 1L << i;
        }
        return mask;
    }

    private long BuildAllCoreMask()
    {
        long mask = 0;
        for (int i = 0; i < Cores.Count && i < 64; i++) mask |= 1L << i;
        return mask;
    }

    private void ApplyMaskToCores(long mask)
    {
        for (int i = 0; i < Cores.Count && i < 64; i++) Cores[i].IsChecked = (mask & (1L << i)) != 0;
    }

    private void AnalyzeCs2IfSelected()
    {
        AnalyzeCs2IfSelected(selectCustomPresetAfterScan: false, customTargets: null);
    }

    private void AnalyzeCs2IfSelected(bool selectCustomPresetAfterScan, IReadOnlyDictionary<string, string>? customTargets)
    {
        Cs2Presets.Clear();
        Cs2SettingChanges.Clear();
        _cs2Analysis = null;
        SelectedCs2Preset = null;
        Cs2StatusMessage = string.Empty;
        Cs2AutoexecText = string.Empty;
        Cs2AutoexecStatusMessage = string.Empty;

        if (!IsCs2Selected || SelectedItem == null)
        {
            NotifyCs2OverviewChanged();
            return;
        }

        RefreshCs2ProcessState();
        if (IsCs2Running)
        {
            Cs2StatusMessage = IsPolish
                ? "CS2 jest uruchomiony. FrameHub nie odczytuje teraz żadnych wartości z plików konfiguracyjnych. Zamknij grę, aby wczytać lub edytować Config."
                : "CS2 is running. FrameHub is not reading any values from CS2 config files right now. Close the game to load or edit Config.";
            Cs2AutoexecStatusMessage = Cs2StatusMessage;
            _runtime.AddActivity(Cs2StatusMessage, "Warn");
            NotifyCs2OverviewChanged();
            return;
        }

        _cs2Analysis = _cs2Service.Analyze(SelectedItem.Item);
        Cs2StatusMessage = _cs2Analysis.StatusMessage;
        foreach (var preset in _cs2Analysis.Presets) Cs2Presets.Add(preset);

        if (selectCustomPresetAfterScan)
        {
            SelectCustomCs2PresetFromCurrentSettings(customTargets);
        }

        SelectedCs2Preset ??= Cs2Presets.FirstOrDefault(preset => preset.Id.Equals("cs2_competitive_baseline", StringComparison.OrdinalIgnoreCase))
            ?? Cs2Presets.FirstOrDefault();

        LoadCs2Autoexec(createIfMissing: false);
        LoadCs2CrosshairSettings();
        NotifyCs2OverviewChanged();
    }

    private void RefreshCs2Changes()
    {
        Cs2SettingChanges.Clear();
        if (SelectedCs2Preset == null) return;

        foreach (var change in SelectedCs2Preset.Changes)
        {
            var vm = new Cs2SettingChangeViewModel(change, _localization);
            vm.TargetValueChangedByUser += (_, _) => MarkCs2PresetAsCustom();
            Cs2SettingChanges.Add(vm);
        }
    }

    private void MarkCs2PresetAsCustom()
    {
        if (SelectedCs2Preset?.Id.Equals("cs2_custom", StringComparison.OrdinalIgnoreCase) == true) return;
        if (Cs2SettingChanges.Count == 0) return;

        var custom = new GameOptimizationPreset
        {
            Id = "cs2_custom",
            DisplayName = IsPolish ? "Niestandardowy" : "Custom",
            Description = IsPolish
                ? "Niestandardowy zestaw ustawień utworzony przez ręczną zmianę wartości w kolumnie Ustaw."
                : "Custom settings created by manually changing values in the Set column.",
            Changes = Cs2SettingChanges.Select(vm => vm.Change).ToList()
        };

        var existing = Cs2Presets.FirstOrDefault(preset => preset.Id.Equals("cs2_custom", StringComparison.OrdinalIgnoreCase));
        if (existing != null) Cs2Presets.Remove(existing);
        Cs2Presets.Add(custom);

        _suppressCs2PresetRefresh = true;
        try
        {
            SelectedCs2Preset = custom;
        }
        finally
        {
            _suppressCs2PresetRefresh = false;
        }

        OnPropertyChanged(nameof(Cs2PresetDescription));
    }

    private void SelectCustomCs2PresetFromCurrentSettings(IReadOnlyDictionary<string, string>? customTargets)
    {
        if (_cs2Analysis == null) return;

        var baseline = _cs2Analysis.Presets.FirstOrDefault(preset => preset.Id.Equals("cs2_competitive_baseline", StringComparison.OrdinalIgnoreCase));
        if (baseline == null) return;

        var custom = new GameOptimizationPreset
        {
            Id = "cs2_custom",
            DisplayName = IsPolish ? "Niestandardowy" : "Custom",
            Description = IsPolish
                ? "Niestandardowy zestaw ustawień utworzony przez ręczną zmianę wartości w kolumnie Ustaw."
                : "Custom settings created by manually changing values in the Set column.",
            Changes = baseline.Changes.Select(change => CloneCs2ChangeForCustomPreset(change, customTargets)).ToList()
        };

        var existing = Cs2Presets.FirstOrDefault(preset => preset.Id.Equals("cs2_custom", StringComparison.OrdinalIgnoreCase));
        if (existing != null) Cs2Presets.Remove(existing);
        Cs2Presets.Add(custom);

        _suppressCs2PresetRefresh = true;
        try
        {
            SelectedCs2Preset = custom;
        }
        finally
        {
            _suppressCs2PresetRefresh = false;
        }

        RefreshCs2Changes();
        OnPropertyChanged(nameof(Cs2PresetDescription));
    }

    private static GameSettingChange CloneCs2ChangeForCustomPreset(GameSettingChange source, IReadOnlyDictionary<string, string>? customTargets)
    {
        string target = source.CurrentValue;
        if (customTargets != null && customTargets.TryGetValue(source.Key, out string? customTarget) && !string.IsNullOrWhiteSpace(customTarget))
        {
            target = customTarget;
        }

        var clone = new GameSettingChange
        {
            Key = source.Key,
            DisplayName = source.DisplayName,
            Description = source.Description,
            CurrentValue = source.CurrentValue,
            RecommendedValue = source.RecommendedValue,
            TargetValue = target,
            TargetFile = source.TargetFile,
            RiskLevel = source.RiskLevel,
            IsOptional = source.IsOptional,
            CanApply = source.CanApply,
            Options = source.Options.Select(option => new GameSettingOption
            {
                Value = option.Value,
                DisplayOverride = option.DisplayOverride
            }).ToList()
        };

        if (string.Equals(clone.CurrentValue, "not found", StringComparison.OrdinalIgnoreCase))
        {
            clone.Status = GameOptimizationSettingStatus.NotDetected;
            clone.IsSelected = false;
        }
        else if (!clone.CanApply)
        {
            clone.Status = GameOptimizationSettingStatus.ReadOnly;
            clone.IsSelected = false;
        }
        else if (clone.IsOptional && !string.Equals(clone.CurrentValue, clone.RecommendedValue, StringComparison.OrdinalIgnoreCase))
        {
            clone.Status = GameOptimizationSettingStatus.OptionalPreference;
            clone.IsSelected = false;
        }
        else
        {
            clone.Status = string.Equals(clone.CurrentValue, clone.RecommendedValue, StringComparison.OrdinalIgnoreCase)
                ? GameOptimizationSettingStatus.MatchesBaseline
                : GameOptimizationSettingStatus.DifferentFromBaseline;
            clone.IsSelected = false;
        }

        return clone;
    }

    private string GetCs2DisplayValue(string key)
    {
        var preset = SelectedCs2Preset ?? Cs2Presets.FirstOrDefault();
        var change = preset?.Changes.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        return change == null ? _localization.T("CS2.NotScannedShort") : new Cs2SettingChangeViewModel(change, _localization).CurrentDisplayValue;
    }

    private void NotifyCs2OverviewChanged()
    {
        OnPropertyChanged(nameof(Cs2Summary));
        OnPropertyChanged(nameof(Cs2ConfigPath));
        OnPropertyChanged(nameof(Cs2ResolutionDisplay));
        OnPropertyChanged(nameof(Cs2DisplayModeDisplay));
        OnPropertyChanged(nameof(Cs2VsyncDisplay));
        OnPropertyChanged(nameof(Cs2BaselineMatchDisplay));
        OnPropertyChanged(nameof(Cs2PresetDescription));
        OnPropertyChanged(nameof(Cs2AutoexecPath));
    }

    private bool EnsureCs2Analysis()
    {
        if (_cs2Analysis != null) return true;
        if (!IsCs2Selected || SelectedItem == null) return false;

        RefreshCs2ProcessState();
        if (IsCs2Running)
        {
            Cs2StatusMessage = IsPolish
                ? "CS2 jest uruchomiony. FrameHub nie odczytuje teraz żadnych wartości z plików konfiguracyjnych."
                : "CS2 is running. FrameHub is not reading any values from CS2 config files right now.";
            Cs2AutoexecStatusMessage = Cs2StatusMessage;
            _runtime.AddActivity(Cs2StatusMessage, "Warn");
            return false;
        }

        _cs2Analysis = _cs2Service.Analyze(SelectedItem.Item);
        return true;
    }

    private void RefreshCs2ProcessState()
    {
        IsCs2Running = IsProcessRunning("cs2");
    }

    private static bool IsProcessRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private bool EnsureCs2CanRead(string actionName)
    {
        RefreshCs2ProcessState();
        if (!IsCs2Running) return true;

        string message = IsPolish
            ? $"Nie wykonano operacji '{actionName}', bo CS2 jest uruchomiony. FrameHub nie odczytuje żadnych wartości z plików konfiguracyjnych podczas działania gry."
            : $"Skipped '{actionName}' because CS2 is running. FrameHub does not read any CS2 config values while the game is running.";
        Cs2StatusMessage = message;
        Cs2AutoexecStatusMessage = message;
        _runtime.AddActivity(message, "Warn");
        return false;
    }

    private bool EnsureCs2CanEdit(string actionName)
    {
        RefreshCs2ProcessState();
        if (!IsCs2Running) return true;

        string message = IsPolish
            ? $"Nie wykonano operacji '{actionName}', bo CS2 jest uruchomiony. Zamknij grę i spróbuj ponownie."
            : $"Skipped '{actionName}' because CS2 is running. Close the game and try again.";
        Cs2StatusMessage = message;
        Cs2AutoexecStatusMessage = message;
        _runtime.AddActivity(message, "Warn");
        return false;
    }

    private void LoadCs2Autoexec(bool createIfMissing)
    {
        if (!EnsureCs2CanRead("load autoexec.cfg")) return;
        if (!EnsureCs2Analysis() || _cs2Analysis == null) return;
        var result = _cs2Service.LoadAutoexec(_cs2Analysis, createIfMissing);
        if (result.Success)
        {
            Cs2AutoexecText = result.Content;
        }
        Cs2AutoexecStatusMessage = result.Message;
        OnPropertyChanged(nameof(Cs2AutoexecPath));
    }

    private void SaveCs2Autoexec()
    {
        if (!EnsureCs2CanEdit("save autoexec.cfg")) return;
        if (!EnsureCs2Analysis() || _cs2Analysis == null) return;
        var result = _cs2Service.SaveAutoexec(_cs2Analysis, Cs2AutoexecText);
        Cs2AutoexecStatusMessage = result.Message;
        _runtime.AddActivity(result.Message, result.Success ? "Info" : "Warn");
        OnPropertyChanged(nameof(Cs2AutoexecPath));
    }

    private void OpenCs2AutoexecFolder()
    {
        if (!EnsureCs2Analysis() || _cs2Analysis == null) return;
        string? path = _cs2Service.GetAutoexecPath(_cs2Analysis);
        string? folder = string.IsNullOrWhiteSpace(path) ? null : Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
        {
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
    }

    private void BackToLibrary()
    {
        SelectedItem = null;
    }

    private void ClearCs2Autoexec()
    {
        if (!EnsureCs2CanEdit("clear autoexec.cfg editor")) return;
        Cs2AutoexecText = string.Empty;
        Cs2AutoexecStatusMessage = IsPolish ? "Wyczyszczono edytor. Kliknij Zapisz autoexec.cfg, aby zapisać pusty plik." : "Editor cleared. Click Save autoexec.cfg to write an empty file.";
    }

    private void InsertCs2GrenadeBinds()
    {
        if (!EnsureCs2CanEdit("insert grenade binds")) return;
        InsertCs2Bind(Cs2SmokeKey, SmokeKeyPlaceholder, "slot8", "smoke");
        InsertCs2Bind(Cs2FlashKey, FlashKeyPlaceholder, "slot7", "flash");
        InsertCs2Bind(Cs2MolotovKey, MolotovKeyPlaceholder, "slot10", "molotov");
        InsertCs2Bind(Cs2HeKey, HeKeyPlaceholder, "slot6", "he");
    }

    private void InsertCs2RadarSettings()
    {
        if (!EnsureCs2CanEdit("insert radar settings")) return;
        UpsertFrameHubCommand("radar", "// QOL", new[]
        {
            "cl_radar_scale \"0.3\"",
            "cl_radar_always_centered \"0\""
        });
    }

    private void InsertCs2Console()
    {
        if (!EnsureCs2CanEdit("insert console setting")) return;
        UpsertFrameHubCommand("console", "// QOL", new[] { "con_enable \"1\"" });
    }

    private void InsertCs2FpsMax()
    {
        if (!EnsureCs2CanEdit("insert fps_max")) return;
        string value = SanitizeValue(SelectedCs2FpsMax, FpsMaxPlaceholder);
        UpsertFrameHubCommand("fps_max", "// General", new[] { $"fps_max \"{value}\"" });
    }

    private void InsertCs2Sensitivity()
    {
        if (!EnsureCs2CanEdit("insert sensitivity")) return;
        string value = SanitizeValue(Cs2MouseSensitivity, MouseSensitivityPlaceholder);
        UpsertFrameHubCommand("sensitivity", "// General", new[] { $"sensitivity \"{value}\"" });
    }

    private void InsertCs2Volume()
    {
        if (!EnsureCs2CanEdit("insert volume")) return;
        string value = SanitizeValue(Cs2Volume, VolumePlaceholder);
        UpsertFrameHubCommand("volume", "// General", new[] { $"volume \"{value}\"" });
    }

    private void InsertCs2Bind(string key, string fallbackKey, string command, string blockKey)
    {
        if (!EnsureCs2CanEdit($"insert bind {blockKey}")) return;
        key = SanitizeKey(SanitizeValue(key, fallbackKey));
        if (string.IsNullOrWhiteSpace(key)) return;
        UpsertFrameHubCommand($"bind_{blockKey}", "// Binds", new[] { $"bind \"{key}\" \"{command}\"" });
    }

    private void InsertCs2JumpBind()
    {
        if (!EnsureCs2CanEdit("insert jump bind")) return;
        string mode = SelectedCs2JumpBindMode ?? string.Empty;
        var lines = new List<string>();
        if (mode.Contains("dół", StringComparison.OrdinalIgnoreCase) || mode.Contains("down", StringComparison.OrdinalIgnoreCase) || mode.Equals("Oba", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add("bind \"mwheeldown\" \"+jump\"");
        }
        if (mode.Contains("gór", StringComparison.OrdinalIgnoreCase) || mode.Contains("up", StringComparison.OrdinalIgnoreCase) || mode.Equals("Oba", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add("bind \"mwheelup\" \"+jump\"");
        }
        if (lines.Count > 0) UpsertFrameHubCommand("bind_jump_scroll", "// Binds", lines);
    }

    private void InsertCs2CustomBind()
    {
        if (!EnsureCs2CanEdit("insert custom bind")) return;
        string key = SanitizeKey(Cs2CustomBindKey);
        string command = (Cs2CustomBindCommandText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(command)) return;
        command = command.Replace("\"", "");
        UpsertFrameHubCommand($"bind_custom_{key.ToLowerInvariant()}", "// Custom binds", new[] { $"bind \"{key}\" \"{command}\"" });
    }

    private void UpsertFrameHubCommand(string commandKey, string categoryHeader, IEnumerable<string> commandLines)
    {
        const string startMarker = "// >>> FrameHub: CS2 generated config";
        const string endMarker = "// <<< FrameHub: CS2 generated config";
        string current = Cs2AutoexecText ?? string.Empty;
        string pattern = Regex.Escape(startMarker) + "(?<body>.*?)" + Regex.Escape(endMarker);
        string keyTag = $"// FrameHubKey:{commandKey}";
        string body;

        var match = Regex.Match(current, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (match.Success)
        {
            body = match.Groups["body"].Value.Trim('\r', '\n');
        }
        else
        {
            body = "// General" + Environment.NewLine + "// QOL" + Environment.NewLine + "// Binds" + Environment.NewLine + "// Custom binds";
        }

        var lines = body.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Where(line => !line.Contains($"FrameHubKey:{commandKey}", StringComparison.OrdinalIgnoreCase))
            .ToList();

        int categoryIndex = lines.FindIndex(line => line.Trim().Equals(categoryHeader, StringComparison.OrdinalIgnoreCase));
        if (categoryIndex < 0)
        {
            lines.Add(categoryHeader);
            categoryIndex = lines.Count - 1;
        }

        int insertAt = categoryIndex + 1;
        while (insertAt < lines.Count && !lines[insertAt].TrimStart().StartsWith("// ", StringComparison.Ordinal)) insertAt++;
        var tagged = commandLines.Select(line => line.Trim() + " " + keyTag).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
        lines.InsertRange(insertAt, tagged);

        string newBlock = startMarker + Environment.NewLine + string.Join(Environment.NewLine, lines).Trim() + Environment.NewLine + endMarker;
        if (match.Success)
        {
            Cs2AutoexecText = Regex.Replace(current, pattern, newBlock, RegexOptions.Singleline | RegexOptions.IgnoreCase);
        }
        else
        {
            string separator = string.IsNullOrWhiteSpace(current) ? string.Empty : Environment.NewLine + Environment.NewLine;
            Cs2AutoexecText = current.TrimEnd() + separator + newBlock + Environment.NewLine;
        }
        Cs2AutoexecStatusMessage = _localization.T("CS2.Autoexec.SnippetAdded");
    }

    private static string SanitizeValue(string? value, string fallback)
    {
        value = (value ?? string.Empty).Trim().Replace("\"", "");
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static string SanitizeKey(string? key)
    {
        return (key ?? string.Empty).Trim().Replace("\"", "");
    }

    private void LoadCs2CrosshairSettings()
    {
        if (!EnsureCs2CanRead("load crosshair")) return;
        if (!EnsureCs2Analysis() || _cs2Analysis == null) return;
        var values = _cs2Service.LoadUserConvars(_cs2Analysis);
        Cs2CrosshairStyle = ReadString(values, "cl_crosshairstyle", Cs2CrosshairStyle);
        Cs2CrosshairSize = ReadDouble(values, "cl_crosshairsize", Cs2CrosshairSize);
        Cs2CrosshairThickness = ReadDouble(values, "cl_crosshairthickness", Cs2CrosshairThickness);
        Cs2CrosshairGap = ReadDouble(values, "cl_crosshairgap", Cs2CrosshairGap);
        Cs2CrosshairAlpha = ReadDouble(values, "cl_crosshairalpha", Cs2CrosshairAlpha);
        Cs2CrosshairRed = ReadDouble(values, "cl_crosshaircolor_r", Cs2CrosshairRed);
        Cs2CrosshairGreen = ReadDouble(values, "cl_crosshaircolor_g", Cs2CrosshairGreen);
        Cs2CrosshairBlue = ReadDouble(values, "cl_crosshaircolor_b", Cs2CrosshairBlue);
        Cs2CrosshairColor = ReadString(values, "cl_crosshaircolor", Cs2CrosshairColor);
        Cs2CrosshairDot = ReadBool(values, "cl_crosshairdot", Cs2CrosshairDot);
        Cs2CrosshairOutline = ReadBool(values, "cl_crosshair_drawoutline", Cs2CrosshairOutline);
        Cs2CrosshairOutlineThickness = ReadDouble(values, "cl_crosshair_outlinethickness", Cs2CrosshairOutlineThickness);
        Cs2CrosshairTStyle = ReadBool(values, "cl_crosshair_t", Cs2CrosshairTStyle);
        Cs2CrosshairUseAlpha = ReadBool(values, "cl_crosshairusealpha", Cs2CrosshairUseAlpha);
        Cs2CrosshairFollowRecoil = ReadBool(values, "cl_crosshair_recoil", Cs2CrosshairFollowRecoil);
        Cs2CrosshairSniperWidth = ReadDouble(values, "cl_crosshair_sniper_width", Cs2CrosshairSniperWidth);
        Cs2CrosshairGapUseWeaponValue = ReadBool(values, "cl_crosshairgap_useweaponvalue", Cs2CrosshairGapUseWeaponValue);
        Cs2AutoexecStatusMessage = IsPolish ? "Wczytano ustawienia celownika." : "Crosshair settings loaded.";
    }

    private void SaveCs2CrosshairSettings()
    {
        if (!EnsureCs2CanEdit("save crosshair to autoexec.cfg")) return;
        if (!EnsureCs2Analysis() || _cs2Analysis == null) return;

        // Odczyt celownika zostaje z aktualnego configu CS2, ale zapis robimy przez autoexec.cfg.
        // Poprzednie podejście z zapisem do cs2_user_convars_*_slot*.vcfg potrafiło trafiać w nieaktywny slot
        // albo zostać nadpisane przez CS2/Steam. Autoexec jest prostszy: CS2 wykona te komendy przy starcie gry.
        var loadedAutoexec = _cs2Service.LoadAutoexec(_cs2Analysis, createIfMissing: true);
        if (!loadedAutoexec.Success)
        {
            Cs2AutoexecStatusMessage = loadedAutoexec.Message;
            _runtime.AddActivity(loadedAutoexec.Message, "Warn");
            return;
        }

        Cs2AutoexecText = loadedAutoexec.Content;
        UpsertFrameHubCommand("crosshair", "// Crosshair", BuildCrosshairAutoexecLines());

        var result = _cs2Service.SaveAutoexec(_cs2Analysis, Cs2AutoexecText);
        string message = result.Success
            ? (IsPolish
                ? $"Zapisano celownik do autoexec.cfg. Plik: {_cs2Service.GetAutoexecPath(_cs2Analysis)}"
                : $"Crosshair commands saved to autoexec.cfg. File: {_cs2Service.GetAutoexecPath(_cs2Analysis)}")
            : result.Message;

        Cs2AutoexecStatusMessage = message;
        _runtime.AddActivity(message, result.Success ? "Info" : "Warn");
        OnPropertyChanged(nameof(Cs2AutoexecPath));
    }

    private IEnumerable<string> BuildCrosshairAutoexecLines()
    {
        string colorMode = NormalizeCrosshairColorMode(Cs2CrosshairColor);
        string style = SanitizeValue(Cs2CrosshairStyle, "4");

        return new[]
        {
            "// Crosshair generated by FrameHub",
            "crosshair \"1\"",
            $"cl_crosshairstyle \"{style}\"",
            $"cl_crosshaircolor \"{colorMode}\"",
            $"cl_crosshaircolor_r \"{FormatNumber(Clamp(Cs2CrosshairRed, 0, 255))}\"",
            $"cl_crosshaircolor_g \"{FormatNumber(Clamp(Cs2CrosshairGreen, 0, 255))}\"",
            $"cl_crosshaircolor_b \"{FormatNumber(Clamp(Cs2CrosshairBlue, 0, 255))}\"",
            $"cl_crosshairsize \"{FormatNumber(Clamp(Cs2CrosshairSize, -20, 20))}\"",
            $"cl_crosshairthickness \"{FormatNumber(Clamp(Cs2CrosshairThickness, -2, 2))}\"",
            $"cl_crosshairgap \"{FormatNumber(Clamp(Cs2CrosshairGap, -10, 10))}\"",
            $"cl_crosshairdot \"{Bool01(Cs2CrosshairDot)}\"",
            $"cl_crosshair_drawoutline \"{Bool01(Cs2CrosshairOutline)}\"",
            $"cl_crosshair_outlinethickness \"{FormatNumber(Clamp(Cs2CrosshairOutlineThickness, 0.1, 3))}\"",
            $"cl_crosshairalpha \"{FormatNumber(Clamp(Cs2CrosshairAlpha, 10, 255))}\"",
            $"cl_crosshairusealpha \"{Bool01(Cs2CrosshairUseAlpha)}\"",
            $"cl_crosshairgap_useweaponvalue \"{Bool01(Cs2CrosshairGapUseWeaponValue)}\"",
            $"cl_crosshair_sniper_width \"{FormatNumber(Clamp(Cs2CrosshairSniperWidth, 0, 20))}\"",
            $"cl_crosshair_t \"{Bool01(Cs2CrosshairTStyle)}\"",
            $"cl_crosshair_recoil \"{Bool01(Cs2CrosshairFollowRecoil)}\""
        };
    }

    private static string NormalizeCrosshairColorMode(string? value)
    {
        value = SanitizeValue(value, "5");
        return value is "0" or "1" or "2" or "3" or "4" or "5" ? value : "5";
    }

    private static string ReadString(IReadOnlyDictionary<string, string> values, string key, string fallback) => values.TryGetValue(key, out string? value) ? value : fallback;
    private static double ReadDouble(IReadOnlyDictionary<string, string> values, string key, double fallback) => values.TryGetValue(key, out string? value) && double.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsed) ? parsed : fallback;
    private static bool ReadBool(IReadOnlyDictionary<string, string> values, string key, bool fallback) => values.TryGetValue(key, out string? value) ? value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1" : fallback;
    private static string FormatNumber(double value) => value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
    private static string BoolString(bool value) => value ? "true" : "false";
    private static string Bool01(bool value) => value ? "1" : "0";
    private static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;

    private void NotifyCrosshairPreviewChanged()
    {
        OnPropertyChanged(nameof(Cs2CrosshairPreviewBrush));
        OnPropertyChanged(nameof(CrosshairPreviewArmLength));
        OnPropertyChanged(nameof(CrosshairPreviewThickness));
        OnPropertyChanged(nameof(CrosshairOutlinePreviewThickness));
        OnPropertyChanged(nameof(CrosshairPreviewGap));
        OnPropertyChanged(nameof(CrosshairLeftX1));
        OnPropertyChanged(nameof(CrosshairLeftX2));
        OnPropertyChanged(nameof(CrosshairRightX1));
        OnPropertyChanged(nameof(CrosshairRightX2));
        OnPropertyChanged(nameof(CrosshairTopY1));
        OnPropertyChanged(nameof(CrosshairTopY2));
        OnPropertyChanged(nameof(CrosshairBottomY1));
        OnPropertyChanged(nameof(CrosshairBottomY2));
        OnPropertyChanged(nameof(CrosshairDotSize));
        OnPropertyChanged(nameof(CrosshairDotLeft));
        OnPropertyChanged(nameof(CrosshairDotTop));
        OnPropertyChanged(nameof(CrosshairRecoilY));
        OnPropertyChanged(nameof(CrosshairTopVisibility));
        OnPropertyChanged(nameof(CrosshairOutlineVisibility));
        OnPropertyChanged(nameof(CrosshairTopOutlineVisibility));
        OnPropertyChanged(nameof(CrosshairRecoilVisibility));
    }

    private void BackupCs2()
    {
        if (!EnsureCs2CanEdit("backup CS2 config")) return;
        if (_cs2Analysis == null) return;
        var result = _cs2Service.CreateBackup(_cs2Analysis);
        Cs2StatusMessage = result.Message;
        _runtime.AddActivity(result.Message, result.Success ? "Info" : "Warn");
        if (result.Success)
        {
            AnalyzeCs2IfSelected();
        }
    }

    private void ApplyCs2Preset()
    {
        if (!EnsureCs2CanEdit("apply CS2 graphics preset")) return;
        if (_cs2Analysis == null || SelectedCs2Preset == null) return;

        bool wasCustomPreset = SelectedCs2Preset.Id.Equals("cs2_custom", StringComparison.OrdinalIgnoreCase);
        var customTargets = Cs2SettingChanges.ToDictionary(
            vm => vm.Change.Key,
            vm => string.IsNullOrWhiteSpace(vm.Change.TargetValue) ? vm.Change.RecommendedValue : vm.Change.TargetValue,
            StringComparer.OrdinalIgnoreCase);

        foreach (var vm in Cs2SettingChanges) vm.Change.IsSelected = vm.IsSelected;
        var result = _cs2Service.ApplyPreset(_cs2Analysis, SelectedCs2Preset);
        Cs2StatusMessage = result.Message;
        _runtime.AddActivity(result.Message, result.Success ? "Info" : "Warn");

        if (result.Success && wasCustomPreset)
        {
            AnalyzeCs2IfSelected(selectCustomPresetAfterScan: true, customTargets: customTargets);
        }
        else
        {
            AnalyzeCs2IfSelected();
        }
    }

    private void RestoreLatestCs2Backup()
    {
        if (!EnsureCs2CanEdit("restore CS2 backup")) return;
        if (_cs2Analysis == null) AnalyzeCs2IfSelected();
        if (_cs2Analysis == null) return;
        var result = _cs2Service.RestoreLatestBackup(_cs2Analysis);
        Cs2StatusMessage = result.Message;
        _runtime.AddActivity(result.Message, result.Success ? "Info" : "Warn");
    }
}
