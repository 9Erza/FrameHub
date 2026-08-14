using FrameHub.App.Helpers;
using FrameHub.App.Services;
using FrameHub.Core.Logging;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Models.SessionOptimization;
using FrameHub.Core.Services.Library;
using FrameHub.Core.Services.SessionOptimization;
using System.Collections.ObjectModel;
using System.Threading;
using System.Windows.Input;
using System.Windows.Threading;

namespace FrameHub.App.ViewModels;

public sealed class SessionOptimizationViewModel : ViewModelBase, IDisposable
{
    private const string ExplorerRuleId = "explorer";

    private readonly LocalizationService _localization;
    private readonly AppRuntimeService _runtime;
    private readonly SessionOptimizationCoordinator _coordinator;
    private readonly LibraryService _libraryService = new();
    private readonly SessionOptimizationSettingsService _settingsService = new();
    private readonly ProcessSuspendService _suspendService = new();
    private readonly ILogger _logger = LoggerService.Instance;
    private readonly DispatcherTimer _autoTimer;

    private SessionOptimizationSettings _settings;
    private ActiveSessionState? _activeSession;
    private SessionGameOptionViewModel? _selectedGame;
    private string _statusText = string.Empty;
    private string _statusLevel = "Info";
    private bool _isBusy;
    private bool _autoDetectionBusy;
    private CancellationTokenSource? _processRefreshCancellation;
    private readonly SemaphoreSlim _processScanGate = new(1, 1);
    private bool _processRefreshQueued;
    private bool _queuedForceScan;
    private SessionProcessSnapshot? _cachedProcessSnapshot;
    private DateTime _cachedProcessSnapshotUtc;
    private bool _isLoadingGameSettings;
    private bool _disposed;

