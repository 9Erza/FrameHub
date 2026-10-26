using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

/// <summary>
/// Ember Ledger shell contract: one sidebar navigation model in three groups
/// (Gra → Narzędzia → System), explicit key order, rail drawer state owned by the
/// ShellViewModel, and navigation-only empty-state commands (no implicit mutation).
/// Existing chapter/utility projections stay untouched (ShellChapterProjectionTests).
/// </summary>
[TestClass]
public sealed class ShellSidebarGroupTests
{
    [TestMethod]
    public void SidebarGroups_CoverAllNineKeysExactlyOnceInContractOrder()
    {
        using var shell = new ShellViewModel();
        var grouped = shell.SidebarGamesItems
            .Concat(shell.SidebarToolsItems)
            .Concat(shell.SidebarSystemItems)
            .Select(item => item.Key)
            .ToArray();
        CollectionAssert.AreEqual(
            new[] { "Dashboard", "Library", "Session", "Processes", "Hardware", "Benchmarks", "Profiles", "Logs", "Settings" },
            grouped);
        CollectionAssert.AreEquivalent(shell.NavigationItems.Select(item => item.Key).ToArray(), grouped);
    }

    [TestMethod]
    public void SidebarGroupTitles_AreLocalizedAndDistinctFromUtilities()
    {
        using var shell = new ShellViewModel();
        Assert.IsFalse(string.IsNullOrWhiteSpace(shell.SidebarGamesTitle));
        Assert.IsFalse(string.IsNullOrWhiteSpace(shell.SidebarToolsTitle));
        Assert.IsFalse(string.IsNullOrWhiteSpace(shell.SidebarSystemTitle));
        Assert.AreNotEqual(shell.SidebarGamesTitle, shell.SidebarToolsTitle);
        Assert.AreNotEqual(shell.SidebarToolsTitle, shell.SidebarSystemTitle);
        Assert.IsFalse(string.IsNullOrWhiteSpace(shell.NavMenuTooltip));
    }

    [TestMethod]
    public void RailMenu_DefaultClosedToggleAndNavigateFromRailCloses()
    {
        using var shell = new ShellViewModel();
        Assert.IsFalse(shell.IsRailMenuOpen);
        shell.ToggleRailMenuCommand.Execute(null);
        Assert.IsTrue(shell.IsRailMenuOpen);
        shell.ToggleRailMenuCommand.Execute(null);
        Assert.IsFalse(shell.IsRailMenuOpen);

        shell.ToggleRailMenuCommand.Execute(null);
        shell.NavigateFromRailCommand.Execute("Library");
        Assert.IsFalse(shell.IsRailMenuOpen);
        Assert.AreEqual("Library", shell.CurrentKey);
        Assert.IsTrue(shell.NavigationItems.Single(item => item.Key == "Library").IsSelected);
    }

    [TestMethod]
    public void RailDrawer_EscapeCloses_FromPopupAndFromMenuButton()
    {
        // QA C B4: Escape did not close the rail drawer when focus rested on the
        // menu button (the PreviewKeyDown handler hung on the Popup only). Both the
        // popup handler and the button handler delegate to CloseRailMenuFromKeyboard;
        // this test drives that contract through a real MainWindow instance (STA,
        // same pattern as LedgerPrototypeRenders) and the static wiring test below
        // pins the handler delegation in XAML/code-behind.
        Exception? failure = null;
        bool closedWhenOpen = false;
        bool staysClosedWhenClosed = false;
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current == null)
                {
                    var application = new FrameHub.App.App();
                    application.InitializeComponent();
                }

