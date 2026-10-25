using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using WpfUserControl = System.Windows.Controls.UserControl;
using WpfWindow = System.Windows.Window;

namespace FrameHub.App.Views;

public partial class LogsView : WpfUserControl
{
    private LogsViewModel? _subscribed;

    public LogsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Subscribe();
        Subscribe();
    }

    private void Subscribe()
    {
        if (_subscribed != null)
        {
            _subscribed.ClearConfirmationRequested -= OnClearConfirmationRequested;
        }

        _subscribed = DataContext as LogsViewModel;
        if (_subscribed != null)
        {
            _subscribed.ClearConfirmationRequested += OnClearConfirmationRequested;
        }
    }

    private void OnClearConfirmationRequested()
    {
        if (_subscribed == null) return;
        WpfWindow? owner = WpfWindow.GetWindow(this);
        string message = string.Format(_subscribed.ClearConfirmMessage, _subscribed.Activity.Count);
        if (ModernDialogService.ShowConfirm(owner, _subscribed.ClearConfirmTitle, message, _subscribed.ClearConfirmText, _subscribed.ClearCancelText))
        {
            _subscribed.ConfirmClearActivity();
        }
    }
}
