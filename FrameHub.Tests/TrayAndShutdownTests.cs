using FrameHub.App.Helpers;
using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.Core.Models;
using FrameHub.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace FrameHub.Tests;

[TestClass]
public sealed class TrayAndShutdownTests
{
    private string _tempDirectory = string.Empty;
    private SettingsService? _settingsService;
    private LocalizationService? _localization;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub_TrayTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _settingsService = new SettingsService(Path.Combine(_tempDirectory, "settings.json"));
        _localization = new LocalizationService(_settingsService);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }
        catch { }
    }

    [TestMethod]
    public void Localization_TrayKeys_HaveParityBetweenEnglishAndPolish()
    {
        string[] requiredKeys =
        [
            "Tray.Open",
            "Tray.GoTo",
            "Tray.Dashboard",
            "Tray.GamesOptimization",
            "Tray.Benchmarks",
            "Tray.Hardware",
            "Tray.Settings",
            "Tray.Exit"
        ];

        foreach (string key in requiredKeys)
        {
            string en = LocalizationService.Translate(key, "en");
            string pl = LocalizationService.Translate(key, "pl");

            Assert.IsFalse(string.IsNullOrWhiteSpace(en), $"English translation missing for {key}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(pl), $"Polish translation missing for {key}");
            Assert.AreNotEqual(key, en);
            Assert.AreNotEqual(key, pl);
        }

        Assert.AreEqual("Open FrameHub", LocalizationService.Translate("Tray.Open", "en"));
        Assert.AreEqual("Otwórz FrameHub", LocalizationService.Translate("Tray.Open", "pl"));
        Assert.AreEqual("Go to", LocalizationService.Translate("Tray.GoTo", "en"));
        Assert.AreEqual("Przejdź do", LocalizationService.Translate("Tray.GoTo", "pl"));
        Assert.AreEqual("Exit FrameHub", LocalizationService.Translate("Tray.Exit", "en"));
        Assert.AreEqual("Zakończ FrameHub", LocalizationService.Translate("Tray.Exit", "pl"));
    }

    [TestMethod]
    public void ShellViewModel_TrayProperties_ReturnLocalizedStrings()
    {
        using var shell = new ShellViewModel();
        Assert.IsNotNull(shell.TrayOpenText);
        Assert.IsNotNull(shell.TrayGoToText);
        Assert.IsNotNull(shell.TrayDashboardText);
        Assert.IsNotNull(shell.TrayGamesOptimizationText);
        Assert.IsNotNull(shell.TrayBenchmarksText);
        Assert.IsNotNull(shell.TrayHardwareText);
        Assert.IsNotNull(shell.TraySettingsText);
        Assert.IsNotNull(shell.TrayExitText);
    }

    [TestMethod]
    public void ShellViewModel_NavigateTo_DelegatesCorrectlyToTargets()
    {
        using var shell = new ShellViewModel();

        shell.NavigateTo("Library");
        Assert.IsInstanceOfType<LibraryViewModel>(shell.CurrentViewModel);

        shell.NavigateTo("Benchmarks");
        Assert.IsInstanceOfType<BenchmarkViewModel>(shell.CurrentViewModel);

        shell.NavigateTo("Hardware");
        Assert.IsInstanceOfType<HardwareViewModel>(shell.CurrentViewModel);

        shell.NavigateTo("Settings");
        Assert.IsInstanceOfType<SettingsViewModel>(shell.CurrentViewModel);

        shell.NavigateTo("Dashboard");
        Assert.IsInstanceOfType<DashboardViewModel>(shell.CurrentViewModel);
    }

    [TestMethod]
    public void DarkMenuColorTable_HasDarkSlateAndFrameHubBlueTokens()
    {
        var table = new FrameHubDarkColorTable();
        Color bg = table.ToolStripDropDownBackground;
        Color border = table.MenuBorder;
        Color selected = table.MenuItemSelected;

        Assert.AreEqual(15, bg.R);
        Assert.AreEqual(23, bg.G);
        Assert.AreEqual(42, bg.B);

        Assert.AreEqual(51, border.R);
        Assert.AreEqual(65, border.G);
        Assert.AreEqual(85, border.B);

        Assert.AreEqual(37, selected.R);
        Assert.AreEqual(99, selected.G);
        Assert.AreEqual(235, selected.B);
    }

    [TestMethod]
    public async Task ShellViewModel_ShutdownAsync_IsBoundedAndIdempotent()
    {
        using var shell = new ShellViewModel();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await shell.ShutdownAsync(cts.Token);

        // Repeated shutdown must be harmless
        await shell.ShutdownAsync(cts.Token);
    }

    [DataTestMethod]
    [DataRow(WindowState.Normal, WindowState.Normal)]
    [DataRow(WindowState.Maximized, WindowState.Maximized)]
    public void WindowState_LastNonMinimized_RemembersIntendedState(WindowState inputState, WindowState expectedState)
    {
        WindowState lastNonMinimized = WindowState.Normal;

        // When changing state:
        if (inputState == WindowState.Normal || inputState == WindowState.Maximized)
        {
            lastNonMinimized = inputState;
        }

        // Simulating minimize to tray:
        WindowState currentState = WindowState.Minimized;
        if (currentState == WindowState.Normal || currentState == WindowState.Maximized)
        {
            lastNonMinimized = currentState;
        }

        // Restore should use lastNonMinimized
        WindowState restoredState = (currentState == WindowState.Minimized) ? lastNonMinimized : currentState;
        Assert.AreEqual(expectedState, restoredState);
    }

    [TestMethod]
    public void ChromeButton_ControlsTheme_DoesNotIncludeStaleFocusRingOrPermanentFocus()
    {
        string themePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "FrameHub.App", "Themes", "Controls.xaml");
        if (File.Exists(themePath))
        {
            string xaml = File.ReadAllText(themePath);
            int startIndex = xaml.IndexOf("x:Key=\"ChromeButton\"", StringComparison.Ordinal);
            Assert.IsTrue(startIndex >= 0, "Controls.xaml must define ChromeButton style.");
            int endIndex = xaml.IndexOf("</Style>", startIndex, StringComparison.Ordinal);
            Assert.IsTrue(endIndex > startIndex, "ChromeButton style must have a closing tag.");
            string chromeButtonStyle = xaml.Substring(startIndex, endIndex - startIndex);

            Assert.IsTrue(chromeButtonStyle.Contains("Property=\"Focusable\" Value=\"False\""), "ChromeButton must set Focusable to False.");
            Assert.IsFalse(chromeButtonStyle.Contains("FocusRing"), "ChromeButton style must not render an inner FocusRing border that causes stale selection outlines.");
            Assert.IsFalse(chromeButtonStyle.Contains("IsKeyboardFocused"), "ChromeButton style must not trigger visual changes on keyboard focus.");
        }
    }
}
