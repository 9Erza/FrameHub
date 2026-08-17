using System.Text.Json.Serialization;

namespace FrameHub.Core.Models.Benchmarking;

public static class BenchmarkFormat
{
    public const int CurrentSchemaVersion = 1;
    public const int CurrentAnalysisVersion = 1;
    public const string RawFileName = "raw-frames.json";
    public const string SessionFileName = "session.json";
    public const string SummaryFileName = "summary.json";
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BenchmarkSessionStatus
{
    Created,
    Capturing,
    Completed,
    Failed,
    Cancelled
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BenchmarkQualityLevel
{
    Valid,
    Warning,
    Invalid
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BenchmarkQualitySeverity
{
    Information,
    Warning,
    Error
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BenchmarkDisplayTimingSource
{
    DisplayedTimeCurrentFrame,
    MsBetweenDisplayChangePreviousFrame
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BenchmarkProcessPathResolution
{
    Managed,
    Unavailable
}

public sealed class BenchmarkTarget
{
    public string LibraryItemId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string LibrarySource { get; init; } = string.Empty;
    public string? SourceId { get; init; }
    public string? ConfiguredExecutablePath { get; init; }
}

public sealed class BenchmarkProcessIdentity
{
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string? ExecutablePath { get; init; }
    public BenchmarkProcessPathResolution ExecutablePathResolution { get; init; } = BenchmarkProcessPathResolution.Managed;
    public DateTime StartTimeUtc { get; init; }

    public bool IsSameInstance(BenchmarkProcessIdentity? other) =>
        other is not null
        && ProcessId == other.ProcessId
        && StartTimeUtc == other.StartTimeUtc
        && string.Equals(ProcessName, other.ProcessName, StringComparison.OrdinalIgnoreCase)
        && (string.IsNullOrWhiteSpace(ExecutablePath)
            || string.IsNullOrWhiteSpace(other.ExecutablePath)
            || string.Equals(ExecutablePath, other.ExecutablePath, StringComparison.OrdinalIgnoreCase));
}

public sealed class BenchmarkSessionMetadata
{
    public int SchemaVersion { get; init; } = BenchmarkFormat.CurrentSchemaVersion;
    public int AnalysisVersion { get; init; } = BenchmarkFormat.CurrentAnalysisVersion;
    public Guid SessionId { get; init; }
    public string FrameHubVersion { get; init; } = string.Empty;
    public string? PresentMonVersion { get; set; }
    public BenchmarkTarget Game { get; init; } = new();
    public BenchmarkProcessIdentity Process { get; init; } = new();
    public DateTime StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }
    public double? RequestedCaptureDurationSeconds { get; init; }
    public double? CaptureDurationSeconds { get; set; }
    public double? AnalyzedDurationSeconds { get; set; }
    public string? ActiveCpuProfileId { get; init; }
    public string? ActiveCpuProfileName { get; init; }
    public bool? SessionOptimizationActive { get; init; }
    public string RawDataFile { get; init; } = BenchmarkFormat.RawFileName;
    public BenchmarkSessionStatus Status { get; set; } = BenchmarkSessionStatus.Created;
    public string? DiagnosticMessage { get; set; }
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Optional one-shot environment context captured at benchmark start. Absent or partially
    /// empty for historical schema-v1 records; loading never requires it to be present.
    /// </summary>
    public BenchmarkEnvironmentSnapshot? Environment { get; init; }
}

public sealed class BenchmarkSession
{
    public BenchmarkSessionMetadata Metadata { get; init; } = new();

    [JsonIgnore]
    public string SessionDirectory { get; init; } = string.Empty;

    [JsonIgnore]
    public string RawDataPath => Path.Combine(SessionDirectory, Metadata.RawDataFile);
}

public sealed class BenchmarkFrameSample
{
    public string? Application { get; init; }
    public int ProcessId { get; init; }
    public string SwapChainAddress { get; init; } = string.Empty;
    public string? PresentRuntime { get; init; }
    public string? PresentMode { get; init; }
    public double? CpuStartTime { get; init; }
    public string? CpuStartTimeUnit { get; init; }
    public double? MsBetweenPresents { get; init; }
    public double? MsBetweenDisplayChange { get; init; }
    public double? DisplayedTime { get; init; }
    public double? MsCpuBusy { get; init; }
    public double? MsCpuWait { get; init; }
    public double? MsGpuLatency { get; init; }
    public double? MsGpuTime { get; init; }
    public double? MsGpuBusy { get; init; }
    public double? MsGpuWait { get; init; }
    public double? DisplayLatency { get; init; }
    public bool? WasDropped { get; init; }
    public bool? WasDisplayed { get; init; }
    public string? FrameType { get; init; }
}

public sealed class BenchmarkMetricSet
{
    public string Methodology { get; init; } = string.Empty;
    public int ValidFrameCount { get; init; }
    public double DurationSeconds { get; init; }
    public double? AverageFps { get; init; }
    public double? MedianFps { get; init; }
    public double? MedianFrameTimeMs { get; init; }
    public double? OnePercentLowFps { get; init; }
    public double? PointOnePercentLowFps { get; init; }
    public double? P95FrameTimeMs { get; init; }
    public double? P99FrameTimeMs { get; init; }
    public double? MinimumFrameTimeMs { get; init; }
    public double? MaximumFrameTimeMs { get; init; }
}

public sealed class BenchmarkSwapChainSummary
{
    public string SwapChainAddress { get; init; } = string.Empty;
    public int UsefulFrameCount { get; init; }
    public double ActiveDurationSeconds { get; init; }
    public double ContinuityRatio { get; init; }
    public BenchmarkMetricSet PresentedMetrics { get; init; } = new();
    public BenchmarkMetricSet? DisplayedMetrics { get; init; }
    public BenchmarkDisplayTimingSource? DisplayedTimingSource { get; init; }
    public BenchmarkMetricSet? BetweenDisplayChangeMetrics { get; init; }
    public BenchmarkDisplayTimingSource? BetweenDisplayChangeTimingSource { get; init; }
    public int? DroppedOrNotDisplayedCount { get; init; }
    public bool FrameTypeAvailable { get; init; }
    public IReadOnlyDictionary<string, int> FrameTypeCounts { get; init; } = new Dictionary<string, int>();
}

public sealed class BenchmarkSummary
{
    public int SchemaVersion { get; init; } = BenchmarkFormat.CurrentSchemaVersion;
    public int AnalysisVersion { get; init; } = BenchmarkFormat.CurrentAnalysisVersion;
    public Guid SessionId { get; init; }
    public double CaptureDurationSeconds { get; init; }
    public double AnalyzedDurationSeconds { get; init; }
    public string? SelectedSwapChainAddress { get; init; }
    public string SwapChainSelectionReason { get; init; } = string.Empty;
    public BenchmarkMetricSet? PrimaryPresentedMetrics { get; init; }
    public BenchmarkMetricSet? SecondaryDisplayedMetrics { get; init; }
    public BenchmarkDisplayTimingSource? SecondaryDisplayedTimingSource { get; init; }
    public BenchmarkMetricSet? BetweenDisplayChangeMetrics { get; init; }
    public BenchmarkDisplayTimingSource? BetweenDisplayChangeTimingSource { get; init; }
    public int? DroppedOrNotDisplayedCount { get; init; }
    public IReadOnlyList<BenchmarkSwapChainSummary> SwapChains { get; init; } = Array.Empty<BenchmarkSwapChainSummary>();
    public BenchmarkQualityResult Quality { get; init; } = new();
    public BenchmarkDataDiagnostics DataDiagnostics { get; init; } = new();
}

public sealed class BenchmarkQualityIssue
{
    public string Code { get; init; } = string.Empty;
    public BenchmarkQualitySeverity Severity { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class BenchmarkQualityResult
{
    public BenchmarkQualityLevel Level { get; init; } = BenchmarkQualityLevel.Invalid;
    public IReadOnlyList<BenchmarkQualityIssue> Issues { get; init; } = Array.Empty<BenchmarkQualityIssue>();
}

public sealed class BenchmarkDataDiagnostics
{
    public int RecordsRead { get; set; }
    public int SamplesParsed { get; set; }
    public int RejectedRecords { get; set; }
    public bool IncompleteInput { get; set; }
    public IReadOnlyList<string> UnavailableOptionalMetrics { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}

public sealed class BenchmarkCaptureResult
{
    public BenchmarkSession Session { get; init; } = new();
    public BenchmarkSummary? Summary { get; init; }
}

public class BenchmarkException : Exception
{
    public BenchmarkException(string code, string message, Exception? innerException = null) : base(message, innerException) => Code = code;
    public string Code { get; }
}

public sealed class PresentMonUnavailableException : BenchmarkException
{
    public PresentMonUnavailableException(string message) : base("presentmon_unavailable", message) { }
}

public sealed class BenchmarkTargetException : BenchmarkException
{
    public BenchmarkTargetException(string code, string message, Exception? innerException = null) : base(code, message, innerException) { }
}
