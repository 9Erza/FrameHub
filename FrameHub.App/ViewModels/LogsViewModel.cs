using FrameHub.App.Helpers;
using FrameHub.App.Services;
using FrameHub.Core.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;

namespace FrameHub.App.ViewModels;

public sealed class LogsViewModel : ViewModelBase
{
    private readonly LocalizationService _localization;
    private readonly AppRuntimeService _runtime;
    private readonly ICollectionView _activityView;
    private string _searchText = string.Empty;
    private int _selectedLevelFilterIndex;

    public string Title => _localization.T("Logs.Title");
    public string Subtitle => _localization.T("Logs.Subtitle");
    public string ActivityTitle => _localization.T("Logs.ActivityTitle");
    public string OpenFolderText => _localization.T("Logs.OpenFolder");
    public string ClearText => _localization.T("Logs.Clear");
    public string FilterLabel => _localization.T("Logs.FilterLabel");
    public string SearchPlaceholderText => _localization.T("Logs.SearchPlaceholder");
    public string CountText => string.Format(_localization.T("Logs.Count"), VisibleCount(), _runtime.Activity.Count);
    public string BoundedNoteText => _localization.T("Logs.BoundedNote");
    public string ClearConfirmTitle => _localization.T("Logs.ClearConfirmTitle");
    public string ClearConfirmMessage => _localization.T("Logs.ClearConfirmMessage");
    public string ClearConfirmText => _localization.T("Logs.ClearConfirm");
    public string ClearCancelText => _localization.T("Dialog.Cancel");

    /// <summary>
    /// Raised when the user clicks Clear: the view shows a confirmation dialog
    /// and calls <see cref="ConfirmClearActivity"/> only on explicit consent.
    /// </summary>
    public event Action? ClearConfirmationRequested;

    public ObservableCollection<ActivityItemViewModel> Activity => _runtime.Activity;
    public ICollectionView ActivityView => _activityView;
    public ObservableCollection<string> LevelFilterOptions { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value ?? string.Empty)) return;
            RefreshView();
        }
    }

    public int SelectedLevelFilterIndex
    {
        get => _selectedLevelFilterIndex;
        set
        {
            if (!SetProperty(ref _selectedLevelFilterIndex, value)) return;
            RefreshView();
        }
    }

    public ICommand OpenLogFolderCommand { get; }
    public ICommand ClearActivityCommand { get; }

    public LogsViewModel(LocalizationService localization, AppRuntimeService runtime)
    {
        _localization = localization;
        _runtime = runtime;
        _activityView = CollectionViewSource.GetDefaultView(Activity);
        _activityView.Filter = MatchesFilter;
        _runtime.Activity.CollectionChanged += (_, _) => RefreshView();
        RefreshLevelFilterOptions();

        OpenLogFolderCommand = new RelayCommand(_ => OpenLogFolder());
        ClearActivityCommand = new RelayCommand(_ => ClearConfirmationRequested?.Invoke());
    }

    /// <summary>Second step of the two-step clear: runs only after explicit user consent.</summary>
    public void ConfirmClearActivity()
    {
        _runtime.Activity.Clear();
        RefreshView();
    }

    /// <summary>
    /// Pure level classifier shared by the filter: Info stays Info, Warn is a
    /// warning, Error/Fatal are errors. Unknown levels are shown everywhere.
    /// </summary>
    public static int ClassifyLevel(string? level) => level switch
    {
        "Info" => 1,
        "Warn" => 2,
        "Error" or "Fatal" => 3,
        _ => 0
    };

    public void RefreshTexts()
    {
        RefreshLevelFilterOptions();
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(ActivityTitle));
        OnPropertyChanged(nameof(OpenFolderText));
        OnPropertyChanged(nameof(ClearText));
        OnPropertyChanged(nameof(FilterLabel));
        OnPropertyChanged(nameof(SearchPlaceholderText));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(BoundedNoteText));
        OnPropertyChanged(nameof(ClearConfirmTitle));
        OnPropertyChanged(nameof(ClearConfirmMessage));
        OnPropertyChanged(nameof(ClearConfirmText));
        OnPropertyChanged(nameof(ClearCancelText));
        RefreshView();
    }

    private void RefreshLevelFilterOptions()
    {
        int current = _selectedLevelFilterIndex;
        LevelFilterOptions.Clear();
        LevelFilterOptions.Add(_localization.T("Logs.LevelAll"));
        LevelFilterOptions.Add(_localization.T("Logs.LevelInfo"));
        LevelFilterOptions.Add(_localization.T("Logs.LevelWarnings"));
        LevelFilterOptions.Add(_localization.T("Logs.LevelErrors"));
        SelectedLevelFilterIndex = Math.Clamp(current, 0, LevelFilterOptions.Count - 1);
    }

    private void RefreshView()
    {
        _activityView.Refresh();
        OnPropertyChanged(nameof(CountText));
    }

    private bool MatchesFilter(object obj)
    {
        if (obj is not ActivityItemViewModel item) return false;
        if (_selectedLevelFilterIndex > 0 && ClassifyLevel(item.Level) != _selectedLevelFilterIndex) return false;
        if (string.IsNullOrWhiteSpace(_searchText)) return true;
        return item.Message.Contains(_searchText.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private int VisibleCount()
    {
        int count = 0;
        foreach (var _ in _activityView) count++;
        return count;
    }

    private static void OpenLogFolder()
    {
        string path = AppPaths.UserDataDirectory;
        System.IO.Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
    }
}
