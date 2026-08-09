using System.Buffers.Binary;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Services.Benchmarking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public sealed class PresentMonApiFrameSourceTests
{
    [TestMethod]
    public async Task Capture_UsesExactPidAndFrameQueryLifecycle()
    {
        var api = new FakeApi();
        var source = new PresentMonApiFrameSource(() => api);

        PresentMonApiCapture result = await source.CaptureAsync(4242, TimeSpan.FromMilliseconds(30));

        Assert.AreEqual(4242, result.Frames.Single().ProcessId);
        CollectionAssert.AreEqual(new[] { "open", "version", "introspection", "register", "start:4242" }, api.Calls.Take(5).ToArray());
        Assert.IsTrue(api.Calls.IndexOf("flush:4242") > api.Calls.IndexOf("register"));
        CollectionAssert.AreEqual(new[] { "stop:4242", "free", "close", "dispose" }, api.Calls.TakeLast(4).ToArray());
        Assert.AreEqual(3, result.ApiVersion.Split('.').Length);
    }

    [TestMethod]
    public async Task Capture_ReportsTypedPmStatusFailure()
    {
        var api = new FakeApi { StartStatus = PmStatus.ServiceError };
        var source = new PresentMonApiFrameSource(() => api);
        BenchmarkException exception = await Assert.ThrowsExactlyAsync<BenchmarkException>(() => source.CaptureAsync(4242, TimeSpan.FromMilliseconds(10)));
        Assert.AreEqual("presentmon_api_status", exception.Code);
        StringAssert.Contains(exception.Message, "pmStartTrackingProcess");
        CollectionAssert.AreEqual(new[] { "free", "close", "dispose" }, api.Calls.TakeLast(3).ToArray());
    }

    [TestMethod]
    public async Task IntrospectionFailure_ClosesTheOpenedSession()
    {
        var api = new FakeApi { IntrospectionError = new BenchmarkException("presentmon_api_introspection_failed", "test") };
        await Assert.ThrowsExactlyAsync<BenchmarkException>(() => new PresentMonApiFrameSource(() => api).CaptureAsync(4242, TimeSpan.FromMilliseconds(10)));
        CollectionAssert.AreEqual(new[] { "open", "version", "introspection", "close", "dispose" }, api.Calls);
    }

    [TestMethod]
    public async Task ZeroFramePoll_DoesNotReduceNextRequestCapacity()
    {
        var api = new FakeApi { ZeroFirstPoll = true };
        PresentMonApiCapture result = await new PresentMonApiFrameSource(() => api).CaptureAsync(4242, TimeSpan.FromMilliseconds(45));
        Assert.AreEqual(1, result.Frames.Count);
        Assert.IsTrue(api.RequestedFrameCapacities.Count >= 2);
        Assert.IsTrue(api.RequestedFrameCapacities.All(capacity => capacity == 256));
        Assert.IsTrue(result.Diagnostics.ZeroFrameConsumeCalls >= 1);
        Assert.IsTrue(result.Diagnostics.NonZeroFrameConsumeCalls >= 1);
    }

    [TestMethod]
    public async Task MultipleBlobs_AreDecodedUsingTheirOwnBlobBases()
    {
        var api = new FakeApi { FramesInFirstPoll = 2 };
        PresentMonApiCapture result = await new PresentMonApiFrameSource(() => api).CaptureAsync(4242, TimeSpan.FromMilliseconds(25));
        CollectionAssert.AreEqual(new[] { "0xABC", "0xABD" }, result.Frames.Take(2).Select(frame => frame.SwapChainAddress).ToArray());
    }

    [TestMethod]
    public async Task Cancellation_StopsTrackingFreesQueryClosesSessionAndDisposesApi()
    {
        using var cancellation = new CancellationTokenSource();
        var api = new FakeApi { OnConsume = cancellation.Cancel };
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => new PresentMonApiFrameSource(() => api).CaptureAsync(4242, TimeSpan.FromSeconds(5), cancellation.Token));
        CollectionAssert.AreEqual(new[] { "stop:4242", "free", "close", "dispose" }, api.Calls.TakeLast(4).ToArray());
    }

    [TestMethod]
    public async Task ConsumeFailure_StillReleasesAllInitializedResources()
    {
        var api = new FakeApi { ConsumeStatus = PmStatus.ServiceError };
        await Assert.ThrowsExactlyAsync<BenchmarkException>(() => new PresentMonApiFrameSource(() => api).CaptureAsync(4242, TimeSpan.FromMilliseconds(10)));
        CollectionAssert.AreEqual(new[] { "stop:4242", "free", "close", "dispose" }, api.Calls.TakeLast(4).ToArray());
    }

    [TestMethod]
    public async Task QueryRegistrationFailure_ClosesSessionWithoutStopOrFree()
    {
        var api = new FakeApi { RegisterStatus = PmStatus.QueryMalformed };
        await Assert.ThrowsExactlyAsync<BenchmarkException>(() => new PresentMonApiFrameSource(() => api).CaptureAsync(4242, TimeSpan.FromMilliseconds(10)));
        CollectionAssert.AreEqual(new[] { "register", "close", "dispose" }, api.Calls.TakeLast(3).ToArray());
        Assert.IsFalse(api.Calls.Any(call => call.StartsWith("stop:", StringComparison.Ordinal)));
        Assert.IsFalse(api.Calls.Contains("free"));
    }

    [TestMethod]
    public async Task CleanupException_DoesNotPreventLaterCleanup()
    {
        var api = new FakeApi { ThrowOnStop = true };
        PresentMonApiCapture capture = await new PresentMonApiFrameSource(() => api).CaptureAsync(4242, TimeSpan.FromMilliseconds(10));
        CollectionAssert.AreEqual(new[] { "stop:4242", "free", "close", "dispose" }, api.Calls.TakeLast(4).ToArray());
        Assert.IsTrue(capture.Warnings.Any(warning => warning.Contains("pmStopTrackingProcess", StringComparison.Ordinal)));
    }

    private sealed class FakeApi : IPresentMonApi
    {
        public List<string> Calls { get; } = [];
        public PmStatus StartStatus { get; init; } = PmStatus.Success;
        public BenchmarkException? IntrospectionError { get; init; }
        public bool ZeroFirstPoll { get; init; }
        public int FramesInFirstPoll { get; init; } = 1;
        public PmStatus RegisterStatus { get; init; } = PmStatus.Success;
        public PmStatus ConsumeStatus { get; init; } = PmStatus.Success;
        public bool ThrowOnStop { get; init; }
        public Action? OnConsume { get; init; }
        public List<uint> RequestedFrameCapacities { get; } = [];
        private bool _sent;
        private bool _zeroSent;
        private PmQueryElement[] _elements = [];
        public PmStatus OpenSession(out nint session) { Calls.Add("open"); session = 1; return PmStatus.Success; }
        public PmStatus CloseSession(nint session) { Calls.Add("close"); return PmStatus.Success; }
        public PmStatus StartTrackingProcess(nint session, uint processId) { Calls.Add($"start:{processId}"); return StartStatus; }
        public PmStatus StopTrackingProcess(nint session, uint processId) { Calls.Add($"stop:{processId}"); if (ThrowOnStop) throw new InvalidOperationException("test cleanup failure"); return PmStatus.Success; }
        public PmStatus FlushFrames(nint session, uint processId) { Calls.Add($"flush:{processId}"); return PmStatus.Success; }
        public PmStatus RegisterFrameQuery(nint session, PmQueryElement[] elements, out nint query, out uint blobSize)
        {
            Calls.Add("register"); query = RegisterStatus == PmStatus.Success ? 2 : 0; blobSize = RegisterStatus == PmStatus.Success ? 80u : 0u; _elements = elements;
            for (int i = 0; i < elements.Length; i++) { elements[i].DataOffset = (ulong)(i * 8); elements[i].DataSize = 8; }
            return RegisterStatus;
        }
        public PmStatus ConsumeFrames(nint query, uint processId, byte[] blobs, ref uint frameCount)
        {
            Calls.Add($"consume:{processId}");
            OnConsume?.Invoke();
            RequestedFrameCapacities.Add(frameCount);
            if (ConsumeStatus != PmStatus.Success) return ConsumeStatus;
            if (ZeroFirstPoll && !_zeroSent) { _zeroSent = true; frameCount = 0; return PmStatus.Success; }
            if (_sent) { frameCount = 0; return PmStatus.Success; }
            _sent = true;
            for (int i = 0; i < FramesInFirstPoll; i++) WriteFrame(blobs.AsSpan(i * 80, 80), 0xABCu + (uint)i);
            frameCount = (uint)FramesInFirstPoll; return PmStatus.Success;
        }
        public PmStatus FreeFrameQuery(nint query) { Calls.Add("free"); return PmStatus.Success; }
        public PmStatus GetApiVersion(out PmVersion version) { Calls.Add("version"); version = new PmVersion { Major = 3, Minor = 3, Patch = 0, Tag = new byte[22], Hash = new byte[8], Config = new byte[4] }; return PmStatus.Success; }
        public IReadOnlyDictionary<PmMetric, PmFrameMetricInfo> GetFrameMetricInfo(nint session) { Calls.Add("introspection"); if (IntrospectionError is not null) throw IntrospectionError; return Enum.GetValues<PmMetric>().ToDictionary(metric => metric, metric => new PmFrameMetricInfo(PmMetricType.FrameEvent, metric switch { PmMetric.SwapChainAddress or PmMetric.CpuStartQpc => PmDataType.UInt64, PmMetric.PresentRuntime or PmMetric.PresentMode or PmMetric.FrameType => PmDataType.Enum, _ => PmDataType.Double })); }
        public void Dispose() { Calls.Add("dispose"); }
        private void WriteFrame(Span<byte> blob, uint swapChain)
        {
            foreach (PmQueryElement element in _elements)
            {
                Span<byte> value = blob.Slice((int)element.DataOffset, (int)element.DataSize);
                switch (element.Metric)
                {
                    case PmMetric.SwapChainAddress: BinaryPrimitives.WriteUInt64LittleEndian(value, swapChain); break;
                    case PmMetric.PresentRuntime: BinaryPrimitives.WriteUInt32LittleEndian(value, 1); break;
                    case PmMetric.PresentMode: BinaryPrimitives.WriteUInt32LittleEndian(value, 3); break;
                    case PmMetric.CpuStartQpc: BinaryPrimitives.WriteUInt64LittleEndian(value, 1000); break;
                    case PmMetric.BetweenPresents: BitConverter.TryWriteBytes(value, 16.67d); break;
                    case PmMetric.DisplayedTime: BitConverter.TryWriteBytes(value, 16.67d); break;
                    case PmMetric.BetweenDisplayChange: BitConverter.TryWriteBytes(value, 16.67d); break;
                    case PmMetric.FrameType: BinaryPrimitives.WriteUInt32LittleEndian(value, 2); break;
                }
            }
        }
    }
}
