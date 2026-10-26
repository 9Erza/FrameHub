using FrameHub.App.Helpers;
using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.Core.Logging;
using FrameHub.Core.Models;
using FrameHub.Core.Services;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DrawingIcon = System.Drawing.Icon;
using DrawingSystemIcons = System.Drawing.SystemIcons;
using WinForms = System.Windows.Forms;

namespace FrameHub.App;

public partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    private enum ShutdownState
    {
        NotStarted,
        InProgress,
        Completed
    }

    private WinForms.NotifyIcon? _trayIcon;
    private WinForms.ToolStripMenuItem? _trayHeaderItem;
    private WinForms.ToolStripMenuItem? _trayOpenItem;
    private WinForms.ToolStripMenuItem? _trayGoToItem;
    private WinForms.ToolStripMenuItem? _trayDashboardItem;
    private WinForms.ToolStripMenuItem? _trayGamesItem;
    private WinForms.ToolStripMenuItem? _trayBenchmarksItem;
    private WinForms.ToolStripMenuItem? _trayHardwareItem;
    private WinForms.ToolStripMenuItem? _traySettingsItem;
    private WinForms.ToolStripSeparator? _traySeparatorItem;
    private WinForms.ToolStripMenuItem? _trayRestartItem;
    private WinForms.ToolStripMenuItem? _trayExitItem;

    private bool _isExitRequested;
    private bool _isHidingToTray;
    private ShutdownState _shutdownState = ShutdownState.NotStarted;
    private WindowState _lastNonMinimizedWindowState = WindowState.Normal;
    private GlobalHotkeyService? _globalHotkeyService;

    private ShellViewModel? ViewModel => DataContext as ShellViewModel;

    public MainWindow()
    {
        InitializeComponent();
        var shellViewModel = new ShellViewModel();
        DataContext = shellViewModel;
        shellViewModel.UiLanguageChanged += (_, _) => RefreshTrayTexts();
        shellViewModel.UserNotificationRequested += ShowBenchmarkNotification;
        shellViewModel.AboutRequested += ShowAboutDialog;
        shellViewModel.Settings.NormalRestartRequested += RestartApplication;
        shellViewModel.UpdateAvailableRequested += ShowUpdateDialog;
        shellViewModel.Runtime.RuntimeStateChanged += (_, _) => ApplyBenchmarkHotkeyRegistration();
        Loaded += MainWindow_Loaded;
        SourceInitialized += (_, _) =>
        {
            ApplyCurrentScreenWorkArea();
            HwndSource? source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            if (source is not null)
            {
                _globalHotkeyService = new GlobalHotkeyService(source);
                _globalHotkeyService.HotkeyPressed += async (_, _) =>
                {
                    if (ViewModel is ShellViewModel shell) await shell.HandleBenchmarkHotkeyAsync();
                };
                ApplyBenchmarkHotkeyRegistration();
            }
        };
        StateChanged += MainWindow_StateChanged;
        SizeChanged += (_, _) => UpdateResponsiveShell();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateResponsiveShell();
        InitializeTrayIcon();
        ApplyStartupWindowBehavior();
        RunAutomaticUpdateCheck();
        SignalRestartReadiness();
    }

    /// <summary>
    /// Successor side of the restart readiness handshake (QA R1 P2): when this
    /// instance was started as a cooperative-restart successor
    /// (<c>--restarted</c>), the predecessor waits for this marker instead of
    /// trusting process liveness. Written once the window is loaded (settings
    /// applied, Companion sync attempted — with bounded retry still draining in
    /// the background on port collision). Best-effort, never throws.
    /// </summary>
    private void SignalRestartReadiness()
    {
        try
        {
            bool restarted = Environment.GetCommandLineArgs()
                .Any(arg => string.Equals(arg?.Trim(), SettingsService.RestartedFlag, StringComparison.OrdinalIgnoreCase));
            if (restarted)
            {
                SettingsService.WriteSuccessorReadyMarker(Environment.ProcessId);
            }
        }
        catch { }
    }

    private void InitializeTrayIcon()
    {
        if (_trayIcon != null) return;

        DrawingIcon icon;
        try
        {
            icon = DrawingIcon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty) ?? DrawingSystemIcons.Application;
        }
        catch
        {
            icon = DrawingSystemIcons.Application;
        }

        var menu = new WinForms.ContextMenuStrip
        {
            Renderer = new FrameHubDarkMenuRenderer(),
            ShowCheckMargin = false,
            ShowImageMargin = false,
            BackColor = System.Drawing.Color.FromArgb(15, 23, 42),
            ForeColor = System.Drawing.Color.FromArgb(241, 245, 249),
            Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular)
        };

        _trayHeaderItem = new WinForms.ToolStripMenuItem($"FrameHub {ViewModel?.AppVersion ?? new AppInfo().Version}")
        {
            Enabled = false,
            Font = new System.Drawing.Font("Segoe UI", 8.5F, System.Drawing.FontStyle.Bold)
        };

        _trayOpenItem = new WinForms.ToolStripMenuItem(ViewModel?.TrayOpenText ?? "Open FrameHub", null, (_, _) => RestoreAndActivate());

        _trayGoToItem = new WinForms.ToolStripMenuItem(ViewModel?.TrayGoToText ?? "Go to");
        _trayGoToItem.DropDown.Renderer = new FrameHubDarkMenuRenderer();
        if (_trayGoToItem.DropDown is WinForms.ToolStripDropDownMenu subMenu)
        {
            subMenu.ShowCheckMargin = false;
            subMenu.ShowImageMargin = false;
        }
        _trayGoToItem.DropDown.BackColor = System.Drawing.Color.FromArgb(15, 23, 42);
        _trayGoToItem.DropDown.ForeColor = System.Drawing.Color.FromArgb(241, 245, 249);
        _trayGoToItem.DropDown.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular);

        _trayDashboardItem = new WinForms.ToolStripMenuItem(ViewModel?.TrayDashboardText ?? "Dashboard", null, (_, _) =>
        {
            RestoreAndActivate();
            ViewModel?.NavigateTo("Dashboard");
        });

        _trayGamesItem = new WinForms.ToolStripMenuItem(ViewModel?.TrayGamesOptimizationText ?? "Games & Optimization", null, (_, _) =>
        {
            RestoreAndActivate();
            ViewModel?.NavigateTo("Library");
        });

        _trayBenchmarksItem = new WinForms.ToolStripMenuItem(ViewModel?.TrayBenchmarksText ?? "Benchmarks", null, (_, _) =>
        {
            RestoreAndActivate();
            ViewModel?.NavigateTo("Benchmarks");
        });

        _trayHardwareItem = new WinForms.ToolStripMenuItem(ViewModel?.TrayHardwareText ?? "Hardware Monitor", null, (_, _) =>
        {
            RestoreAndActivate();
            ViewModel?.NavigateTo("Hardware");
        });

        _traySettingsItem = new WinForms.ToolStripMenuItem(ViewModel?.TraySettingsText ?? "Settings", null, (_, _) =>
        {
            RestoreAndActivate();
            ViewModel?.NavigateTo("Settings");
        });

        _trayGoToItem.DropDownItems.AddRange(new WinForms.ToolStripItem[]
        {
            _trayDashboardItem,
            _trayGamesItem,
            _trayBenchmarksItem,
            _trayHardwareItem,
            _traySettingsItem
        });

        _traySeparatorItem = new WinForms.ToolStripSeparator();
        _trayRestartItem = new WinForms.ToolStripMenuItem(ViewModel?.TrayRestartText ?? "Restart FrameHub", null, (_, _) => RestartApplication());
        _trayExitItem = new WinForms.ToolStripMenuItem(ViewModel?.TrayExitText ?? "Exit FrameHub", null, (_, _) => ExitApplication());

        menu.Items.Add(_trayHeaderItem);
        menu.Items.Add(_trayOpenItem);
        menu.Items.Add(_trayGoToItem);
        menu.Items.Add(_traySeparatorItem);
        menu.Items.Add(_trayRestartItem);
        menu.Items.Add(_trayExitItem);

        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = icon,
            Text = "FrameHub",
            ContextMenuStrip = menu,
            Visible = ShouldKeepTrayIconVisible()
        };

        _trayIcon.MouseClick += (_, me) =>
        {
            if (me.Button == WinForms.MouseButtons.Left)
            {
                RestoreAndActivate();
            }
        };

        _trayIcon.DoubleClick += (_, _) => RestoreAndActivate();
    }

    private void ApplyStartupWindowBehavior()
    {
        var args = Environment.GetCommandLineArgs();
        bool startToTray = args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
        bool startMinimizedArg = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        if (startToTray)
        {
            HideToTray();
            return;
        }

        if (startMinimizedArg)
        {
            WindowState = WindowState.Minimized;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch
            {
                // DragMove can throw if the mouse state changes during window chrome processing.
            }
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.Runtime.Settings.MinimizeToTray == true)
        {
            HideToTray();
            return;
        }

        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleWindowState();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.Runtime.Settings.CloseToTray == true)
        {
            HideToTray();
            return;
        }

        _isExitRequested = true;
        Close();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Normal || WindowState == WindowState.Maximized)
        {
            _lastNonMinimizedWindowState = WindowState;
        }

        if (WindowState == WindowState.Maximized)
        {
            ApplyCurrentScreenWorkArea();
        }

        if (WindowState == WindowState.Minimized && ViewModel?.Runtime.Settings.MinimizeToTray == true)
        {
            HideToTray();
        }

        RunAutomaticUpdateCheck();
    }

    private void ToggleWindowState()
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            return;
        }

        ApplyCurrentScreenWorkArea();
        WindowState = WindowState.Maximized;
    }

    private void ApplyCurrentScreenWorkArea()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var workingArea = WinForms.Screen.FromHandle(handle).WorkingArea;
            var source = PresentationSource.FromVisual(this);
            double scaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double scaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            MaxWidth = workingArea.Width / scaleX;
            MaxHeight = workingArea.Height / scaleY;
        }
        catch
        {
            MaxWidth = SystemParameters.WorkArea.Width;
            MaxHeight = SystemParameters.WorkArea.Height;
        }
    }

    private void RefreshTrayTexts()
    {
        if (_trayHeaderItem != null) _trayHeaderItem.Text = $"FrameHub {ViewModel?.AppVersion ?? new AppInfo().Version}";
        if (_trayOpenItem != null) _trayOpenItem.Text = ViewModel?.TrayOpenText ?? "Open FrameHub";
        if (_trayGoToItem != null) _trayGoToItem.Text = ViewModel?.TrayGoToText ?? "Go to";
        if (_trayDashboardItem != null) _trayDashboardItem.Text = ViewModel?.TrayDashboardText ?? "Dashboard";
        if (_trayGamesItem != null) _trayGamesItem.Text = ViewModel?.TrayGamesOptimizationText ?? "Games & Optimization";
        if (_trayBenchmarksItem != null) _trayBenchmarksItem.Text = ViewModel?.TrayBenchmarksText ?? "Benchmarks";
        if (_trayHardwareItem != null) _trayHardwareItem.Text = ViewModel?.TrayHardwareText ?? "Hardware Monitor";
        if (_traySettingsItem != null) _traySettingsItem.Text = ViewModel?.TraySettingsText ?? "Settings";
        if (_trayRestartItem != null) _trayRestartItem.Text = ViewModel?.TrayRestartText ?? "Restart FrameHub";
        if (_trayExitItem != null) _trayExitItem.Text = ViewModel?.TrayExitText ?? "Exit FrameHub";
    }

    private void ApplyBenchmarkHotkeyRegistration()
    {
        if (_globalHotkeyService is null || ViewModel is not ShellViewModel shell) return;
        var settings = shell.Runtime.Settings;
        BenchmarkHotkeyGesture? gesture = BenchmarkHotkeyGesture.FromSettings(
            settings.BenchmarkHotkeyEnabled,
            settings.BenchmarkHotkeyModifiers,
            settings.BenchmarkHotkeyVirtualKey);
        bool registered = _globalHotkeyService.UpdateRegistration(gesture);
        if (gesture is not null && !registered) shell.ReportBenchmarkHotkeyRegistrationFailure();
    }

    private void ShowAboutDialog(string title, string message)
    {
        if (ViewModel == null) return;
        // Identity block and SupporterNote stay separate: the dialog renders the
        // supporter chapter from the exact SupporterNote wording, never paraphrased.
        string identity = string.Format(
            ViewModel.Localization.T("Shell.About.Message"),
            ViewModel.AppVersion,
            ViewModel.Localization.T("Footer.AuthorLabel"),
            ViewModel.Localization.T("Footer.Website"));
        ModernDialogService.ShowAbout(
            this,
            title,
            identity,
            ViewModel.Localization.T("Shell.About.SupporterNote"),
            ViewModel.Localization.T("Footer.Support"),
            ViewModel.Settings.OpenThirdPartyNoticesText,
            () => ExternalLinkService.TryOpen(FrameHubExternalLink.Support),
            () => ViewModel.Settings.OpenThirdPartyNoticesCommand.Execute(null),
            ViewModel.Localization.T("Footer.AuthorLabel"),
            ViewModel.Localization.T("Footer.Website"),
            ViewModel.Localization.T("Footer.Repository"),
            () => { if (ViewModel.OpenAuthorCommand.CanExecute(null)) ViewModel.OpenAuthorCommand.Execute(null); },
            () => { if (ViewModel.OpenWebsiteCommand.CanExecute(null)) ViewModel.OpenWebsiteCommand.Execute(null); },
            () => { if (ViewModel.OpenRepositoryCommand.CanExecute(null)) ViewModel.OpenRepositoryCommand.Execute(null); });
    }

    private void ShowBenchmarkNotification(string message)
    {
        if (_trayIcon is null) InitializeTrayIcon();
        if (_trayIcon is null) return;
        _trayIcon.Visible = true;
        _trayIcon.ShowBalloonTip(2500, "FrameHub", message, WinForms.ToolTipIcon.Info);
    }

    /// <summary>
    /// True while the client area is narrower than 1100 DIP: the single sidebar
    /// collapses to the 64 DIP icon rail (tooltips + automation names preserved,
    /// full names available in the rail drawer). Threshold is evaluated on client
    /// width (window minus 1 DIP border each side).
    /// </summary>
    public static readonly DependencyProperty IsNarrowWindowProperty =
        DependencyProperty.Register(nameof(IsNarrowWindow), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    public bool IsNarrowWindow
    {
        get => (bool)GetValue(IsNarrowWindowProperty);
        set => SetValue(IsNarrowWindowProperty, value);
    }

    /// <summary>
    /// True while the window is too short for secondary chrome (min-window 880x460).
    /// Navigation and all actions stay visible; only decorative density adapts.
    /// </summary>
    public static readonly DependencyProperty IsShortHeightProperty =
        DependencyProperty.Register(nameof(IsShortHeight), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    public bool IsShortHeight
    {
        get => (bool)GetValue(IsShortHeightProperty);
        set => SetValue(IsShortHeightProperty, value);
    }

    private void UpdateResponsiveShell()
    {
        double clientWidth = ActualWidth > 0 ? ActualWidth - 2 : 0;
        bool compact = clientWidth > 0 && clientWidth < 1100;
        ContentHost.Margin = compact ? new Thickness(16, 16, 16, 20) : new Thickness(24, 20, 24, 24);
        IsNarrowWindow = compact;
        IsShortHeight = ActualHeight > 0 && ActualHeight < 620;
    }

    /// <summary>
    /// Rail drawer keyboard contract: Escape closes the overlay and returns focus to
    /// the rail menu button. Activation inside the drawer navigates and closes it.
    /// Focus may rest on the menu button itself (it keeps focus after the toggle
    /// click), so Escape is handled both on the popup and on the button.
    /// </summary>
    internal bool CloseRailMenuFromKeyboard()
    {
        if (DataContext is ShellViewModel shell && shell.IsRailMenuOpen)
        {
            shell.IsRailMenuOpen = false;
            RailMenuButton.Focus();
            return true;
        }

        return false;
    }

    private void RailMenuPopup_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape) return;
        if (CloseRailMenuFromKeyboard()) e.Handled = true;
    }

    private void RailMenuButton_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape) return;
        if (CloseRailMenuFromKeyboard()) e.Handled = true;
    }

    /// <summary>
    /// When the drawer opens, move keyboard focus to its first focusable item so
    /// Escape and Tab always reach the drawer instead of stranding focus on the
    /// menu button that opened it.
    /// </summary>
    private void RailMenuPopup_Opened(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(FocusFirstRailDrawerItem));
    }

    private void FocusFirstRailDrawerItem()
    {
        if (RailMenuPopup.Child is not System.Windows.DependencyObject root) return;
        var pending = new System.Collections.Generic.Queue<System.Windows.DependencyObject>();
        pending.Enqueue(root);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (current is System.Windows.UIElement element && element.Focusable && element.IsEnabled && element.Focus())
            {
                return;
            }

            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(current);
            for (int i = 0; i < count; i++)
            {
                pending.Enqueue(System.Windows.Media.VisualTreeHelper.GetChild(current, i));
            }
        }
    }

    private void HideToTray()
    {
        _isHidingToTray = true;
        if (_trayIcon != null)
        {
            _trayIcon.Visible = true;
        }

        Hide();
        WindowState = WindowState.Minimized;
        _isHidingToTray = false;
    }

    public void RestoreAndActivate()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized || WindowState != _lastNonMinimizedWindowState)
        {
            WindowState = _lastNonMinimizedWindowState;
        }

        Activate();

        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                BringWindowToTop(handle);
                SetForegroundWindow(handle);
            }
        }
        catch
        {
            // Best effort foreground activation
        }

        // Two-phase restore: Windows notification overflow flyout retains foreground capture during
        // the initial synchronous MouseClick turn. Re-assert activation on the next dispatcher turn
        // so FrameHub reliably foregrounds on the very first left click.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
        {
            try
            {
                if (!IsVisible) Show();
                if (WindowState == WindowState.Minimized || WindowState != _lastNonMinimizedWindowState)
                {
                    WindowState = _lastNonMinimizedWindowState;
                }
                Activate();
                var handle = new WindowInteropHelper(this).Handle;
                if (handle != IntPtr.Zero)
                {
                    BringWindowToTop(handle);
                    SetForegroundWindow(handle);
                }
            }
            catch
            {
                // Best effort
            }
        }));

        RunAutomaticUpdateCheck();
    }

    /// <summary>
    /// Automatic update checks run only when the main window is actually presented to the user,
    /// never during hidden tray startup or while minimized. The once-per-process gate lives in
    /// ShellViewModel/UpdateCheckSession, so repeated presentations never repeat the check.
    /// </summary>
    private void RunAutomaticUpdateCheck()
    {
        if (!IsVisible || WindowState == WindowState.Minimized) return;
        if (ViewModel is ShellViewModel shell)
        {
            _ = shell.RunAutomaticUpdateCheckIfEligibleAsync();
        }
    }

    private void ShowUpdateDialog(Core.Services.UpdateCheckResult result)
    {
        if (!IsVisible) return;
        Views.UpdateAvailableDialog.Present(this, result.LatestVersion, result.ReleaseUrl, ViewModel!.Localization);
    }

    private bool ShouldKeepTrayIconVisible()
    {
        var settings = ViewModel?.Runtime.Settings;
        return settings?.MinimizeToTray == true || settings?.CloseToTray == true || IsVisible == false;
    }

    private void ExitApplication()
    {
        _isExitRequested = true;
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
        }
        _ = PerformGracefulShutdownAsync();
    }

    /// <summary>
    /// Cooperative restart (Researcher R1): start the verified successor FIRST,
    /// then run the standard graceful shutdown and exit. Any non-success start
    /// result aborts the restart — the running instance is never exited on a
    /// failed start, and a shutdown timeout is a loud error, never a blind kill.
    /// </summary>
    private void RestartApplication()
    {
        if (ViewModel?.Runtime.SettingsService is not { } settingsService)
        {
            LoggerService.Instance.Warn("Restart unavailable: settings service is not initialized.");
            return;
        }

        string[] forwarded = SettingsService.BuildRestartForwardedArgs(
            Environment.GetCommandLineArgs().Skip(1));
        SettingsService.RestartResult result = settingsService.TryStartSuccessorProcess(forwarded);
        if (result.Status != SettingsService.RestartStatus.Started)
        {
            string reason = result.Error ?? result.Status.ToString();
            LoggerService.Instance.Warn($"Restart aborted: {reason}.");
            try { ViewModel?.Runtime.AddActivity(string.Format(ViewModel.Localization.T("Shell.Restart.Aborted"), reason), "Warn"); }
            catch { }
            return;
        }

        // Readiness handshake (QA R1 P2): the successor was started with
        // --restarted and writes its ready marker once its main window reaches
        // the loaded state (settings applied, Companion sync attempted). Wait
        // for the marker — not mere process liveness — then report the outcome.
        // A living process without a marker, or an early exit, releases the
        // single-flight so retry is a fresh attempt.
        if (result.ProcessId is int successorPid)
        {
            _ = Task.Run(() =>
            {
                bool ready = SettingsService.WaitForSuccessorReady(successorPid, TimeSpan.FromSeconds(20));
                try { SettingsService.ClearSuccessorReadyMarker(successorPid); } catch { }
                if (ready)
                {
                    LoggerService.Instance.Info($"Restart successor (PID {successorPid}) signalled ready.");
                    try { ViewModel?.Runtime.AddActivity(string.Format(ViewModel.Localization.T("Shell.Restart.SuccessorReady"), successorPid), "Info"); }
                    catch { }
                    return;
                }

                try { settingsService.ResetRestartState(); } catch { }
                if (!SettingsService.IsSuccessorAlive(successorPid, TimeSpan.Zero))
                {
                    LoggerService.Instance.Warn($"Restart successor (PID {successorPid}) exited within seconds of launch.");
                    try { ViewModel?.Runtime.AddActivity(ViewModel.Localization.T("Shell.Restart.SuccessorDied"), "Warn"); }
                    catch { }
                }
                else
                {
                    LoggerService.Instance.Warn($"Restart successor (PID {successorPid}) is alive but never signalled ready within 20 s.");
                    try { ViewModel?.Runtime.AddActivity(string.Format(ViewModel.Localization.T("Shell.Restart.SuccessorTimeout"), successorPid), "Warn"); }
                    catch { }
                }
            });
        }

        _isExitRequested = true;
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
        }
        _ = PerformGracefulShutdownAsync();
    }

    /// <summary>
    /// Total wall-clock envelope for cooperative shutdown, shared with
    /// <see cref="ShellViewModel.ShutdownAsync(CancellationToken, TimeSpan)"/>.
    /// When the deadline expires the window closes regardless (fail-safe: session
    /// WAL and recovery are protected); background disposal keeps draining.
    /// </summary>
    private static readonly TimeSpan ShutdownBudget = AppRuntimeService.DefaultShutdownBudget;

    private async Task PerformGracefulShutdownAsync()
    {
        if (_shutdownState != ShutdownState.NotStarted) return;
        _shutdownState = ShutdownState.InProgress;

        _globalHotkeyService?.Dispose();
        _globalHotkeyService = null;

        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
        }

        Hide();

        if (ViewModel is ShellViewModel shell)
        {
            using var shutdownCts = new CancellationTokenSource(ShutdownBudget);
            try
            {
                await Task.Run(async () => await shell.ShutdownAsync(shutdownCts.Token, ShutdownBudget).ConfigureAwait(false), shutdownCts.Token);
            }
            catch (OperationCanceledException)
            {
                LoggerService.Instance.Warn("Graceful application shutdown deadline exceeded.");
                RecoverRestartAfterFailedShutdown("shutdown deadline exceeded");
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn($"Error during application shutdown: {ex.Message}");
                RecoverRestartAfterFailedShutdown(ex.Message);
            }
        }

        _shutdownState = ShutdownState.Completed;
        Close();
    }

    /// <summary>
    /// QA P3.1: if a restart successor was started but this instance failed to
    /// shut down (timeout/error above), the old instance keeps running — release
    /// the restart single-flight so a retry is a fresh attempt instead of a
    /// permanent <c>AlreadyInProgress</c>, and leave user-visible feedback.
    /// Harmless on a plain exit (the process is going away anyway).
    /// </summary>
    private void RecoverRestartAfterFailedShutdown(string reason)
    {
        try
        {
            if (ViewModel?.Runtime.SettingsService is { } settingsService)
            {
                settingsService.ResetRestartState();
            }
            try { ViewModel?.Runtime.AddActivity(string.Format(ViewModel.Localization.T("Shell.Restart.Incomplete"), reason), "Warn"); }
            catch { }
        }
        catch { }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExitRequested && !_isHidingToTray && ViewModel?.Runtime.Settings.CloseToTray == true)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        if (_shutdownState == ShutdownState.NotStarted)
        {
            e.Cancel = true;
            _ = PerformGracefulShutdownAsync();
            return;
        }

        if (_shutdownState == ShutdownState.InProgress)
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _globalHotkeyService?.Dispose();
        _globalHotkeyService = null;
        _trayIcon?.Dispose();
        _trayIcon = null;

        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnClosed(e);
    }
}
