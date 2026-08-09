using System.Runtime.InteropServices;
using FrameHub.Core.Models.Benchmarking;

namespace FrameHub.Core.Services.Benchmarking;

/// <summary>Small, dynamically loaded ABI boundary for the public PresentMonAPI2 v3.3 contract.</summary>
public interface IPresentMonApi : IDisposable
{
    PmStatus OpenSession(out nint session);
    PmStatus CloseSession(nint session);
    PmStatus StartTrackingProcess(nint session, uint processId);
    PmStatus StopTrackingProcess(nint session, uint processId);
    PmStatus FlushFrames(nint session, uint processId);
    PmStatus RegisterFrameQuery(nint session, PmQueryElement[] elements, out nint query, out uint blobSize);
    PmStatus ConsumeFrames(nint query, uint processId, byte[] blobs, ref uint frameCount);
    PmStatus FreeFrameQuery(nint query);
    PmStatus GetApiVersion(out PmVersion version);
    IReadOnlyDictionary<PmMetric, PmFrameMetricInfo> GetFrameMetricInfo(nint session);
}

public enum PmStatus : int { Success, Failure, BadArgument, BadHandle, ServiceError, InvalidEtlFile, InvalidPid, AlreadyTrackingProcess, UnableToCreateNsm, InvalidAdapterId, OutOfRange, InsufficientBuffer, PipeError, SessionNotOpen, MiddlewareMissingPath, NonexistentFilePath, MiddlewareInvalidSignature, MiddlewareMissingEndpoint, MiddlewareVersionLow, MiddlewareVersionHigh, MiddlewareServiceMismatch, QueryMalformed, ModeMismatch, FeatureDisabled }
public enum PmMetric : int { Application, SwapChainAddress, GpuVendor, GpuName, CpuVendor, CpuName, CpuStartTime, CpuStartQpc, CpuFrameTime, CpuBusy, CpuWait, DisplayedFps, PresentedFps, GpuTime, GpuBusy, GpuWait, DroppedFrames, DisplayedTime, SyncInterval, PresentFlags, PresentMode, PresentRuntime, AllowsTearing, GpuLatency, DisplayLatency, ClickToPhotonLatency, GpuSustainedPowerLimit, GpuPower, GpuVoltage, GpuFrequency, GpuTemperature, GpuFanSpeed, GpuUtilization, GpuRenderComputeUtilization, GpuMediaUtilization, GpuPowerLimited, GpuTemperatureLimited, GpuCurrentLimited, GpuVoltageLimited, GpuUtilizationLimited, GpuMemPower, GpuMemVoltage, GpuMemFrequency, GpuMemEffectiveFrequency, GpuMemTemperature, GpuMemSize, GpuMemUsed, GpuMemUtilization, GpuMemMaxBandwidth, GpuMemWriteBandwidth, GpuMemReadBandwidth, GpuMemPowerLimited, GpuMemTemperatureLimited, GpuMemCurrentLimited, GpuMemVoltageLimited, GpuMemUtilizationLimited, CpuUtilization, CpuPowerLimit, CpuPower, CpuTemperature, CpuFrequency, CpuCoreUtility, ApplicationFps, FrameType, AnimationError, AllInputToPhotonLatency, InstrumentedLatency, AnimationTime, GpuEffectiveFrequency, GpuVoltageRegulatorTemperature, GpuMemEffectiveBandwidth, GpuOvervoltagePercent, GpuTemperaturePercent, GpuPowerPercent, GpuFanSpeedPercent, GpuCardPower, PresentStartTime, PresentStartQpc, BetweenPresents, InPresentApi, BetweenDisplayChange, UntilDisplayed, RenderPresentLatency, BetweenSimulationStart, PcLatency, DisplayedFrameTime, BetweenAppStart, PresentedFrameTime, FlipDelay, ProcessId, SessionStartQpc }
public enum PmMetricType : int { Dynamic, Static, FrameEvent, DynamicFrame }
public enum PmDataType : int { Double, Int32, UInt32, Enum, String, UInt64, Bool, Void }
public enum PmStat : int { None }
public readonly record struct PmFrameMetricInfo(PmMetricType MetricType, PmDataType FrameType);

