using System.Collections.ObjectModel;
using System.Globalization;
using FrameHub.App.Helpers;
using FrameHub.App.Services;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Services.Benchmarking;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FrameHub.App.ViewModels.Benchmark;

public enum BenchmarkUiState { Idle, Waiting, Capturing, Completing, Completed, Cancelled, Failed }

public sealed class BenchmarkGameOptionViewModel : ViewModelBase
{
    public required LibraryItem Item { get; init; }
    public BenchmarkRunningGame? RunningGame { get; init; }
    public bool HasMultipleInstances { get; init; }
    public required string StatusText { get; init; }
    public bool IsRunning => RunningGame is not null;
    public string DisplayName => Item.DisplayName;
    public required string SourceText { get; init; }
    public string ProcessDetail => RunningGame is null ? string.Empty : $"PID {RunningGame.Process.ProcessId}";
    public ImageSource? IconSource
    {
        get
        {
            try
            {
                string? path = !string.IsNullOrWhiteSpace(Item.IconPath) ? Item.IconPath : Item.ExecutablePath;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                return icon is null ? null : System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
            }
            catch { return null; }
        }
    }
}

public sealed class BenchmarkHistoryItemViewModel
{
    private readonly LocalizationService _localization;
    public BenchmarkHistoryEntry Entry { get; }
    public BenchmarkHistoryItemViewModel(BenchmarkHistoryEntry entry, LocalizationService localization) { Entry = entry; _localization = localization; }
    public string GameName => Entry.Metadata.Game.DisplayName;
    public string DateText => Entry.Metadata.StartUtc.ToLocalTime().ToString("g", Culture);
    public string StatusText => _localization.T($"Benchmark.Status.{Entry.Metadata.Status}");
    public string AverageFpsText => Format(Entry.Summary?.PrimaryPresentedMetrics?.AverageFps, "0.0");
    public string OnePercentLowText => Format(Entry.Summary?.PrimaryPresentedMetrics?.OnePercentLowFps, "0.0");
    public string PointOnePercentLowText => Format(Entry.Summary?.PrimaryPresentedMetrics?.PointOnePercentLowFps, "0.0");
    public string DurationText => Format(Entry.Summary?.AnalyzedDurationSeconds ?? Entry.Metadata.CaptureDurationSeconds, "0.0") + " s";
    public string QualityText => Entry.Summary is null ? "—" : _localization.T($"Benchmark.Quality.{Entry.Summary.Quality.Level}");
    public string SummaryText => $"{_localization.T("Benchmark.Metric.average_fps")} {AverageFpsText} · 1% {OnePercentLowText} · {QualityText}";
    public BenchmarkQualityLevel? QualityLevel => Entry.Summary?.Quality.Level;
    public string AverageLabel => _localization.T("Benchmark.Metric.average_fps");
    public string OneLowLabel => _localization.T("Benchmark.Metric.one_percent_low");
    public string ComparisonDisplayText => $"{GameName} — {DateText} — {AverageLabel} {AverageFpsText} FPS";
    public string CpuProfileText => string.IsNullOrWhiteSpace(Entry.Metadata.ActiveCpuProfileName) ? _localization.T("Benchmark.Context.NoProfile") : Entry.Metadata.ActiveCpuProfileName!;
    public string SessionOptimizationText => Entry.Metadata.SessionOptimizationActive == true ? _localization.T("Benchmark.Context.Active") : Entry.Metadata.SessionOptimizationActive == false ? _localization.T("Benchmark.Context.Inactive") : _localization.T("Benchmark.Unavailable");
    public string CpuProfileDisplayText => $"{_localization.T("Benchmark.Context.CpuProfile")}: {CpuProfileText}";
    public string SessionOptimizationDisplayText => $"{_localization.T("Benchmark.Context.SessionOptimization")}: {SessionOptimizationText}";
    public string QualityDisplayText => $"{_localization.T("Benchmark.Result.Quality")}: {QualityText}";
    public string DurationDisplayText => $"{_localization.T("Benchmark.Result.CaptureDuration")}: {DurationText}";
    public bool IsCompleted => Entry.Metadata.Status == BenchmarkSessionStatus.Completed && Entry.Summary is not null;
    private CultureInfo Culture => CultureInfo.GetCultureInfo(_localization.CurrentLanguage == "pl" ? "pl-PL" : "en-US");
    private string Format(double? value, string format) => value is double number && double.IsFinite(number) ? number.ToString(format, Culture) : "—";
}

public sealed class BenchmarkResultViewModel
{
    private readonly LocalizationService _localization;
    public BenchmarkHistoryEntry Entry { get; }
    public IReadOnlyList<BenchmarkChartPoint> ChartPoints { get; }
    public BenchmarkResultViewModel(BenchmarkHistoryEntry entry, IReadOnlyList<BenchmarkChartPoint> chartPoints, LocalizationService localization)
    {
        Entry = entry; ChartPoints = chartPoints; _localization = localization;
    }

