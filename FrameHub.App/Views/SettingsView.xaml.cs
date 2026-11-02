using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Input;
using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.Core.Logging;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace FrameHub.App.Views;

public partial class SettingsView : WpfUserControl
{
    public SettingsView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateResponsiveLayout();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is SettingsViewModel oldViewModel)
            {
                oldViewModel.PropertyChanged -= SettingsPropertyChanged;
                oldViewModel.ResetSettingsConfirmationRequested -= OnResetSettingsConfirmationRequested;
                oldViewModel.SetupWizardRequested -= OnSetupWizardRequested;
            }
            if (e.NewValue is SettingsViewModel newViewModel)
            {
                newViewModel.PropertyChanged += SettingsPropertyChanged;
                newViewModel.ResetSettingsConfirmationRequested += OnResetSettingsConfirmationRequested;
                newViewModel.SetupWizardRequested += OnSetupWizardRequested;
            }
            UpdateResponsiveLayout();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) Dispatcher.BeginInvoke(SettingsScroll.ScrollToTop, DispatcherPriority.Loaded);
        };
        Loaded += (_, _) =>
        {
            UpdateResponsiveLayout();
            SettingsScroll.ScrollToTop();
        };
    }

    private void SettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.SelectedSettingsCategoryIndex)) UpdateResponsiveLayout();
    }

    private void OnResetSettingsConfirmationRequested()
    {
        if (DataContext is not SettingsViewModel viewModel) return;
        Window? owner = Window.GetWindow(this);
        if (ModernDialogService.ShowConfirm(
            owner,
            viewModel.ResetConfirmTitle,
            viewModel.ResetConfirmMessage,
            viewModel.ResetConfirmText,
            viewModel.ResetCancelText))
        {
            _ = viewModel.ConfirmResetSettingsAsync();
        }
    }

    private void OnSetupWizardRequested()
    {
        if (DataContext is not SettingsViewModel viewModel) return;
        Window? owner = Window.GetWindow(this);
        if (owner == null) return;
        try
        {
            var wizard = new FirstRunSetupViewModel(viewModel.Localization, viewModel.Runtime);
            FirstRunSetupWindow.Present(owner, wizard);
        }
        catch (Exception ex)
        {
            LoggerService.Instance.Warn($"Setup wizard could not be shown from Settings: {ex.Message}");
        }
    }

    private void RecordBenchmarkHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel) return;
        viewModel.BeginBenchmarkHotkeyRecording();
        RecordBenchmarkHotkeyButton.Focus();
        Keyboard.Focus(RecordBenchmarkHotkeyButton);
    }

    private void RecordBenchmarkHotkeyButton_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel || !viewModel.IsRecordingBenchmarkHotkey) return;
        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            viewModel.CancelBenchmarkHotkeyRecording();
            return;
        }
        viewModel.TryRecordBenchmarkHotkey(key, Keyboard.Modifiers);
    }

    private void UpdateResponsiveLayout()
    {
        // Below ~1100 DIP the local index becomes a category ComboBox with full PL/EN
        // labels; the rows panel takes the full width in one scroll, never clipped.
        // Compact keeps a single star column (0) for the rows; the index column is
        // gone in this mode, so the rows must NOT be moved into a zero-width column.
        bool compact = ActualWidth > 0 && ActualWidth < 1100;
        if (SettingsIndexBorder != null) SettingsIndexBorder.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        if (CategoryCombo != null) CategoryCombo.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        bool wide = ActualWidth >= 1500 && DataContext is SettingsViewModel vm
            && (vm.IsGeneralVisible || vm.IsStartupWindowVisible || vm.IsCompanionNetworkVisible);
        SettingsColumns.ColumnDefinitions[1].Width = new GridLength(wide ? 16 : 0);
        SettingsColumns.ColumnDefinitions[2].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(RightSettingsColumn, wide ? 2 : 0);
        Grid.SetRow(RightSettingsColumn, wide ? 0 : 1);
        if (SettingsIndexLayout != null)
        {
            SettingsIndexLayout.ColumnDefinitions[0].Width = new GridLength(compact ? 1 : 240, compact ? GridUnitType.Star : GridUnitType.Pixel);
            SettingsIndexLayout.ColumnDefinitions[1].Width = new GridLength(compact ? 0 : 24);
            SettingsIndexLayout.ColumnDefinitions[2].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(SettingsRowsPanel, compact ? 0 : 2);
        }
    }
}
