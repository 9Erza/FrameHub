using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using System.Windows;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace FrameHub.App.Views;

public partial class LibraryView : WpfUserControl
{
    private LibraryViewModel? _viewModel;

    public LibraryView()
    {
        InitializeComponent();
        DataContextChanged += LibraryView_DataContextChanged;
        Loaded += (_, _) => AttachViewModel(DataContext as LibraryViewModel);
        Unloaded += (_, _) => AttachViewModel(null);
    }

    private void LibraryView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        AttachViewModel(e.NewValue as LibraryViewModel);
    }

    private void AttachViewModel(LibraryViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        if (_viewModel != null)
        {
            _viewModel.InfoDialogRequested -= ViewModel_InfoDialogRequested;
        }

        _viewModel = viewModel;

        if (_viewModel != null)
        {
            _viewModel.InfoDialogRequested += ViewModel_InfoDialogRequested;
        }
    }

    private void ViewModel_InfoDialogRequested(string title, string message)
    {
        ModernDialogService.ShowInfo(Window.GetWindow(this), title, message);
    }

    private void ScanMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.ContextMenu != null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            button.ContextMenu.IsOpen = true;
            e.Handled = true;
        }
    }
}
