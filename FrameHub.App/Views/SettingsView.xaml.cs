using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
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
