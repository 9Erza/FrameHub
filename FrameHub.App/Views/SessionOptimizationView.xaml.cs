namespace FrameHub.App.Views;

public partial class SessionOptimizationView : System.Windows.Controls.UserControl
{
    public SessionOptimizationView()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateResponsiveLayout();
        SizeChanged += (_, _) => UpdateResponsiveLayout();
    }

    private void PageScrollViewer_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        PageScrollViewer.ScrollToTop();
    }

    /// <summary>
    /// The game and session controls lead, followed by the preview and rules.
    /// Compact layouts preserve that order rather than squeezing both columns.
    /// </summary>
    private void UpdateResponsiveLayout()
    {
        if (PlanWorkspace == null || SessionPlanPanel == null || SessionConsolePanel == null) return;
        bool compact = ActualWidth > 0 && ActualWidth < 1080;
        PlanWorkspace.ColumnDefinitions[0].Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star);
        PlanWorkspace.ColumnDefinitions[1].Width = new System.Windows.GridLength(compact ? 0 : 16);
        PlanWorkspace.ColumnDefinitions[2].Width = new System.Windows.GridLength(compact ? 0 : 1.6, System.Windows.GridUnitType.Star);
        PlanWorkspace.RowDefinitions[1].Height = new System.Windows.GridLength(compact ? 16 : 0);
        PlanWorkspace.RowDefinitions[2].Height = new System.Windows.GridLength(compact ? 1 : 0, System.Windows.GridUnitType.Star);
        System.Windows.Controls.Grid.SetColumn(SessionConsolePanel, 0);
        System.Windows.Controls.Grid.SetRow(SessionConsolePanel, 0);
        System.Windows.Controls.Grid.SetColumn(SessionPlanPanel, compact ? 0 : 2);
        System.Windows.Controls.Grid.SetRow(SessionPlanPanel, compact ? 2 : 0);
    }
}
