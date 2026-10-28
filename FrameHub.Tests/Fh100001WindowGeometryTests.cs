using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

/// <summary>
/// FH-100-001 — main-window geometry / WindowChrome regression.
/// Borderless (WindowStyle=None) maximization must cover the current monitor
/// WORK area (rcWork), not the full monitor rect: without a WM_GETMINMAXINFO
/// hook WPF maximizes to the monitor bounds and the bottom ~48 px of content
/// (footer, "Przywróć sesję") hides behind the taskbar (QA P1: MAX_RECT
/// -7,-7,1934,1094 at 1920x1080/DPI100 instead of work area 1920x1032).
/// These tests pin the hook mechanism and forbid both the old manual
/// MaxWidth/MaxHeight clamp and any hardcoded geometry/magic margins.
/// Real Windows GUI + DPI matrix (100/125/150/200, mixed-DPI): NOT RUN here —
/// single host, no isolated multi-DPI rig; covered by independent QA.
/// </summary>
[TestClass]
public sealed class Fh100001WindowGeometryTests
{
    [TestMethod]
    public void MainWindowCodeBehind_HasNoManualWorkAreaClamp()
    {
        string codeBehind = ReadMainWindowCodeBehind();

        StringAssert.DoesNotMatch(codeBehind, new System.Text.RegularExpressions.Regex("ApplyCurrentScreenWorkArea"),
            "FH-100-001: manual work-area clamp must be gone; WindowChrome owns maximized geometry.");
        StringAssert.DoesNotMatch(codeBehind, new System.Text.RegularExpressions.Regex("Screen\\.FromHandle"),
            "FH-100-001: WinForms pixel work-area must not feed WPF maximize sizing (DPI/mixed-monitor hazard).");
        StringAssert.DoesNotMatch(codeBehind, new System.Text.RegularExpressions.Regex("MaxWidth\\s*="),
            "FH-100-001: no MaxWidth clamp may constrain the maximized window (1px white bleed).");
        StringAssert.DoesNotMatch(codeBehind, new System.Text.RegularExpressions.Regex("MaxHeight\\s*="),
            "FH-100-001: no MaxHeight clamp may constrain the maximized window or leak into restore bounds.");
    }

    [TestMethod]
    public void ToggleWindowState_IsTrivialMaximizeRestoreToggle()
    {
        string codeBehind = ReadMainWindowCodeBehind();
        int toggle = codeBehind.IndexOf("private void ToggleWindowState()", StringComparison.Ordinal);
        Assert.IsTrue(toggle >= 0, "ToggleWindowState must exist.");
        string body = codeBehind.Substring(toggle, Math.Min(1200, codeBehind.Length - toggle));

        StringAssert.Contains(body, "WindowState.Normal",
            "Toggle must restore to Normal (button, double-click, Win+Down path).");
        StringAssert.Contains(body, "WindowState.Maximized",
            "Toggle must maximize (button, double-click, Win+Up path).");
        Assert.IsFalse(body.Contains("WorkArea", StringComparison.Ordinal),
            "Toggle must not recompute any work area before maximizing.");
    }

    [TestMethod]
    public void StateChanged_KeepsRestoreTrackingWithoutReclamping()
    {
        string codeBehind = ReadMainWindowCodeBehind();
        int handler = codeBehind.IndexOf("private void MainWindow_StateChanged", StringComparison.Ordinal);
        Assert.IsTrue(handler >= 0, "MainWindow_StateChanged must exist.");
        int nextMethod = codeBehind.IndexOf("private void ToggleWindowState()", handler, StringComparison.Ordinal);
        string body = codeBehind.Substring(handler,
            (nextMethod > handler ? nextMethod : codeBehind.Length) - handler);

        StringAssert.Contains(body, "_lastNonMinimizedWindowState",
            "Restore-bounds tracking (Normal/Maximized memory for tray restore) must survive the fix.");
        Assert.IsFalse(body.Contains("WorkArea", StringComparison.Ordinal),
            "StateChanged must not re-apply a size clamp after the system maximize pass.");
    }

    [TestMethod]
    public void DoubleClick_RoutesThroughToggle_MaximizeConstrainedToWorkArea()
    {
        string codeBehind = ReadMainWindowCodeBehind();

        StringAssert.Contains(codeBehind, "e.ClickCount == 2",
            "Title-bar double-click maximize/restore path must stay wired.");
        Assert.IsTrue(codeBehind.Contains("ToggleWindowState()", StringComparison.Ordinal),
            "Double-click and chrome button must share the single toggle path.");
    }

