using FrameHub.App.Helpers;
using FrameHub.App.Services;
using FrameHub.Core.Models;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Library;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace FrameHub.App.ViewModels;

public sealed class ProcessesViewModel : ViewModelBase, IDisposable
{
    private readonly LocalizationService _localization;
    private readonly AppRuntimeService _runtime;
    private readonly DispatcherTimer _refreshTimer;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Dispatcher _uiDispatcher;
    private bool _refreshPending;
    private bool _disposed;
    private ProcessItem? _selectedProcess;
    private string _selectedPriority = "Normal";
    private OptimizationModeOptionViewModel? _selectedOptimizationModeOption;
    private string _searchText = string.Empty;
    private readonly ICollectionView _processesView;

    public string Title => _localization.T("Processes.Title");
    public string Subtitle => _localization.T("Processes.Subtitle");
    public string ProcessListTitle => _localization.T("Processes.ListTitle");
    public string CoreSelectionTitle => _localization.T("Processes.CoreSelectionTitle");
    public string SettingsTitle => _localization.T("Processes.SettingsTitle");
    public string SettingsSubtitle => _localization.T("Processes.SettingsSubtitle");
    public string PriorityLabel => _localization.T("Processes.Priority");
    public string ModeLabel => _localization.T("Processes.Mode");
    public string ApplyButtonText => _localization.T("Processes.Apply");
    public string SaveProfileButtonText => _localization.T("Processes.SaveProfile");
    public string SelectAllText => _localization.T("Processes.SelectAll");
    public string ClearAllText => _localization.T("Processes.ClearAll");
    public string DisableSmtText => _localization.T("Processes.DisableSmt");
    public string DisableECoresText => _localization.T("Processes.DisableECores");
    public string NameHeader => _localization.T("Processes.Name");
    public string InstancesHeader => _localization.T("Processes.Instances");
    public string PriorityHeader => _localization.T("Processes.Priority");
    public string RamHeader => _localization.T("Processes.Ram");
    public string CpuHeader => _localization.T("Processes.Cpu");
    public string PidHeader => _localization.T("Processes.Pid");
    public string ModeHeader => _localization.T("Processes.ModeShort");
    public string RefreshButtonText => _localization.T("Processes.Refresh");
    public string SearchPlaceholderText => _localization.T("Processes.SearchPlaceholder");
    public string StatusHeader => _localization.T("Processes.Status");
    public string DetailsTitle => _localization.T("Processes.DetailsTitle");
    public string ExecutableLabel => _localization.T("Processes.Executable");
    public string TopologyLabel => _localization.T("Processes.Topology");
    public string FilteredCountText => string.Format(_localization.T("Processes.FilteredCount"), _processesView?.Cast<object>().Count() ?? Processes.Count, Processes.Count);
    public string PhysicalCoresTitle => _localization.T("Processes.PhysicalCores");
    public string ThreadCoresTitle => _localization.T("Processes.ThreadCores");
    public string StatusText => _selectedProcess == null ? _localization.T("Processes.NoSelection") : string.Format(_localization.T("Processes.Selected"), _selectedProcess.Name);

