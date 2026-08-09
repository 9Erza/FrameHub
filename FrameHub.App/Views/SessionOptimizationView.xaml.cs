namespace FrameHub.App.Views;

public partial class SessionOptimizationView : System.Windows.Controls.UserControl
{
    public SessionOptimizationView()
    {
        InitializeComponent();
    }

    private void PageScrollViewer_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        PageScrollViewer.ScrollToTop();
    }
}
