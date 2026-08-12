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

        base.OnStartup(e);
    }
}

