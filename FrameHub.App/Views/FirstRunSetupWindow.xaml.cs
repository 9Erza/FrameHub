using FrameHub.App.ViewModels;
using System.Windows;

namespace FrameHub.App.Views;

/// <summary>
/// First-run setup wizard window. Owned by the main window, modal, never
/// breaks startup: any failure to show it is swallowed by the caller and the
/// app continues with safe defaults. Closing via Skip (or Esc) applies the
/// recommended light configuration through the view-model.
/// </summary>
public partial class FirstRunSetupWindow : Window
{
    public FirstRunSetupWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => FinishButton.Focus();
    }

    public static void Present(Window owner, FirstRunSetupViewModel viewModel)
    {
        var window = new FirstRunSetupWindow
        {
            Owner = owner,
            DataContext = viewModel
        };
        viewModel.CloseRequested += window.Close;
        window.ShowDialog();
    }

    private async void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is FirstRunSetupViewModel viewModel)
        {
            await viewModel.SkipAsync().ConfigureAwait(true);
        }
    }
}
