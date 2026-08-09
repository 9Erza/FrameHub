using System.Globalization;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Services.Benchmarking;

namespace FrameHub.BenchmarkHarness;

public static class BenchmarkReportWriter
{
    public static void WriteApiDiagnostics(TextWriter writer, PresentMonApiCaptureDiagnostics diagnostics, string? apiVersion)
    {
        writer.WriteLine(); writer.WriteLine("PRESENTMON API DIAGNOSTICS:");
        writer.WriteLine($"API version: {apiVersion ?? "Unavailable"}");
        writer.WriteLine($"Registered metric count: {diagnostics.RegisteredMetrics.Count}");
        writer.WriteLine($"Registered metrics: {string.Join(", ", diagnostics.RegisteredMetrics.Select(metric => $"{metric.Metric} ({metric.FrameType})"))}");
        writer.WriteLine($"Blob size: {diagnostics.BlobSize}"); writer.WriteLine($"Frame buffer capacity: {diagnostics.FrameBufferCapacity}");
        writer.WriteLine($"pmConsumeFrames calls: {diagnostics.ConsumeCalls}; zero: {diagnostics.ZeroFrameConsumeCalls}; nonzero: {diagnostics.NonZeroFrameConsumeCalls}; blobs: {diagnostics.TotalBlobsReturned}; max batch: {diagnostics.MaximumFramesInOneCall}");
        writer.WriteLine($"Non-success PM_STATUS: {(diagnostics.NonSuccessStatuses.Count == 0 ? "None" : string.Join(", ", diagnostics.NonSuccessStatuses.Select(pair => $"{pair.Key}={pair.Value}")))}");
        writer.WriteLine($"Samples: swap chain {diagnostics.SamplesWithSwapChainAddress}; positive BetweenPresents {diagnostics.SamplesWithPositiveBetweenPresents}; DisplayedTime {diagnostics.SamplesWithDisplayedTime}; BetweenDisplayChange {diagnostics.SamplesWithBetweenDisplayChange}");
    }
    public static void WriteResolvedIdentity(TextWriter writer, HarnessTargetResolution resolution, BenchmarkProcessIdentity process)
    {
        writer.WriteLine($"Game: {resolution.Target.DisplayName}");
        writer.WriteLine($"Source: {resolution.Target.LibrarySource}");
        if (!string.IsNullOrWhiteSpace(resolution.Target.SourceId))
        {
            string label = resolution.Target.LibrarySource.Equals("Steam", StringComparison.OrdinalIgnoreCase) ? "Steam AppID" : "Source ID";
            writer.WriteLine($"{label}: {resolution.Target.SourceId}");
        }
        writer.WriteLine($"Executable: {process.ExecutablePath ?? "Unavailable"}");
        writer.WriteLine($"Path resolution: {process.ExecutablePathResolution}");
        writer.WriteLine($"PID: {process.ProcessId}");
        writer.WriteLine($"Started: {process.StartTimeUtc:O}");
        writer.WriteLine($"Identity confidence: {resolution.Confidence}");
    }

