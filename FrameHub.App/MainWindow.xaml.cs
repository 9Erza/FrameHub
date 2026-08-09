using FrameHub.App.ViewModels;
using System;
using System.Diagnostics;
using DrawingIcon = System.Drawing.Icon;
using DrawingSystemIcons = System.Drawing.SystemIcons;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WinForms = System.Windows.Forms;
using FrameHub.App.Services;

namespace FrameHub.App;

public partial class MainWindow : Window
{
    private WinForms.NotifyIcon? _trayIcon;
    private WinForms.ToolStripItem? _trayOpenItem;
    private WinForms.ToolStripItem? _trayExitItem;
    private bool _isExitRequested;
    private bool _isHidingToTray;
    private GlobalHotkeyService? _globalHotkeyService;

    private ShellViewModel? ViewModel => DataContext as ShellViewModel;

    public MainWindow()
    {
        InitializeComponent();
        var shellViewModel = new ShellViewModel();
        DataContext = shellViewModel;
        shellViewModel.UiLanguageChanged += (_, _) => RefreshTrayTexts();
        shellViewModel.UserNotificationRequested += ShowBenchmarkNotification;
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
        Tag = true;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateResponsiveShell();
        InitializeTrayIcon();
        ApplyStartupWindowBehavior();
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

        var menu = new WinForms.ContextMenuStrip();
        _trayOpenItem = menu.Items.Add(ViewModel?.TrayOpenText ?? "Open FrameHub", null, (_, _) => ShowFromTray());
        _trayExitItem = menu.Items.Add(ViewModel?.TrayExitText ?? "Exit", null, (_, _) => ExitApplication());

        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = icon,
            Text = "FrameHub",
            ContextMenuStrip = menu,
            Visible = ShouldKeepTrayIconVisible()
        };
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();
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
        if (WindowState == WindowState.Maximized)
        {
            ApplyCurrentScreenWorkArea();
        }

        if (WindowState == WindowState.Minimized && ViewModel?.Runtime.Settings.MinimizeToTray == true)
        {
            HideToTray();
        }
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
        if (_trayOpenItem != null) _trayOpenItem.Text = ViewModel?.TrayOpenText ?? "Open FrameHub";
        if (_trayExitItem != null) _trayExitItem.Text = ViewModel?.TrayExitText ?? "Exit";
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

    private void ShowBenchmarkNotification(string message)
    {
        if (_trayIcon is null) InitializeTrayIcon();
        if (_trayIcon is null) return;
        _trayIcon.Visible = true;
        _trayIcon.ShowBalloonTip(2500, "FrameHub", message, WinForms.ToolTipIcon.Info);
    }

    private void UpdateResponsiveShell()
    {
        bool compact = ActualWidth > 0 && ActualWidth < 1080;
        NavigationColumn.Width = new GridLength(224);
        NavigationPane.Padding = new Thickness(12, 18, 12, 14);
        PageHost.Margin = compact ? new Thickness(18, 20, 18, 24) : new Thickness(28, 24, 28, 28);
        Tag = true;
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

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
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
        Show();
        Close();
    }

    private bool _shutdownCompleted;

    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExitRequested && !_isHidingToTray && ViewModel?.Runtime.Settings.CloseToTray == true)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        if (!_shutdownCompleted && ViewModel is ShellViewModel shell)
        {
            e.Cancel = true;
            IsEnabled = false;
            _globalHotkeyService?.Dispose();
            _globalHotkeyService = null;
            await shell.ShutdownAsync();
            _shutdownCompleted = true;
            Close();
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
