using FrameHub.App.Helpers;
using FrameHub.App.Services;
using FrameHub.Core.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;

namespace FrameHub.App.ViewModels;

public sealed class LogsViewModel : ViewModelBase
{
    private readonly LocalizationService _localization;
    private readonly AppRuntimeService _runtime;

    public string Title => _localization.T("Logs.Title");
    public string Subtitle => _localization.T("Logs.Subtitle");
    public string ActivityTitle => _localization.T("Logs.ActivityTitle");
    public string OpenFolderText => _localization.T("Logs.OpenFolder");
    public string ClearText => _localization.T("Logs.Clear");

    public ObservableCollection<ActivityItemViewModel> Activity => _runtime.Activity;

    public ICommand OpenLogFolderCommand { get; }
    public ICommand ClearActivityCommand { get; }

    public LogsViewModel(LocalizationService localization, AppRuntimeService runtime)
    {
        _localization = localization;
        _runtime = runtime;
        OpenLogFolderCommand = new RelayCommand(_ => OpenLogFolder());
        ClearActivityCommand = new RelayCommand(_ => _runtime.Activity.Clear());
    }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(ActivityTitle));
        OnPropertyChanged(nameof(OpenFolderText));
        OnPropertyChanged(nameof(ClearText));
    }

    private static void OpenLogFolder()
    {
        string path = AppPaths.UserDataDirectory;
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }
}
