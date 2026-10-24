using FrameHub.App.Helpers;
using FrameHub.App.Services;
using FrameHub.Core.Models;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Library;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;

namespace FrameHub.App.ViewModels;

public sealed class ProfilesViewModel : ViewModelBase
{
    private readonly LocalizationService _localization;
    private readonly AppRuntimeService _runtime;
    private ProcessProfile? _selectedProfile;
    private string _selectedPriority = "Normal";
    private OptimizationModeOptionViewModel? _selectedOptimizationModeOption;
    private bool _isProfileEnabled = true;
    private string _searchText = string.Empty;
    private readonly ICollectionView _profilesView;

    public string Title => _localization.T("Profiles.Title");
    public string Subtitle => _localization.T("Profiles.Subtitle");
    public string SavedProfilesTitle => _localization.T("Profiles.SavedTitle");
    public string ProfileDetailsTitle => _localization.T("Profiles.DetailsTitle");
    public string ApplyButtonText => _localization.T("Profiles.Apply");
    public string DeleteButtonText => _localization.T("Profiles.Delete");
    public string SaveChangesButtonText => _localization.T("Profiles.SaveChanges");
    public string EmptyText => _localization.T("Profiles.Empty");
    public string EmptyStateText => _localization.T("Profiles.EmptyState");
    public string SelectProfileTitle => _localization.T("Profiles.SelectTitle");
    public string SelectProfileDescription => _localization.T("Profiles.SelectDescription");
    public string RefreshText => _localization.T("Profiles.Refresh");
    public string NameHeader => _localization.T("Profiles.Name");
    public string ModeHeader => _localization.T("Profiles.Mode");
    public string PriorityHeader => _localization.T("Profiles.Priority");
    public string EnabledHeader => _localization.T("Profiles.Enabled");
    public string SavedProfilesHint => _localization.T("Profiles.SavedHint");
    public string ProfileDetailsHint => _localization.T("Profiles.DetailsHint");
    public string WatcherInfoText => _localization.T("Profiles.WatcherInfo");
    public string StorageInfoText => _localization.T("Profiles.StorageInfo");
    public string EditorTitle => _localization.T("Profiles.EditorTitle");
    public string EditorHint => _localization.T("Profiles.EditorHint");
    public string ModeLabel => _localization.T("Profiles.ModeLabel");
    public string PriorityLabel => _localization.T("Profiles.PriorityLabel");
    public string IsEnabledLabel => _localization.T("Profiles.EnabledLabel");
    public string CoreSelectionTitle => _localization.T("Profiles.CoreSelectionTitle");
    public string PhysicalCoresTitle => _localization.T("Profiles.PhysicalCores");
    public string ThreadsTitle => _localization.T("Profiles.Threads");
    public string SelectAllText => _localization.T("Profiles.SelectAll");
    public string ClearAllText => _localization.T("Profiles.ClearAll");
    public string DisableSmtText => _localization.T("Profiles.DisableSmt");
    public string DisableECoresText => _localization.T("Profiles.DisableECores");
    public string ConflictWarningText => BuildConflictWarning();
    public bool HasConflictWarning => FindConflictingGroups(_runtime.Profiles).Count != 0;
    public string ProfilesCountText => string.Format(_localization.T("Profiles.Count"), Profiles.Count);
    public string SearchPlaceholderText => _localization.T("Profiles.Search");

