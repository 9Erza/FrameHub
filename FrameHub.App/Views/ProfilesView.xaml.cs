using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.Core.Models;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfRoutedEventArgs = System.Windows.RoutedEventArgs;
using WpfUserControl = System.Windows.Controls.UserControl;
using WpfWindow = System.Windows.Window;

namespace FrameHub.App.Views;

public partial class ProfilesView : WpfUserControl
{
    private ProfilesViewModel? _subscribed;

    public ProfilesView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Subscribe();
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
        SizeChanged += (_, _) => UpdateWorkspace();
        Subscribe();
    }

    private void Subscribe()
    {
        Unsubscribe();

        _subscribed = DataContext as ProfilesViewModel;
        if (_subscribed != null)
        {
            _subscribed.DeleteConfirmationRequested += OnDeleteConfirmationRequested;
            _subscribed.PropertyChanged += OnViewModelPropertyChanged;
        }
        UpdateWorkspace();
    }

    private void Unsubscribe()
    {
        if (_subscribed == null) return;
        _subscribed.DeleteConfirmationRequested -= OnDeleteConfirmationRequested;
        _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
        _subscribed = null;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProfilesViewModel.IsProfileSelected))
        {
            UpdateWorkspace();
            if (_subscribed?.IsProfileSelected == true && ActualWidth > 0 && ActualWidth < 1000)
                Dispatcher.BeginInvoke(new Action(() => ProfileEditorPanel.BringIntoView()), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void UpdateWorkspace()
    {
        if (ProfilesWorkspace == null || ProfilesListPanel == null || ProfileEditorPanel == null) return;
        bool selected = _subscribed?.IsProfileSelected == true;
        bool stacked = selected && ActualWidth > 0 && ActualWidth < 1000;
        ProfilesWorkspace.Height = stacked ? Math.Max(680, ActualHeight - 120) : double.NaN;
        ProfilesWorkspaceScroller.VerticalScrollBarVisibility = stacked ? System.Windows.Controls.ScrollBarVisibility.Auto : System.Windows.Controls.ScrollBarVisibility.Disabled;
        ProfilesWorkspace.ColumnDefinitions[0].Width = new System.Windows.GridLength(selected && !stacked ? 1.15 : 1, System.Windows.GridUnitType.Star);
        ProfilesWorkspace.ColumnDefinitions[1].Width = new System.Windows.GridLength(selected && !stacked ? 16 : 0);
        ProfilesWorkspace.ColumnDefinitions[2].Width = new System.Windows.GridLength(selected && !stacked ? 1 : 0, System.Windows.GridUnitType.Star);
        ProfilesWorkspace.RowDefinitions[0].Height = new System.Windows.GridLength(stacked ? 1.3 : 1, System.Windows.GridUnitType.Star);
        ProfilesWorkspace.RowDefinitions[1].Height = new System.Windows.GridLength(stacked ? 12 : 0);
        ProfilesWorkspace.RowDefinitions[2].Height = new System.Windows.GridLength(stacked ? 1 : 0, System.Windows.GridUnitType.Star);
        System.Windows.Controls.Grid.SetColumn(ProfileEditorPanel, stacked ? 0 : 2);
        System.Windows.Controls.Grid.SetRow(ProfileEditorPanel, stacked ? 2 : 0);
    }

    private void OnDeleteConfirmationRequested(string processName)
    {
        if (_subscribed == null) return;
        WpfWindow? owner = WpfWindow.GetWindow(this);
        string message = string.Format(_subscribed.DeleteConfirmMessage, processName);
        if (ModernDialogService.ShowConfirm(owner, _subscribed.DeleteConfirmTitle, message, _subscribed.DeleteConfirmText, _subscribed.DeleteCancelText))
        {
            _subscribed.ConfirmDeleteSelected();
        }
    }

    private void ProfileEnabledCheckBox_Changed(object sender, WpfRoutedEventArgs e)
    {
        if (_subscribed == null) return;
        if ((sender as WpfCheckBox)?.DataContext is ProcessProfile profile)
        {
            _subscribed.ToggleProfileEnabled(profile);
        }
    }
}
