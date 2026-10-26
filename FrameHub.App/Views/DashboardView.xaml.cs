using System.Windows;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace FrameHub.App.Views;

public partial class DashboardView : WpfUserControl
{
    public DashboardView()
    {
        InitializeComponent();
        Loaded += (_, _) => { UpdateLedgerLayout(); AttachWindowListener(); };
        Unloaded += (_, _) => DetachWindowListener();
        SizeChanged += (_, _) => UpdateLedgerLayout();
    }

    private Window? _ownerWindow;

    private void AttachWindowListener()
    {
        DetachWindowListener();
        _ownerWindow = Window.GetWindow(this);
        if (_ownerWindow != null)
        {
            _ownerWindow.SizeChanged += OwnerWindow_SizeChanged;
        }
    }

    private void DetachWindowListener()
    {
        if (_ownerWindow != null)
        {
            _ownerWindow.SizeChanged -= OwnerWindow_SizeChanged;
            _ownerWindow = null;
        }
    }

    private void OwnerWindow_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateLedgerLayout();

    /// <summary>
    /// Ember Ledger responsive contract: the hero stacks vertically and the work zones
    /// collapse to a single column below 1100 DIP of client width. The shell owns the
    /// single breakpoint (<see cref="MainWindow.IsNarrowWindow"/>); this view only
    /// rearranges its own panels and never hides functionality.
    /// </summary>
    private void UpdateLedgerLayout()
    {
        if (HeroGrid == null || MainGrid == null || LeftStack == null || RightStack == null) return;
        if (HeroStatusPanel == null || HeroActionsPanel == null) return;

        bool narrow = true;
        if (Window.GetWindow(this) is MainWindow shell)
        {
            narrow = shell.IsNarrowWindow;
        }
        else if (ActualWidth > 0)
        {
            narrow = ActualWidth < 884;
        }

        if (narrow)
        {
            HeroGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            HeroGrid.ColumnDefinitions[1].Width = new GridLength(0);
            System.Windows.Controls.Grid.SetColumn(HeroStatusPanel, 0);
            System.Windows.Controls.Grid.SetRow(HeroStatusPanel, 0);
            System.Windows.Controls.Grid.SetColumn(HeroActionsPanel, 0);
            System.Windows.Controls.Grid.SetRow(HeroActionsPanel, 1);
            HeroStatusPanel.Margin = new Thickness(0);
            HeroActionsPanel.Margin = new Thickness(0, 16, 0, 0);

            MainGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            MainGrid.ColumnDefinitions[1].Width = new GridLength(0);
            MainGrid.ColumnDefinitions[2].Width = new GridLength(0);
            System.Windows.Controls.Grid.SetColumn(LeftStack, 0);
            System.Windows.Controls.Grid.SetRow(LeftStack, 0);
            System.Windows.Controls.Grid.SetColumn(RightStack, 0);
            System.Windows.Controls.Grid.SetRow(RightStack, 1);
            RightStack.Margin = new Thickness(0, 16, 0, 0);
        }
        else
        {
            HeroGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            HeroGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Auto);
            System.Windows.Controls.Grid.SetColumn(HeroStatusPanel, 0);
            System.Windows.Controls.Grid.SetRow(HeroStatusPanel, 0);
            System.Windows.Controls.Grid.SetColumn(HeroActionsPanel, 1);
            System.Windows.Controls.Grid.SetRow(HeroActionsPanel, 0);
            HeroStatusPanel.Margin = new Thickness(0, 0, 24, 0);
            HeroActionsPanel.Margin = new Thickness(0);

            MainGrid.ColumnDefinitions[0].Width = new GridLength(1.35, GridUnitType.Star);
            MainGrid.ColumnDefinitions[1].Width = new GridLength(16);
            MainGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            System.Windows.Controls.Grid.SetColumn(LeftStack, 0);
            System.Windows.Controls.Grid.SetRow(LeftStack, 0);
            System.Windows.Controls.Grid.SetColumn(RightStack, 2);
            System.Windows.Controls.Grid.SetRow(RightStack, 0);
            RightStack.Margin = new Thickness(0);
        }
    }
}