    public ObservableCollection<SessionRuleViewModel> Rules { get; } = new();
    public ObservableCollection<SessionGameOptionViewModel> Games { get; } = new();
    public ObservableCollection<RunningProcessRuleViewModel> RunningProcesses { get; } = new();
    public ObservableCollection<SuspendCandidateViewModel> Candidates { get; } = new();
    public ObservableCollection<SuspendedProcessViewModel> SuspendedProcesses { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand RefreshRunningProcessesCommand { get; }
    public ICommand StartManualSessionCommand { get; }
    public ICommand StopSessionCommand { get; }
    public ICommand ReloadLibraryCommand { get; }
    public ICommand TestDetectionCommand { get; }

    public string Title => IsPolish ? "Optymalizacja sesji" : "Session Optimization";
    public string Subtitle => IsPolish
        ? "Zwolnij zasoby podczas grania, tymczasowo wstrzymując wybrane aplikacje."
        : "Free resources while gaming by temporarily suspending selected applications.";

    public string AutoModeTitle => IsPolish ? "Automatyzacja" : "Automation";
    public string ManualModeTitle => IsPolish ? "Ręczne włączenie sesji optymalizacji" : "Manual session start";
    public string AutoModeDescription => IsPolish
        ? "Automatycznie rozpocznij optymalizację po uruchomieniu wybranej gry."
        : "Automatically begin optimization when a configured game starts.";
    public string WhatGetsPausedTitle => IsPolish ? "Co wstrzymywać" : "What gets paused";
    public string WhatGetsPausedDescription => IsPolish
        ? "Wybierz aplikacje, które FrameHub może tymczasowo wstrzymać podczas sesji."
        : "Choose which applications FrameHub may temporarily suspend during a session.";
    public string ManualModeDescription => IsPolish
        ? "Wybierz grę i uruchom sesję teraz."
        : "Choose a game and start a session now.";
    public string AdvancedOptionsTitle => IsPolish ? "Opcje zaawansowane" : "Advanced options";
    public string SessionOptionsTitle => IsPolish ? "Opcje sesji" : "Session options";
    public string BackgroundRulesTitle => IsPolish ? "Podstawowe aplikacje do wstrzymania" : "Base apps to suspend";
    public string RunningProcessesTitle => IsPolish ? "Ręczny wybór procesów" : "Manual process selection";
    public string CandidateTitle => IsPolish ? "Podgląd sesji" : "Session preview";
    public string ActiveSessionTitle => IsPolish ? "Wstrzymane procesy" : "Suspended processes";

    public string AutoModeSwitchLabel => IsPolish ? "Automatyczna optymalizacja sesji" : "Automatic session optimization";
    public string AutoGamesLabel => IsPolish ? "Gry z automatyczną sesją" : "Games with automatic session";
    public string SelectedGameLabel => IsPolish ? "Konfiguracja gry" : "Game configuration";
    public string ManualGameLabel => IsPolish ? "Gra" : "Game";
    public string HideTaskbarLabel => IsPolish ? "Ukryj pasek zadań podczas sesji" : "Hide taskbar during session";
    public string HideTaskbarHint => IsPolish ? "Pasek zadań zostanie przywrócony po zakończeniu sesji." : "The taskbar is restored when the session ends.";
    public string WindowsInterfaceLabel => IsPolish ? "Interfejs Windows" : "Windows interface";
    public string WindowsInterfaceHint => IsPolish
        ? "Wstrzymuje explorer.exe. Może utrudnić Alt+Tab oraz korzystanie z paska zadań i menu Start do czasu przywrócenia sesji."
        : "Suspends explorer.exe. Alt+Tab, taskbar and Start menu behavior may be affected until the session is restored.";
    public string ShowManualProcessesLabel => IsPolish ? "Procesy ręczne" : "Manual processes";
    public string ManualProcessesRecommendationTitle => IsPolish ? "Procesy ręczne" : "Manual processes";
    public string ManualProcessesRecommendationText => IsPolish
        ? "Włącz tę sekcję, aby dodać własne procesy dla aktualnie wybranej gry, np. SimHub, MOZA Pit House albo Teams."
        : "Enable this section to add custom processes for the selected game, for example SimHub, MOZA Pit House or Teams.";

    public string BackgroundRulesHint => IsPolish
        ? "Ustawienia bazowe są zapisywane osobno dla wybranej gry."
        : "Base app rules are saved separately for the selected game.";
    public string RunningProcessesHint => IsPolish
        ? "Włącz dodatkowe procesy dla aktualnie wybranej gry. Procesy chronione, anty-cheat, gry oraz procesy objęte regułami bazowymi są ukryte."
        : "Enable additional processes for the selected game. Protected, anti-cheat, game and base-rule processes are hidden.";
    public string CandidateHint => IsPolish
        ? "Procesy, które zostaną wstrzymane przy starcie sesji."
        : "Processes that will be suspended when the session starts.";

    public string NoCandidatesText => IsPolish ? "Brak pasujących procesów." : "No matching processes.";
    public string NoSuspendedText => IsPolish ? "Brak procesów ruszonych przez sesję." : "No processes touched by this session.";
    public string NoRunningProcessesText => IsPolish ? "Brak procesów do wyboru." : "No processes to select.";

    public string RefreshButtonText => IsPolish ? "Odśwież" : "Refresh";
    public string RefreshProcessListButtonText => IsPolish ? "Odśwież procesy" : "Refresh processes";
    public string StartButtonText => IsPolish ? "Start sesji" : "Start session";
    public string StopButtonText => IsPolish ? "Przywróć sesję" : "Restore session";
    public string ReloadLibraryButtonText => IsPolish ? "Odśwież gry" : "Reload games";
    public string TestDetectionButtonText => IsPolish ? "Sprawdź wykrywanie" : "Test detection";

    public string SuspendColumnHeader => IsPolish ? "Wstrzymaj" : "Suspend";
    public string RuleColumnHeader => IsPolish ? "Reguła" : "Rule";
    public string ProcessColumnHeader => IsPolish ? "Proces" : "Process";
    public string InstanceColumnHeader => IsPolish ? "Inst." : "Inst.";
    public string PathColumnHeader => IsPolish ? "Ścieżka" : "Path";
    public string PidColumnHeader => "PID";
    public string TimeColumnHeader => IsPolish ? "Czas" : "Time";

    public bool IsPolish => _localization.CurrentLanguage.Equals("pl", StringComparison.OrdinalIgnoreCase);

    public bool AutoModeEnabled
    {
        get => _settings.AutoModeEnabled;
        set
        {
            if (_settings.AutoModeEnabled == value) return;
            _settings.AutoModeEnabled = value;
            SaveSettings();
            OnPropertyChanged();
            UpdateAutoTimerState();
            if (!value && _activeSession?.IsActive == true && _activeSession.Trigger.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            {
                StopSession();
            }
            RefreshCandidates();
            SetStatus(value
                ? (IsPolish ? "Automatyczna sesja włączona." : "Automatic session enabled.")
                : (IsPolish ? "Automatyczna sesja wyłączona." : "Automatic session disabled."));
        }
    }

    public bool HideTaskbarDuringSession
    {
        get => _settings.HideTaskbarDuringSession;
        set
        {
            if (_settings.HideTaskbarDuringSession == value) return;
            _settings.HideTaskbarDuringSession = value;
            SaveSettings();
            OnPropertyChanged();
        }
    }

    public bool WindowsInterfaceEnabled
    {
        get
        {
            if (SelectedGame == null)
            {
                return false;
            }

            var gameSettings = GetSelectedGameSettings();
            return gameSettings.RuleEnabledStates.TryGetValue(ExplorerRuleId, out bool enabled) && enabled;
        }
        set
        {
            if (SelectedGame == null)
            {
                return;
            }

            var gameSettings = GetSelectedGameSettings();
            bool current = gameSettings.RuleEnabledStates.TryGetValue(ExplorerRuleId, out bool enabled) && enabled;
            if (current == value)
            {
                return;
            }

            gameSettings.RuleEnabledStates[ExplorerRuleId] = value;
            SaveSettings();
            OnPropertyChanged();
            RefreshCandidates();
        }
    }

    public bool ShowManualProcessList
    {
        get => SelectedGame != null && GetSelectedGameSettings().ManualProcessRulesEnabled;
        set
        {
            if (SelectedGame == null) return;
            var gameSettings = GetSelectedGameSettings();
            if (gameSettings.ManualProcessRulesEnabled == value) return;
            gameSettings.ManualProcessRulesEnabled = value;
            _settings.ShowManualProcessList = value; // legacy compatibility only
            SaveSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ManualProcessesPlaceholderVisible));
            if (value)
            {
                RefreshRunningProcesses();
            }
            RefreshCandidates();
        }
    }

