using FrameHub.App;
using FrameHub.App.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

/// <summary>
/// Developer tool, not a regression test: shows the REAL shell window (off-screen), navigates
/// through the pages and saves PNG captures plus first-navigation timings. Unlike the offscreen
/// renders, a shown window gets real layout passes (DataGrid star columns, virtualization), so
/// these captures match what users see. Skipped unless FH_CAPTURE_DIR is set.
///
///   FH_CAPTURE_DIR   output folder (required)
///   FH_CAPTURE_DATA  isolated FrameHub data folder (default: FH_CAPTURE_DIR\data)
///   FH_CAPTURE_VIEWS comma-separated navigation keys (default: all pages)
///   FH_CAPTURE_SCAN  "1" runs a real Steam/Epic/Riot library scan into the isolated data first
///
///   dotnet test FrameHub.Tests/FrameHub.Tests.csproj -c Release --filter "TestCategory=DevCapture"
/// </summary>
[TestClass]
public sealed class LiveShellCapture
{
    [TestMethod]
    [TestCategory("DevCapture")]
    public void CaptureLiveShell()
    {
        string? outDir = Environment.GetEnvironmentVariable("FH_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(outDir))
        {
            Assert.Inconclusive("Developer capture tool; set FH_CAPTURE_DIR to run it.");
            return;
        }

        Directory.CreateDirectory(outDir);
        string views = Environment.GetEnvironmentVariable("FH_CAPTURE_VIEWS") ?? "Dashboard,Library,Session,Processes,Profiles,Hardware,Benchmarks,Logs,Settings";
        string dataDir = Environment.GetEnvironmentVariable("FH_CAPTURE_DATA") ?? Path.Combine(outDir, "data");
        Directory.CreateDirectory(dataDir);
        string? previousData = Environment.GetEnvironmentVariable("FRAMEHUB_DATA_DIR");
        string? previousLocal = Environment.GetEnvironmentVariable("FRAMEHUB_LOCAL_DATA_DIR");
        Environment.SetEnvironmentVariable("FRAMEHUB_DATA_DIR", dataDir);
        Environment.SetEnvironmentVariable("FRAMEHUB_LOCAL_DATA_DIR", Path.Combine(dataDir, "local"));
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current == null)
                {
                    var application = new FrameHub.App.App();
                    application.InitializeComponent();
                }

                var shell = new MainWindow
                {
                    Left = -12000,
                    Top = 0,
                    Width = 1540,
                    Height = 900,
                    ShowActivated = false,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.Manual
                };
                shell.Show();
                Pump(1500);
                var vm = (ShellViewModel)shell.DataContext;
                if (Environment.GetEnvironmentVariable("FH_CAPTURE_SCAN") == "1")
                {
                    vm.NavigateTo("Library");
                    Pump(500);
                    var library = (LibraryViewModel)vm.CurrentViewModel;
                    library.ScanAllCommand.Execute(null);
                    for (int i = 0; i < 60 && library.IsBusy; i++) Pump(500);
                    Pump(1500);
                }

                int index = 0;
                foreach (string view in views.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    index++;
                    var root = (System.Windows.FrameworkElement)shell.Content;
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    vm.NavigateTo(view);
                    root.UpdateLayout();
                    long layoutMs = stopwatch.ElapsedMilliseconds;
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    File.AppendAllText(Path.Combine(outDir, "nav-timings.txt"),
                        $"{view}: navigate+layout {layoutMs} ms, to idle {stopwatch.ElapsedMilliseconds} ms{Environment.NewLine}");
                    Pump(2500);

                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                        (int)root.ActualWidth, (int)root.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(root);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(outDir, $"live-{index:00}-{view.ToLowerInvariant()}.png"));
                    encoder.Save(stream);
                }

                shell.Close();
                ((IDisposable)vm).Dispose();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try
        {
            Assert.IsTrue(thread.Join(TimeSpan.FromMinutes(5)), "Capture timed out.");
            if (failure is not null) Assert.Fail(failure.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("FRAMEHUB_DATA_DIR", previousData);
            Environment.SetEnvironmentVariable("FRAMEHUB_LOCAL_DATA_DIR", previousLocal);
        }
    }

    private static void Pump(int milliseconds)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}