    public static void WriteSummary(TextWriter writer, BenchmarkCaptureResult result)
    {
        BenchmarkSession session = result.Session;
        BenchmarkSummary summary = result.Summary ?? throw new ArgumentException("A completed capture summary is required.", nameof(result));
        BenchmarkMetricSet? presented = summary.PrimaryPresentedMetrics;

        writer.WriteLine();
        writer.WriteLine($"Game: {session.Metadata.Game.DisplayName}");
        writer.WriteLine($"Source: {session.Metadata.Game.LibrarySource}");
        writer.WriteLine($"Executable: {session.Metadata.Process.ExecutablePath ?? "Unavailable"}");
        writer.WriteLine($"PID: {session.Metadata.Process.ProcessId}");
        writer.WriteLine($"Capture duration: {Number(summary.CaptureDurationSeconds)} s");
        writer.WriteLine($"Analyzed duration: {Number(summary.AnalyzedDurationSeconds)} s");
        writer.WriteLine($"Valid frame count: {presented?.ValidFrameCount ?? 0}");
        writer.WriteLine($"Selected swap chain: {summary.SelectedSwapChainAddress ?? "Unavailable"}");
        writer.WriteLine($"Swap-chain selection reason: {summary.SwapChainSelectionReason}");

        writer.WriteLine();
        writer.WriteLine("PRIMARY PRESENTED METRICS:");
        writer.WriteLine($"Average FPS: {Number(presented?.AverageFps)}");
        writer.WriteLine($"Median FPS: {Number(presented?.MedianFps)}");
        writer.WriteLine($"1% Low: {Number(presented?.OnePercentLowFps)}");
        writer.WriteLine($"0.1% Low: {Number(presented?.PointOnePercentLowFps)}");
        writer.WriteLine($"P95 frame time: {Number(presented?.P95FrameTimeMs)} ms");
        writer.WriteLine($"P99 frame time: {Number(presented?.P99FrameTimeMs)} ms");
        writer.WriteLine($"Min frame time: {Number(presented?.MinimumFrameTimeMs)} ms");
        writer.WriteLine($"Max frame time: {Number(presented?.MaximumFrameTimeMs)} ms");

        bool hasDisplayedTime = summary.SecondaryDisplayedMetrics is { ValidFrameCount: > 0 };
        bool hasBetweenDisplayChange = summary.BetweenDisplayChangeMetrics is { ValidFrameCount: > 0 };
        if (hasDisplayedTime || hasBetweenDisplayChange)
        {
            writer.WriteLine();
            writer.WriteLine("DISPLAY METRICS:");
            if (summary.SecondaryDisplayedMetrics is { ValidFrameCount: > 0 } displayed)
            {
                writer.WriteLine($"Timing source: {DisplaySource(summary.SecondaryDisplayedTimingSource)}");
                writer.WriteLine($"Valid display samples: {displayed.ValidFrameCount}");
                writer.WriteLine($"Average displayed FPS: {Number(displayed.AverageFps)}");
                writer.WriteLine($"Median display time: {Number(displayed.MedianFrameTimeMs)} ms");
                writer.WriteLine($"P95 display time: {Number(displayed.P95FrameTimeMs)} ms");
                writer.WriteLine($"P99 display time: {Number(displayed.P99FrameTimeMs)} ms");
            }
            if (summary.BetweenDisplayChangeMetrics is { ValidFrameCount: > 0 } previousFrame)
            {
                writer.WriteLine($"Separate timing source: {DisplaySource(summary.BetweenDisplayChangeTimingSource)}; samples: {previousFrame.ValidFrameCount}; average {Number(previousFrame.AverageFps)} FPS-equivalent");
            }
        }

        BenchmarkSwapChainSummary? selected = summary.SwapChains.FirstOrDefault(chain =>
            string.Equals(chain.SwapChainAddress, summary.SelectedSwapChainAddress, StringComparison.OrdinalIgnoreCase));
        writer.WriteLine();
        writer.WriteLine("FrameType:");
        if (selected?.FrameTypeAvailable == true)
        {
            foreach ((string type, int count) in selected.FrameTypeCounts.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                writer.WriteLine($"  {type}: {count}");
            }
        }
        else writer.WriteLine("  Unavailable");

        writer.WriteLine($"Dropped/not displayed: {(summary.DroppedOrNotDisplayedCount.HasValue ? summary.DroppedOrNotDisplayedCount.Value.ToString(CultureInfo.InvariantCulture) : "Unavailable")}");
        writer.WriteLine($"Quality: {summary.Quality.Level}");
        writer.WriteLine("Warnings:");
        BenchmarkQualityIssue[] warnings = summary.Quality.Issues.Where(issue => issue.Severity != BenchmarkQualitySeverity.Information).ToArray();
        if (warnings.Length == 0) writer.WriteLine("  None");
        else foreach (BenchmarkQualityIssue warning in warnings) writer.WriteLine($"  [{warning.Code}] {warning.Message}");
        writer.WriteLine($"Storage path: {session.SessionDirectory}");
    }

    private static string Number(double? value) => value.HasValue && double.IsFinite(value.Value)
        ? value.Value.ToString("0.00", CultureInfo.InvariantCulture)
        : "Unavailable";

    private static string DisplaySource(BenchmarkDisplayTimingSource? source) => source switch
    {
        BenchmarkDisplayTimingSource.DisplayedTimeCurrentFrame => "DisplayedTime (current frame duration; never substituted from MsBetweenDisplayChange)",
        BenchmarkDisplayTimingSource.MsBetweenDisplayChangePreviousFrame => "MsBetweenDisplayChange (previous-frame duration; kept separate from DisplayedTime)",
        _ => "Unavailable"
    };
}