    public SessionGameOptionViewModel? SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (SetProperty(ref _selectedGame, value))
            {
                _settings.SelectedGameId = value?.Id;
                SaveSettings();
                LoadSelectedGameConfiguration();
                RefreshRunningProcesses();
                RefreshCandidates();
            }
        }
    }

    // Kept for old bindings / compatibility.
    public SessionGameOptionViewModel? SelectedManualGame
    {
        get => SelectedGame;
        set => SelectedGame = value;
    }

    public bool IsSessionActive => _activeSession?.IsActive == true;
    public bool IsIdle => !IsSessionActive;
    public bool IsRecoveryPending => _activeSession?.IsRecoveryPending == true;
    public string SessionStateDisplay => IsRecoveryPending
        ? _localization.T("Session.State.Recovery")
        : IsSessionActive
            ? (_activeSession?.Trigger.Equals("Auto", StringComparison.OrdinalIgnoreCase) == true
                ? _localization.T("Session.State.Automatic")
                : _localization.T("Session.State.Active"))
            : _localization.T("Session.State.Idle");
    public bool HasCandidates => Candidates.Count > 0;
    public bool HasSuspendedProcesses => SuspendedProcesses.Count > 0;
    public bool HasRunningProcesses => RunningProcesses.Count > 0;
    public bool ManualProcessesPlaceholderVisible => !ShowManualProcessList;
    public bool HasGames => Games.Count > 0;
    public bool NoCandidatesVisible => !HasCandidates;
    public bool NoSuspendedProcessesVisible => !HasSuspendedProcesses;
    public bool NoRunningProcessesVisible => !HasRunningProcesses;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string StatusLevel
    {
        get => _statusLevel;
        private set => SetProperty(ref _statusLevel, value);
    }

    public string ActiveSessionInfo
    {
        get
        {
            if (_activeSession?.IsActive != true)
            {
                return IsPolish ? "Sesja nieaktywna." : "Session inactive.";
            }

            string source = _activeSession.Trigger.Equals("Auto", StringComparison.OrdinalIgnoreCase)
                ? (IsPolish ? "auto" : "auto")
                : (IsPolish ? "ręcznie" : "manual");
            string game = string.IsNullOrWhiteSpace(_activeSession.GameName)
                ? (IsPolish ? "brak gry" : "no game")
                : _activeSession.GameName!;
            string taskbar = _activeSession.TaskbarHidden
                ? (IsPolish ? " Pasek zadań ukryty." : " Taskbar hidden.")
                : string.Empty;

            return IsPolish
                ? $"Aktywna ({source}) dla: {game}. Wstrzymane procesy: {_activeSession.SuspendedProcesses.Count}.{taskbar}"
                : $"Active ({source}) for: {game}. Suspended processes: {_activeSession.SuspendedProcesses.Count}.{taskbar}";
        }
    }

    public SessionOptimizationViewModel(LocalizationService localization, AppRuntimeService runtime, SessionOptimizationCoordinator? coordinator = null)
    {
        _localization = localization;
        _runtime = runtime;
        _coordinator = coordinator ?? runtime.SessionOptimizationCoordinator ?? new SessionOptimizationCoordinator();
        _settings = _settingsService.Load();
        MigrateSessionSettingsForSafeSuspend();
        _activeSession = _coordinator.ActiveSession;
        _coordinator.SessionStateChanged += OnCoordinatorSessionStateChanged;

        RefreshCommand = new RelayCommand(_ => RequestProcessRefresh(forceScan: true), _ => !_isBusy && !IsSessionActive);
        RefreshRunningProcessesCommand = new RelayCommand(_ => RequestProcessRefresh(forceScan: true), _ => !_isBusy && !IsSessionActive);
        StartManualSessionCommand = new RelayCommand(async _ => await StartManualSessionAsync(), _ => !_isBusy && !IsSessionActive && SelectedGame != null);
        StopSessionCommand = new RelayCommand(_ => StopSession(), _ => !_isBusy && IsSessionActive);
        ReloadLibraryCommand = new RelayCommand(_ => ReloadGames());
        TestDetectionCommand = new RelayCommand(async _ => await TestDetectionAsync(), _ => !_isBusy && SelectedGame != null);

        ReloadGames();
        LoadSelectedGameConfiguration();
        RebuildSuspendedList();
        RequestProcessRefresh(forceScan: true);

        _autoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _autoTimer.Tick += async (_, _) => await RunAutoDetectionTickAsync();
        UpdateAutoTimerState();

        SetStatus(IsSessionActive
            ? (IsPolish ? "Wykryto aktywną sesję. Użyj przywracania, aby wznowić procesy." : "Active session detected. Use restore to resume processes.")
            : (IsPolish ? "Gotowe." : "Ready."));
    }

    private void OnCoordinatorSessionStateChanged(object? sender, ActiveSessionState? session)
    {
        _activeSession = session;
        RebuildSuspendedList();
        RefreshRunningProcesses();
        OnStateChanged();
    }

    private void MigrateSessionSettingsForSafeSuspend()
    {
        bool changed = false;

        if (_settings.SchemaVersion < 4)
        {
            // Emergency safety migration for v0.4.0 release:
            // old builds could keep aggressive session settings in AppData and start too many suspends.
            _settings.AutoModeEnabled = false;
            _settings.HideTaskbarDuringSession = false;
            _settings.ShowManualProcessList = false;
            _settings.RuleEnabledStates.Clear();

            foreach (var gameSettings in _settings.GameSettings.Values)
            {
                gameSettings.AutoEnabled = false;
                gameSettings.ManualProcessRulesEnabled = false;
                gameSettings.RulesConfigured = true;

                gameSettings.RuleEnabledStates[ExplorerRuleId] = false;
                gameSettings.RuleEnabledStates["browsers"] = false;
                gameSettings.RuleEnabledStates["spotify"] = true;
                gameSettings.RuleEnabledStates["discord"] = false;
                gameSettings.RuleEnabledStates["teamspeak"] = false;
            }

            _settings.AutoEnabledGameIds.Clear();
            _settings.SchemaVersion = 4;
            changed = true;
        }

        RemoveSteamSessionSettings();

        if (changed)
        {
            SaveSettings();
            _logger.Warn("Session optimization settings migrated to safe defaults. Automatic session optimization was disabled and aggressive old rules were cleared.");
        }
        else
        {
            SaveSettings();
        }
    }

    private void RemoveSteamSessionSettings()
    {
        _settings.RuleEnabledStates.Remove("steamwebhelper");
        foreach (var gameSettings in _settings.GameSettings.Values)
        {
            gameSettings.RuleEnabledStates.Remove("steamwebhelper");

            foreach (var key in gameSettings.CustomProcessEnabledStates.Keys
                .Where(IsSteamRelatedSettingKey)
                .ToList())
            {
                gameSettings.CustomProcessEnabledStates.Remove(key);
            }
        }
    }

    private static bool IsSteamRelatedSettingKey(string key)
    {
        string normalized = ProcessSuspendService.NormalizeProcessName(key);
        return normalized.Contains("steam", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("gameoverlayui", StringComparison.OrdinalIgnoreCase);
    }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(AutoModeTitle));
        OnPropertyChanged(nameof(ManualModeTitle));
        OnPropertyChanged(nameof(AutoModeDescription));
        OnPropertyChanged(nameof(WhatGetsPausedTitle));
        OnPropertyChanged(nameof(WhatGetsPausedDescription));
        OnPropertyChanged(nameof(ManualModeDescription));
        OnPropertyChanged(nameof(AdvancedOptionsTitle));
        OnPropertyChanged(nameof(SessionOptionsTitle));
        OnPropertyChanged(nameof(BackgroundRulesTitle));
        OnPropertyChanged(nameof(RunningProcessesTitle));
        OnPropertyChanged(nameof(CandidateTitle));
        OnPropertyChanged(nameof(ActiveSessionTitle));
        OnPropertyChanged(nameof(AutoModeSwitchLabel));
        OnPropertyChanged(nameof(AutoGamesLabel));
        OnPropertyChanged(nameof(SelectedGameLabel));
        OnPropertyChanged(nameof(ManualGameLabel));
        OnPropertyChanged(nameof(HideTaskbarLabel));
        OnPropertyChanged(nameof(HideTaskbarHint));
        OnPropertyChanged(nameof(WindowsInterfaceLabel));
        OnPropertyChanged(nameof(WindowsInterfaceHint));
        OnPropertyChanged(nameof(ShowManualProcessesLabel));
        OnPropertyChanged(nameof(ManualProcessesRecommendationTitle));
        OnPropertyChanged(nameof(ManualProcessesRecommendationText));
        OnPropertyChanged(nameof(BackgroundRulesHint));
        OnPropertyChanged(nameof(RunningProcessesHint));
        OnPropertyChanged(nameof(CandidateHint));
        OnPropertyChanged(nameof(NoCandidatesText));
        OnPropertyChanged(nameof(NoSuspendedText));
        OnPropertyChanged(nameof(NoRunningProcessesText));
        OnPropertyChanged(nameof(RefreshButtonText));
        OnPropertyChanged(nameof(RefreshProcessListButtonText));
        OnPropertyChanged(nameof(StartButtonText));
        OnPropertyChanged(nameof(StopButtonText));
        OnPropertyChanged(nameof(ReloadLibraryButtonText));
        OnPropertyChanged(nameof(TestDetectionButtonText));
        OnPropertyChanged(nameof(SuspendColumnHeader));
        OnPropertyChanged(nameof(RuleColumnHeader));
        OnPropertyChanged(nameof(ProcessColumnHeader));
        OnPropertyChanged(nameof(InstanceColumnHeader));
        OnPropertyChanged(nameof(PathColumnHeader));
        OnPropertyChanged(nameof(PidColumnHeader));
        OnPropertyChanged(nameof(TimeColumnHeader));
        OnPropertyChanged(nameof(ActiveSessionInfo));
        OnPropertyChanged(nameof(IsRecoveryPending));
        OnPropertyChanged(nameof(SessionStateDisplay));

        foreach (var rule in Rules)
        {
            rule.RefreshTexts();
        }

        RefreshCandidates();
        RebuildSuspendedList();
    }

    private void ReloadGames()
    {
        string? previousSelectedId = SelectedGame?.Id ?? _settings.SelectedGameId;
        Games.Clear();

        var libraryGames = _libraryService.LoadItems()
            .Where(x => x.Type == LibraryItemType.Game)
            .OrderBy(x => x.DisplayName)
            .ToList();

        foreach (var game in libraryGames)
        {
            var gameSettings = GetGameSettings(game.Id);
            bool autoEnabled = gameSettings.AutoEnabled || _settings.AutoEnabledGameIds.Contains(game.Id, StringComparer.OrdinalIgnoreCase);
            gameSettings.AutoEnabled = autoEnabled;
            Games.Add(new SessionGameOptionViewModel(game, autoEnabled, OnGameChanged));
        }

        SelectedGame = Games.FirstOrDefault(x => !string.IsNullOrWhiteSpace(previousSelectedId) && x.Id.Equals(previousSelectedId, StringComparison.OrdinalIgnoreCase))
            ?? Games.FirstOrDefault();

        SyncLegacyAutoGameIds();
        OnPropertyChanged(nameof(HasGames));
        SetStatus(Games.Count == 0
            ? (IsPolish ? "Brak gier w Bibliotece." : "No games in Library.")
            : (IsPolish ? $"Załadowano gry: {Games.Count}." : $"Loaded games: {Games.Count}."));
    }

    private void LoadSelectedGameConfiguration()
    {
        _isLoadingGameSettings = true;
        try
        {
            Rules.Clear();
            var gameSettings = GetSelectedGameSettings();
            foreach (var rule in BackgroundProcessRuleFactory.CreateDefaultRules(_settings, gameSettings)
                .Where(rule => !rule.Id.Equals(ExplorerRuleId, StringComparison.OrdinalIgnoreCase)))
            {
                Rules.Add(new SessionRuleViewModel(rule, _localization, OnRuleChanged));
            }
        }
        finally
        {
            _isLoadingGameSettings = false;
        }

        OnPropertyChanged(nameof(SelectedManualGame));
        OnPropertyChanged(nameof(WindowsInterfaceEnabled));
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnRuleChanged()
    {
        if (_isLoadingGameSettings || SelectedGame == null)
        {
            return;
        }

        var gameSettings = GetSelectedGameSettings();
        gameSettings.RulesConfigured = true;
        bool explorerEnabled = gameSettings.RuleEnabledStates.TryGetValue(ExplorerRuleId, out bool value) && value;
        gameSettings.RuleEnabledStates.Clear();
        gameSettings.RuleEnabledStates[ExplorerRuleId] = explorerEnabled;
        foreach (var rule in Rules)
        {
            gameSettings.RuleEnabledStates[rule.Id] = rule.IsEnabled;
        }

        SaveSettings();
        RefreshRunningProcesses();
        RefreshCandidates();
    }

    private void OnGameChanged(SessionGameOptionViewModel changedGame)
    {
        if (SelectedGame == null || !SelectedGame.Id.Equals(changedGame.Id, StringComparison.OrdinalIgnoreCase))
        {
            SelectedGame = changedGame;
        }

        foreach (var game in Games)
        {
            GetGameSettings(game.Id).AutoEnabled = game.AutoEnabled;
        }

        SyncLegacyAutoGameIds();
        SaveSettings();
        RefreshCandidates();

        if (_activeSession?.IsActive == true
            && _activeSession.Trigger.Equals("Auto", StringComparison.OrdinalIgnoreCase)
            && _activeSession.GameId?.Equals(changedGame.Id, StringComparison.OrdinalIgnoreCase) == true
            && !changedGame.AutoEnabled)
        {
            StopSession();
        }
    }

    private void OnRunningProcessChanged()
    {
        if (SelectedGame == null)
        {
            return;
        }

        var gameSettings = GetSelectedGameSettings();
        foreach (var process in RunningProcesses)
        {
            if (process.IsEnabled)
            {
                gameSettings.CustomProcessEnabledStates[process.NormalizedProcessName] = true;
            }
            else
            {
                gameSettings.CustomProcessEnabledStates.Remove(process.NormalizedProcessName);
            }
        }

        SaveSettings();
        RefreshCandidates();
    }

    private void RefreshRunningProcesses()
    {
        RequestProcessRefresh();
    }

    private void RefreshCandidates()
    {
        RequestProcessRefresh();
    }

    private void RequestProcessRefresh(bool forceScan = false)
    {
        if (IsSessionActive)
        {
            return;
        }

        _queuedForceScan |= forceScan;
        if (_processRefreshQueued)
        {
            return;
        }

        _processRefreshQueued = true;
        _ = Dispatcher.CurrentDispatcher.InvokeAsync(() =>
        {
            _processRefreshQueued = false;
            bool queuedForceScan = _queuedForceScan;
            _queuedForceScan = false;
            StartProcessRefresh(queuedForceScan);
        }, DispatcherPriority.Background);
    }

    private void StartProcessRefresh(bool forceScan)
    {

        _processRefreshCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _processRefreshCancellation = cancellation;
        _ = RefreshProcessViewsAsync(forceScan, cancellation);
    }

    private async Task RefreshProcessViewsAsync(bool forceScan, CancellationTokenSource cancellation)
    {
        try
        {
            SessionProcessSnapshot snapshot = _cachedProcessSnapshot != null
                && !forceScan
                && DateTime.UtcNow - _cachedProcessSnapshotUtc < TimeSpan.FromSeconds(1)
                ? _cachedProcessSnapshot
                : await CaptureSessionProcessSnapshotAsync(cancellation.Token);

            cancellation.Token.ThrowIfCancellationRequested();
            _cachedProcessSnapshot = snapshot;
            _cachedProcessSnapshotUtc = snapshot.CapturedAtUtc;

            var protectedNames = GetProtectedProcessNamesForGame(SelectedGame).ToArray();
            var game = SelectedGame;
            var groups = await Task.Run(() => _suspendService.GetRunningProcessGroups(snapshot, protectedNames), cancellation.Token);
            var candidates = BuildCandidatesForGame(game, snapshot);
            if (cancellation.IsCancellationRequested || !ReferenceEquals(_processRefreshCancellation, cancellation)) return;

            ApplyRunningProcesses(groups);
            ReplaceCandidatePreview(candidates);
        }
        catch (OperationCanceledException)
        {
            // A newer UI change superseded this refresh.
        }
    }

    private void ApplyRunningProcesses(IReadOnlyList<RunningProcessGroup> groups)
    {
        var protectedNames = GetProtectedProcessNamesForGame(SelectedGame);
        var gameSettings = GetSelectedGameSettings();
        var selectedCustomStates = gameSettings.CustomProcessEnabledStates;
        foreach (var key in selectedCustomStates.Keys.Where(IsSteamRelatedSettingKey).ToList())
        {
            selectedCustomStates.Remove(key);
        }

        var ruleCoveredProcessNames = GetRuleCoveredProcessNames(gameSettings);
        groups = groups.Where(group => !ruleCoveredProcessNames.Contains(group.NormalizedProcessName)).ToList();

        bool removedStaleCustomState = false;
        foreach (string staleCustomProcess in selectedCustomStates.Keys
            .Where(ruleCoveredProcessNames.Contains)
            .ToList())
        {
            removedStaleCustomState |= selectedCustomStates.Remove(staleCustomProcess);
        }

        RunningProcesses.Clear();
        foreach (var group in groups)
        {
            bool enabled = selectedCustomStates.TryGetValue(group.NormalizedProcessName, out bool value) && value;
            RunningProcesses.Add(new RunningProcessRuleViewModel(group, enabled, OnRunningProcessChanged));
        }

        if (removedStaleCustomState)
        {
            SaveSettings();
        }

        OnPropertyChanged(nameof(HasRunningProcesses));
        OnPropertyChanged(nameof(NoRunningProcessesVisible));
        CommandManager.InvalidateRequerySuggested();
    }

    private IReadOnlyList<SuspendCandidate> BuildCandidatesForGame(SessionGameOptionViewModel? game)
    {
        var gameSettings = GetGameSettings(game?.Id);
        var allRules = BuildRulesForGame(gameSettings);
        var enabledRules = allRules.Where(x => x.IsEnabled);
        var ruleCoveredProcessNames = GetRuleCoveredProcessNames(allRules);
        IEnumerable<string> customProcesses = Enumerable.Empty<string>();
        if (gameSettings.ManualProcessRulesEnabled)
        {
            customProcesses = gameSettings.CustomProcessEnabledStates
                .Where(x => x.Value)
                .Select(x => x.Key)
                .Where(x => !ruleCoveredProcessNames.Contains(ProcessSuspendService.NormalizeProcessName(x)));
        }
        var protectedNames = GetProtectedProcessNamesForGame(game);
        return _suspendService.BuildCandidates(enabledRules, customProcesses, protectedNames);
    }

    private IReadOnlyList<SuspendCandidate> BuildCandidatesForGame(SessionGameOptionViewModel? game, SessionProcessSnapshot snapshot)
    {
        var gameSettings = GetGameSettings(game?.Id);
        var allRules = BuildRulesForGame(gameSettings);
        var enabledRules = allRules.Where(x => x.IsEnabled);
        var ruleCoveredProcessNames = GetRuleCoveredProcessNames(allRules);
        IEnumerable<string> customProcesses = gameSettings.ManualProcessRulesEnabled
            ? gameSettings.CustomProcessEnabledStates.Where(x => x.Value).Select(x => x.Key)
                .Where(x => !ruleCoveredProcessNames.Contains(ProcessSuspendService.NormalizeProcessName(x)))
            : Enumerable.Empty<string>();
        return _suspendService.BuildCandidates(snapshot, enabledRules, customProcesses, GetProtectedProcessNamesForGame(game));
    }

    private List<BackgroundProcessRule> BuildRulesForGame(SessionGameSuspendSettings gameSettings)
    {
        return BackgroundProcessRuleFactory.CreateDefaultRules(_settings, gameSettings);
    }

    private HashSet<string> GetRuleCoveredProcessNames(SessionGameSuspendSettings gameSettings)
    {
        return GetRuleCoveredProcessNames(BuildRulesForGame(gameSettings));
    }

    private static HashSet<string> GetRuleCoveredProcessNames(IEnumerable<BackgroundProcessRule> rules)
    {
        return rules
            .SelectMany(rule => rule.ProcessNames)
            .Select(ProcessSuspendService.NormalizeProcessName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<string> GetProtectedProcessNamesForGame(SessionGameOptionViewModel? game)
    {
        var names = new List<string>();

        if (!string.IsNullOrWhiteSpace(game?.Item.ProcessName))
        {
            names.Add(game.Item.ProcessName!);
        }

        names.AddRange(Games
            .Where(x => !string.IsNullOrWhiteSpace(x.Item.ProcessName))
            .Select(x => x.Item.ProcessName!));

        return names;
    }

    private void ReplaceCandidatePreview(IEnumerable<SuspendCandidate> candidates)
    {
        Candidates.Clear();
        foreach (var candidate in candidates)
        {
            Candidates.Add(new SuspendCandidateViewModel(candidate, _localization));
        }

        OnPropertyChanged(nameof(HasCandidates));
        OnPropertyChanged(nameof(NoCandidatesVisible));
        CommandManager.InvalidateRequerySuggested();
    }

    private void LogSessionCandidateList(string trigger, SessionGameOptionViewModel? game, IReadOnlyList<SuspendCandidate> candidates)
    {
        string gameName = string.IsNullOrWhiteSpace(game?.DisplayName) ? "unknown" : game.DisplayName;
        string summary = candidates.Count == 0
            ? "none"
            : string.Join("; ", candidates
                .OrderBy(x => x.RuleName)
                .ThenBy(x => x.ProcessName)
                .Select(x => $"{x.RuleName}: {x.ProcessName} PID={x.ProcessId}"));

        _logger.Info($"Session candidates ({trigger}) for {gameName}: count={candidates.Count}; {summary}");
    }

    private async Task StartManualSessionAsync()
    {
        await StartSessionAsync("Manual", SelectedGame);
    }

    private async Task StartSessionAsync(string trigger, SessionGameOptionViewModel? game)
    {
        if (_isBusy || IsSessionActive)
        {
            return;
        }

        if (trigger.Equals("Auto", StringComparison.OrdinalIgnoreCase) && (!AutoModeEnabled || game?.AutoEnabled != true))
        {
            return;
        }

        _isBusy = true;
        try
        {
            SessionProcessSnapshot snapshot = _cachedProcessSnapshot != null
                && DateTime.UtcNow - _cachedProcessSnapshotUtc < TimeSpan.FromSeconds(1)
                ? _cachedProcessSnapshot
                : await CaptureSessionProcessSnapshotAsync(CancellationToken.None);
            var candidates = BuildCandidatesForGame(game, snapshot).ToList();
            ReplaceCandidatePreview(candidates);
            LogSessionCandidateList(trigger, game, candidates);

            var result = await _coordinator.StartSessionAsync(trigger, game?.Item);

            if (result.Success)
            {
                string taskbarPart = result.TaskbarHidden
                    ? (IsPolish ? " Pasek zadań ukryty." : " Taskbar hidden.")
                    : string.Empty;
                string message = IsPolish
                    ? $"Sesja uruchomiona. Wstrzymano {result.SuspendedCount} procesów. Błędy: {result.FailedCount}.{taskbarPart}"
                    : $"Session started. Suspended {result.SuspendedCount} processes. Failed: {result.FailedCount}.{taskbarPart}";
                SetStatus(message, result.FailedCount > 0 ? "Warn" : "Info");
                _runtime.AddActivity(message, result.FailedCount > 0 ? "Warn" : "Info");
            }
            else if (result.ErrorCode == "no_candidates")
            {
                SetStatus(IsPolish
                    ? "Brak aktywnych procesów dla tej konfiguracji."
                    : "No active processes for this configuration.", "Warn");
            }
            else
            {
                SetStatus(result.Message, "Warn");
            }
        }
        finally
        {
            _isBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void StopSession()
    {
        _ = StopSessionAsync();
    }

    private async Task StopSessionAsync()
    {
        if (_isBusy || _activeSession?.IsActive != true)
        {
            return;
        }

        _isBusy = true;
        try
        {
            var result = await _coordinator.StopSessionAsync();

            if (result.Success)
            {
                string taskbarPart = result.TaskbarRestored
                    ? (IsPolish ? " Pasek zadań przywrócony." : " Taskbar restored.")
                    : string.Empty;
                string message = IsPolish
                    ? $"Sesja przywrócona. Wznowiono {result.ResumedCount} procesów. Błędy: {result.FailedCount}.{taskbarPart}"
                    : $"Session restored. Resumed {result.ResumedCount} processes. Failed: {result.FailedCount}.{taskbarPart}";
                if (result.RemainingCount > 0)
                {
                    message = IsPolish
                        ? $"Przywracanie niepełne. Pozostało do recovery: {result.RemainingCount}."
                        : $"Restore incomplete. {result.RemainingCount} processes remain for recovery.";
                }

                SetStatus(message, result.FailedCount > 0 ? "Warn" : "Info");
                _runtime.AddActivity(message, result.FailedCount > 0 ? "Warn" : "Info");
            }
            else
            {
                SetStatus(result.Message, "Warn");
            }
        }
        finally
        {
            _isBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task TestDetectionAsync()
    {
        if (SelectedGame == null)
        {
            SetStatus(IsPolish ? "Wybierz grę." : "Select a game.", "Warn");
            return;
        }

        await _processScanGate.WaitAsync();
        IReadOnlySet<string> runningGameIds;
        try
        {
            runningGameIds = await _runtime.ProcessScanner.FindRunningLibraryItemIdsAsync(new[] { SelectedGame.Item });
        }
        finally
        {
            _processScanGate.Release();
        }
        bool detected = runningGameIds.Contains(SelectedGame.Id);
        RefreshCandidates();

        if (detected)
        {
            SetStatus(IsPolish
                ? $"Wykryto proces gry: {SelectedGame.ProcessName}."
                : $"Detected game process: {SelectedGame.ProcessName}.", "Info");
        }
        else
        {
            SetStatus(IsPolish
                ? $"Nie wykryto procesu gry: {SelectedGame.ProcessName}."
                : $"Game process not detected: {SelectedGame.ProcessName}.", "Warn");
        }
    }

    private async Task RunAutoDetectionTickAsync()
    {
        if (_isBusy || _autoDetectionBusy) return;

        _autoDetectionBusy = true;
        try
        {
            if (!AutoModeEnabled)
            {
                if (_activeSession?.IsActive == true
                    && _activeSession.Trigger.Equals("Auto", StringComparison.OrdinalIgnoreCase)
                    && !_activeSession.IsRecoveryPending)
                {
                    StopSession();
                }
                return;
            }

            var autoGames = Games.Where(game => game.AutoEnabled).ToList();
            await _processScanGate.WaitAsync();
            IReadOnlySet<string> runningGameIds;
            try
            {
                runningGameIds = await _runtime.ProcessScanner.FindRunningLibraryItemIdsAsync(autoGames.Select(game => game.Item));
            }
            finally
            {
                _processScanGate.Release();
            }

            if (_activeSession?.IsActive == true)
            {
                if (_activeSession.Trigger.Equals("Auto", StringComparison.OrdinalIgnoreCase)
                    && !_activeSession.IsRecoveryPending
                    && (string.IsNullOrWhiteSpace(_activeSession.GameId) || !runningGameIds.Contains(_activeSession.GameId)))
                {
                    StopSession();
                }
                return;
            }

            var game = autoGames.FirstOrDefault(candidate => runningGameIds.Contains(candidate.Id));
            if (game != null) await StartSessionAsync("Auto", game);
        }
        finally
        {
            _autoDetectionBusy = false;
        }
    }

    private async Task<SessionProcessSnapshot> CaptureSessionProcessSnapshotAsync(CancellationToken cancellationToken)
    {
        await _processScanGate.WaitAsync(cancellationToken);
        try
        {
            return await _suspendService.CaptureProcessSnapshotAsync(cancellationToken);
        }
        finally
        {
            _processScanGate.Release();
        }
    }

    private SessionGameSuspendSettings GetSelectedGameSettings()
    {
        return GetGameSettings(SelectedGame?.Id);
    }

    private SessionGameSuspendSettings GetGameSettings(string? gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return new SessionGameSuspendSettings();
        }

        if (!_settings.GameSettings.TryGetValue(gameId, out var gameSettings) || gameSettings == null)
        {
            gameSettings = new SessionGameSuspendSettings();
            _settings.GameSettings[gameId] = gameSettings;
        }

        return gameSettings;
    }

    private void SyncLegacyAutoGameIds()
    {
        _settings.AutoEnabledGameIds = Games.Where(x => x.AutoEnabled).Select(x => x.Id).ToList();
    }

    private void RebuildSuspendedList()
    {
        SuspendedProcesses.Clear();
        if (_activeSession?.SuspendedProcesses != null)
        {
            foreach (var record in _activeSession.SuspendedProcesses)
            {
                SuspendedProcesses.Add(new SuspendedProcessViewModel(record, _localization));
            }
        }

        OnPropertyChanged(nameof(HasSuspendedProcesses));
        OnPropertyChanged(nameof(NoSuspendedProcessesVisible));
    }

    private void UpdateAutoTimerState()
    {
        if (_autoTimer == null)
        {
            return;
        }

        if (AutoModeEnabled && !_autoTimer.IsEnabled)
        {
            _autoTimer.Start();
        }
        else if (!AutoModeEnabled && _autoTimer.IsEnabled)
        {
            _autoTimer.Stop();
        }
    }

    private void SaveSettings()
    {
        _settingsService.Save(_settings);
    }

    private void SetStatus(string message, string level = "Info")
    {
        StatusText = message;
        StatusLevel = level;
    }

    private void OnStateChanged()
    {
        OnPropertyChanged(nameof(IsSessionActive));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsRecoveryPending));
        OnPropertyChanged(nameof(SessionStateDisplay));
        OnPropertyChanged(nameof(ActiveSessionInfo));
        OnPropertyChanged(nameof(HasCandidates));
        OnPropertyChanged(nameof(NoCandidatesVisible));
        OnPropertyChanged(nameof(HasSuspendedProcesses));
        OnPropertyChanged(nameof(NoSuspendedProcessesVisible));
        OnPropertyChanged(nameof(StopButtonText));
        CommandManager.InvalidateRequerySuggested();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _coordinator.SessionStateChanged -= OnCoordinatorSessionStateChanged;
        _processRefreshCancellation?.Cancel();
        _autoTimer.Stop();
    }
}
