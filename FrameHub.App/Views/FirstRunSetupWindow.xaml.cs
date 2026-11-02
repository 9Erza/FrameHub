using FrameHub.App.ViewModels;
using FrameHub.Core.Logging;
using System;
using System.Windows;

namespace FrameHub.App.Views;

/// <summary>
/// First-run setup wizard window. Owned by the main window, modal, never
/// breaks startup: any failure to show it is swallowed by the caller and the
/// app continues with safe defaults. Closing via Skip (or Esc) applies the
/// recommended light configuration through the view-model.
/// Initial keyboard focus lands on the visible primary action (Next, then
/// Finish on the last step) so Tab/Enter behave predictably; Esc activates
/// the Skip button.
/// </summary>
public partial class FirstRunSetupWindow : Window
{
    private FirstRunSetupViewModel? _viewModel;

    public FirstRunSetupWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => NextButton.Focus();
        DataContextChanged += OnDataContextChanged;
        Closed += (_, _) => DetachViewModel();
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

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachViewModel();
        _viewModel = e.NewValue as FirstRunSetupViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void DetachViewModel()
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FirstRunSetupViewModel.IsLastStep))
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_viewModel?.IsLastStep == true)
                {
                    FinishButton.Focus();
                }
                else
                {
                    NextButton.Focus();
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private async void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is FirstRunSetupViewModel viewModel)
        {
            try
            {
                await viewModel.SkipAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn($"First-run skip failed: {ex.Message}");
            }
        }
    }
}
