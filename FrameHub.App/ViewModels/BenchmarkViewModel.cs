using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using FrameHub.App.Helpers;
using FrameHub.App.Services;
using FrameHub.App.ViewModels.Benchmark;
using FrameHub.Core.Logging;
using FrameHub.Core.Models;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Benchmarking;
using FrameHub.Core.Services.Library;
using FrameHub.Core.Services.SessionOptimization;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfMessageBoxResult = System.Windows.MessageBoxResult;

namespace FrameHub.App.ViewModels;

public sealed class BenchmarkViewModel : ViewModelBase, IDisposable
{
    public event Action<string>? UserNotificationRequested;
    private readonly LocalizationService _localization;
    private readonly IBenchmarkRuntimeContext _runtime;
    private readonly BenchmarkStorageService _storage;
    private readonly BenchmarkGameDetectionService _detector;
    private readonly Func<IReadOnlyList<LibraryItem>> _libraryProvider;
    private readonly Func<IBenchmarkCaptureBackend> _backendFactory;
    private readonly Func<bool?> _sessionOptimizationProvider;
    private readonly ILogger _logger;
    private readonly Func<(bool Ready, string? Version, string? Error)>? _engineProbe;
    private readonly IBenchmarkCaptureCoordinator _coordinator;
    private readonly DispatcherTimer _detectionTimer;
    private readonly DispatcherTimer _progressTimer;
    private readonly SemaphoreSlim _historyRefreshGate = new(1, 1);
    private readonly Stopwatch _captureClock = new();
    private CancellationTokenSource? _visualResetCts;
    private bool _refreshingGames;
    private bool _disposed;
    private string? _preselectedLibraryItemId;
    private BenchmarkGameOptionViewModel? _selectedGame;
    private BenchmarkHistoryItemViewModel? _selectedHistory;
    private BenchmarkHistoryItemViewModel? _comparisonA;
    private BenchmarkHistoryItemViewModel? _comparisonB;
    private BenchmarkResultViewModel? _currentResult;
    private BenchmarkUiState _state;
    private string _statusMessage = string.Empty;
    private string _technicalError = string.Empty;
    private bool _engineReady;
    private string? _engineVersion;
    private int _selectedDuration = 30;
    private int _customDuration = 30;
    private bool _useCustomDuration;
    private int _countdownSeconds = 3;
    private int _remainingCountdown;
    private double _elapsedSeconds;
    private int _selectedTabIndex;

    public ObservableCollection<BenchmarkGameOptionViewModel> Games { get; } = new();
    public ObservableCollection<BenchmarkHistoryItemViewModel> History { get; } = new();
    public ObservableCollection<BenchmarkComparisonRowViewModel> ComparisonRows { get; } = new();
    public IReadOnlyList<int> DurationOptions { get; } = [30, 60, 120];
    public IReadOnlyList<int> CountdownOptions { get; } = [0, 3, 5];

    public ICommand RefreshGamesCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand RefreshHistoryCommand { get; }
    public ICommand OpenSessionFolderCommand { get; }
    public ICommand DeleteSessionCommand { get; }

