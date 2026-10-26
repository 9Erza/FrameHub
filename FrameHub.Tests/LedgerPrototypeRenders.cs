using FrameHub.App;
using FrameHub.App.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

/// <summary>
/// UI NEXT GENERATION 2.0 etap B: offscreen renders of the REAL prototype shell
/// (Ember Ledger sidebar + Dashboard + Library empty states) with REAL view-models
/// and production theme, at contract window sizes in EN and PL. Genuine XAML pixels
/// (same visual tree as on screen); on-screen window capture remains QA's job and
/// is NOT claimed here. Populated states use no fixtures at all — the profile is
/// really empty, so these are honest empty-state renders, not demo data.
/// </summary>
[TestClass]
public sealed class LedgerPrototypeRenders
{
    private static readonly (int Width, int Height)[] Sizes =
    {
        (1540, 900), (1280, 800), (1100, 700), (1024, 640), (880, 460),
    };

    private static readonly string[] Views = { "Dashboard", "Library" };

    private string _tempDirectory = null!;
    private string _shotDirectory = null!;
    private string? _previousDataDir;
    private string? _previousLocalDir;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.LedgerB", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _shotDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "hermes", "profiles", "coder", "cache", "scratch", "fh-v2-b-shots");
        Directory.CreateDirectory(_shotDirectory);
        _previousDataDir = Environment.GetEnvironmentVariable("FRAMEHUB_DATA_DIR");
        _previousLocalDir = Environment.GetEnvironmentVariable("FRAMEHUB_LOCAL_DATA_DIR");
    }

    [TestCleanup]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("FRAMEHUB_DATA_DIR", _previousDataDir);
        Environment.SetEnvironmentVariable("FRAMEHUB_LOCAL_DATA_DIR", _previousLocalDir);
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { }
        }
    }

    [TestMethod]
    public void RenderPrototypeShell_AllSizes_EnAndPl()
    {
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

                foreach (string language in new[] { "en", "pl" })
                {
                    RenderLanguage(language);
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromMinutes(15)), "Prototype render timed out.");
        if (failure is not null) Assert.Fail(failure.ToString());

        string[] shots = Directory.GetFiles(_shotDirectory, "b-shell-*.png");
        Assert.AreEqual(Sizes.Length * 2 * Views.Length, shots.Length,
            $"Expected {Sizes.Length} sizes x 2 languages x {Views.Length} views.");
        foreach (string shot in shots)
        {
            var info = new FileInfo(shot);
            Assert.IsTrue(info.Length > 8_000,
                $"{Path.GetFileName(shot)} suspiciously small ({info.Length} B).");
        }
    }

    private void RenderLanguage(string language)
    {
        // Isolated profile, born in the shot language (startup Activity entries
        // record Settings.Language at runtime construction).
        string dataDir = Path.Combine(_tempDirectory, $"data-{language}");
        string localDir = Path.Combine(_tempDirectory, $"local-{language}");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(localDir);
        File.WriteAllText(Path.Combine(dataDir, "settings.json"), $"{{\"Language\":\"{language}\"}}");
        Environment.SetEnvironmentVariable("FRAMEHUB_DATA_DIR", dataDir);
        Environment.SetEnvironmentVariable("FRAMEHUB_LOCAL_DATA_DIR", localDir);

        var shell = new MainWindow();
        try
        {
            // Offscreen a Window never connects to a presentation source, so its
            // visual children stay at zero. Render the root shell element instead —
            // it owns the entire prototype surface (titlebar, sidebar, content, status).
            var root = (System.Windows.FrameworkElement)shell.Content;
            var vm = (ShellViewModel)shell.DataContext;
            Assert.AreEqual(language == "pl" ? "Biblioteka" : "Library",
                vm.NavigationItems.Single(item => item.Key == "Library").Title);
            foreach (var (width, height) in Sizes)
            {
                // Client-width breakpoint owned by the shell (window minus 1 DIP border each side).
                shell.IsNarrowWindow = (width - 2) < 1100;
                root.Measure(new System.Windows.Size(width, height));
                root.Arrange(new System.Windows.Rect(0, 0, width, height));
                root.UpdateLayout();
                foreach (string view in Views)
                {
                    vm.NavigateTo(view);
                    root.UpdateLayout();
                    Shoot(language, view, width, height, root);
                }
            }
        }
        finally
        {
            ((IDisposable)shell.DataContext).Dispose();
        }
    }

    private void Shoot(string language, string view, int width, int height, System.Windows.FrameworkElement root)
    {
        int texts = 0;
        CountTexts(root, ref texts);
        int minimum = view == "Dashboard" ? 10 : 8;

        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(root);

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        string path = Path.Combine(_shotDirectory, $"b-shell-{view.ToLowerInvariant()}-{language}-{width}x{height}.png");
        using (var stream = File.Create(path))
        {
            encoder.Save(stream);
        }

        int visualChildren;
        try { visualChildren = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); }
        catch { visualChildren = -1; }
        Assert.IsTrue(texts >= minimum,
            $"{language}-{view}-{width}x{height}: only {texts} bound texts (min {minimum}), visualChildren={visualChildren}.");
    }

    private static void CountTexts(System.Windows.DependencyObject root, ref int count)
    {
        int children = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < children; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is System.Windows.Controls.TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text))
            {
                count++;
            }
            CountTexts(child, ref count);
        }
    }
}
