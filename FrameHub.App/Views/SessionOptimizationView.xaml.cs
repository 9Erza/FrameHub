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
    /// Plan + console sit side by side at full width; below ~1080 DIP the console
    /// stacks underneath the plan instead of squeezing two panels.
    /// </summary>
    private void UpdateResponsiveLayout()
    {
        if (PlanWorkspace == null || SessionPlanPanel == null || SessionConsolePanel == null) return;
        bool compact = ActualWidth > 0 && ActualWidth < 1080;
        PlanWorkspace.ColumnDefinitions[0].Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star);
        PlanWorkspace.ColumnDefinitions[1].Width = new System.Windows.GridLength(compact ? 0 : 16);
        PlanWorkspace.ColumnDefinitions[2].Width = new System.Windows.GridLength(compact ? 0 : 1, System.Windows.GridUnitType.Star);
        PlanWorkspace.RowDefinitions[1].Height = new System.Windows.GridLength(compact ? 16 : 0);
        PlanWorkspace.RowDefinitions[2].Height = new System.Windows.GridLength(compact ? 1 : 0, System.Windows.GridUnitType.Star);
        System.Windows.Controls.Grid.SetColumn(SessionPlanPanel, 0);
        System.Windows.Controls.Grid.SetRow(SessionPlanPanel, 0);
        System.Windows.Controls.Grid.SetColumn(SessionConsolePanel, compact ? 0 : 2);
        System.Windows.Controls.Grid.SetRow(SessionConsolePanel, compact ? 2 : 0);
    }
}