    public string Title => _localization.T("Benchmark.Title");
    public string Subtitle => _localization.T("Benchmark.Subtitle");
    public string CaptureTabText => _localization.T("Benchmark.Tab.Capture");
    public string HistoryTabText => _localization.T("Benchmark.Tab.History");
    public string CompareTabText => _localization.T("Benchmark.Tab.Compare");
    public string EngineLabel => _localization.T("Benchmark.Engine.Label");
    public string EngineStatus => _engineReady ? _localization.T("Benchmark.Engine.Ready") : _localization.T("Benchmark.Engine.Unavailable");
    public string EngineVersionText => _engineVersion ?? _localization.T("Benchmark.Unavailable");
    public string EmptyGamesText => _localization.T("Benchmark.Games.Empty");
    public string GameLabel => _localization.T("Benchmark.Game");
    public string RunningText => _localization.T("Benchmark.Running");
    public string NotRunningText => _localization.T("Benchmark.NotRunning");
    public string RefreshText => _localization.T("Benchmark.Refresh");
    public string DurationLabel => _localization.T("Benchmark.Duration");
    public string CustomDurationLabel => _localization.T("Benchmark.Duration.Custom");
    public string CountdownLabel => _localization.T("Benchmark.Countdown");
    public string StartText => _localization.T("Benchmark.Start");
    public string StopText => _localization.T("Benchmark.Stop");
    public string ComparableHint => _localization.T("Benchmark.ComparableHint");
    public string ProgressText => string.Format(_localization.T("Benchmark.Progress"), ElapsedSeconds, RequestedDurationSeconds);
    public string StateText => State == BenchmarkUiState.Waiting ? string.Format(_localization.T("Benchmark.State.Countdown"), RemainingCountdown) : _localization.T($"Benchmark.State.{State}");
    public string HistoryEmptyText => _localization.T("Benchmark.History.Empty");
    public string HistoryEmptyDescription => _localization.T("Benchmark.History.EmptyDescription");
    public string CompareHint => _localization.T("Benchmark.Compare.Hint");
    public string CompareValidationText => BuildCompareValidationText();
    public string CompareMetricHeader => _localization.T("Benchmark.Compare.Column.Metric");
    public string CompareSessionAHeader => _localization.T("Benchmark.Compare.Column.SessionA");
    public string CompareSessionBHeader => _localization.T("Benchmark.Compare.Column.SessionB");
    public string CompareDeltaHeader => _localization.T("Benchmark.Compare.Column.Delta");
    public string CompareChangeHeader => _localization.T("Benchmark.Compare.Column.Change");
    public string OpenFolderText => _localization.T("Benchmark.OpenFolder");
    public string DeleteText => _localization.T("Benchmark.Delete");
    public string ResultTitle => _localization.T("Benchmark.Result.Title");
    public string AdvancedDetailsText => _localization.T("Benchmark.AdvancedDetails");
    public string DiagnosticsText => _localization.T("Benchmark.Diagnostics");
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public string TechnicalError { get => _technicalError; private set => SetProperty(ref _technicalError, value); }
    public bool HasGames => Games.Count > 0;
    public bool HasNoRunningGames => !Games.Any(game => game.IsRunning);
    public bool HasHistory => History.Count > 0;
    public bool HasNoHistory => History.Count == 0;
    public bool HasResult => CurrentResult is not null;
    public bool IsCaptureActive => _coordinator.IsActive || State is BenchmarkUiState.Waiting or BenchmarkUiState.Capturing or BenchmarkUiState.Completing;
    public bool ShowCaptureProgress => IsCaptureActive;
    public bool ShowStartAction => !IsCaptureActive;
    public bool ShowStopAction => IsCaptureActive;
    public string HotkeyStatusText
    {
        get
        {
            BenchmarkHotkeyGesture? gesture = BenchmarkHotkeyGesture.FromSettings(_runtime.Settings.BenchmarkHotkeyEnabled, _runtime.Settings.BenchmarkHotkeyModifiers, _runtime.Settings.BenchmarkHotkeyVirtualKey);
            return string.Format(_localization.T("Benchmark.Hotkey.Status"), gesture?.ToString() ?? _localization.T("Benchmark.Hotkey.NotConfigured"));
        }
    }
    public IReadOnlyList<string> HotkeyTokens
    {
        get
        {
            BenchmarkHotkeyGesture? gesture = BenchmarkHotkeyGesture.FromSettings(_runtime.Settings.BenchmarkHotkeyEnabled, _runtime.Settings.BenchmarkHotkeyModifiers, _runtime.Settings.BenchmarkHotkeyVirtualKey);
            return gesture is null ? [_localization.T("Benchmark.Hotkey.NotConfigured")] : gesture.Value.ToString().Split(" + ", StringSplitOptions.RemoveEmptyEntries);
        }
    }
    public string TestConfigurationTitle => _localization.T("Benchmark.Capture.Configuration");
    public string CurrentStateTitle => _localization.T("Benchmark.Capture.CurrentState");
    public string LatestResultTitle => _localization.T("Benchmark.Capture.LatestResult");
    public string ChangeGameText => _localization.T("Benchmark.Game.Change");
    public string NoRunningGameTitle => _localization.T("Benchmark.Game.NoneTitle");
    public string CustomDurationUnit => _localization.T("Benchmark.Seconds.Short");
    public string CompareEmptyTitle => _localization.T("Benchmark.Compare.EmptyTitle");
    public string CompareEmptyText => _localization.T("Benchmark.Compare.EmptyText");
    public bool HasSelectedGame => SelectedGame is not null;
    public bool HasNoSelectedGame => SelectedGame is null;
    public bool Duration30Selected { get => !UseCustomDuration && SelectedDuration == 30; set { if (value) { UseCustomDuration = false; SelectedDuration = 30; NotifyDurationSelections(); } } }
    public bool Duration60Selected { get => !UseCustomDuration && SelectedDuration == 60; set { if (value) { UseCustomDuration = false; SelectedDuration = 60; NotifyDurationSelections(); } } }
    public bool Duration120Selected { get => !UseCustomDuration && SelectedDuration == 120; set { if (value) { UseCustomDuration = false; SelectedDuration = 120; NotifyDurationSelections(); } } }
    public bool DurationCustomSelected { get => UseCustomDuration; set { if (value) { UseCustomDuration = true; NotifyDurationSelections(); } } }
    public bool Countdown0Selected { get => CountdownSeconds == 0; set { if (value) { CountdownSeconds = 0; NotifyCountdownSelections(); } } }
    public bool Countdown3Selected { get => CountdownSeconds == 3; set { if (value) { CountdownSeconds = 3; NotifyCountdownSelections(); } } }
    public bool Countdown5Selected { get => CountdownSeconds == 5; set { if (value) { CountdownSeconds = 5; NotifyCountdownSelections(); } } }
    public bool HasEnoughComparisonSessions
    {
        get
        {
            List<BenchmarkHistoryItemViewModel> completed = History.Where(item => item.IsCompleted).ToList();
            return completed.SelectMany((left, index) => completed.Skip(index + 1).Select(right => (left, right)))
                .Any(pair => BenchmarkComparisonService.IsSameGame(pair.left.Entry.Metadata.Game, pair.right.Entry.Metadata.Game));
        }
    }
    public bool HasInsufficientComparisonSessions => !HasEnoughComparisonSessions;
    public bool CanStart => !IsCaptureActive && EngineReady && SelectedGame?.IsRunning == true;
    public bool EngineReady { get => _engineReady; private set { if (SetProperty(ref _engineReady, value)) { OnPropertyChanged(nameof(EngineStatus)); OnPropertyChanged(nameof(CanStart)); RaiseCommandStates(); } } }
    public int RequestedDurationSeconds => UseCustomDuration ? Math.Clamp(CustomDuration, 10, 600) : SelectedDuration;
    public double ProgressValue => RequestedDurationSeconds <= 0 ? 0 : Math.Clamp(ElapsedSeconds / RequestedDurationSeconds * 100, 0, 100);

