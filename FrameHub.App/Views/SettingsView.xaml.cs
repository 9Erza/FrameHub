using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Input;
using FrameHub.App.ViewModels;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace FrameHub.App.Views;

public partial class SettingsView : WpfUserControl
{
    public SettingsView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateResponsiveLayout();
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
        bool singleColumn = ActualWidth < 940;
        ContentLayout.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        ContentLayout.ColumnDefinitions[1].Width = new GridLength(singleColumn ? 0 : 24);
        ContentLayout.ColumnDefinitions[2].Width = new GridLength(singleColumn ? 0 : 1, GridUnitType.Star);
        ContentLayout.RowDefinitions[1].Height = new GridLength(singleColumn ? 16 : 0);
        Grid.SetColumn(RightSettingsColumn, singleColumn ? 0 : 2);
        Grid.SetRow(RightSettingsColumn, singleColumn ? 2 : 0);
    }
}