[StructLayout(LayoutKind.Sequential)]
public struct PmQueryElement
{
    public PmMetric Metric;
    public PmStat Stat;
    public uint DeviceId;
    public uint ArrayIndex;
    public ulong DataOffset;
    public ulong DataSize;
}

[StructLayout(LayoutKind.Sequential)]
public struct PmVersion
{
    public ushort Major;
    public ushort Minor;
    public ushort Patch;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 22)] public byte[] Tag;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] Hash;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public byte[] Config;
    public override readonly string ToString() => $"{Major}.{Minor}.{Patch}";
}

public sealed class PresentMonApi : IPresentMonApi
{
    private nint _library;
    private readonly OpenSessionDelegate _openSession;
    private readonly CloseSessionDelegate _closeSession;
    private readonly StartStopDelegate _startTracking;
    private readonly StartStopDelegate _stopTracking;
    private readonly StartStopDelegate _flushFrames;
    private readonly RegisterFrameQueryDelegate _registerFrameQuery;
    private readonly ConsumeFramesDelegate _consumeFrames;
    private readonly FreeFrameQueryDelegate _freeFrameQuery;
    private readonly GetVersionDelegate _getVersion;
    private readonly GetIntrospectionDelegate _getIntrospection;
    private readonly FreeIntrospectionDelegate _freeIntrospection;

