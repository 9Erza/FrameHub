using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.App.ViewModels.Benchmark;
using FrameHub.Core.Models;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Benchmarking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

/// <summary>
/// Premium PAKIET A1 / dyrektywa §D (Researcher R3): eksport porównania do
/// CSV/Markdown. Czysty konsument istniejących usług (zero zmian matematyki);
/// granica Free/Premium jest addytywna — eksport nie gate'uje żadnej
/// dotychczasowej funkcji, nie ma licencji/sond/DRM. Testy pinują determinizm
/// (re-eksport bajt-identyczny) i dokładny kształt dokumentów (golden).
/// </summary>
[TestClass]
public sealed class ComparisonExportTests
{
    private static readonly Guid SessionAId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SessionBId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime StartA = new(2026, 10, 20, 18, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime StartB = new(2026, 10, 21, 18, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void ExportCsv_ProducesStableSchemaWithDeltas()
    {
        string csv = BenchmarkComparisonExportService.ExportCsv(Entry(SessionAId, StartA, 120), Entry(SessionBId, StartB, 90));

        string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.AreEqual("metric,session_a,session_b,delta,delta_pct,direction", lines[0].TrimEnd('\r'));
        Assert.AreEqual("average_fps,120,90,-30,-25,higher_is_better", lines[1].TrimEnd('\r'));
        Assert.AreEqual(12, lines.Length, "Header + 11 metric rows in Compare() order.");
        Assert.AreEqual("analyzed_duration,60,60,0,0,neutral", lines[10].TrimEnd('\r'));
        Assert.AreEqual("dropped_count,n/a,n/a,n/a,n/a,lower_is_better", lines[11].TrimEnd('\r'));
    }

    [TestMethod]
    public void Export_Reexport_IsByteIdentical()
    {
        var first = Entry(SessionAId, StartA, 120);
        var second = Entry(SessionBId, StartB, 90);

        Assert.AreEqual(
            BenchmarkComparisonExportService.ExportCsv(first, second),
            BenchmarkComparisonExportService.ExportCsv(Entry(SessionAId, StartA, 120), Entry(SessionBId, StartB, 90)));
        Assert.AreEqual(
            BenchmarkComparisonExportService.ExportMarkdown(first, second),
            BenchmarkComparisonExportService.ExportMarkdown(Entry(SessionAId, StartA, 120), Entry(SessionBId, StartB, 90)));
    }

    [TestMethod]
    public void ExportMarkdown_ContainsSessionsMetricsAndEnvironment()
    {
        string markdown = BenchmarkComparisonExportService.ExportMarkdown(
            Entry(SessionAId, StartA, 120, gpuDriver: "32.0.15.6094"),
            Entry(SessionBId, StartB, 90, gpuDriver: "33.0.15.5224"));

        StringAssert.Contains(markdown, "# Benchmark comparison");
        StringAssert.Contains(markdown, "Game @ 2026-10-20T18:00:00Z (11111111-1111-1111-1111-111111111111)");
        StringAssert.Contains(markdown, "Game @ 2026-10-21T18:00:00Z (22222222-2222-2222-2222-222222222222)");
        StringAssert.Contains(markdown, "| average_fps | 120 | 90 | -30 | -25 | higher_is_better |");
        StringAssert.Contains(markdown, "| gpu_driver | 32.0.15.6094 | 33.0.15.5224 |");
    }

    [TestMethod]
    public void ExportMarkdown_IdenticalEnvironments_ReportsNoDifferences()
    {
        string markdown = BenchmarkComparisonExportService.ExportMarkdown(
            Entry(SessionAId, StartA, 120), Entry(SessionBId, StartB, 90));

        StringAssert.Contains(markdown, "_No environment differences._");
    }

    [TestMethod]
    public void Export_MissingSummaries_UsesNotAvailableCells()
    {
        var first = new BenchmarkHistoryEntry
        {
            SessionDirectory = SessionAId.ToString("N"),
            Metadata = Entry(SessionAId, StartA, 120).Metadata,
            Summary = null
        };
        string csv = BenchmarkComparisonExportService.ExportCsv(first, Entry(SessionBId, StartB, 90));

        StringAssert.Contains(csv, "average_fps,n/a,90,n/a,n/a,higher_is_better");
    }

    [TestMethod]
    public void Export_DifferentGames_ThrowsBenchmarkException()
    {
        var first = Entry(SessionAId, StartA, 120);
        var second = new BenchmarkHistoryEntry
        {
            SessionDirectory = SessionBId.ToString("N"),
            Metadata = new BenchmarkSessionMetadata
            {
                SessionId = SessionBId,
                FrameHubVersion = "0.7.0",
                Game = new BenchmarkTarget { LibraryItemId = "other", DisplayName = "Other", LibrarySource = "Manual" },
                Status = BenchmarkSessionStatus.Completed,
                StartUtc = StartB
            },
            Summary = Entry(SessionBId, StartB, 90).Summary
        };

        Assert.ThrowsException<BenchmarkException>(() =>
            BenchmarkComparisonExportService.ExportCsv(first, second));
    }

    [TestMethod]
    public void CanExportComparison_ReflectsSelectionValidity()
    {
        using var vm = CreateComparisonVm();
        Assert.IsFalse(vm.CanExportComparison, "Nothing selected: export must be disabled.");

        vm.ComparisonA = Item(Entry(SessionAId, StartA, 120));
        Assert.IsFalse(vm.CanExportComparison, "Single session: export must be disabled.");
        vm.ComparisonB = Item(Entry(SessionBId, StartB, 90));
        Assert.IsTrue(vm.CanExportComparison, "Two completed same-game sessions: export must be enabled.");
        Assert.IsTrue(vm.ExportComparisonCommand.CanExecute("csv"));
        Assert.IsTrue(vm.ExportComparisonCommand.CanExecute("md"));

        vm.ComparisonB = Item(Entry(SessionAId, StartA, 120));
        Assert.IsFalse(vm.CanExportComparison, "Same session twice: export must be disabled.");
    }

    [TestMethod]
    public void ExportTexts_HaveEnglishAndPolishParity()
    {
        Assert.AreEqual("Export comparison (CSV)", LocalizationService.Translate("Benchmark.Export.ComparisonCsv", "en"));
        Assert.AreEqual("Eksportuj porównanie (CSV)", LocalizationService.Translate("Benchmark.Export.ComparisonCsv", "pl"));
        Assert.AreEqual("Export comparison (Markdown)", LocalizationService.Translate("Benchmark.Export.ComparisonMarkdown", "en"));
        Assert.AreEqual("Eksportuj porównanie (Markdown)", LocalizationService.Translate("Benchmark.Export.ComparisonMarkdown", "pl"));
        Assert.AreEqual("The comparison report could not be exported.", LocalizationService.Translate("Benchmark.Error.Export", "en"));
        Assert.AreEqual("Nie udało się wyeksportować raportu porównania.", LocalizationService.Translate("Benchmark.Error.Export", "pl"));

        using var vm = CreateComparisonVm();
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.ExportComparisonCsvText));
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.ExportComparisonMarkdownText));
    }

    private BenchmarkViewModel CreateComparisonVm()
    {
        var localization = new LocalizationService(new SettingsService());
        var storage = new BenchmarkStorageService(Path.Combine(Path.GetTempPath(), "FrameHubExportProbe", Guid.NewGuid().ToString("N")));
        return new BenchmarkViewModel(localization, new ComparisonRuntime(), storage: storage, engineProbe: () => (false, null, null));
    }

    private static BenchmarkHistoryItemViewModel Item(BenchmarkHistoryEntry entry) =>
        new(entry, new LocalizationService(new SettingsService()));

    private sealed class ComparisonRuntime : IBenchmarkRuntimeContext
    {
        public AppSettings Settings { get; } = new();
        public List<ProcessProfile> Profiles { get; } = [];
        public string? LastAppliedProfile => null;
        public IBenchmarkCaptureCoordinator BenchmarkCoordinator { get; } = new BenchmarkCaptureCoordinator();
        public void AddActivity(string message, string level = "Info") { }
    }

    private static BenchmarkHistoryEntry Entry(Guid sessionId, DateTime startUtc, double averageFps, string gpuDriver = "32.0.15.6094") => new()
    {
        SessionDirectory = sessionId.ToString("N"),
        Metadata = new BenchmarkSessionMetadata
        {
            SessionId = sessionId,
            FrameHubVersion = "0.7.0",
            Game = new BenchmarkTarget { LibraryItemId = "game", DisplayName = "Game", LibrarySource = "Manual" },
            Status = BenchmarkSessionStatus.Completed,
            StartUtc = startUtc,
            Environment = new BenchmarkEnvironmentSnapshot
            {
                OsDescription = "Microsoft Windows 10.0.26100",
                OsBuild = "26100",
                CpuName = "AMD Ryzen 9 7950X",
                GpuName = "NVIDIA GeForce RTX 4080",
                GpuDriverVersion = gpuDriver,
                TotalMemoryBytes = 34_359_738_368UL,
                DisplayWidth = 2560,
                DisplayHeight = 1440,
                DisplayRefreshRateHz = 165
            }
        },
        Summary = new BenchmarkSummary
        {
            AnalyzedDurationSeconds = 60,
            PrimaryPresentedMetrics = new BenchmarkMetricSet { AverageFps = averageFps }
        }
    };
}
