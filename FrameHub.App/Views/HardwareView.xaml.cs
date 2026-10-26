using FrameHub.App.ViewModels;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace FrameHub.App.Views;

public partial class HardwareView : WpfUserControl
{
    private HardwareViewModel? _viewModel;

    public HardwareView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachViewModel();
        Loaded += (_, _) => AttachViewModel();
        Unloaded += (_, _) => DetachViewModel();
        SizeChanged += (_, _) => UpdateOverview();
    }

    private void AttachViewModel()
    {
        DetachViewModel();
        _viewModel = DataContext as HardwareViewModel;
        if (_viewModel != null) _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        UpdateOverview();
    }

    private void DetachViewModel()
    {
        if (_viewModel != null) _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel = null;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HardwareViewModel.IsMonitorEnabled)) UpdateOverview();
    }

    private void UpdateOverview()
    {
        if (HardwareOverview == null || HardwareOffPanel == null) return;
        bool showOff = _viewModel?.IsMonitorEnabled != true;
        bool sideBySide = showOff && (ActualWidth <= 0 || ActualWidth >= 900);
        HardwareOverview.ColumnDefinitions[0].Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star);
        HardwareOverview.ColumnDefinitions[1].Width = new System.Windows.GridLength(sideBySide ? 16 : 0);
        HardwareOverview.ColumnDefinitions[2].Width = new System.Windows.GridLength(sideBySide ? 1 : 0, System.Windows.GridUnitType.Star);
        HardwareOverview.RowDefinitions[1].Height = new System.Windows.GridLength(showOff && !sideBySide ? 16 : 0);
        System.Windows.Controls.Grid.SetColumn(HardwareOffPanel, sideBySide ? 2 : 0);
        System.Windows.Controls.Grid.SetRow(HardwareOffPanel, sideBySide ? 0 : 2);
    }
}
