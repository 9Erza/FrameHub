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
        Subscribe();
    }

    private void Subscribe()
    {
        if (_subscribed != null)
        {
            _subscribed.DeleteConfirmationRequested -= OnDeleteConfirmationRequested;
        }

        _subscribed = DataContext as ProfilesViewModel;
        if (_subscribed != null)
        {
            _subscribed.DeleteConfirmationRequested += OnDeleteConfirmationRequested;
        }
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
