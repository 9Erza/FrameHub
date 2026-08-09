using WpfUserControl = System.Windows.Controls.UserControl;

namespace FrameHub.App.Views;

public partial class BenchmarkView : WpfUserControl
{
    public BenchmarkView()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateResponsiveLayout();
        SizeChanged += (_, _) => UpdateResponsiveLayout();
    }

    private void UpdateResponsiveLayout()
    {
        bool compact = ActualWidth > 0 && ActualWidth < 940;

        CaptureWorkspace.ColumnDefinitions[0].Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star);
        CaptureWorkspace.ColumnDefinitions[1].Width = new System.Windows.GridLength(compact ? 0 : 16);
        CaptureWorkspace.ColumnDefinitions[2].Width = new System.Windows.GridLength(compact ? 0 : 1, System.Windows.GridUnitType.Star);
        CaptureWorkspace.RowDefinitions[1].Height = new System.Windows.GridLength(compact ? 14 : 0);
        System.Windows.Controls.Grid.SetColumn(CaptureStatusPanel, compact ? 0 : 2);
        System.Windows.Controls.Grid.SetRow(CaptureStatusPanel, compact ? 2 : 0);

        HistoryWorkspace.ColumnDefinitions[0].Width = compact ? new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) : new System.Windows.GridLength(350);
        HistoryWorkspace.ColumnDefinitions[1].Width = new System.Windows.GridLength(compact ? 0 : 16);
        HistoryWorkspace.ColumnDefinitions[2].Width = new System.Windows.GridLength(compact ? 0 : 1, System.Windows.GridUnitType.Star);
        HistoryWorkspace.RowDefinitions[0].Height = compact ? new System.Windows.GridLength(340) : new System.Windows.GridLength(1, System.Windows.GridUnitType.Star);
        HistoryWorkspace.RowDefinitions[1].Height = new System.Windows.GridLength(compact ? 14 : 0);
        HistoryWorkspace.RowDefinitions[2].Height = compact ? new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) : new System.Windows.GridLength(0);
        System.Windows.Controls.Grid.SetColumn(HistoryDetailPanel, compact ? 0 : 2);
        System.Windows.Controls.Grid.SetRow(HistoryDetailPanel, compact ? 2 : 0);
    }
}