    public BenchmarkUiState State
    {
        get => _state;
        private set
        {
            if (!SetProperty(ref _state, value)) return;
            OnPropertyChanged(nameof(StateText)); OnPropertyChanged(nameof(IsCaptureActive)); OnPropertyChanged(nameof(ShowCaptureProgress)); OnPropertyChanged(nameof(ShowStartAction)); OnPropertyChanged(nameof(ShowStopAction)); OnPropertyChanged(nameof(CanStart)); RaiseCommandStates();
        }
    }

    public BenchmarkGameOptionViewModel? SelectedGame
    {
        get => _selectedGame;
        set
        {
            string? previousId = _selectedGame?.Item.Id;
            if (!SetProperty(ref _selectedGame, value)) return;
            _preselectedLibraryItemId = value?.Item.Id;
            StatusMessage = value?.HasMultipleInstances == true ? _localization.T("Benchmark.Error.MultipleInstances") : string.Empty;
            OnPropertyChanged(nameof(CanStart)); OnPropertyChanged(nameof(HasSelectedGame)); OnPropertyChanged(nameof(HasNoSelectedGame)); RaiseCommandStates();
            if (value is not null && !string.Equals(previousId, value.Item.Id, StringComparison.OrdinalIgnoreCase))
                _runtime.AddActivity(string.Format(_localization.T("Benchmark.Log.TargetSelected"), value.DisplayName));
        }
    }

    public BenchmarkHistoryItemViewModel? SelectedHistory
    {
        get => _selectedHistory;
        set { if (SetProperty(ref _selectedHistory, value) && value is not null) _ = ShowHistoryResultAsync(value); }
    }
    public BenchmarkHistoryItemViewModel? ComparisonA { get => _comparisonA; set { if (SetProperty(ref _comparisonA, value)) RebuildComparison(); } }
    public BenchmarkHistoryItemViewModel? ComparisonB { get => _comparisonB; set { if (SetProperty(ref _comparisonB, value)) RebuildComparison(); } }
    public BenchmarkResultViewModel? CurrentResult { get => _currentResult; private set { if (SetProperty(ref _currentResult, value)) OnPropertyChanged(nameof(HasResult)); } }
    public int SelectedDuration { get => _selectedDuration; set { if (SetProperty(ref _selectedDuration, value)) { DurationChanged(); NotifyDurationSelections(); } } }
    public int CustomDuration { get => _customDuration; set { if (SetProperty(ref _customDuration, Math.Clamp(value, 10, 600))) DurationChanged(); } }
    public bool UseCustomDuration { get => _useCustomDuration; set { if (SetProperty(ref _useCustomDuration, value)) { DurationChanged(); NotifyDurationSelections(); } } }
    public int CountdownSeconds { get => _countdownSeconds; set { if (SetProperty(ref _countdownSeconds, value)) NotifyCountdownSelections(); } }
    public int RemainingCountdown { get => _remainingCountdown; private set { if (SetProperty(ref _remainingCountdown, value)) OnPropertyChanged(nameof(StateText)); } }
    public double ElapsedSeconds { get => _elapsedSeconds; private set { if (SetProperty(ref _elapsedSeconds, value)) { OnPropertyChanged(nameof(ProgressValue)); OnPropertyChanged(nameof(ProgressText)); } } }
    public int SelectedTabIndex { get => _selectedTabIndex; set => SetProperty(ref _selectedTabIndex, value); }

