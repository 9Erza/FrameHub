using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using System.Windows;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace FrameHub.App.Views;

public partial class LibraryView : WpfUserControl
{
    private LibraryViewModel? _viewModel;

    public LibraryView()
    {
        InitializeComponent();
        DataContextChanged += LibraryView_DataContextChanged;
        Loaded += (_, _) => { AttachViewModel(DataContext as LibraryViewModel); UpdateInspectorLayout(); UpdateEmptyLayout(); };
        Unloaded += (_, _) => AttachViewModel(null);
        SizeChanged += (_, _) => { UpdateInspectorLayout(); UpdateEmptyLayout(); };
    }

    private void LibraryView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        AttachViewModel(e.NewValue as LibraryViewModel);
    }

    private void AttachViewModel(LibraryViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        if (_viewModel != null)
        {
            _viewModel.InfoDialogRequested -= ViewModel_InfoDialogRequested;
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        _viewModel = viewModel;

        if (_viewModel != null)
        {
            _viewModel.InfoDialogRequested += ViewModel_InfoDialogRequested;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        UpdateInspectorLayout();
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LibraryViewModel.ShowSelectedDetail) or nameof(LibraryViewModel.ShowLibraryHome))
        {
            UpdateInspectorLayout();
        }
        if (e.PropertyName is nameof(LibraryViewModel.ShowManageLibrary) && _viewModel?.ShowManageLibrary == true)
        {
            BringManageFormIntoView();
        }
        if (e.PropertyName is nameof(LibraryViewModel.HasLibraryItems))
        {
            UpdateEmptyLayout();
        }
    }

    /// <summary>
    /// W5 (QA C runda 4): at narrow widths the manage form expands above the
    /// viewport with no visible feedback. Scroll it into view after layout
    /// settles; no focus theft, no command or state change.
    /// </summary>
    private void BringManageFormIntoView()
    {
        if (ManageLibraryExpander == null) return;
        Dispatcher.BeginInvoke(new Action(() => ManageLibraryExpander.BringIntoView()),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Catalog + inspector sit side by side at >= 1200 DIP with a 320+ DIP inspector;
    /// below that the inspector stacks underneath the catalog (detail layout, never squeezed).
    /// With no selection the catalog takes the full width — no dead inspector column.
    /// Filter/search state is untouched by layout; Back keeps its existing deselect semantics.
    /// </summary>
    private void UpdateInspectorLayout()
    {
        if (CatalogWorkspace == null || LibraryCatalogPanel == null || LibraryInspectorPanel == null) return;
        bool sideBySide = ActualWidth <= 0 || ActualWidth >= 1200;
        sideBySide = sideBySide && (_viewModel?.ShowSelectedDetail == true);
        CatalogWorkspace.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        CatalogWorkspace.ColumnDefinitions[1].Width = new GridLength(sideBySide ? 16 : 0);
        CatalogWorkspace.ColumnDefinitions[2].Width = sideBySide
            ? new GridLength(400, GridUnitType.Pixel)
            : new GridLength(0);
        CatalogWorkspace.RowDefinitions[1].Height = new GridLength(sideBySide ? 0 : 16);
        CatalogWorkspace.RowDefinitions[2].Height = new GridLength(sideBySide ? 0 : 1, GridUnitType.Star);
        System.Windows.Controls.Grid.SetColumn(LibraryCatalogPanel, 0);
        System.Windows.Controls.Grid.SetRow(LibraryCatalogPanel, 0);
        System.Windows.Controls.Grid.SetColumn(LibraryInspectorPanel, sideBySide ? 2 : 0);
        System.Windows.Controls.Grid.SetRow(LibraryInspectorPanel, sideBySide ? 0 : 2);
    }

    /// <summary>
    /// Empty-state zones sit side by side at >= 980 DIP of view width (welcome ~58%,
    /// sources panel ~42%) and stack vertically below it. The stacked layout uses
    /// three rows: welcome (Auto) + 16-DIP spacer + sources (Auto), so the sources
    /// panel always gets its own properly sized row and is never placed inside the
    /// spacer row (B1: previously two rows shared row 1 for spacer and panel,
    /// clipping the panel at narrow widths). Same elements, gaps 16.
    /// Filter/search state is untouched by layout; the zones only show while the
    /// library itself is empty (see the XAML visibility bindings).
    /// QA E F2: in the compact empty state the chrome above the cards is tightened
    /// (subtitle collapsed, smaller gaps, decorative glyph hidden) so the empty
    /// headline and the first CTA render inside the first viewport.
    /// </summary>
    private void UpdateEmptyLayout()
    {
        if (LibraryEmptyZones == null || LibraryWelcomePanel == null || LibrarySourcesPanel == null) return;
        bool sideBySide = ActualWidth <= 0 || ActualWidth >= 980;
        LibraryEmptyZones.ColumnDefinitions[0].Width = new GridLength(sideBySide ? 1.38 : 1, GridUnitType.Star);
        LibraryEmptyZones.ColumnDefinitions[1].Width = new GridLength(sideBySide ? 16 : 0);
        LibraryEmptyZones.ColumnDefinitions[2].Width = sideBySide
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
        LibraryEmptyZones.RowDefinitions[1].Height = new GridLength(sideBySide ? 0 : 16);
        LibraryEmptyZones.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Auto);
        System.Windows.Controls.Grid.SetColumn(LibraryWelcomePanel, 0);
        System.Windows.Controls.Grid.SetRow(LibraryWelcomePanel, 0);
        System.Windows.Controls.Grid.SetColumn(LibrarySourcesPanel, sideBySide ? 2 : 0);
        System.Windows.Controls.Grid.SetRow(LibrarySourcesPanel, sideBySide ? 0 : 2);

        bool compactEmpty = !sideBySide && _viewModel?.HasLibraryItems == false;
        if (LibrarySubtitle != null) LibrarySubtitle.Visibility = compactEmpty ? Visibility.Collapsed : Visibility.Visible;
        if (LibraryTopRow != null) LibraryTopRow.Margin = new Thickness(0, 0, 0, compactEmpty ? 8 : 16);
        if (CatalogWorkspace != null) CatalogWorkspace.Margin = new Thickness(0, sideBySide ? 16 : 8, 0, 0);
        if (ManageLibraryExpander != null) ManageLibraryExpander.Margin = new Thickness(0, 0, 0, compactEmpty ? 8 : 16);
        if (WelcomeGlyph != null) WelcomeGlyph.Visibility = compactEmpty ? Visibility.Collapsed : Visibility.Visible;
        // Panel title stays for populated libraries and wide empty states (context);
        // only the compact empty state hides it so the welcome card leads (QA E F2).
        if (LibraryItemsHeader != null) LibraryItemsHeader.Visibility = compactEmpty ? Visibility.Collapsed : Visibility.Visible;
        if (LibraryWelcomePanel != null) LibraryWelcomePanel.Padding = compactEmpty ? new Thickness(12) : new Thickness(20);
        if (WelcomeScanButton != null) WelcomeScanButton.Margin = compactEmpty ? new Thickness(0, 8, 0, 0) : new Thickness(0, 14, 0, 0);
        if (WelcomeSecondaryRow != null) WelcomeSecondaryRow.Margin = compactEmpty ? new Thickness(0, 6, 0, 0) : new Thickness(0, 8, 0, 0);
    }

    private void ViewModel_InfoDialogRequested(string title, string message)
    {
        ModernDialogService.ShowInfo(Window.GetWindow(this), title, message);
    }

    private void ScanMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.ContextMenu != null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            button.ContextMenu.IsOpen = true;
            e.Handled = true;
        }
    }
}
