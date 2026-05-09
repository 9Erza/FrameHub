using FrameHub.App.Helpers;
using FrameHub.App.Services;
using FrameHub.Core.Models;
using FrameHub.Core.Services;
using System.Collections.ObjectModel;
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

    public string Title => _localization.T("Profiles.Title");
    public string Subtitle => _localization.T("Profiles.Subtitle");
    public string SavedProfilesTitle => _localization.T("Profiles.SavedTitle");
    public string ProfileDetailsTitle => _localization.T("Profiles.DetailsTitle");
    public string ApplyButtonText => _localization.T("Profiles.Apply");
    public string DeleteButtonText => _localization.T("Profiles.Delete");
    public string SaveChangesButtonText => _localization.T("Profiles.SaveChanges");
    public string EmptyText => _localization.T("Profiles.Empty");
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

        ApplyProfileCommand = new RelayCommand(_ => ApplySelected(), _ => SelectedProfile != null);
        DeleteProfileCommand = new RelayCommand(_ => DeleteSelected(), _ => SelectedProfile != null);
        SaveProfileChangesCommand = new RelayCommand(_ => SaveSelectedChanges(), _ => SelectedProfile != null);
        RefreshCommand = new RelayCommand(_ => RefreshProfiles());
        SelectAllCommand = new RelayCommand(_ => SetAllCores(true), _ => SelectedProfile != null);
        ClearAllCommand = new RelayCommand(_ => SetAllCores(false), _ => SelectedProfile != null);
        DisableSmtCommand = new RelayCommand(_ => DisableSmtThreads(), _ => SelectedProfile != null);
        DisableECoresCommand = new RelayCommand(_ => DisableEfficiencyCores(), _ => SelectedProfile != null);

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
        if (SelectedProfile == null) return;
        var profile = BuildProfileFromEditor();
        _runtime.ApplyProfileNow(profile, force: true);
    }

    private void SaveSelectedChanges()
    {
        if (SelectedProfile == null) return;

        var updated = BuildProfileFromEditor();
        string selectedId = updated.Id;
        _runtime.UpsertProfile(updated, replaceSameProcessName: false);
        _runtime.AddActivity($"Zaktualizowano profil '{updated.ProcessName}'.");

        SelectedProfile = _runtime.Profiles.FirstOrDefault(p => p.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase));
        RefreshProfiles();
    }

    private void DeleteSelected()
    {
        if (SelectedProfile == null) return;
        var profile = SelectedProfile;
        SelectedProfile = null;
        _runtime.DeleteProfile(profile);
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