    public BenchmarkViewModel(
        LocalizationService localization,
        IBenchmarkRuntimeContext runtime,
        BenchmarkStorageService? storage = null,
        BenchmarkGameDetectionService? detector = null,
        Func<IReadOnlyList<LibraryItem>>? libraryProvider = null,
        Func<IBenchmarkCaptureBackend>? backendFactory = null,
        Func<bool?>? sessionOptimizationProvider = null,
        ILogger? logger = null,
        Func<(bool Ready, string? Version, string? Error)>? engineProbe = null,
        IBenchmarkCaptureCoordinator? coordinator = null)
    {
        _localization = localization;
        _runtime = runtime;
        _storage = storage ?? new BenchmarkStorageService();
        _detector = detector ?? new BenchmarkGameDetectionService();
        _libraryProvider = libraryProvider ?? (() => new LibraryService().LoadItems());
        _backendFactory = backendFactory ?? (() => new PresentMonApiCaptureBackend(storage: _storage));
        _sessionOptimizationProvider = sessionOptimizationProvider ?? (() => new SessionStateService().Load()?.IsActive);
        _logger = logger ?? LoggerService.Instance;
        _engineProbe = engineProbe;
        _coordinator = coordinator ?? (backendFactory != null || storage != null ? new BenchmarkCaptureCoordinator(_storage, _backendFactory) : (runtime.BenchmarkCoordinator ?? new BenchmarkCaptureCoordinator(_storage, _backendFactory)));
        _coordinator.StateChanged += OnCoordinatorStateChanged;

        _detectionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _detectionTimer.Tick += async (_, _) => { if (!IsCaptureActive) await RefreshGamesAsync(); };
        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _progressTimer.Tick += (_, _) => ElapsedSeconds = _captureClock.Elapsed.TotalSeconds;

        RefreshGamesCommand = new AsyncRelayCommand(_ => RefreshGamesAsync(), _ => !IsCaptureActive);
        StartCommand = new AsyncRelayCommand(_ => RunTrackedCaptureAsync(), _ => CanStart);
        StopCommand = new RelayCommand(_ => Stop(), _ => IsCaptureActive);
        RefreshHistoryCommand = new AsyncRelayCommand(_ => RefreshHistoryAsync());
        OpenSessionFolderCommand = new RelayCommand(OpenSessionFolder, parameter => ResolveEntry(parameter) is not null);
        DeleteSessionCommand = new RelayCommand(DeleteSession, parameter => ResolveEntry(parameter) is not null);

        State = BenchmarkUiState.Idle;
        ProbeEngine();
        _ = RefreshHistoryAsync();
    }