    [TestMethod]
    public void MaximizedGeometry_UsesWorkAreaHook_NoClamp_NoMagicMargins()
    {
        string codeBehind = ReadMainWindowCodeBehind();

        // Mechanism: WM_GETMINMAXINFO answered from the NEAREST monitor's
        // rcWork — per-monitor, taskbar-aware, device pixels (DPI-safe).
        StringAssert.Contains(codeBehind, "WmGetMinMaxInfo",
            "FH-100-001: WM_GETMINMAXINFO must be handled to bound maximization.");
        StringAssert.Contains(codeBehind, "MonitorFromWindow",
            "FH-100-001: the hook must resolve the current (nearest) monitor, never assume primary.");
        StringAssert.Contains(codeBehind, "MonitorDefaultToNearest",
            "FH-100-001: MonitorFromWindow must use MONITOR_DEFAULTTONEAREST.");
        StringAssert.Contains(codeBehind, "GetMonitorInfo",
            "FH-100-001: the hook must read MONITORINFO for rcWork.");
        StringAssert.Contains(codeBehind, "MaxSize",
            "FH-100-001: the hook must bound ptMaxSize to the work-area size.");
        StringAssert.Contains(codeBehind, "MaxPosition",
            "FH-100-001: the hook must origin ptMaxPosition at the work-area offset.");
        StringAssert.Contains(codeBehind, "AddHook",
            "FH-100-001: the WndProc hook must be registered on the window source.");
        Assert.IsTrue(codeBehind.Contains("info.Work", StringComparison.Ordinal),
            "FH-100-001: maximized bounds must derive from rcWork (taskbar-aware), not rcMonitor.");

        // Forbid the old broken compensator and hardcoded geometry.
        StringAssert.DoesNotMatch(codeBehind, new System.Text.RegularExpressions.Regex("MaxWidth\\s*="),
            "FH-100-001: no MaxWidth clamp may constrain the maximized window.");
        StringAssert.DoesNotMatch(codeBehind, new System.Text.RegularExpressions.Regex("MaxHeight\\s*="),
            "FH-100-001: no MaxHeight clamp may constrain the maximized window or leak into restore bounds.");
        foreach (string magic in new[] { "1920", "1080", "1032", "1094" })
        {
            Assert.IsFalse(codeBehind.Contains(magic, StringComparison.Ordinal),
                $"FH-100-001: no hardcoded screen geometry ({magic}); bounds come from the live monitor work area.");
        }
    }

    [TestMethod]
    public void MainWindowXaml_WindowChromeContractIntact_NoMagicMargins()
    {
        string? root = FindRepoRoot();
        Assert.IsNotNull(root, "Repo root not found.");
        string xaml = File.ReadAllText(Path.Combine(root, "FrameHub.App", "MainWindow.xaml"));

        StringAssert.Contains(xaml, "<shell:WindowChrome",
            "WindowChrome declaration must stay the maximized-geometry owner.");
        StringAssert.Contains(xaml, "CaptionHeight=\"44\"",
            "Custom title-bar hit height must stay 44 DIP.");
        StringAssert.Contains(xaml, "ResizeBorderThickness=\"6\"",
            "Invisible resize border must stay 6 DIP (resize affordance, no dead zones).");
        StringAssert.Contains(xaml, "GlassFrameThickness=\"0\"",
            "Glass frame must stay 0 (opaque custom chrome).");
        StringAssert.Contains(xaml, "WindowStyle=\"None\"",
            "Borderless custom-chrome window style must be preserved.");
        StringAssert.Contains(xaml, "AllowsTransparency=\"False\"",
            "Opaque window must be preserved (no transparency-driven geometry shift).");
        StringAssert.Contains(xaml, "ResizeMode=\"CanResize\"",
            "User resize must stay enabled.");
        StringAssert.Contains(xaml, "BorderThickness=\"1\"",
            "Visible 1-DIP root border must stay exactly 1 (no magic-margin compensation).");
    }

    private static string ReadMainWindowCodeBehind()
    {
        string? root = FindRepoRoot();
        Assert.IsNotNull(root, "Repo root not found.");
        string path = Path.Combine(root, "FrameHub.App", "MainWindow.xaml.cs");
        Assert.IsTrue(File.Exists(path), "MainWindow.xaml.cs must exist in scope A.");
        return File.ReadAllText(path);
    }

    private static string? FindRepoRoot()
    {
        string? directory = Path.GetDirectoryName(typeof(Fh100001WindowGeometryTests).Assembly.Location);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory, "FrameHub.slnx")) ||
                File.Exists(Path.Combine(directory, "FrameHub.sln")))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }
}
