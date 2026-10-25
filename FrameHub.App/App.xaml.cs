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

        ApplyHighContrastThemeIfNeeded();
        base.OnStartup(e);
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

