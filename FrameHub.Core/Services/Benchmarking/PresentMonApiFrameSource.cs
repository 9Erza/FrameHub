using System.Buffers.Binary;
using System.Runtime.InteropServices;
using FrameHub.Core.Models.Benchmarking;

namespace FrameHub.Core.Services.Benchmarking;

public interface IPresentMonFrameSource
{
    Task<PresentMonApiCapture> CaptureAsync(int processId, TimeSpan duration, CancellationToken cancellationToken = default);
}

public sealed class PresentMonApiCapture
{
    public required IReadOnlyList<BenchmarkFrameSample> Frames { get; init; }
    public required string ApiVersion { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required PresentMonApiCaptureDiagnostics Diagnostics { get; init; }
}

public sealed class PresentMonApiCaptureDiagnostics
{
    public List<PresentMonApiRegisteredMetric> RegisteredMetrics { get; } = [];
    public uint BlobSize { get; internal set; }
    public uint FrameBufferCapacity { get; internal set; }
    public int ConsumeCalls { get; internal set; }
    public int ZeroFrameConsumeCalls { get; internal set; }
    public int NonZeroFrameConsumeCalls { get; internal set; }
    public int TotalBlobsReturned { get; internal set; }
    public int MaximumFramesInOneCall { get; internal set; }
    public Dictionary<PmStatus, int> NonSuccessStatuses { get; } = [];
    public int SamplesWithSwapChainAddress { get; internal set; }
    public int SamplesWithPositiveBetweenPresents { get; internal set; }
    public int SamplesWithDisplayedTime { get; internal set; }
    public int SamplesWithBetweenDisplayChange { get; internal set; }
}
public readonly record struct PresentMonApiRegisteredMetric(PmMetric Metric, PmDataType FrameType);

/// <summary>Service/API frame source. It observes PresentMon output only; it does not access game memory or inject code.</summary>
public sealed class PresentMonApiFrameSource : IPresentMonFrameSource
{
    private const uint FrameCapacity = 256;
    private static readonly PmMetric[] RequestedMetrics = [PmMetric.SwapChainAddress, PmMetric.PresentRuntime, PmMetric.PresentMode, PmMetric.CpuStartQpc, PmMetric.BetweenPresents, PmMetric.DisplayedTime, PmMetric.BetweenDisplayChange, PmMetric.FrameType];
    private readonly Func<IPresentMonApi> _apiFactory;
    private readonly string? _application;
    public PresentMonApiFrameSource(Func<IPresentMonApi>? apiFactory = null, string? application = null) { _apiFactory = apiFactory ?? (() => new PresentMonApi()); _application = application; }
    public async Task<PresentMonApiCapture> CaptureAsync(int processId, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        if (processId <= 0) throw new ArgumentOutOfRangeException(nameof(processId));
        using IPresentMonApi api = _apiFactory(); nint session = 0; nint query = 0; bool tracking = false;
        var frames = new List<BenchmarkFrameSample>(); var warnings = new List<string>(); var diagnostics = new PresentMonApiCaptureDiagnostics { FrameBufferCapacity = FrameCapacity };
        try
        {
            PmStatus openStatus = api.OpenSession(out session);
            if (openStatus != PmStatus.Success) throw new PresentMonUnavailableException($"PresentMon Service/API is unavailable: pmOpenSession returned PM_STATUS_{openStatus}. Install and start the official PresentMon v2.5.1 Service.");
            Require(api.GetApiVersion(out PmVersion version), "pmGetApiVersion");
            IReadOnlyDictionary<PmMetric, PmFrameMetricInfo> available = api.GetFrameMetricInfo(session);
            PmMetric[] metrics = RequestedMetrics.Where(metric => available.TryGetValue(metric, out PmFrameMetricInfo info) && info.MetricType is PmMetricType.FrameEvent or PmMetricType.DynamicFrame && info.FrameType != PmDataType.Void).ToArray();
            foreach (PmMetric metric in RequestedMetrics.Except(metrics)) warnings.Add($"{metric} is unavailable or not a valid frame-event metric and was not queried.");
            if (!metrics.Contains(PmMetric.SwapChainAddress) || !metrics.Contains(PmMetric.BetweenPresents)) throw new BenchmarkException("presentmon_api_metric_unavailable", "PresentMon API did not expose required frame metrics SwapChainAddress and BetweenPresents.");
            PmQueryElement[] elements = metrics.Select(metric => new PmQueryElement { Metric = metric, Stat = PmStat.None }).ToArray();
            Require(api.RegisterFrameQuery(session, elements, out query, out uint blobSize), "pmRegisterFrameQuery");
            if (blobSize == 0) throw new BenchmarkException("presentmon_api_invalid_blob", "pmRegisterFrameQuery returned a zero-sized frame blob.");
            foreach (PmQueryElement element in elements) ValidateElement(element, blobSize);
            diagnostics.BlobSize = blobSize;
            foreach (PmQueryElement element in elements) diagnostics.RegisteredMetrics.Add(new(element.Metric, available[element.Metric].FrameType));
            Require(api.StartTrackingProcess(session, (uint)processId), "pmStartTrackingProcess"); tracking = true;
            DateTime end = DateTime.UtcNow + duration;
            while (DateTime.UtcNow < end) { Consume(api, query, processId, _application, elements, available, blobSize, frames, diagnostics); await Task.Delay(20, cancellationToken).ConfigureAwait(false); }
            Require(api.FlushFrames(session, (uint)processId), "pmFlushFrames");
            for (int i = 0; i < 3; i++) { if (Consume(api, query, processId, _application, elements, available, blobSize, frames, diagnostics) == 0) break; }
            diagnostics.SamplesWithSwapChainAddress = frames.Count(frame => !string.IsNullOrWhiteSpace(frame.SwapChainAddress));
            diagnostics.SamplesWithPositiveBetweenPresents = frames.Count(frame => frame.MsBetweenPresents > 0);
            diagnostics.SamplesWithDisplayedTime = frames.Count(frame => frame.DisplayedTime.HasValue);
            diagnostics.SamplesWithBetweenDisplayChange = frames.Count(frame => frame.MsBetweenDisplayChange.HasValue);
            return new PresentMonApiCapture { Frames = frames, ApiVersion = version.ToString(), Warnings = warnings, Diagnostics = diagnostics };
        }
        finally
        {
            if (tracking) Cleanup("pmStopTrackingProcess", () => api.StopTrackingProcess(session, (uint)processId), warnings);
            if (query != 0) Cleanup("pmFreeFrameQuery", () => api.FreeFrameQuery(query), warnings);
            if (session != 0) Cleanup("pmCloseSession", () => api.CloseSession(session), warnings);
        }
    }
    public static IReadOnlyList<PmMetric> FrameQueryMetrics => RequestedMetrics;
    private static int Consume(IPresentMonApi api, nint query, int processId, string? application, PmQueryElement[] elements, IReadOnlyDictionary<PmMetric, PmFrameMetricInfo> types, uint blobSize, List<BenchmarkFrameSample> destination, PresentMonApiCaptureDiagnostics diagnostics)
    {
        byte[] blobs = new byte[checked((int)(blobSize * FrameCapacity))]; uint count = FrameCapacity;
        diagnostics.ConsumeCalls++; PmStatus status = api.ConsumeFrames(query, (uint)processId, blobs, ref count);
        if (status != PmStatus.Success) diagnostics.NonSuccessStatuses[status] = diagnostics.NonSuccessStatuses.GetValueOrDefault(status) + 1;
        Require(status, "pmConsumeFrames");
        if (count == 0) diagnostics.ZeroFrameConsumeCalls++; else diagnostics.NonZeroFrameConsumeCalls++;
        diagnostics.TotalBlobsReturned += checked((int)count); diagnostics.MaximumFramesInOneCall = Math.Max(diagnostics.MaximumFramesInOneCall, checked((int)count));
        for (uint i = 0; i < count; i++) destination.Add(Decode(blobs.AsSpan(checked((int)(i * blobSize)), checked((int)blobSize)), processId, application, elements, types));
        return (int)count;
    }
    private static BenchmarkFrameSample Decode(ReadOnlySpan<byte> blob, int processId, string? application, PmQueryElement[] e, IReadOnlyDictionary<PmMetric, PmFrameMetricInfo> types)
    {
        bool hasSwap = TryFind(e, PmMetric.SwapChainAddress, out PmQueryElement swap); double? displayed = ReadMetricNumber(blob, e, types, PmMetric.DisplayedTime);
        return new BenchmarkFrameSample { ProcessId = processId, Application = application, SwapChainAddress = hasSwap ? $"0x{ReadU64(blob, swap):X}" : string.Empty, PresentRuntime = Runtime(ReadMetricEnum(blob, e, PmMetric.PresentRuntime)), PresentMode = Mode(ReadMetricEnum(blob, e, PmMetric.PresentMode)), CpuStartTime = ReadMetricNumber(blob, e, types, PmMetric.CpuStartQpc), CpuStartTimeUnit = "QpcTicks", MsBetweenPresents = ReadMetricNumber(blob, e, types, PmMetric.BetweenPresents), DisplayedTime = displayed, MsBetweenDisplayChange = ReadMetricNumber(blob, e, types, PmMetric.BetweenDisplayChange), WasDisplayed = displayed.HasValue, FrameType = FrameType(ReadMetricEnum(blob, e, PmMetric.FrameType)) };
    }
    private static bool TryFind(PmQueryElement[] elements, PmMetric metric, out PmQueryElement result) { foreach (PmQueryElement element in elements) if (element.Metric == metric) { result = element; return true; } result = default; return false; }
    private static double? ReadMetricNumber(ReadOnlySpan<byte> blob, PmQueryElement[] elements, IReadOnlyDictionary<PmMetric, PmFrameMetricInfo> types, PmMetric metric) => TryFind(elements, metric, out PmQueryElement element) && types.TryGetValue(metric, out PmFrameMetricInfo info) ? ReadNumber(blob, element, info.FrameType) : null;
    private static uint ReadMetricEnum(ReadOnlySpan<byte> blob, PmQueryElement[] elements, PmMetric metric) => TryFind(elements, metric, out PmQueryElement element) ? ReadU32(blob, element) : 0;
    private static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> blob, PmQueryElement element) => blob.Slice(checked((int)element.DataOffset), checked((int)element.DataSize));
    private static uint ReadU32(ReadOnlySpan<byte> blob, PmQueryElement element) => Slice(blob, element).Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(Slice(blob, element)) : 0;
    private static ulong ReadU64(ReadOnlySpan<byte> blob, PmQueryElement element) => Slice(blob, element).Length >= 8 ? BinaryPrimitives.ReadUInt64LittleEndian(Slice(blob, element)) : 0;
    private static double? ReadNumber(ReadOnlySpan<byte> blob, PmQueryElement element, PmDataType type) { ReadOnlySpan<byte> value = Slice(blob, element); double? result = type switch { PmDataType.Double when value.Length >= 8 => BitConverter.Int64BitsToDouble((long)BinaryPrimitives.ReadUInt64LittleEndian(value)), PmDataType.Int32 when value.Length >= 4 => BinaryPrimitives.ReadInt32LittleEndian(value), PmDataType.UInt32 or PmDataType.Enum when value.Length >= 4 => BinaryPrimitives.ReadUInt32LittleEndian(value), PmDataType.UInt64 when value.Length >= 8 => BinaryPrimitives.ReadUInt64LittleEndian(value), PmDataType.Bool when value.Length >= 1 => value[0] == 0 ? 0 : 1, _ => null }; return result is double number && double.IsNaN(number) ? null : result; }
    private static void ValidateElement(PmQueryElement element, uint blobSize) { if (element.DataOffset > blobSize || element.DataSize > blobSize - element.DataOffset) throw new BenchmarkException("presentmon_api_invalid_blob", $"pmRegisterFrameQuery returned out-of-range dataOffset/dataSize for {element.Metric}."); }
    private static string Runtime(uint value) => value switch { 1 => "DXGI", 2 => "D3D9", _ => "Other" };
    private static string Mode(uint value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private static string? FrameType(uint value) => value switch { 0 => null, 1 or 2 or 3 => "Application", 50 => "Intel XeSS-FG", 100 => "AMD_AFMF", _ => "Other" };
    private static void Require(PmStatus status, string operation) { if (status != PmStatus.Success) throw new BenchmarkException("presentmon_api_status", $"{operation} failed with PM_STATUS_{status}."); }
    private static void Cleanup(string operation, Func<PmStatus> action, List<string> warnings)
    {
        try { PmStatus status = action(); if (status != PmStatus.Success) warnings.Add($"{operation}: {status}"); }
        catch (Exception ex) { warnings.Add($"{operation} cleanup threw {ex.GetType().Name}: {ex.Message}"); }
    }
}