    private BenchmarkMetricSet? Metrics => Entry.Summary?.PrimaryPresentedMetrics;
    private CultureInfo Culture => CultureInfo.GetCultureInfo(_localization.CurrentLanguage == "pl" ? "pl-PL" : "en-US");
    public string GameName => Entry.Metadata.Game.DisplayName;
    public string SourceText => Entry.Metadata.Game.LibrarySource;
    public string DateText => Entry.Metadata.StartUtc.ToLocalTime().ToString("g", Culture);
    public string AverageFps => Format(Metrics?.AverageFps, "0.0");
    public string MedianFps => Format(Metrics?.MedianFps, "0.0");
    public string OnePercentLow => Format(Metrics?.OnePercentLowFps, "0.0");
    public string PointOnePercentLow => Format(Metrics?.PointOnePercentLowFps, "0.0");
    public string P95 => Format(Metrics?.P95FrameTimeMs, "0.00") + Unit(Metrics?.P95FrameTimeMs, " ms");
    public string P99 => Format(Metrics?.P99FrameTimeMs, "0.00") + Unit(Metrics?.P99FrameTimeMs, " ms");
    public string P99Value => Format(Metrics?.P99FrameTimeMs, "0.00");
    public string FpsUnit => "FPS";
    public string MillisecondsUnit => "ms";
    public string MinFrameTime => Format(Metrics?.MinimumFrameTimeMs, "0.00") + Unit(Metrics?.MinimumFrameTimeMs, " ms");
    public string MaxFrameTime => Format(Metrics?.MaximumFrameTimeMs, "0.00") + Unit(Metrics?.MaximumFrameTimeMs, " ms");
    public string ValidFrameCount => Metrics?.ValidFrameCount.ToString("N0", Culture) ?? "—";
    public string CaptureDuration => Format(Entry.Metadata.CaptureDurationSeconds, "0.0") + Unit(Entry.Metadata.CaptureDurationSeconds, " s");
    public string AnalyzedDuration => Format(Entry.Summary?.AnalyzedDurationSeconds, "0.0") + Unit(Entry.Summary?.AnalyzedDurationSeconds, " s");
    public string DroppedCount => Entry.Summary?.DroppedOrNotDisplayedCount?.ToString("N0", Culture) ?? "—";
    public string Quality => Entry.Summary is null ? "—" : _localization.T($"Benchmark.Quality.{Entry.Summary.Quality.Level}");
    public BenchmarkQualityLevel? QualityLevel => Entry.Summary?.Quality.Level;
    public string QualityIssues => Entry.Summary?.Quality.Issues.Count > 0
        ? string.Join(Environment.NewLine, Entry.Summary.Quality.Issues.Select(issue => $"• {LocalizeQualityIssue(issue.Code)}"))
        : _localization.T("Benchmark.Result.NoWarnings");
    public string CpuProfile => string.IsNullOrWhiteSpace(Entry.Metadata.ActiveCpuProfileName) ? _localization.T("Benchmark.Context.NoProfile") : Entry.Metadata.ActiveCpuProfileName!;
    public string SessionOptimization => Entry.Metadata.SessionOptimizationActive == true ? _localization.T("Benchmark.Context.Active") : Entry.Metadata.SessionOptimizationActive == false ? _localization.T("Benchmark.Context.Inactive") : _localization.T("Benchmark.Unavailable");
    public string RequestedDuration => Format(Entry.Metadata.RequestedCaptureDurationSeconds, "0") + Unit(Entry.Metadata.RequestedCaptureDurationSeconds, " s");
    public string PresentMonVersion => Entry.Metadata.PresentMonVersion ?? _localization.T("Benchmark.Unavailable");
    public string FrameHubVersion => Entry.Metadata.FrameHubVersion;
    public string SwapChain => Entry.Summary?.SelectedSwapChainAddress ?? _localization.T("Benchmark.Unavailable");
    public string SelectionReason => Entry.Summary?.SwapChainSelectionReason ?? _localization.T("Benchmark.Unavailable");
    public string Methodology => Metrics?.Methodology ?? _localization.T("Benchmark.Unavailable");
    public string DisplayedTiming => MetricSummary(Entry.Summary?.SecondaryDisplayedMetrics);
    public string BetweenDisplayChange => MetricSummary(Entry.Summary?.BetweenDisplayChangeMetrics);
    public string FrameTypes
    {
        get
        {
            BenchmarkSwapChainSummary? chain = Entry.Summary?.SwapChains.FirstOrDefault(item => item.SwapChainAddress.Equals(Entry.Summary.SelectedSwapChainAddress, StringComparison.OrdinalIgnoreCase));
            return chain?.FrameTypeAvailable == true ? string.Join(", ", chain.FrameTypeCounts.Select(pair => $"{pair.Key}: {pair.Value}")) : _localization.T("Benchmark.Unavailable");
        }
    }
    public string Diagnostics => Entry.Summary is null ? Entry.ReadError ?? "—" : string.Join(Environment.NewLine,
        Entry.Summary.DataDiagnostics.Warnings
            .Concat(Entry.Summary.DataDiagnostics.UnavailableOptionalMetrics)
            .Concat(Entry.Summary.Quality.Issues.Select(issue => $"[{issue.Code}] {issue.Message}")));
    public string ProcessDetails => $"PID {Entry.Metadata.Process.ProcessId} · {Entry.Metadata.Process.ProcessName} · {Entry.Metadata.Process.StartTimeUtc:O}";
    public string ExecutablePath => Entry.Metadata.Process.ExecutablePath ?? _localization.T("Benchmark.Unavailable");
    public string StoragePath => Entry.SessionDirectory;
    public string AverageLabel => _localization.T("Benchmark.Metric.average_fps");
    public string MedianLabel => _localization.T("Benchmark.Metric.median_fps");
    public string OneLowLabel => _localization.T("Benchmark.Metric.one_percent_low");
    public string PointOneLowLabel => _localization.T("Benchmark.Metric.point_one_percent_low");
    public string P95Label => _localization.T("Benchmark.Metric.p95_frame_time");
    public string P99Label => _localization.T("Benchmark.Metric.p99_frame_time");
    public string MinLabel => _localization.T("Benchmark.Metric.minimum_frame_time");
    public string MaxLabel => _localization.T("Benchmark.Metric.maximum_frame_time");
    public string ValidFramesLabel => _localization.T("Benchmark.Metric.valid_frame_count");
    public string CaptureDurationLabel => _localization.T("Benchmark.Result.CaptureDuration");
    public string AnalyzedDurationLabel => _localization.T("Benchmark.Metric.analyzed_duration");
    public string DroppedLabel => _localization.T("Benchmark.Metric.dropped_count");
    public string QualityLabel => _localization.T("Benchmark.Result.Quality");
    public string ChartLabel => _localization.T("Benchmark.Result.Chart");
    public string ContextLabel => _localization.T("Benchmark.Result.Context");
    public string CpuProfileLabel => _localization.T("Benchmark.Context.CpuProfile");
    public string SessionOptimizationLabel => _localization.T("Benchmark.Context.SessionOptimization");
    public string RequestedDurationLabel => _localization.T("Benchmark.Duration");
    public string QualityIssuesLabel => _localization.T("Benchmark.Result.QualityIssues");
    public string AdvancedLabel => _localization.T("Benchmark.AdvancedDetails");
    private string MetricSummary(BenchmarkMetricSet? metrics) => metrics is null ? _localization.T("Benchmark.Unavailable") : $"{metrics.ValidFrameCount:N0} · P95 {metrics.P95FrameTimeMs?.ToString("0.00", Culture) ?? "—"} ms · P99 {metrics.P99FrameTimeMs?.ToString("0.00", Culture) ?? "—"} ms";
    private string LocalizeQualityIssue(string code)
    {
        string key = $"Benchmark.QualityIssue.{code}";
        string localized = _localization.T(key);
        return localized.Equals(key, StringComparison.OrdinalIgnoreCase) ? _localization.T("Benchmark.QualityIssue.Unknown") : localized;
    }
    private string Format(double? value, string format) => value is double number && double.IsFinite(number) ? number.ToString(format, Culture) : "—";
    private static string Unit(double? value, string unit) => value.HasValue && double.IsFinite(value.Value) ? unit : string.Empty;
}

public sealed class BenchmarkComparisonRowViewModel
{
    private readonly CultureInfo _culture;
    public BenchmarkComparisonMetric Metric { get; }
    public string Name { get; }
    public BenchmarkComparisonRowViewModel(BenchmarkComparisonMetric metric, string name, string language)
    {
        Metric = metric; Name = name; _culture = CultureInfo.GetCultureInfo(language == "pl" ? "pl-PL" : "en-US");
    }
    public string SessionA => Format(Metric.SessionA);
    public string SessionB => Format(Metric.SessionB);
    public string Delta => Metric.Delta is double value && double.IsFinite(value) ? value.ToString("+0.00;-0.00;0.00", _culture) : "—";
    public string Percentage => Metric.PercentageDelta is double value && double.IsFinite(value) ? value.ToString("+0.0;-0.0;0.0", _culture) + " %" : "—";
    private string Format(double? value) => value is double number && double.IsFinite(number) ? number.ToString("0.00", _culture) : "—";
}
