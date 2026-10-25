using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.App.Views;
using FrameHub.Core.Models;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

/// <summary>
/// UI overhaul part C (Ember C8): offscreen after-screenshots. Renders the REAL
/// XAML views with REAL view-models and the production theme at a fixed
/// 1600x900 (96 dpi) in EN and PL, with SYNTHETIC fixtures only. These are
/// genuine app renders (same visual tree as on screen), not mockups; on-screen
/// verification remains QA's job.
/// </summary>
[TestClass]
public sealed class OverhaulCScreenshots
{
    public const int ShotWidth = 1552;
    public const int ShotHeight = 910;

    private string _tempDirectory = null!;
    private string _shotDirectory = null!;
    private string? _previousDataDir;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.EmberC8", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _shotDirectory = Path.Combine(Path.GetTempPath(), "FrameHub-C-Screens");
        Directory.CreateDirectory(_shotDirectory);
        _previousDataDir = Environment.GetEnvironmentVariable("FRAMEHUB_DATA_DIR");
        Environment.SetEnvironmentVariable("FRAMEHUB_DATA_DIR", _tempDirectory);
    }

    [TestCleanup]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("FRAMEHUB_DATA_DIR", _previousDataDir);
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { }
        }
    }

    [TestMethod]
    public void RenderAfterScreenshots_EnAndPl()
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
                    RenderAll(language);
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromMinutes(15)), "Screenshot render timed out.");
        if (failure is not null) Assert.Fail(failure.ToString());

        string[] shots = Directory.GetFiles(_shotDirectory, "after-*.png");
        Assert.AreEqual(20, shots.Length, "Expected 10 views x 2 languages.");
        foreach (string shot in shots)
        {
            var info = new FileInfo(shot);
            // Sparse-but-real states (e.g. Hardware OFF) compress small; a blank
            // 1552x910 frame is ~4 KB, so 8 KB separates real renders from blanks.
            Assert.IsTrue(info.Length > 8_000, $"{Path.GetFileName(shot)} suspiciously small ({info.Length} B).");
        }

        foreach ((string name, int minimum) in new[]
        {
            ("01-dashboard", 10), ("02-library", 5), ("03-session", 5), ("04-benchmarks", 5),
            ("05-processes", 10), ("06-profiles", 8), ("07-hardware", 8), ("08-logs", 5),
            ("09-settings-general", 15), ("10-settings-network", 15)
        })
        {
            foreach (string language in new[] { "en", "pl" })
            {
                string key = $"{language}-{name}";
                Assert.IsTrue(_textCounts.TryGetValue(key, out int count) && count >= minimum,
                    $"{key}: only {count} bound texts (min {minimum}) — view rendered empty.");
            }
        }
    }

    private readonly System.Collections.Generic.Dictionary<string, int> _textCounts = new();

    private void RenderAll(string language)
    {
        // QA R2 F3: the runtime MUST be born in the shot language. Its
        // constructor records startup Activity entries via Settings.Language,
        // and LocalizationService loads a separate settings snapshot — so
        // creating the runtime first and calling SetLanguage afterwards left
        // stale English history ("Recent activity", log lines) on PL shots
        // while the live app was correctly Polish.
        using var runtime = CreateRuntime(language);
        var localization = new LocalizationService(runtime.SettingsService);
        localization.SetLanguage(language);

        // E01 Dashboard (idle; async top-process preview intentionally unpumped
        // so no real process names leak into the synthetic evidence).
        using (var vm = new DashboardViewModel(localization, runtime))
        {
            vm.RefreshTexts();
            Shoot(language, "01-dashboard", new DashboardView { DataContext = vm });
        }

        // E02 Library with 2 synthetic games.
        using (var vm = CreateLibraryViewModel(localization, runtime))
        {
            vm.Reload();
            vm.RefreshTexts();
            Shoot(language, "02-library", new LibraryView { DataContext = vm });
        }

        // E03 Session idle.
        using (var vm = new SessionOptimizationViewModel(localization, runtime))
        {
            vm.RefreshTexts();
            Shoot(language, "03-session", new SessionOptimizationView { DataContext = vm });
        }

        // E04 Benchmarks (engine state honest on this machine).
        using (var vm = new BenchmarkViewModel(localization, runtime))
        {
            try
            {
                System.Threading.Tasks.Task.Run(async () =>
                {
                    await vm.RefreshGamesAsync().ConfigureAwait(false);
                    await vm.RefreshHistoryAsync().ConfigureAwait(false);
                }).GetAwaiter().GetResult();
            }
            catch { }
            vm.RefreshTexts();
            Shoot(language, "04-benchmarks", new BenchmarkView { DataContext = vm });
        }

        // E05 Processes with 100 synthetic rows.
        using (var vm = new ProcessesViewModel(localization, runtime))
        {
            // QA R1 P3: fixtures must show what a real row shows — a translated
            // priority, not a hardcoded English "Normal" on the PL screen.
            string shotPriority = PriorityService.Translate("Normal", language);
            for (int i = 0; i < 100; i++)
            {
                vm.Processes.Add(new ProcessItem
                {
                    Id = 1000 + i,
                    Name = "synthetic-" + i,
                    InstanceCount = 1,
                    CpuUsage = "1.5%",
                    RamUsageMB = "120 MB",
                    Priority = shotPriority,
                    StatusDisplay = "Available"
                });
            }
            vm.RefreshTexts();
            Shoot(language, "05-processes", new ProcessesView { DataContext = vm });
        }

        // E06 Profiles with 50 synthetic profiles.
        {
            var vm = new ProfilesViewModel(localization, runtime);
            for (int i = 0; i < 50; i++)
            {
                runtime.UpsertProfile(new ProcessProfile
                {
                    Id = "shot-" + i,
                    ProcessName = "game-" + (i % 20),
                    DisplayName = "game-" + (i % 20),
                    Priority = "Normal",
                    OptimizationMode = OptimizationMode.CpuSets,
                    AffinityMask = 0xFF,
                    IsEnabled = i % 2 == 0,
                    ApplyCoreOptimization = true,
                    ApplyPriority = true
                }, replaceSameProcessName: false);
            }
            vm.RefreshProfiles();
            vm.RefreshTexts();
            Shoot(language, "06-profiles", new ProfilesView { DataContext = vm });
        }

        // E07 Hardware OFF state (opt-in, default off).
        using (var vm = new HardwareViewModel(localization, runtime))
        {
            vm.RefreshTexts();
            Shoot(language, "07-hardware", new HardwareView { DataContext = vm });
        }

        // E08 Logs with synthetic activity (localized templates — the live app
        // localizes new entries at insert time, so fixtures must do the same;
        // hardcoded English strings leaked onto PL shots in R2).
        {
            var vm = new LogsViewModel(localization, runtime);
            runtime.Activity.Clear();
            runtime.Activity.Add(new ActivityItemViewModel { Time = "10:00", Message = string.Format(localization.T("Library.ProfileApplied"), "cs2"), Level = "Info" });
            runtime.Activity.Add(new ActivityItemViewModel { Time = "10:01", Message = string.Format(localization.T("Activity.ProfileNoProcess"), "cs2"), Level = "Warn" });
            runtime.Activity.Add(new ActivityItemViewModel { Time = "10:02", Message = string.Format(localization.T("Activity.MonitorError"), "device lost"), Level = "Error" });
            runtime.Activity.Add(new ActivityItemViewModel { Time = "10:03", Message = localization.T("Library.Ready"), Level = "Info" });
            vm.RefreshTexts();
            Shoot(language, "08-logs", new LogsView { DataContext = vm });
        }

        // E09 Settings/General + E10 Settings/Companion&Network (with DNS panel).
        // QA R2 F3: the DNS presets/compare block sits below the 910px fold,
        // so the network shot never evidenced the new read-only QOL panel.
        // Scroll the shot viewport to the DNS section before rendering — the
        // visual tree is unchanged, only the viewport moves.
        {
            var vm = new SettingsViewModel(localization, runtime);
            vm.SelectedSettingsCategoryIndex = 0;
            vm.RefreshTexts();
            Shoot(language, "09-settings-general", new SettingsView { DataContext = vm });
            vm.SelectedSettingsCategoryIndex = 3;
            Shoot(language, "10-settings-network", new SettingsView { DataContext = vm }, scrollToBottom: true);
        }
    }

    private void Shoot(string language, string name, System.Windows.Controls.UserControl view, bool scrollToBottom = false)
    {
        // QA R1 P2: a bare UserControl has no Background, so RenderTargetBitmap
        // leaves every unpainted pixel at alpha=0 (up to ~95% transparent) and the
        // PNG is unreadable on a light viewer. Paint the real shell background
        // opaquely first — still a genuine XAML render, not an edited composite.
        if (view.Background == null)
        {
            System.Windows.Media.Brush? shell = null;
            try { shell = System.Windows.Application.Current?.TryFindResource("BrushBackground") as System.Windows.Media.Brush; }
            catch { }
            view.Background = shell
                ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x12, 0x14, 0x19));
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        view.Measure(new System.Windows.Size(ShotWidth, ShotHeight));
        view.Arrange(new System.Windows.Rect(0, 0, ShotWidth, ShotHeight));
        // Required: without a pumped dispatcher, template expansion happens only
        // synchronously here. (Do NOT pump: pending async previews must stay empty
        // so no real process names leak into the synthetic evidence.)
        view.UpdateLayout();
        if (scrollToBottom && FindScrollViewer(view) is System.Windows.Controls.ScrollViewer scroller)
        {
            scroller.ScrollToBottom();
            view.UpdateLayout();
        }
        long layoutMs = sw.ElapsedMilliseconds;

        int texts = 0;
        CountTexts(view, ref texts);
        _textCounts[$"{language}-{name}"] = texts;

        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            ShotWidth, ShotHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(view);

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        string path = Path.Combine(_shotDirectory, $"after-{language}-{name}.png");
        using (var stream = File.Create(path))
        {
            encoder.Save(stream);
        }
        File.AppendAllText(
            Path.Combine(_shotDirectory, "timings.log"),
            $"{DateTime.Now:HH:mm:ss} {language}-{name} layout={layoutMs}ms total={sw.ElapsedMilliseconds}ms{Environment.NewLine}");
    }

    private static System.Windows.Controls.ScrollViewer? FindScrollViewer(System.Windows.DependencyObject root)
    {
        if (root is System.Windows.Controls.ScrollViewer viewer) return viewer;
        int children = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < children; i++)
        {
            var found = FindScrollViewer(System.Windows.Media.VisualTreeHelper.GetChild(root, i));
            if (found is not null) return found;
        }
        return null;
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

    private LibraryViewModel CreateLibraryViewModel(LocalizationService localization, AppRuntimeService runtime)
    {
        string libraryPath = Path.Combine(_tempDirectory, $"library-{Guid.NewGuid():N}.json");
        var libraryService = new LibraryService(libraryPath);
        libraryService.SaveItems(new[]
        {
            new LibraryItem
            {
                Id = "cs2",
                DisplayName = "Counter-Strike 2",
                Type = LibraryItemType.Game,
                ProcessName = "cs2",
                ExecutablePath = @"C:\Games\cs2.exe"
            },
            new LibraryItem
            {
                Id = "demo-game",
                DisplayName = "Demo Game",
                Type = LibraryItemType.Game,
                ProcessName = "demogame",
                ExecutablePath = @"C:\Games\demogame.exe"
            }
        });

        var observationProvider = new ProcessObservationSnapshotProvider(
            timeToLive: TimeSpan.Zero,
            enumerate: () => new List<ProcessObservation>(),
            clock: () => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var scanner = new ProcessScannerService(new ProcessService(), observationProvider);
        return new LibraryViewModel(localization, runtime, libraryService, scanner, new ShotFakeLaunchService());
    }

    private sealed class ShotFakeLaunchService : IAppLibraryLaunchService
    {
        public LibraryLaunchResult Launch(LibraryItem? item) => LibraryLaunchResult.Ok();
    }

    private AppRuntimeService CreateRuntime(string? language = null)
    {
        string settingsPath = Path.Combine(_tempDirectory, $"settings-{Guid.NewGuid():N}.json");
        if (!string.IsNullOrEmpty(language))
        {
            // Pre-seed so the runtime is born in the shot language (startup
            // Activity entries are recorded from Settings.Language at ctor).
            File.WriteAllText(settingsPath, $"{{\"Language\":\"{language}\"}}");
        }
        return new AppRuntimeService(settingsPath);
    }
}
