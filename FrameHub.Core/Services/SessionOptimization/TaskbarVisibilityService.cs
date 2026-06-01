using FrameHub.Core.Logging;
using System;
using System.Runtime.InteropServices;

namespace FrameHub.Core.Services.SessionOptimization;

public sealed class TaskbarVisibilityService
{
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    private readonly ILogger _logger = LoggerService.Instance;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public bool HideTaskbars() => SetTaskbarVisibility(visible: false);

    public bool ShowTaskbars() => SetTaskbarVisibility(visible: true);

    private bool SetTaskbarVisibility(bool visible)
    {
        int command = visible ? SW_SHOW : SW_HIDE;
        bool anyWindowFound = false;

        try
        {
            IntPtr primaryTaskbar = FindWindow("Shell_TrayWnd", null);
            if (primaryTaskbar != IntPtr.Zero)
            {
                ShowWindow(primaryTaskbar, command);
                anyWindowFound = true;
            }

            IntPtr secondaryTaskbar = IntPtr.Zero;
            while (true)
            {
                secondaryTaskbar = FindWindowEx(IntPtr.Zero, secondaryTaskbar, "Shell_SecondaryTrayWnd", null);
                if (secondaryTaskbar == IntPtr.Zero)
                {
                    break;
                }

                ShowWindow(secondaryTaskbar, command);
                anyWindowFound = true;
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Taskbar visibility change failed: {ex.Message}");
            return false;
        }

        return anyWindowFound;
    }
}