    public PresentMonApi(PresentMonApiDllLocator? locator = null, Func<string, nint>? libraryLoader = null)
    {
        string dllPath = (locator ?? new PresentMonApiDllLocator()).Locate();
        try { _library = (libraryLoader ?? NativeLibrary.Load)(dllPath); }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or FileLoadException)
        { throw new PresentMonUnavailableException($"PresentMon Service/API DLL could not be loaded from '{dllPath}': {ex.Message}"); }
        try
        {
            _openSession = Load<OpenSessionDelegate>("pmOpenSession"); _closeSession = Load<CloseSessionDelegate>("pmCloseSession");
            _startTracking = Load<StartStopDelegate>("pmStartTrackingProcess"); _stopTracking = Load<StartStopDelegate>("pmStopTrackingProcess"); _flushFrames = Load<StartStopDelegate>("pmFlushFrames");
            _registerFrameQuery = Load<RegisterFrameQueryDelegate>("pmRegisterFrameQuery"); _consumeFrames = Load<ConsumeFramesDelegate>("pmConsumeFrames"); _freeFrameQuery = Load<FreeFrameQueryDelegate>("pmFreeFrameQuery");
            _getVersion = Load<GetVersionDelegate>("pmGetApiVersion"); _getIntrospection = Load<GetIntrospectionDelegate>("pmGetIntrospectionRoot"); _freeIntrospection = Load<FreeIntrospectionDelegate>("pmFreeIntrospectionRoot");
        }
        catch (EntryPointNotFoundException ex) { NativeLibrary.Free(_library); throw new PresentMonUnavailableException($"PresentMon API DLL at '{dllPath}' is missing a required export: {ex.Message}"); }
        catch { NativeLibrary.Free(_library); throw; }
    }
    public PmStatus OpenSession(out nint session) => _openSession(out session);
    public PmStatus CloseSession(nint session) => _closeSession(session);
    public PmStatus StartTrackingProcess(nint session, uint processId) => _startTracking(session, processId);
    public PmStatus StopTrackingProcess(nint session, uint processId) => _stopTracking(session, processId);
    public PmStatus FlushFrames(nint session, uint processId) => _flushFrames(session, processId);
    public PmStatus RegisterFrameQuery(nint session, PmQueryElement[] elements, out nint query, out uint blobSize) => _registerFrameQuery(session, out query, elements, (ulong)elements.Length, out blobSize);
    public PmStatus ConsumeFrames(nint query, uint processId, byte[] blobs, ref uint frameCount) => _consumeFrames(query, processId, blobs, ref frameCount);
    public PmStatus FreeFrameQuery(nint query) => _freeFrameQuery(query);
    public PmStatus GetApiVersion(out PmVersion version) => _getVersion(out version);
    public IReadOnlyDictionary<PmMetric, PmFrameMetricInfo> GetFrameMetricInfo(nint session)
    {
        PmStatus status = _getIntrospection(session, out nint root);
        if (status != PmStatus.Success) throw new BenchmarkException("presentmon_api_introspection_failed", $"pmGetIntrospectionRoot failed with PM_STATUS_{status}.");
        if (root == 0) throw new BenchmarkException("presentmon_api_interop_invalid", "pmGetIntrospectionRoot succeeded but returned a null root pointer.");
        try
        {
            if (!PresentMonIntrospection.TryReadFrameMetricInfo(root, out IReadOnlyDictionary<PmMetric, PmFrameMetricInfo>? metrics, out string? error))
                throw new BenchmarkException("presentmon_api_interop_invalid", error!);
            return metrics;
        }
        finally { _freeIntrospection(root); }
    }
    public void Dispose() { nint library = Interlocked.Exchange(ref _library, 0); if (library != 0) NativeLibrary.Free(library); }
    private T Load<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate PmStatus OpenSessionDelegate(out nint session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate PmStatus CloseSessionDelegate(nint session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate PmStatus StartStopDelegate(nint session, uint processId);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate PmStatus RegisterFrameQueryDelegate(nint session, out nint query, [In, Out] PmQueryElement[] elements, ulong count, out uint blobSize);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate PmStatus ConsumeFramesDelegate(nint query, uint processId, [Out] byte[] blobs, ref uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate PmStatus FreeFrameQueryDelegate(nint query);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate PmStatus GetVersionDelegate(out PmVersion version);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate PmStatus GetIntrospectionDelegate(nint session, out nint root);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate PmStatus FreeIntrospectionDelegate(nint root);
}

[StructLayout(LayoutKind.Sequential)]
public struct PmIntrospectionRoot { public nint Metrics, Enums, Devices, Units; }
[StructLayout(LayoutKind.Sequential)]
public struct PmIntrospectionObjectArray { public nint Data; public nuint Size; }
[StructLayout(LayoutKind.Sequential)]
public struct PmIntrospectionMetric { public PmMetric Id; public PmMetricType Type; public int Unit, PreferredUnitHint; public nint TypeInfo, StatInfo, DeviceMetricInfo; }
[StructLayout(LayoutKind.Sequential)]
public struct PmIntrospectionDataTypeInfo { public PmDataType PolledType, FrameType; public int EnumId; }

public static class PresentMonIntrospection
{
    private const nuint MaximumMetricCount = 4096;
    public static bool TryReadFrameMetricInfo(nint rootPointer, out IReadOnlyDictionary<PmMetric, PmFrameMetricInfo> metrics, out string? error)
    {
        metrics = new Dictionary<PmMetric, PmFrameMetricInfo>(); error = null;
        if (rootPointer == 0) { error = "PresentMon introspection root was null."; return false; }
        try
        {
            PmIntrospectionRoot root = Marshal.PtrToStructure<PmIntrospectionRoot>(rootPointer);
            if (root.Metrics == 0) { error = "PresentMon introspection root had a null pMetrics pointer."; return false; }
            PmIntrospectionObjectArray array = Marshal.PtrToStructure<PmIntrospectionObjectArray>(root.Metrics);
            if (array.Size > MaximumMetricCount) { error = $"PresentMon introspection reported an unreasonable metric count ({array.Size})."; return false; }
            if (array.Size > 0 && array.Data == 0) { error = "PresentMon introspection pMetrics had a null pData pointer with nonzero size."; return false; }
            var result = new Dictionary<PmMetric, PmFrameMetricInfo>();
            for (nuint index = 0; index < array.Size; index++)
            {
                nint metricPointer = Marshal.ReadIntPtr(array.Data, checked((int)(index * (nuint)IntPtr.Size)));
                if (metricPointer == 0) continue;
                PmIntrospectionMetric metric = Marshal.PtrToStructure<PmIntrospectionMetric>(metricPointer);
                if (metric.TypeInfo == 0) continue;
                PmIntrospectionDataTypeInfo typeInfo = Marshal.PtrToStructure<PmIntrospectionDataTypeInfo>(metric.TypeInfo);
                result[metric.Id] = new PmFrameMetricInfo(metric.Type, typeInfo.FrameType);
            }
            metrics = result; return true;
        }
        catch (Exception ex)
        { error = $"PresentMon introspection could not be decoded safely: {ex.Message}"; return false; }
    }
}