    /// <summary>Live search filter (process or display name). Bound via <see cref="ProfilesView"/>.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value ?? string.Empty)) return;
            _profilesView.Refresh();
            OnPropertyChanged(nameof(ProfilesCountText));
        }
    }

    public ICollectionView ProfilesView => _profilesView;
    public string CreateFirstProfileText => _localization.T("Profiles.CreateFirst");
    public string FirstProfileHintText => _localization.T("Profiles.FirstProfileHint");
    public string EditorNoticeText => BuildEditorNotice();
    public bool IsEditorNoticeVisible => !string.IsNullOrEmpty(EditorNoticeText);
    public string DeleteConfirmTitle => _localization.T("Profiles.DeleteConfirmTitle");
    public string DeleteConfirmMessage => _localization.T("Profiles.DeleteConfirmMessage");
    public string DeleteConfirmText => _localization.T("Profiles.DeleteConfirm");
    public string DeleteCancelText => _localization.T("Dialog.Cancel");

    /// <summary>
    /// Raised when the user clicks Delete: the view shows a confirmation dialog
    /// and calls <see cref="ConfirmDeleteSelected"/> only on explicit consent.
    /// </summary>
    public event Action<string>? DeleteConfirmationRequested;

    /// <summary>
    /// Raised by the empty-state / first-profile CTA: the shell navigates to
    /// Processes &amp; CPU where a profile is created from a running process.
    /// </summary>
    public event Action? CreateFirstProfileRequested;

    public ICommand CreateFirstProfileCommand { get; }

    public ObservableCollection<ProcessProfile> Profiles { get; } = new();
    public ObservableCollection<CoreInfo> PhysicalCores { get; } = new();
    public ObservableCollection<CoreInfo> ThreadCores { get; } = new();
    public ObservableCollection<string> AvailablePriorities { get; } = new();
    public ObservableCollection<OptimizationModeOptionViewModel> AvailableOptimizationModes { get; } = new();

    public ICommand ApplyProfileCommand { get; }
    public ICommand DeleteProfileCommand { get; }
    public ICommand SaveProfileChangesCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand ClearAllCommand { get; }
    public ICommand DisableSmtCommand { get; }
    public ICommand DisableECoresCommand { get; }

    public ProcessProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!SetProperty(ref _selectedProfile, value)) return;
            LoadEditorFromSelectedProfile();
            OnPropertyChanged(nameof(IsProfileSelected));
            OnPropertyChanged(nameof(SelectedProfileDescription));
            OnPropertyChanged(nameof(EditorNoticeText));
            OnPropertyChanged(nameof(IsEditorNoticeVisible));
            OnPropertyChanged(nameof(IsEditorValid));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsProfileSelected => SelectedProfile != null;

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

    public bool IsProfileEnabled
    {
        get => _isProfileEnabled;
        set => SetProperty(ref _isProfileEnabled, value);
    }

    public string SelectedProfileDescription
    {
        get
        {
            if (SelectedProfile == null) return EmptyText;
            return $"{SelectedProfile.ProcessName} · {FormatMode(SelectedProfile.OptimizationMode)} · {PriorityService.Translate(SelectedProfile.Priority, _localization.CurrentLanguage)}";
        }
    }

    public ProfilesViewModel(LocalizationService localization, AppRuntimeService runtime)
    {
        _localization = localization;
        _runtime = runtime;
        _runtime.ProfilesChanged += (_, _) => RefreshProfiles();

        BuildEditorCoreLists();
        RefreshOptimizationModes();
        RefreshPriorities();

        _profilesView = CollectionViewSource.GetDefaultView(Profiles);
        _profilesView.Filter = MatchesSearchFilter;

        ApplyProfileCommand = new RelayCommand(_ => ApplySelected(), _ => SelectedProfile != null);
        DeleteProfileCommand = new RelayCommand(_ => RequestDeleteSelected(), _ => SelectedProfile != null);
        SaveProfileChangesCommand = new RelayCommand(_ => SaveSelectedChanges(), _ => SelectedProfile != null);
        RefreshCommand = new RelayCommand(_ => RefreshProfiles());
        CreateFirstProfileCommand = new RelayCommand(_ => CreateFirstProfileRequested?.Invoke());
        SelectAllCommand = new RelayCommand(_ => TouchEditor(() => SetAllCores(true)), _ => SelectedProfile != null);
        ClearAllCommand = new RelayCommand(_ => TouchEditor(() => SetAllCores(false)), _ => SelectedProfile != null);
        DisableSmtCommand = new RelayCommand(_ => TouchEditor(() => DisableSmtThreads()), _ => SelectedProfile != null);
        DisableECoresCommand = new RelayCommand(_ => TouchEditor(() => DisableEfficiencyCores()), _ => SelectedProfile != null);

        SetAllCores(true);
        RefreshProfiles();
    }

    public void RefreshProfiles()
    {
        string? selectedId = SelectedProfile?.Id;

        Profiles.Clear();
        foreach (var profile in _runtime.Profiles.OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            profile.DisplayPriority = PriorityService.Translate(profile.Priority, _localization.CurrentLanguage);
            profile.DisplayOptimizationMode = FormatMode(profile.OptimizationMode);
            Profiles.Add(profile);
        }

        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            SelectedProfile = Profiles.FirstOrDefault(p => p.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase));
        }
        else if (SelectedProfile != null && !Profiles.Contains(SelectedProfile))
        {
            SelectedProfile = null;
        }

        OnPropertyChanged(nameof(SelectedProfileDescription));
        OnPropertyChanged(nameof(ConflictWarningText));
        OnPropertyChanged(nameof(HasConflictWarning));
        OnPropertyChanged(nameof(ProfilesCountText));
        _profilesView.Refresh();
    }

    /// <summary>
    /// Groups of ENABLED profiles whose identities overlap (same process name),
    /// so the user sees which rules compete for one process. Pure and testable.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<ProcessProfile>> FindConflictingGroups(IEnumerable<ProcessProfile> profiles)
    {
        return profiles
            .Where(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.ProcessName))
            .GroupBy(p => p.ProcessName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(g => (IReadOnlyList<ProcessProfile>)g.ToList())
            .ToList();
    }

    private string BuildConflictWarning()
    {
        var groups = FindConflictingGroups(_runtime.Profiles);
        if (groups.Count == 0) return string.Empty;
        string names = string.Join(", ", groups.Select(g => g[0].ProcessName).Distinct(StringComparer.OrdinalIgnoreCase));
        return string.Format(_localization.T("Profiles.ConflictWarning"), names);
    }

    /// <summary>Fail-closed editor validation: empty name blocks save/apply; the
    /// zero-core mask is auto-expanded to all cores (surfaced as a notice, not silent).</summary>
    public bool IsEditorValid => SelectedProfile != null && !string.IsNullOrWhiteSpace(SelectedProfile.ProcessName);

    private string BuildEditorNotice()
    {
        if (SelectedProfile == null) return string.Empty;
        if (string.IsNullOrWhiteSpace(SelectedProfile.ProcessName))
            return _localization.T("Profiles.ValidationNameEmpty");
        if (RiotGameProcesses.IsProtectedProcessName(SelectedProfile.ProcessName))
            return _localization.T("Profiles.ValidationProtected");
        if (BuildSelectedCoreMask() == 0)
            return _localization.T("Profiles.ValidationAllCores");
        return string.Empty;
    }

    private void TouchEditor(Action mutate)
    {
        mutate();
        OnPropertyChanged(nameof(EditorNoticeText));
        OnPropertyChanged(nameof(IsEditorNoticeVisible));
        OnPropertyChanged(nameof(IsEditorValid));
    }

    public void RefreshTexts()
    {
        RefreshOptimizationModes();
        RefreshPriorities();

        foreach (var profile in Profiles)
        {
            profile.DisplayPriority = PriorityService.Translate(profile.Priority, _localization.CurrentLanguage);
            profile.DisplayOptimizationMode = FormatMode(profile.OptimizationMode);
        }

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(SavedProfilesTitle));
        OnPropertyChanged(nameof(ProfileDetailsTitle));
        OnPropertyChanged(nameof(ApplyButtonText));
        OnPropertyChanged(nameof(DeleteButtonText));
        OnPropertyChanged(nameof(SaveChangesButtonText));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(EmptyStateText));
        OnPropertyChanged(nameof(SelectProfileTitle));
        OnPropertyChanged(nameof(SelectProfileDescription));
        OnPropertyChanged(nameof(RefreshText));
        OnPropertyChanged(nameof(NameHeader));
        OnPropertyChanged(nameof(ModeHeader));
        OnPropertyChanged(nameof(PriorityHeader));
        OnPropertyChanged(nameof(EnabledHeader));
        OnPropertyChanged(nameof(SavedProfilesHint));
        OnPropertyChanged(nameof(ProfileDetailsHint));
        OnPropertyChanged(nameof(WatcherInfoText));
        OnPropertyChanged(nameof(StorageInfoText));
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(EditorHint));
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(PriorityLabel));
        OnPropertyChanged(nameof(IsEnabledLabel));
        OnPropertyChanged(nameof(CoreSelectionTitle));
        OnPropertyChanged(nameof(PhysicalCoresTitle));
        OnPropertyChanged(nameof(ThreadsTitle));
        OnPropertyChanged(nameof(SelectAllText));
        OnPropertyChanged(nameof(ClearAllText));
        OnPropertyChanged(nameof(DisableSmtText));
        OnPropertyChanged(nameof(DisableECoresText));
        OnPropertyChanged(nameof(SelectedProfileDescription));
        OnPropertyChanged(nameof(ConflictWarningText));
        OnPropertyChanged(nameof(HasConflictWarning));
        OnPropertyChanged(nameof(ProfilesCountText));
        OnPropertyChanged(nameof(CreateFirstProfileText));
        OnPropertyChanged(nameof(FirstProfileHintText));
        OnPropertyChanged(nameof(EditorNoticeText));
        OnPropertyChanged(nameof(IsEditorNoticeVisible));
        OnPropertyChanged(nameof(IsEditorValid));
        OnPropertyChanged(nameof(DeleteConfirmTitle));
        OnPropertyChanged(nameof(DeleteConfirmMessage));
        OnPropertyChanged(nameof(DeleteConfirmText));
        OnPropertyChanged(nameof(DeleteCancelText));
        OnPropertyChanged(nameof(SearchPlaceholderText));
        OnPropertyChanged(nameof(SearchText));
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

    private void BuildEditorCoreLists()
    {
        PhysicalCores.Clear();
        ThreadCores.Clear();

        foreach (var source in _runtime.Cores.OrderBy(c => c.Index))
        {
            var clone = CloneCore(source);
            if (clone.IsThread)
            {
                ThreadCores.Add(clone);
            }
            else
            {
                PhysicalCores.Add(clone);
            }
        }
    }

    private static CoreInfo CloneCore(CoreInfo source)
    {
        return new CoreInfo
        {
            Index = source.Index,
            Group = source.Group,
            CoreIndex = source.CoreIndex,
            EfficiencyClass = source.EfficiencyClass,
            TypeTag = source.TypeTag,
            IsThread = source.IsThread,
            IsECore = source.IsECore,
            IsChecked = source.IsChecked,
            LoadUsage = source.LoadUsage
        };
    }

    private void LoadEditorFromSelectedProfile()
    {
        if (SelectedProfile == null)
        {
            SelectedPriority = PriorityService.Translate("Normal", _localization.CurrentLanguage);
            SelectedOptimizationMode = OptimizationMode.CpuSets;
            IsProfileEnabled = true;
            SetAllCores(true);
            return;
        }

        SelectedPriority = PriorityService.Translate(SelectedProfile.Priority, _localization.CurrentLanguage);
        SelectedOptimizationMode = SelectedProfile.OptimizationMode;
        IsProfileEnabled = SelectedProfile.IsEnabled;
        ApplyMaskToCores(SelectedProfile.AffinityMask);
    }

    private void ApplySelected()
    {
        if (SelectedProfile == null || !IsEditorValid) return;
        if (RiotGameProcesses.IsProtectedProcessName(SelectedProfile.ProcessName))
        {
            _runtime.AddActivity(string.Format(_localization.T("Profiles.Activity.ApplyBlocked"), SelectedProfile.ProcessName), "Warn");
            return;
        }
        var profile = BuildProfileFromEditor();
        _runtime.ApplyProfileNow(profile, force: true);
    }

    private void SaveSelectedChanges()
    {
        if (SelectedProfile == null || !IsEditorValid) return;

        var updated = BuildProfileFromEditor();
        string selectedId = updated.Id;
        _runtime.UpsertProfile(updated, replaceSameProcessName: false);
        _runtime.AddActivity(string.Format(_localization.T("Profiles.Activity.Updated"), updated.ProcessName));

        SelectedProfile = _runtime.Profiles.FirstOrDefault(p => p.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase));
        RefreshProfiles();
    }

    /// <summary>First step of the two-step delete: the view confirms, then calls <see cref="ConfirmDeleteSelected"/>.</summary>
    public void RequestDeleteSelected()
    {
        if (SelectedProfile == null) return;
        DeleteConfirmationRequested?.Invoke(SelectedProfile.ProcessName);
    }

    /// <summary>Second step of the two-step delete: runs only after explicit user consent.</summary>
    public void ConfirmDeleteSelected()
    {
        if (SelectedProfile == null) return;
        var profile = SelectedProfile;
        SelectedProfile = null;
        _runtime.DeleteProfile(profile);
    }

    /// <summary>
    /// Persists a per-row enable/disable toggle from the grid. The CheckBox binding
    /// already flipped <see cref="ProcessProfile.IsEnabled"/>; this saves it through
    /// the runtime owner (single persistence path) without auto-applying anything:
    /// the watcher picks the new state up on its normal cadence with user consent
    /// already recorded in the toggle itself.
    /// </summary>
    public void ToggleProfileEnabled(ProcessProfile profile)
    {
        if (profile == null) return;
        _runtime.UpsertProfile(profile, replaceSameProcessName: false);
        string stateText = _localization.T(profile.IsEnabled ? "Profiles.ToggledOn" : "Profiles.ToggledOff");
        _runtime.AddActivity(string.Format(_localization.T("Profiles.Activity.Toggled"), profile.ProcessName, stateText));
        RefreshProfiles();
    }

    private bool MatchesSearchFilter(object obj)
    {
        if (obj is not ProcessProfile profile) return false;
        if (string.IsNullOrWhiteSpace(_searchText)) return true;
        string needle = _searchText.Trim();
        return profile.ProcessName.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || (profile.DisplayName ?? string.Empty).Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    private ProcessProfile BuildProfileFromEditor()
    {
        if (SelectedProfile == null) throw new InvalidOperationException("No selected profile.");

        long mask = BuildSelectedCoreMask();
        if (mask == 0)
        {
            mask = BuildAllCoreMask();
        }

        return new ProcessProfile
        {
            Id = SelectedProfile.Id,
            SchemaVersion = SelectedProfile.SchemaVersion,
            ProcessName = SelectedProfile.ProcessName,
            DisplayName = SelectedProfile.DisplayName,
            ExecutablePath = SelectedProfile.ExecutablePath,
            LibraryItemId = SelectedProfile.LibraryItemId,
            OptimizationMode = SelectedOptimizationMode,
            AffinityMask = mask,
            Priority = PriorityService.Normalize(SelectedPriority, _runtime.Settings.AllowRealtimePriority),
            ApplyPriority = true,
            ApplyCoreOptimization = true,
            IsEnabled = IsProfileEnabled,
            Notes = SelectedProfile.Notes,
            CreatedAt = SelectedProfile.CreatedAt,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private void SetAllCores(bool isChecked)
    {
        foreach (var core in PhysicalCores)
        {
            core.IsChecked = isChecked;
        }

        foreach (var core in ThreadCores)
        {
            core.IsChecked = isChecked;
        }
    }

    private void DisableSmtThreads()
    {
        foreach (var core in ThreadCores)
        {
            core.IsChecked = false;
        }
    }

    private void DisableEfficiencyCores()
    {
        foreach (var core in PhysicalCores.Concat(ThreadCores).Where(c => c.IsECore))
        {
            core.IsChecked = false;
        }
    }

    private long BuildSelectedCoreMask()
    {
        long mask = 0;
        foreach (var core in PhysicalCores.Concat(ThreadCores).Where(c => c.Index >= 0 && c.Index < 64 && c.IsChecked))
        {
            mask |= 1L << core.Index;
        }
        return mask;
    }

    private long BuildAllCoreMask()
    {
        long mask = 0;
        foreach (var core in PhysicalCores.Concat(ThreadCores).Where(c => c.Index >= 0 && c.Index < 64))
        {
            mask |= 1L << core.Index;
        }
        return mask;
    }

    private void ApplyMaskToCores(long mask)
    {
        if (mask == 0)
        {
            SetAllCores(true);
            return;
        }

        foreach (var core in PhysicalCores.Concat(ThreadCores))
        {
            core.IsChecked = core.Index >= 0 && core.Index < 64 && (mask & (1L << core.Index)) != 0;
        }
    }

    private string FormatMode(OptimizationMode mode)
    {
        return mode == OptimizationMode.CpuSets
            ? _localization.T("OptimizationMode.CpuSets")
            : _localization.T("OptimizationMode.Affinity");
    }
}
