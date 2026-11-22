using System.Configuration;
using System.Data;
using FrameHub.Core.Logging;
using FrameHub.Core.Services;
using System.Windows;
using WpfApplication = System.Windows.Application;

namespace FrameHub.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : WpfApplication
{
    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            LoggerService.Instance.Error($"Unhandled WPF Exception: {e.Exception.Message}", e.Exception);
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length > 0 && string.Equals(e.Args[0], "--startup-helper", StringComparison.OrdinalIgnoreCase))
        {
            int exitCode = SettingsService.RunStartupHelper(e.Args);
            Shutdown(exitCode);
            return;
        }

        NormalizeOwnProcessPriority(new ProcessService());
        ApplyHighContrastThemeIfNeeded();
        base.OnStartup(e);
    }

    /// <summary>
    /// Older elevated autostart tasks were registered with the Task Scheduler default
    /// priority (BELOW_NORMAL). Games launched from FrameHub inherit a BELOW_NORMAL or IDLE
    /// parent priority class, so FrameHub restores its own process to NORMAL at startup.
    /// It never raises priority above NORMAL and never touches any other process.
    /// </summary>
    internal static bool NormalizeOwnProcessPriority(ProcessService processService)
    {
        try
        {
            int pid = Environment.ProcessId;
            var current = processService.GetPriority(pid);
            if (current is not (System.Diagnostics.ProcessPriorityClass.BelowNormal or System.Diagnostics.ProcessPriorityClass.Idle))
            {
                return false;
            }

            bool changed = processService.SetPriority(pid, System.Diagnostics.ProcessPriorityClass.Normal);
            LoggerService.Instance.Info($"FrameHub started with {current} priority; restored to Normal: {changed}.");
            return changed;
        }
        catch (Exception ex)
        {
            LoggerService.Instance.Warn($"Could not normalize FrameHub process priority: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// QA R1 P2: minimal HighContrast support. When the OS reports high-contrast
    /// mode at startup, <c>Themes/HighContrast.xaml</c> is merged last so its
    /// opaque black/white/yellow values win over the Ember defaults. Checked
    /// once at startup (documented limitation: toggling HC mid-session needs an
    /// app restart — same rule as the language switch persistence).
    /// </summary>
    internal void ApplyHighContrastThemeIfNeeded()
    {
        if (!ShouldApplyHighContrastTheme()) return;
        try
        {
            var dictionary = new ResourceDictionary
            {
                Source = new Uri("Themes/HighContrast.xaml", UriKind.Relative)
            };
            Resources.MergedDictionaries.Add(dictionary);
            LoggerService.Instance.Info("High-contrast theme applied (SystemParameters.HighContrast).");
        }
        catch (Exception ex)
        {
            LoggerService.Instance.Warn($"High-contrast theme failed to load: {ex.Message}");
        }
    }

    internal static bool ShouldApplyHighContrastTheme() => SystemParameters.HighContrast;
}

