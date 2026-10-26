using FrameHub.App.ViewModels;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace FrameHub.App.Views;

public partial class ProcessesView : WpfUserControl
{
    private ProcessesViewModel? _viewModel;

    public ProcessesView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachViewModel();
        Loaded += (_, _) => AttachViewModel();
        Unloaded += (_, _) => DetachViewModel();
        SizeChanged += (_, _) => UpdateWorkspace();
        AttachViewModel();
    }

    private void AttachViewModel()
    {
        DetachViewModel();
        _viewModel = DataContext as ProcessesViewModel;
        if (_viewModel != null) _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        UpdateWorkspace();
    }

    private void DetachViewModel()
    {
        if (_viewModel != null) _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel = null;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProcessesViewModel.SelectedProcess))
        {
            UpdateWorkspace();
            if (_viewModel?.SelectedProcess != null && ActualWidth > 0 && ActualWidth < 1000)
                Dispatcher.BeginInvoke(new Action(() => ProcessInspector.BringIntoView()), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void UpdateWorkspace()
    {
        if (ProcessWorkspace == null || ProcessInspector == null) return;
        bool selected = _viewModel?.SelectedProcess != null;
        bool stacked = selected && ActualWidth > 0 && ActualWidth < 1000;
        ProcessWorkspace.Height = stacked ? Math.Max(680, ActualHeight - 120) : double.NaN;
        ProcessWorkspaceScroller.VerticalScrollBarVisibility = stacked ? System.Windows.Controls.ScrollBarVisibility.Auto : System.Windows.Controls.ScrollBarVisibility.Disabled;
        ProcessWorkspace.ColumnDefinitions[0].Width = new System.Windows.GridLength(selected && !stacked ? 1.7 : 1, System.Windows.GridUnitType.Star);
        ProcessWorkspace.ColumnDefinitions[1].Width = new System.Windows.GridLength(selected && !stacked ? 16 : 0);
        ProcessWorkspace.ColumnDefinitions[2].Width = new System.Windows.GridLength(selected && !stacked ? 1 : 0, System.Windows.GridUnitType.Star);
        ProcessWorkspace.RowDefinitions[0].Height = new System.Windows.GridLength(stacked ? 1.3 : 1, System.Windows.GridUnitType.Star);
        ProcessWorkspace.RowDefinitions[1].Height = new System.Windows.GridLength(stacked ? 12 : 0);
        ProcessWorkspace.RowDefinitions[2].Height = new System.Windows.GridLength(stacked ? 1 : 0, System.Windows.GridUnitType.Star);
        System.Windows.Controls.Grid.SetColumn(ProcessInspector, stacked ? 0 : 2);
        System.Windows.Controls.Grid.SetRow(ProcessInspector, stacked ? 2 : 0);
    }
}
