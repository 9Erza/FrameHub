using FrameHub.App.Helpers;
using FrameHub.App.Services;
using FrameHub.Core.Models;
using FrameHub.Core.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;

namespace FrameHub.App.ViewModels;

public sealed class ProcessesViewModel : ViewModelBase, IDisposable
{
    private readonly LocalizationService _localization;
    private readonly AppRuntimeService _runtime;
    private readonly DispatcherTimer _refreshTimer;
    private bool _isRefreshing;
    private bool _disposed;
    private ProcessItem? _selectedProcess;
    private string _selectedPriority = "Normal";
    private OptimizationModeOptionViewModel? _selectedOptimizationModeOption;

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
    public string PhysicalCoresTitle => _localization.CurrentLanguage == "pl" ? "Rdzenie" : "Cores";
    public string ThreadCoresTitle => _localization.CurrentLanguage == "pl" ? "Wątki SMT / HT" : "SMT / HT threads";
    public string StatusText => _selectedProcess == null ? _localization.T("Processes.NoSelection") : string.Format(_localization.T("Processes.Selected"), _selectedProcess.Name);

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
        if (_isRefreshing && !force) return;
        _isRefreshing = true;

        try
        {
            var scan = await _runtime.ScanFullProcessListAsync();
            UpdateRows(scan.Groups);
        }
        catch (Exception ex)
        {
            _runtime.AddActivity($"Process list refresh failed: {ex.Message}", "Warn");
        }
        finally
        {
            _isRefreshing = false;
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
        OnPropertyChanged(nameof(PhysicalCoresTitle));
        OnPropertyChanged(nameof(ThreadCoresTitle));
        OnPropertyChanged(nameof(StatusText));
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
            var profile = _runtime.Profiles.FirstOrDefault(p => p.IsEnabled && p.ProcessName.Equals(group.Name, StringComparison.OrdinalIgnoreCase));
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
            item.InstanceCount = group.InstanceCount;
            item.RamUsageMB = $"{group.TotalMemoryBytes / 1024 / 1024} MB";
            item.CpuUsage = $"{Math.Round(group.CpuUsagePercent, 1)}%";
            item.Priority = PriorityService.Translate(group.Priority.ToString(), _localization.CurrentLanguage);
            item.IsOptimized = profile != null;
            item.ModeTag = tag;
        }
    }


    private void LoadSelectionFromProcessOrProfile()
    {
        if (SelectedProcess == null) return;

        var profile = _runtime.Profiles.FirstOrDefault(p => p.IsEnabled && p.ProcessName.Equals(SelectedProcess.Name, StringComparison.OrdinalIgnoreCase));
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
        _runtime.AddActivity($"Nie udało się odczytać obecnego przypisania CPU dla '{SelectedProcess.Name}': {currentSelection.Message}", "Warn");
    }

    private void ApplySelectedOptimization()
    {
        if (SelectedProcess == null) return;
        var profile = BuildProfileFromSelection(save: false);
        var result = _runtime.ApplyProfileNow(profile, force: true);
        _runtime.AddActivity(result.HasAnySuccess
            ? $"Applied temporary optimization for '{SelectedProcess.Name}'."
            : $"Temporary optimization for '{SelectedProcess.Name}' did not apply.", result.HasAnySuccess ? "Info" : "Warn");
    }

    private void SaveSelectedProfile()
    {
        if (SelectedProcess == null) return;
        var profile = BuildProfileFromSelection(save: true);
        _runtime.UpsertProfile(profile);
        _runtime.ApplyProfileNow(profile, force: true);
        _runtime.AddActivity($"Saved profile for '{SelectedProcess.Name}'.");
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