    private void OnCoordinatorStateChanged(object? sender, BenchmarkCaptureStateSnapshot snapshot)
    {
        void Action()
        {
            if (_disposed) return;
            if (snapshot.State == CoordinatorState.Waiting)
            {
                RemainingCountdown = snapshot.RemainingCountdownSeconds;
                State = BenchmarkUiState.Waiting;
            }
            else if (snapshot.State == CoordinatorState.Capturing)
            {
                State = BenchmarkUiState.Capturing;
                _captureClock.Restart();
                _progressTimer.Start();
            }
            else if (snapshot.State == CoordinatorState.Completing)
            {
                State = BenchmarkUiState.Completing;
                _captureClock.Stop();
                _progressTimer.Stop();
            }
        }

        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(Action);
        }
        else
        {
            Action();
        }
    }

    public void Activate()
    {
        if (_disposed) return;
        _detectionTimer.Start();
        _ = RefreshGamesAsync();
    }

    public void Deactivate() => _detectionTimer.Stop();

    public void Preselect(LibraryItem item)
    {
        _preselectedLibraryItemId = item.Id;
        SelectedTabIndex = 0;
        BenchmarkGameOptionViewModel? existing = Games.FirstOrDefault(game => game.Item.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) SelectedGame = existing;
        _ = RefreshGamesAsync();
    }

    public async Task RefreshGamesAsync()
    {
        if (_refreshingGames || _disposed || IsCaptureActive) return;
        _refreshingGames = true;
        try
        {
            (IReadOnlyList<LibraryItem> items, IReadOnlyList<BenchmarkRunningGame> running) = await Task.Run(() =>
            {
                IReadOnlyList<LibraryItem> loaded = _libraryProvider();
                return (loaded, _detector.Detect(loaded));
            });
            string? selectedId = _preselectedLibraryItemId ?? SelectedGame?.Item.Id;
            Games.Clear();
            foreach (LibraryItem item in items.Where(item => item.Type == LibraryItemType.Game && item.IsEnabled).OrderBy(item => item.DisplayName))
            {
                List<BenchmarkRunningGame> matches = running.Where(match => match.LibraryItem.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase)).ToList();
                Games.Add(new BenchmarkGameOptionViewModel
                {
                    Item = item,
                    RunningGame = matches.Count == 1 ? matches[0] : null,
                    HasMultipleInstances = matches.Count > 1,
                    SourceText = _localization.T($"Benchmark.Source.{item.Source}"),
                    StatusText = matches.Count > 1 ? _localization.T("Benchmark.Error.MultipleInstances") : matches.Count == 1 ? RunningText : NotRunningText
                });
            }

            BenchmarkGameOptionViewModel? requested = Games.FirstOrDefault(game => game.Item.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase));
            List<BenchmarkGameOptionViewModel> runningOptions = Games.Where(game => game.IsRunning).ToList();
            SelectedGame = requested ?? (runningOptions.Count == 1 ? runningOptions[0] : SelectedGame is null ? null : Games.FirstOrDefault(game => game.Item.Id.Equals(SelectedGame.Item.Id, StringComparison.OrdinalIgnoreCase)));
            OnPropertyChanged(nameof(HasGames));
            OnPropertyChanged(nameof(HasNoRunningGames));
            if (Games.Count == 0 || runningOptions.Count == 0) StatusMessage = EmptyGamesText;
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.T("Benchmark.Error.Detection");
            TechnicalError = ex.ToString();
            _logger.Warn($"Benchmark game detection failed: {ex.Message}");
        }
        finally { _refreshingGames = false; }
    }

    public Task StartAsync() => PrepareAndStartCaptureAsync(CountdownSeconds, notify: false);

    private async Task PrepareAndStartCaptureAsync(int effectiveCountdownSeconds, bool notify)
    {
        if (!CanStart || SelectedGame?.RunningGame is not BenchmarkRunningGame running) return;

        _visualResetCts?.Cancel();
        CurrentResult = null;
        TechnicalError = string.Empty;
        ElapsedSeconds = 0;

        ProcessProfile? profile = FindActiveProfile(running.LibraryItem);
        var request = new BenchmarkCaptureRequest
        {
            Target = running.Target,
            Process = running.Process,
            AppVersion = new AppInfo().Version,
            ProfileId = profile?.Id,
            ProfileName = profile?.DisplayName,
            SessionOptimizationActive = _sessionOptimizationProvider(),
            DurationSeconds = RequestedDurationSeconds,
            CountdownSeconds = effectiveCountdownSeconds
        };

        _runtime.AddActivity(string.Format(_localization.T("Benchmark.Log.Started"), running.LibraryItem.DisplayName, RequestedDurationSeconds));
        if (notify) UserNotificationRequested?.Invoke(string.Format(_localization.T("Benchmark.Hotkey.Started"), running.LibraryItem.DisplayName));

        _captureClock.Restart();
        _progressTimer.Start();

        BenchmarkCaptureOutcome outcome = await _coordinator.StartCaptureAsync(request);

        _captureClock.Stop();
        _progressTimer.Stop();

        if (outcome.Status == CoordinatorStatus.Completed)
        {
            ElapsedSeconds = outcome.Result?.Session.Metadata.CaptureDurationSeconds ?? _captureClock.Elapsed.TotalSeconds;
            await RefreshHistoryAsync();
            if (outcome.Result is not null)
            {
                BenchmarkHistoryItemViewModel? completed = History.FirstOrDefault(item => item.Entry.Metadata.SessionId == outcome.Result.Session.Metadata.SessionId);
                if (completed is not null) await ShowHistoryResultAsync(completed);
            }
            State = BenchmarkUiState.Completed;
            StatusMessage = _localization.T("Benchmark.Message.Completed");
            _runtime.AddActivity(string.Format(_localization.T("Benchmark.Log.Completed"), running.LibraryItem.DisplayName));
            if (notify) UserNotificationRequested?.Invoke(_localization.T("Benchmark.Hotkey.Completed"));
            _ = ResetTerminalStateAsync(BenchmarkUiState.Completed);
        }
        else if (outcome.Status == CoordinatorStatus.Cancelled)
        {
            State = BenchmarkUiState.Cancelled;
            StatusMessage = _localization.T("Benchmark.Message.Cancelled");
            _runtime.AddActivity(_localization.T("Benchmark.Log.Cancelled"), "Warn");
            await RefreshHistoryAsync();
            _ = ResetTerminalStateAsync(BenchmarkUiState.Cancelled);
        }
        else if (outcome.Status == CoordinatorStatus.Failed)
        {
            State = BenchmarkUiState.Failed;
            string code = outcome.ErrorCode ?? "capture_failed";
            StatusMessage = FriendlyError(code);
            TechnicalError = string.IsNullOrWhiteSpace(outcome.TechnicalDetail) ? $"[{code}]" : $"[{code}] {outcome.TechnicalDetail}";
            _logger.Error($"Benchmark capture failed [{code}].");
            _runtime.AddActivity(string.Format(_localization.T("Benchmark.Log.Failed"), code), "Error");
            await RefreshHistoryAsync();
            if (notify) UserNotificationRequested?.Invoke(_localization.T("Benchmark.Hotkey.CouldNotStart"));
            _ = ResetTerminalStateAsync(BenchmarkUiState.Failed);
        }
        else if (outcome.Status == CoordinatorStatus.AlreadyRunning)
        {
            _logger.Warn("StartCaptureAsync ignored because benchmark capture is already running.");
        }

        RaiseCommandStates();
    }

    public void Stop() => _ = _coordinator.StopAsync();

    private Task RunTrackedCaptureAsync() => StartAsync();

    public async Task HandleGlobalHotkeyAsync()
    {
        if (IsCaptureActive || _coordinator.IsActive)
        {
            await _coordinator.StopAsync();
            _runtime.AddActivity(_localization.T("Benchmark.Log.HotkeyStop"), "Warn");
            UserNotificationRequested?.Invoke(_localization.T("Benchmark.Hotkey.Stopped"));
            return;
        }

        await RefreshGamesAsync();
        BenchmarkGameOptionViewModel? target = SelectedGame?.IsRunning == true ? SelectedGame : null;
        if (target is null)
        {
            List<BenchmarkGameOptionViewModel> running = Games.Where(game => game.IsRunning).ToList();
            if (running.Count == 1) target = running[0];
            else
            {
                string message = running.Count > 1 ? _localization.T("Benchmark.Hotkey.Ambiguous") : _localization.T("Benchmark.Hotkey.NoGame");
                StatusMessage = message;
                _runtime.AddActivity(message, "Warn");
                UserNotificationRequested?.Invoke(_localization.T("Benchmark.Hotkey.CouldNotStart"));
                return;
            }
        }

        SelectedGame = target;
        await PrepareAndStartCaptureAsync(0, notify: true);
    }

    private async Task ResetTerminalStateAsync(BenchmarkUiState terminalState)
    {
        _visualResetCts?.Cancel();
        _visualResetCts = new CancellationTokenSource();
        var token = _visualResetCts.Token;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), token);
            if (!_disposed && State == terminalState) State = BenchmarkUiState.Idle;
        }
        catch (OperationCanceledException)
        {
            // Visual reset canceled
        }
    }

    public async Task CancelAndWaitForCleanupAsync()
    {
        await _coordinator.StopAsync();
    }

    public async Task RefreshHistoryAsync()
    {
        await _historyRefreshGate.WaitAsync();
        try
        {
            BenchmarkHistoryResult result = await Task.Run(_storage.EnumerateSessions);
            Guid? selectedId = SelectedHistory?.Entry.Metadata.SessionId;
            History.Clear();
            foreach (BenchmarkHistoryEntry entry in result.Sessions) History.Add(new BenchmarkHistoryItemViewModel(entry, _localization));
            SelectedHistory = History.FirstOrDefault(item => item.Entry.Metadata.SessionId == selectedId && item.IsCompleted)
                ?? History.FirstOrDefault(item => item.IsCompleted);
            OnPropertyChanged(nameof(HasHistory));
            OnPropertyChanged(nameof(HasNoHistory));
            OnPropertyChanged(nameof(HasEnoughComparisonSessions));
            OnPropertyChanged(nameof(HasInsufficientComparisonSessions));
            RebuildComparison();
        }
        catch (Exception ex)
        {
            StatusMessage = _localization.T("Benchmark.Error.History");
            TechnicalError = ex.ToString();
            _logger.Warn($"Benchmark history refresh failed: {ex.Message}");
        }
        finally
        {
            _historyRefreshGate.Release();
        }
    }

    public void RefreshTexts()
    {
        Guid? selectedId = SelectedHistory?.Entry.Metadata.SessionId;
        Guid? comparisonAId = ComparisonA?.Entry.Metadata.SessionId;
        Guid? comparisonBId = ComparisonB?.Entry.Metadata.SessionId;
        IReadOnlyList<BenchmarkHistoryEntry> entries = History.Select(item => item.Entry).ToList();
        History.Clear();
        foreach (BenchmarkHistoryEntry entry in entries) History.Add(new BenchmarkHistoryItemViewModel(entry, _localization));
        SelectedHistory = History.FirstOrDefault(item => item.Entry.Metadata.SessionId == selectedId);
        ComparisonA = History.FirstOrDefault(item => item.Entry.Metadata.SessionId == comparisonAId);
        ComparisonB = History.FirstOrDefault(item => item.Entry.Metadata.SessionId == comparisonBId);
        OnPropertyChanged(string.Empty);
        if (CurrentResult is not null) CurrentResult = new BenchmarkResultViewModel(CurrentResult.Entry, CurrentResult.ChartPoints, _localization);
        RebuildComparison();
    }

    private async Task ShowHistoryResultAsync(BenchmarkHistoryItemViewModel item)
    {
        if (!item.IsCompleted) { CurrentResult = null; StatusMessage = item.Entry.Metadata.DiagnosticMessage ?? item.Entry.ReadError ?? item.StatusText; return; }
        try
        {
            IReadOnlyList<BenchmarkChartPoint> points = await Task.Run(() =>
            {
                IReadOnlyList<BenchmarkFrameSample> frames = _storage.LoadRawFrames(item.Entry.SessionDirectory);
                return BenchmarkChartData.BuildPresentedSeries(frames, item.Entry.Metadata.Process.ProcessId, item.Entry.Summary?.SelectedSwapChainAddress);
            });
            CurrentResult = new BenchmarkResultViewModel(item.Entry, points, _localization);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            CurrentResult = new BenchmarkResultViewModel(item.Entry, Array.Empty<BenchmarkChartPoint>(), _localization);
            StatusMessage = _localization.T("Benchmark.Error.RawData");
            TechnicalError = ex.ToString();
            _logger.Warn($"Benchmark raw data could not be loaded from '{item.Entry.SessionDirectory}': {ex.Message}");
        }
    }

    private void RebuildComparison()
    {
        ComparisonRows.Clear();
        OnPropertyChanged(nameof(CompareValidationText));
        if (ComparisonA?.IsCompleted != true || ComparisonB?.IsCompleted != true
            || ComparisonA.Entry.Metadata.SessionId == ComparisonB.Entry.Metadata.SessionId
            || !BenchmarkComparisonService.IsSameGame(ComparisonA.Entry.Metadata.Game, ComparisonB.Entry.Metadata.Game)) return;
        foreach (BenchmarkComparisonMetric metric in BenchmarkComparisonService.Compare(ComparisonA.Entry, ComparisonB.Entry))
        {
            ComparisonRows.Add(new BenchmarkComparisonRowViewModel(metric, _localization.T($"Benchmark.Metric.{metric.Key}"), _localization.CurrentLanguage));
        }
    }

    string BuildCompareValidationText()
    {
        if (ComparisonA is null || ComparisonB is null) return _localization.T("Benchmark.Compare.SelectTwo");
        if (!ComparisonA.IsCompleted || !ComparisonB.IsCompleted) return _localization.T("Benchmark.Compare.CompletedOnly");
        if (ComparisonA.Entry.Metadata.SessionId == ComparisonB.Entry.Metadata.SessionId) return _localization.T("Benchmark.Compare.SameSession");
        return BenchmarkComparisonService.IsSameGame(ComparisonA.Entry.Metadata.Game, ComparisonB.Entry.Metadata.Game)
            ? _localization.T("Benchmark.Compare.Ready")
            : _localization.T("Benchmark.Compare.DifferentGames");
    }

    private ProcessProfile? FindActiveProfile(LibraryItem item) => _runtime.Profiles.FirstOrDefault(profile => profile.IsEnabled
        && string.Equals(ProfileService.NormalizeProcessName(profile.ProcessName), ProfileService.NormalizeProcessName(_runtime.LastAppliedProfile), StringComparison.OrdinalIgnoreCase)
        && (profile.LibraryItemId?.Equals(item.Id, StringComparison.OrdinalIgnoreCase) == true || ProfileService.MatchesIdentity(profile, item.ProcessName, item.ExecutablePath)));

    private void ProbeEngine()
    {
        if (_engineProbe is not null)
        {
            (bool ready, string? version, string? error) = _engineProbe();
            EngineReady = ready;
            _engineVersion = version;
            TechnicalError = error ?? string.Empty;
            OnPropertyChanged(nameof(EngineVersionText));
            return;
        }
        try
        {
            string path = new PresentMonApiDllLocator().Locate();
            EngineReady = true;
            _engineVersion = FileVersionInfo.GetVersionInfo(path).FileVersion;
            OnPropertyChanged(nameof(EngineVersionText));
        }
        catch (Exception ex)
        {
            EngineReady = false;
            TechnicalError = ex.Message;
            _logger.Warn($"PresentMon benchmark engine unavailable: {ex.Message}");
        }
    }

    private string FriendlyError(string code) => code switch
    {
        "presentmon_unavailable" or "presentmon_api_status" => _localization.T("Benchmark.Error.EngineUnavailable"),
        "target_disappeared" or "target_not_running" => _localization.T("Benchmark.Error.GameExited"),
        "target_identity_changed" => _localization.T("Benchmark.Error.IdentityChanged"),
        "target_path_mismatch" or "missing_library_identity" => _localization.T("Benchmark.Error.Target"),
        "zero_usable_frames" => _localization.T("Benchmark.Error.ZeroFrames"),
        _ => _localization.T("Benchmark.Error.Capture")
    };

    private void OpenSessionFolder(object? parameter)
    {
        BenchmarkHistoryEntry? entry = ResolveEntry(parameter);
        if (entry is null) return;
        try { Process.Start(new ProcessStartInfo { FileName = _storage.ValidateSessionDirectory(entry.SessionDirectory), UseShellExecute = true }); }
        catch (Exception ex) { StatusMessage = _localization.T("Benchmark.Error.OpenFolder"); TechnicalError = ex.ToString(); }
    }

    private void DeleteSession(object? parameter)
    {
        BenchmarkHistoryEntry? entry = ResolveEntry(parameter);
        if (entry is null) return;
        WpfMessageBoxResult answer = WpfMessageBox.Show(_localization.T("Benchmark.Delete.Confirm"), _localization.T("Benchmark.Delete.Title"), WpfMessageBoxButton.YesNo, WpfMessageBoxImage.Warning);
        if (answer != WpfMessageBoxResult.Yes) return;
        try
        {
            _storage.DeleteSession(entry.SessionDirectory);
            _runtime.AddActivity(string.Format(_localization.T("Benchmark.Log.Deleted"), entry.Metadata.Game.DisplayName));
            _ = RefreshHistoryAsync();
        }
        catch (Exception ex) { StatusMessage = _localization.T("Benchmark.Error.Delete"); TechnicalError = ex.ToString(); _logger.Error("Benchmark session delete failed.", ex); }
    }

    private static BenchmarkHistoryEntry? ResolveEntry(object? parameter) => parameter switch
    {
        BenchmarkHistoryItemViewModel item => item.Entry,
        BenchmarkResultViewModel result => result.Entry,
        _ => null
    };

    private void DurationChanged()
    {
        OnPropertyChanged(nameof(RequestedDurationSeconds)); OnPropertyChanged(nameof(ProgressValue)); OnPropertyChanged(nameof(ProgressText));
    }

    public void RefreshHotkeyStatus() { OnPropertyChanged(nameof(HotkeyStatusText)); OnPropertyChanged(nameof(HotkeyTokens)); }

    private void NotifyDurationSelections()
    {
        OnPropertyChanged(nameof(Duration30Selected)); OnPropertyChanged(nameof(Duration60Selected));
        OnPropertyChanged(nameof(Duration120Selected)); OnPropertyChanged(nameof(DurationCustomSelected));
    }

    private void NotifyCountdownSelections()
    {
        OnPropertyChanged(nameof(Countdown0Selected)); OnPropertyChanged(nameof(Countdown3Selected)); OnPropertyChanged(nameof(Countdown5Selected));
    }

    private void RaiseCommandStates()
    {
        (StartCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _visualResetCts?.Cancel();
        _visualResetCts?.Dispose();
        _coordinator.StateChanged -= OnCoordinatorStateChanged;
        _detectionTimer.Stop(); _progressTimer.Stop();
    }
}
