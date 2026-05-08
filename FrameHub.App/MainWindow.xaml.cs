using FrameHub.App.ViewModels;
using System;
using System.Diagnostics;
using DrawingIcon = System.Drawing.Icon;
using DrawingSystemIcons = System.Drawing.SystemIcons;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using WinForms = System.Windows.Forms;

namespace FrameHub.App;

public partial class MainWindow : Window
{
    private WinForms.NotifyIcon? _trayIcon;
    private bool _isExitRequested;
    private bool _isHidingToTray;

    private ShellViewModel? ViewModel => DataContext as ShellViewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new ShellViewModel();
        Loaded += MainWindow_Loaded;
        StateChanged += MainWindow_StateChanged;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
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
        menu.Items.Add("Open FrameHub", null, (_, _) => ShowFromTray());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());

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
        bool startMinimized = ViewModel?.Runtime.Settings.StartMinimized == true;
        bool minimizeToTray = ViewModel?.Runtime.Settings.MinimizeToTray == true;

        if (startToTray || (startMinimized && minimizeToTray))
        {
            HideToTray();
            return;
        }

        if (startMinimizedArg || startMinimized)
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
        if (WindowState == WindowState.Minimized && ViewModel?.Runtime.Settings.MinimizeToTray == true)
        {
            HideToTray();
        }
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
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

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExitRequested && !_isHidingToTray && ViewModel?.Runtime.Settings.CloseToTray == true)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _trayIcon?.Dispose();
        _trayIcon = null;

        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnClosed(e);
    }
}