    /// <summary>Live search filter (name or PID). Bound to the grid via <see cref="ProcessesView"/>.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value ?? string.Empty)) return;
            _processesView.Refresh();
            OnPropertyChanged(nameof(FilteredCountText));
        }
    }

    public ICollectionView ProcessesView => _processesView;

    public bool IsBenchmarkActive => _runtime.BenchmarkCoordinator.IsActive;

    public bool IsSelectedProcessProtected => SelectedProcess?.IsProtected == true;

    /// <summary>
    /// Fail-closed mutation gate for the UI: protected (Riot/anti-cheat) processes
    /// and an active benchmark capture both disable Apply/Save, with the reason
    /// surfaced via <see cref="ApplyDisabledReason"/>.
    /// </summary>
    public bool CanApplyToSelection => SelectedProcess != null && !IsSelectedProcessProtected && !IsBenchmarkActive;

    public string ApplyDisabledReason
    {
        get
        {
            if (SelectedProcess == null) return _localization.T("Processes.NoSelection");
            if (IsSelectedProcessProtected) return _localization.T("Processes.ProtectionReason");
            if (IsBenchmarkActive) return _localization.T("Processes.BenchmarkActiveReason");
            return string.Empty;
        }
    }

    public bool IsApplyDisabledReasonVisible => SelectedProcess == null || IsSelectedProcessProtected || IsBenchmarkActive;

    public string SelectedProcessExecutableText => SelectedProcess?.ExecutablePath ?? _localization.T("Processes.ExecutableUnknown");

    public string SelectedProcessTopologyText => BuildTopologyText();

    public string SelectedProcessStatusText => SelectedProcess == null
        ? string.Empty
        : SelectedProcess.StatusDisplay;

    /// <summary>
    /// Pure status classifier shared by the grid and the details panel.
    /// Protected (Riot/anti-cheat) wins over managed: such rows are never mutable.
    /// </summary>
    public static string ResolveStatusKey(string? processName, bool hasManagedProfile)
    {
        if (RiotGameProcesses.IsProtectedProcessName(processName)) return "Processes.StatusProtected";
        return hasManagedProfile ? "Processes.StatusManaged" : "Processes.StatusAvailable";
    }

    public ObservableCollection<ProcessItem> Processes { get; } = new();
    public ObservableCollection<CoreInfo> Cores { get; } = new();
    public ObservableCollection<CoreInfo> PhysicalCores { get; } = new();
    public ObservableCollection<CoreInfo> ThreadCores { get; } = new();
    public ObservableCollection<string> AvailablePriorities { get; } = new();
    public ObservableCollection<OptimizationModeOptionViewModel> AvailableOptimizationModes { get; } = new();

    public ICommand ApplyCommand { get; }
    public ICommand SaveProfileCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand ClearAllCommand { get; }
    public ICommand DisableSmtCommand { get; }
    public ICommand DisableECoresCommand { get; }
    public ICommand RefreshCommand { get; }

    public ProcessItem? SelectedProcess
    {
        get => _selectedProcess;
        set
        {
            if (!SetProperty(ref _selectedProcess, value)) return;
            LoadSelectionFromProcessOrProfile();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IsSelectedProcessProtected));
            OnPropertyChanged(nameof(CanApplyToSelection));
            OnPropertyChanged(nameof(ApplyDisabledReason));
            OnPropertyChanged(nameof(IsApplyDisabledReasonVisible));
            OnPropertyChanged(nameof(SelectedProcessExecutableText));
            OnPropertyChanged(nameof(SelectedProcessTopologyText));
            OnPropertyChanged(nameof(SelectedProcessStatusText));
        }
    }

    public string SelectedPriority
    {
        get => _selectedPriority;
        set => SetProperty(ref _selectedPriority, value);
    }

    public OptimizationMode SelectedOptimizationMode
    {
        get => _selectedOptimizationModeOption?.Mode ?? OptimizationMode.CpuSets;
        set
        {
            var normalized = (int)value == 2 ? OptimizationMode.Affinity : value;
            SelectedOptimizationModeOption = AvailableOptimizationModes.FirstOrDefault(x => x.Mode == normalized);
        }
    }

    public OptimizationModeOptionViewModel? SelectedOptimizationModeOption
    {
        get => _selectedOptimizationModeOption;
        set => SetProperty(ref _selectedOptimizationModeOption, value);
    }

    public ProcessesViewModel(LocalizationService localization, AppRuntimeService runtime)
    {
        _localization = localization;
        _runtime = runtime;
        // Captured on the UI thread: the tail-pass in RefreshAsync runs on the
        // ThreadPool (ConfigureAwait(false)) and must marshal the filtered view
        // updates back here — CollectionView rejects background-thread changes.
        _uiDispatcher = Dispatcher.CurrentDispatcher;

        foreach (var core in _runtime.Cores)
        {
            Cores.Add(core);
            if (core.IsThread)
            {
                ThreadCores.Add(core);
            }
            else
            {
                PhysicalCores.Add(core);
            }
        }

        RefreshOptimizationModes();
        RefreshPriorities();

        _processesView = CollectionViewSource.GetDefaultView(Processes);
        _processesView.Filter = MatchesSearchFilter;

        _runtime.BenchmarkCoordinator.StateChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsBenchmarkActive));
            OnPropertyChanged(nameof(CanApplyToSelection));
            OnPropertyChanged(nameof(ApplyDisabledReason));
            OnPropertyChanged(nameof(IsApplyDisabledReasonVisible));
        };

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Clamp(_runtime.Settings.ProcessListRefreshSeconds, 1, 10))
        };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();

        ApplyCommand = new RelayCommand(_ => ApplySelectedOptimization(), _ => SelectedProcess != null);
        SaveProfileCommand = new RelayCommand(_ => SaveSelectedProfile(), _ => SelectedProcess != null);
        SelectAllCommand = new RelayCommand(_ => SetAllCores(true));
        ClearAllCommand = new RelayCommand(_ => SetAllCores(false));
        DisableSmtCommand = new RelayCommand(_ => DisableSmtThreads());
        DisableECoresCommand = new RelayCommand(_ => DisableEfficiencyCores());
        RefreshCommand = new RelayCommand(async _ => await RefreshAsync(force: true));
    }

    public void Start()
    {
        if (!_refreshTimer.IsEnabled)
        {
            _refreshTimer.Start();
        }

        _ = RefreshAsync(force: true);
    }

    public void Stop()
    {
        _refreshTimer.Stop();
    }

    public async Task RefreshAsync(bool force = false)
    {
        if (_disposed) return;
        if (!await _refreshGate.WaitAsync(0).ConfigureAwait(false))
        {
            // A refresh tick is already scanning: coalesce instead of overlapping.
            // force:true no longer bypasses the guard (REA F3) — it keeps priority
            // meaning by always scheduling one follow-up pass after the running one.
            _refreshPending = true;
            return;
        }

        try
        {
            do
            {
                _refreshPending = false;
                var scan = await _runtime.ScanFullProcessListAsync();
                if (_disposed) return;
                if (_uiDispatcher.CheckAccess())
                {
                    UpdateRows(scan.Groups);
                }
                else
                {
                    await _uiDispatcher.InvokeAsync(() => UpdateRows(scan.Groups));
                }
            } while (_refreshPending && !_disposed);
        }
        catch (Exception ex)
        {
            _runtime.AddActivity(string.Format(_localization.T("Processes.Activity.RefreshFailed"), ex.Message), "Warn");
        }
        finally
        {
            _refreshGate.Release();
        }

        // QA P3.5: close the lost-wakeup window between the loop-exit check and
        // Release(). A request that landed exactly there left _refreshPending set
        // with no consumer; one tail pass honors it immediately instead of
        // waiting for the next 1 s tick (force:true no longer returns unscanned).
        // Single-level: the recursive call drains via its own loop.
        if (_refreshPending && !_disposed)
        {
            await RefreshAsync().ConfigureAwait(false);
        }
    }

    public void RefreshTexts()
    {
        RefreshOptimizationModes();
        RefreshPriorities();
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(ProcessListTitle));
        OnPropertyChanged(nameof(CoreSelectionTitle));
        OnPropertyChanged(nameof(SettingsTitle));
        OnPropertyChanged(nameof(SettingsSubtitle));
        OnPropertyChanged(nameof(PriorityLabel));
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(ApplyButtonText));
        OnPropertyChanged(nameof(SaveProfileButtonText));
        OnPropertyChanged(nameof(SelectAllText));
        OnPropertyChanged(nameof(ClearAllText));
        OnPropertyChanged(nameof(DisableSmtText));
        OnPropertyChanged(nameof(DisableECoresText));
        OnPropertyChanged(nameof(NameHeader));
        OnPropertyChanged(nameof(InstancesHeader));
        OnPropertyChanged(nameof(PriorityHeader));
        OnPropertyChanged(nameof(RamHeader));
        OnPropertyChanged(nameof(CpuHeader));
        OnPropertyChanged(nameof(PidHeader));
        OnPropertyChanged(nameof(ModeHeader));
        OnPropertyChanged(nameof(RefreshButtonText));
        OnPropertyChanged(nameof(SearchPlaceholderText));
        OnPropertyChanged(nameof(StatusHeader));
        OnPropertyChanged(nameof(DetailsTitle));
        OnPropertyChanged(nameof(ExecutableLabel));
        OnPropertyChanged(nameof(TopologyLabel));
        OnPropertyChanged(nameof(FilteredCountText));
        OnPropertyChanged(nameof(PhysicalCoresTitle));
        OnPropertyChanged(nameof(ThreadCoresTitle));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsSelectedProcessProtected));
        OnPropertyChanged(nameof(CanApplyToSelection));
        OnPropertyChanged(nameof(ApplyDisabledReason));
        OnPropertyChanged(nameof(IsApplyDisabledReasonVisible));
        OnPropertyChanged(nameof(SelectedProcessExecutableText));
        OnPropertyChanged(nameof(SelectedProcessTopologyText));
        OnPropertyChanged(nameof(SelectedProcessStatusText));
        RefreshRowStatuses();
    }

    private void RefreshOptimizationModes()
    {
        var current = SelectedOptimizationMode;
        AvailableOptimizationModes.Clear();
        AvailableOptimizationModes.Add(new OptimizationModeOptionViewModel(OptimizationMode.CpuSets, _localization));
        AvailableOptimizationModes.Add(new OptimizationModeOptionViewModel(OptimizationMode.Affinity, _localization));
        SelectedOptimizationMode = current;
    }

    private void RefreshPriorities()
    {
        string currentRaw = PriorityService.Normalize(_selectedPriority, allowRealtime: true);
        AvailablePriorities.Clear();
        foreach (var priority in PriorityService.GetDisplayPriorities(_localization.CurrentLanguage, _runtime.Settings.AllowRealtimePriority))
        {
            AvailablePriorities.Add(priority);
        }
        SelectedPriority = PriorityService.Translate(currentRaw, _localization.CurrentLanguage);
    }

    private void UpdateRows(IEnumerable<ProcessGroupSnapshot> groups)
    {
        var groupList = groups.ToList();
        for (int i = Processes.Count - 1; i >= 0; i--)
        {
            if (!groupList.Any(g => g.Name.Equals(Processes[i].Name, StringComparison.OrdinalIgnoreCase)))
            {
                Processes.RemoveAt(i);
            }
        }

        foreach (var group in groupList)
        {
            var item = Processes.FirstOrDefault(p => p.Name.Equals(group.Name, StringComparison.OrdinalIgnoreCase));
            var profile = _runtime.Profiles.FirstOrDefault(p => p.IsEnabled && ProfileService.MatchesIdentity(p, group.Name, group.ExecutablePath));
            string tag = profile == null
                ? string.Empty
                : profile.OptimizationMode == OptimizationMode.CpuSets
                    ? _localization.T("OptimizationMode.CpuSetsShort")
                    : _localization.T("OptimizationMode.AffinityShort");

            if (item == null)
            {
                item = new ProcessItem { Name = group.Name };
                Processes.Add(item);
            }

            item.Id = group.FirstProcessId;
            item.ExecutablePath = group.ExecutablePath;
            item.InstanceCount = group.InstanceCount;
            item.RamUsageMB = $"{group.TotalMemoryBytes / 1024 / 1024} MB";
            item.CpuUsage = $"{Math.Round(group.CpuUsagePercent, 1)}%";
            item.Priority = PriorityService.Translate(group.Priority.ToString(), _localization.CurrentLanguage);
            item.IsOptimized = profile != null;
            item.ModeTag = tag;
            item.IsProtected = RiotGameProcesses.IsProtectedProcessName(group.Name);
            item.StatusDisplay = _localization.T(ResolveStatusKey(group.Name, profile != null));
        }

        _processesView.Refresh();
        OnPropertyChanged(nameof(FilteredCountText));
    }

    private void RefreshRowStatuses()
    {
        foreach (var item in Processes)
        {
            bool managed = _runtime.Profiles.Any(p => p.IsEnabled && ProfileService.MatchesIdentity(p, item.Name, item.ExecutablePath));
            item.IsProtected = RiotGameProcesses.IsProtectedProcessName(item.Name);
            item.IsOptimized = managed;
            item.StatusDisplay = _localization.T(ResolveStatusKey(item.Name, managed));
        }

        _processesView.Refresh();
        OnPropertyChanged(nameof(FilteredCountText));
    }

    private bool MatchesSearchFilter(object obj)
    {
        if (obj is not ProcessItem item) return false;
        if (string.IsNullOrWhiteSpace(_searchText)) return true;
        string needle = _searchText.Trim();
        return item.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || item.Id.ToString(System.Globalization.CultureInfo.InvariantCulture).Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    private string BuildTopologyText()
    {
        int physical = PhysicalCores.Count;
        int threads = ThreadCores.Count;
        int efficient = Cores.Count(c => c.IsECore);
        return string.Format(_localization.T("Processes.TopologyValue"), physical, threads, efficient, Cores.Count);
    }


    private void LoadSelectionFromProcessOrProfile()
    {
        if (SelectedProcess == null) return;

        var profile = _runtime.Profiles.FirstOrDefault(p => p.IsEnabled && ProfileService.MatchesIdentity(p, SelectedProcess.Name, SelectedProcess.ExecutablePath));
        if (profile != null)
        {
            SelectedPriority = PriorityService.Translate(profile.Priority, _localization.CurrentLanguage);
            SelectedOptimizationMode = profile.OptimizationMode;
            ApplyMaskToCores(profile.AffinityMask);
            return;
        }

        SelectedPriority = SelectedProcess.Priority;

        var currentSelection = _runtime.ProcessService.GetCurrentCoreSelection(SelectedProcess.Id, _runtime.CpuSetMap);
        if (currentSelection.Success && currentSelection.Mask != 0)
        {
            SelectedOptimizationMode = currentSelection.Mode;
            ApplyMaskToCores(currentSelection.Mask);
            return;
        }

        SelectedOptimizationMode = OptimizationMode.CpuSets;
        SetAllCores(true);
        _runtime.AddActivity(string.Format(_localization.T("Processes.Activity.CpuReadFailed"), SelectedProcess.Name, currentSelection.Message), "Warn");
    }

    private void ApplySelectedOptimization()
    {
        if (SelectedProcess == null) return;
        if (!CanApplyToSelection)
        {
            _runtime.AddActivity(string.Format(_localization.T("Processes.Activity.ApplyBlocked"), SelectedProcess.Name, ApplyDisabledReason), "Warn");
            return;
        }
        var profile = BuildProfileFromSelection(save: false);
        var result = _runtime.ApplyProfileNow(profile, force: true);
        _runtime.AddActivity(string.Format(_localization.T(result.HasAnySuccess ? "Processes.Activity.TempApplied" : "Processes.Activity.TempNotApplied"), SelectedProcess.Name), result.HasAnySuccess ? "Info" : "Warn");
    }

    private void SaveSelectedProfile()
    {
        if (SelectedProcess == null) return;
        if (!CanApplyToSelection)
        {
            _runtime.AddActivity(string.Format(_localization.T("Processes.Activity.ApplyBlocked"), SelectedProcess.Name, ApplyDisabledReason), "Warn");
            return;
        }
        var profile = BuildProfileFromSelection(save: true);
        _runtime.UpsertProfile(profile);
        _runtime.ApplyProfileNow(profile, force: true);
        _runtime.AddActivity(string.Format(_localization.T("Processes.Activity.ProfileSaved"), SelectedProcess.Name));
    }

    private ProcessProfile BuildProfileFromSelection(bool save)
    {
        if (SelectedProcess == null) throw new InvalidOperationException("No selected process.");
        long mask = BuildSelectedCoreMask();
        if (mask == 0)
        {
            mask = Cores.Take(64).Select(c => c.Index).Aggregate(0L, (current, index) => current | (1L << index));
        }

        return new ProcessProfile
        {
            ProcessName = SelectedProcess.Name,
            DisplayName = SelectedProcess.Name,
            ExecutablePath = SelectedProcess.ExecutablePath,
            AffinityMask = mask,
            Priority = PriorityService.Normalize(SelectedPriority, _runtime.Settings.AllowRealtimePriority),
            OptimizationMode = SelectedOptimizationMode,
            IsEnabled = true,
            ApplyCoreOptimization = true,
            ApplyPriority = true,
            Notes = save ? "Created from FrameHub Processes module." : "Temporary FrameHub optimization."
        };
    }

    private void SetAllCores(bool isChecked)
    {
        foreach (var core in Cores)
        {
            core.IsChecked = isChecked;
        }
    }

    private void DisableSmtThreads()
    {
        foreach (var core in Cores.Where(c => c.IsThread))
        {
            core.IsChecked = false;
        }
    }

    private void DisableEfficiencyCores()
    {
        foreach (var core in Cores.Where(c => c.IsECore))
        {
            core.IsChecked = false;
        }
    }

    private long BuildSelectedCoreMask()
    {
        long mask = 0;
        for (int i = 0; i < Cores.Count && i < 64; i++)
        {
            if (Cores[i].IsChecked)
            {
                mask |= 1L << i;
            }
        }
        return mask;
    }

    private void ApplyMaskToCores(long mask)
    {
        for (int i = 0; i < Cores.Count && i < 64; i++)
        {
            Cores[i].IsChecked = (mask & (1L << i)) != 0;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
