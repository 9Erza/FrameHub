using FrameHub.Core.Logging;
using FrameHub.Core.Models.SessionOptimization;
using System;
using System.Runtime.InteropServices;

namespace FrameHub.Core.Services.SessionOptimization;

public class TaskbarVisibilityService
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

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    public virtual bool HideTaskbars() => SetTaskbarVisibility(visible: false);

    public virtual bool ShowTaskbars() => SetTaskbarVisibility(visible: true);

    public virtual TaskbarVisibilityState? CaptureVisibilityState()
    {
        try
        {
            IntPtr primaryTaskbar = FindWindow("Shell_TrayWnd", null);
            var secondaryVisibility = new List<bool>();
            IntPtr secondaryTaskbar = IntPtr.Zero;
            while (true)
            {
                secondaryTaskbar = FindWindowEx(IntPtr.Zero, secondaryTaskbar, "Shell_SecondaryTrayWnd", null);
                if (secondaryTaskbar == IntPtr.Zero) break;
                secondaryVisibility.Add(IsWindowVisible(secondaryTaskbar));
            }

            if (primaryTaskbar == IntPtr.Zero && secondaryVisibility.Count == 0) return null;
            return new TaskbarVisibilityState
            {
                PrimaryTaskbarFound = primaryTaskbar != IntPtr.Zero,
                PrimaryTaskbarVisible = primaryTaskbar != IntPtr.Zero && IsWindowVisible(primaryTaskbar),
                SecondaryTaskbarsVisible = secondaryVisibility
            };
        }
        catch (Exception ex)
        {
            _logger.Warn($"Taskbar visibility inspection failed: {ex.Message}");
            return null;
        }
    }

    public virtual bool RestoreVisibilityState(TaskbarVisibilityState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        try
        {
            bool restored = true;
            IntPtr primaryTaskbar = FindWindow("Shell_TrayWnd", null);
            if (state.PrimaryTaskbarFound)
            {
                if (primaryTaskbar == IntPtr.Zero)
                {
                    restored = false;
                }
                else
                {
                    ShowWindow(primaryTaskbar, state.PrimaryTaskbarVisible ? SW_SHOW : SW_HIDE);
                    restored &= IsWindowVisible(primaryTaskbar) == state.PrimaryTaskbarVisible;
                }
            }

            IntPtr secondaryTaskbar = IntPtr.Zero;
            for (int index = 0; index < state.SecondaryTaskbarsVisible.Count; index++)
            {
                secondaryTaskbar = FindWindowEx(IntPtr.Zero, secondaryTaskbar, "Shell_SecondaryTrayWnd", null);
                if (secondaryTaskbar == IntPtr.Zero)
                {
                    restored = false;
                    break;
                }

                bool visible = state.SecondaryTaskbarsVisible[index];
                ShowWindow(secondaryTaskbar, visible ? SW_SHOW : SW_HIDE);
                restored &= IsWindowVisible(secondaryTaskbar) == visible;
            }

            return restored;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Taskbar visibility restore failed: {ex.Message}");
            return false;
        }
    }

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
