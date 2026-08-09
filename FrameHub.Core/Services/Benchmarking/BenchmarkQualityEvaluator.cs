using FrameHub.Core.Models.Benchmarking;

namespace FrameHub.Core.Services.Benchmarking;

public sealed class BenchmarkQualityEvaluator
{
    public BenchmarkQualityResult Evaluate(
        BenchmarkMetricSet? metrics,
        double captureDurationSeconds,
        BenchmarkDataDiagnostics diagnostics,
        bool ambiguousSwapChain,
        bool targetIdentityValid,
        bool frameTypeAvailable,
        bool mixedFrameTypes)
    {
        var issues = new List<BenchmarkQualityIssue>();

        if (!targetIdentityValid) Add(issues, "target_identity_invalid", BenchmarkQualitySeverity.Error, "The target process disappeared or its PID identity changed during capture.");
        if (metrics is null || metrics.ValidFrameCount == 0) Add(issues, "zero_usable_frames", BenchmarkQualitySeverity.Error, "No usable presented-frame intervals were captured for the target PID.");
        if (captureDurationSeconds < 2) Add(issues, "capture_too_short", BenchmarkQualitySeverity.Error, "Capture duration is below 2 seconds.");
        else if (captureDurationSeconds < 10) Add(issues, "capture_short", BenchmarkQualitySeverity.Warning, "Capture duration is below the recommended 10 seconds.");

        if (metrics is { ValidFrameCount: > 0 and < 60 }) Add(issues, "low_sample_count", BenchmarkQualitySeverity.Warning, "Fewer than 60 usable presented-frame intervals were analyzed.");
        if (ambiguousSwapChain) Add(issues, "ambiguous_swap_chain", BenchmarkQualitySeverity.Warning, "Two swap chains had materially similar dominance scores; the deterministic tie-breaker was used.");
        if (diagnostics.IncompleteInput) Add(issues, "incomplete_input", BenchmarkQualitySeverity.Warning, "The raw frame input ended incompletely.");

        double rejectedRatio = diagnostics.RecordsRead == 0 ? 0 : (double)diagnostics.RejectedRecords / diagnostics.RecordsRead;
        if (rejectedRatio > 0.20) Add(issues, "many_rejected_records", BenchmarkQualitySeverity.Error, "More than 20% of raw frame records were rejected.");
        else if (diagnostics.RejectedRecords > 0) Add(issues, "rejected_records", BenchmarkQualitySeverity.Warning, $"{diagnostics.RejectedRecords} raw frame record(s) were excluded.");

        if (!frameTypeAvailable) Add(issues, "frame_type_unavailable", BenchmarkQualitySeverity.Information, "PresentMon did not provide FrameType; generated frames cannot be classified.");
        if (mixedFrameTypes) Add(issues, "mixed_frame_types", BenchmarkQualitySeverity.Warning, "Multiple FrameType values are present. Aggregate metrics are explicitly unfiltered; per-type counts are retained.");
        if (diagnostics.UnavailableOptionalMetrics.Count > 0) Add(issues, "optional_telemetry_unavailable", BenchmarkQualitySeverity.Information, $"Optional PresentMon metrics unavailable: {string.Join(", ", diagnostics.UnavailableOptionalMetrics)}.");

        BenchmarkQualityLevel level = issues.Any(issue => issue.Severity == BenchmarkQualitySeverity.Error)
            ? BenchmarkQualityLevel.Invalid
            : issues.Any(issue => issue.Severity == BenchmarkQualitySeverity.Warning)
                ? BenchmarkQualityLevel.Warning
                : BenchmarkQualityLevel.Valid;

        return new BenchmarkQualityResult { Level = level, Issues = issues };
    }

    private static void Add(List<BenchmarkQualityIssue> issues, string code, BenchmarkQualitySeverity severity, string message) =>
        issues.Add(new BenchmarkQualityIssue { Code = code, Severity = severity, Message = message });
}