                var window = new FrameHub.App.MainWindow();
                try
                {
                    var shell = (ShellViewModel)window.DataContext;
                    Assert.IsFalse(shell.IsRailMenuOpen);

                    shell.IsRailMenuOpen = true;
                    closedWhenOpen = window.CloseRailMenuFromKeyboard();
                    Assert.IsFalse(shell.IsRailMenuOpen);

                    staysClosedWhenClosed = !window.CloseRailMenuFromKeyboard();
                    Assert.IsFalse(shell.IsRailMenuOpen);
                }
                finally
                {
                    ((IDisposable)window.DataContext).Dispose();
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromMinutes(5)), "Rail drawer Escape test timed out.");
        if (failure is not null) Assert.Fail(failure.ToString());
        Assert.IsTrue(closedWhenOpen, "Escape with the drawer open must close it (QA C B4).");
        Assert.IsTrue(staysClosedWhenClosed, "Escape with the drawer closed must be a no-op (QA C B4).");
    }

    [TestMethod]
    public void RailDrawer_Wiring_FocusMovesInAndReturnsToMenuButton()
    {
        // Static contract: opening the drawer must schedule a focus move into it
        // (Popup.Opened), and closing must return focus to RailMenuButton.
        string? directory = Path.GetDirectoryName(typeof(ShellSidebarGroupTests).Assembly.Location);
        while (directory != null && !Directory.Exists(Path.Combine(directory, "FrameHub.App")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        if (directory == null) Assert.Inconclusive("Repository source root was not discoverable.");
        string xaml = File.ReadAllText(Path.Combine(directory, "FrameHub.App", "MainWindow.xaml"));
        StringAssert.Contains(xaml, "PreviewKeyDown=\"RailMenuButton_PreviewKeyDown\"",
            "RailMenuButton must handle Escape while it keeps focus (QA C B4).");
        StringAssert.Contains(xaml, "Opened=\"RailMenuPopup_Opened\"",
            "RailMenuPopup must move focus into the drawer on open (QA C B4).");
        string codeBehind = File.ReadAllText(Path.Combine(directory, "FrameHub.App", "MainWindow.xaml.cs"));
        StringAssert.Contains(codeBehind, "RailMenuButton.Focus()",
            "Closing the drawer must return focus to RailMenuButton (QA C B4).");
        StringAssert.Contains(codeBehind, "FocusFirstRailDrawerItem",
            "Opening the drawer must move focus to its first item (QA C B4).");
        StringAssert.Contains(codeBehind, "RailMenuButton_PreviewKeyDown",
            "Button-level Escape handler must exist (QA C B4).");
        StringAssert.Contains(codeBehind, "if (CloseRailMenuFromKeyboard()) e.Handled = true;",
            "Both Escape handlers must delegate to the shared close contract (QA C B4).");
    }

    [TestMethod]
    public void RailMode_FooterCollapsesAndDrawerHostsLinkCommands()
    {
        // QA C2 F1: the 5 rail footer links shared the nav lane (8 overlaps at 880x460,
        // 4 at 1024x640). The in-lane footer must collapse in rail mode and all 5 link
        // commands must stay reachable from the drawer overlay with UIA names + tooltips.
        string? directory = Path.GetDirectoryName(typeof(ShellSidebarGroupTests).Assembly.Location);
        while (directory != null && !Directory.Exists(Path.Combine(directory, "FrameHub.App")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        if (directory == null) Assert.Inconclusive("Repository source root was not discoverable.");
        string xaml = File.ReadAllText(Path.Combine(directory, "FrameHub.App", "MainWindow.xaml"));
        StringAssert.Contains(xaml, "<StackPanel Grid.Row=\"2\"",
            "Sidebar footer strip must exist for the wide layout (QA C2 F1).");
        int footerIndex = xaml.IndexOf("<StackPanel Grid.Row=\"2\"", StringComparison.Ordinal);
        int footerEnd = xaml.IndexOf("</StackPanel>", footerIndex, StringComparison.Ordinal);
        string footerStrip = xaml.Substring(footerIndex, Math.Min(footerEnd, footerIndex + 600) - footerIndex);
        StringAssert.Contains(footerStrip, "InverseBoolToVisibilityConverter",
            "Sidebar footer strip must collapse in rail mode so no nav/foot rects can overlap (QA C2 F1).");
        foreach (string command in new[] { "OpenRepositoryCommand", "OpenAuthorCommand", "OpenWebsiteCommand", "OpenSupportCommand", "OpenAboutCommand" })
        {
            Assert.IsTrue(CountOccurrences(xaml, "{Binding " + command + "}") >= 2,
                "Drawer overlay must host " + command + " alongside the wide footer (QA C2 F1: no link may be dropped).");
        }
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    [TestMethod]
    public void Dashboard_NavigateSessionCommand_NavigatesToSessionWithoutMutation()
    {
        string settingsPath = Path.Combine(Path.GetTempPath(), $"fh-ledger-{Guid.NewGuid():N}.json");
        using var runtime = new AppRuntimeService(settingsPath);
        var localization = new LocalizationService(runtime.SettingsService);
        using var vm = new DashboardViewModel(localization, runtime);
        string? navigated = null;
        vm.NavigationRequested += key => navigated = key;

        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.OpenLibraryText));
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.ConfigureSessionText));
        vm.NavigateSessionCommand.Execute(null);
        Assert.AreEqual("Session", navigated);
        Assert.IsFalse(vm.IsGamingModeActive);
    }

    [TestMethod]
    public void Library_ClearLibraryFilterCommand_ResetsSearchAndFilterOnly()
    {
        string settingsPath = Path.Combine(Path.GetTempPath(), $"fh-ledger-{Guid.NewGuid():N}.json");
        using var runtime = new AppRuntimeService(settingsPath);
        var localization = new LocalizationService(runtime.SettingsService);
        using var viewModel = new LibraryViewModel(localization, runtime);

        Assert.IsFalse(string.IsNullOrWhiteSpace(viewModel.ClearFilterText));
        viewModel.SearchText = "no-such-game-xyz";
        viewModel.SelectedFilterIndex = 1;
        viewModel.ClearLibraryFilterCommand.Execute(null);
        Assert.AreEqual(string.Empty, viewModel.SearchText);
        Assert.AreEqual(0, viewModel.SelectedFilterIndex);
    }
}
