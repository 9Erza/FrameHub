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

    public string Title => _localization.T("Profiles.Title");
    public string Subtitle => _localization.T("Profiles.Subtitle");
    public string SavedProfilesTitle => _localization.T("Profiles.SavedTitle");
    public string ProfileDetailsTitle => _localization.T("Profiles.DetailsTitle");
    public string ApplyButtonText => _localization.T("Profiles.Apply");
    public string DeleteButtonText => _localization.T("Profiles.Delete");
    public string EmptyText => _localization.T("Profiles.Empty");
    public string NameHeader => _localization.T("Profiles.Name");
    public string ModeHeader => _localization.T("Profiles.Mode");
    public string PriorityHeader => _localization.T("Profiles.Priority");
    public string EnabledHeader => _localization.T("Profiles.Enabled");

    public ObservableCollection<ProcessProfile> Profiles { get; } = new();

    public ICommand ApplyProfileCommand { get; }
    public ICommand DeleteProfileCommand { get; }
    public ICommand RefreshCommand { get; }

    public ProcessProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!SetProperty(ref _selectedProfile, value)) return;
            OnPropertyChanged(nameof(SelectedProfileDescription));
        }
    }

    public string SelectedProfileDescription
    {
        get
        {
            if (SelectedProfile == null) return EmptyText;
            return $"{SelectedProfile.ProcessName} · {SelectedProfile.OptimizationMode} · {PriorityService.Translate(SelectedProfile.Priority, _localization.CurrentLanguage)}";
        }
    }

    public ProfilesViewModel(LocalizationService localization, AppRuntimeService runtime)
    {
        _localization = localization;
        _runtime = runtime;
        _runtime.ProfilesChanged += (_, _) => RefreshProfiles();

        ApplyProfileCommand = new RelayCommand(_ => ApplySelected(), _ => SelectedProfile != null);
        DeleteProfileCommand = new RelayCommand(_ => DeleteSelected(), _ => SelectedProfile != null);
        RefreshCommand = new RelayCommand(_ => RefreshProfiles());

        RefreshProfiles();
    }

    public void RefreshProfiles()
    {
        Profiles.Clear();
        foreach (var profile in _runtime.Profiles.OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            profile.DisplayPriority = PriorityService.Translate(profile.Priority, _localization.CurrentLanguage);
            Profiles.Add(profile);
        }
        OnPropertyChanged(nameof(SelectedProfileDescription));
    }

    public void RefreshTexts()
    {
        foreach (var profile in Profiles)
        {
            profile.DisplayPriority = PriorityService.Translate(profile.Priority, _localization.CurrentLanguage);
        }
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(SavedProfilesTitle));
        OnPropertyChanged(nameof(ProfileDetailsTitle));
        OnPropertyChanged(nameof(ApplyButtonText));
        OnPropertyChanged(nameof(DeleteButtonText));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(NameHeader));
        OnPropertyChanged(nameof(ModeHeader));
        OnPropertyChanged(nameof(PriorityHeader));
        OnPropertyChanged(nameof(EnabledHeader));
        OnPropertyChanged(nameof(SelectedProfileDescription));
    }

    private void ApplySelected()
    {
        if (SelectedProfile == null) return;
        _runtime.ApplyProfileNow(SelectedProfile, force: true);
    }

    private void DeleteSelected()
    {
        if (SelectedProfile == null) return;
        var profile = SelectedProfile;
        SelectedProfile = null;
        _runtime.DeleteProfile(profile);
    }
}
