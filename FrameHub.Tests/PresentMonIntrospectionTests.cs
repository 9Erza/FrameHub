using System.Runtime.InteropServices;
using FrameHub.Core.Services.Benchmarking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public sealed class PresentMonIntrospectionTests
{
    [TestMethod]
    public void ValidTree_ExtractsFrameType() { using var tree = NativeTree.Create((PmMetric.SwapChainAddress, PmMetricType.FrameEvent, PmDataType.UInt64)); Assert.IsTrue(PresentMonIntrospection.TryReadFrameMetricInfo(tree.Root, out var metrics, out var error), error); Assert.AreEqual(new PmFrameMetricInfo(PmMetricType.FrameEvent, PmDataType.UInt64), metrics[PmMetric.SwapChainAddress]); }
    [TestMethod]
    public void MultipleMetrics_AreRead() { using var tree = NativeTree.Create((PmMetric.SwapChainAddress, PmMetricType.FrameEvent, PmDataType.UInt64), (PmMetric.BetweenPresents, PmMetricType.DynamicFrame, PmDataType.Double)); Assert.IsTrue(PresentMonIntrospection.TryReadFrameMetricInfo(tree.Root, out var metrics, out _)); Assert.AreEqual(2, metrics.Count); }
    [TestMethod]
    public void NullRoot_IsRejected() => Assert.IsFalse(PresentMonIntrospection.TryReadFrameMetricInfo(0, out _, out _));
    [TestMethod]
    public void NullMetrics_IsRejected() { using var tree = NativeTree.Empty(); tree.WriteRoot(new PmIntrospectionRoot()); Assert.IsFalse(PresentMonIntrospection.TryReadFrameMetricInfo(tree.Root, out _, out _)); }
    [TestMethod]
    public void NullDataWithSize_IsRejected() { using var tree = NativeTree.Empty(); tree.WriteArray(new PmIntrospectionObjectArray { Data = 0, Size = 1 }); tree.WriteRoot(new PmIntrospectionRoot { Metrics = tree.Array }); Assert.IsFalse(PresentMonIntrospection.TryReadFrameMetricInfo(tree.Root, out _, out _)); }
    [TestMethod]
    public void NullMetricElement_IsSkipped() { using var tree = NativeTree.CreateRaw([0]); Assert.IsTrue(PresentMonIntrospection.TryReadFrameMetricInfo(tree.Root, out var metrics, out _)); Assert.AreEqual(0, metrics.Count); }
    [TestMethod]
    public void NullTypeInfo_IsSkipped() { using var tree = NativeTree.Create((PmMetric.FrameType, PmMetricType.FrameEvent, PmDataType.Enum), includeTypeInfo: false); Assert.IsTrue(PresentMonIntrospection.TryReadFrameMetricInfo(tree.Root, out var metrics, out _)); Assert.AreEqual(0, metrics.Count); }
    [TestMethod]
    public void UnreasonableSize_IsRejected() { using var tree = NativeTree.Empty(); tree.WriteArray(new PmIntrospectionObjectArray { Data = tree.Alloc(IntPtr.Size), Size = 4097 }); tree.WriteRoot(new PmIntrospectionRoot { Metrics = tree.Array }); Assert.IsFalse(PresentMonIntrospection.TryReadFrameMetricInfo(tree.Root, out _, out _)); }
    [TestMethod]
    public void FrameQuery_ExcludesUnstableIdentityMetrics() { CollectionAssert.DoesNotContain(PresentMonApiFrameSource.FrameQueryMetrics.ToArray(), PmMetric.Application); CollectionAssert.DoesNotContain(PresentMonApiFrameSource.FrameQueryMetrics.ToArray(), PmMetric.ProcessId); }

    private sealed class NativeTree : IDisposable
    {
        private readonly List<nint> _allocations = [];
        public nint Root { get; private set; }
        public nint Array { get; private set; }
        public static NativeTree Empty() { var tree = new NativeTree(); tree.Root = tree.Alloc(Marshal.SizeOf<PmIntrospectionRoot>()); tree.Array = tree.Alloc(Marshal.SizeOf<PmIntrospectionObjectArray>()); return tree; }
        public static NativeTree Create(params (PmMetric metric, PmMetricType type, PmDataType frameType)[] metrics) => Create(metrics, true);
        public static NativeTree Create((PmMetric metric, PmMetricType type, PmDataType frameType) metric, bool includeTypeInfo) => Create([metric], includeTypeInfo);
        private static NativeTree Create((PmMetric metric, PmMetricType type, PmDataType frameType)[] metrics, bool includeTypeInfo)
        {
            var tree = Empty(); nint pointers = tree.Alloc(IntPtr.Size * metrics.Length); tree.WriteArray(new PmIntrospectionObjectArray { Data = pointers, Size = (nuint)metrics.Length }); tree.WriteRoot(new PmIntrospectionRoot { Metrics = tree.Array });
            for (int i = 0; i < metrics.Length; i++) { nint typeInfo = includeTypeInfo ? tree.Struct(new PmIntrospectionDataTypeInfo { FrameType = metrics[i].frameType }) : 0; nint metric = tree.Struct(new PmIntrospectionMetric { Id = metrics[i].metric, Type = metrics[i].type, TypeInfo = typeInfo }); Marshal.WriteIntPtr(pointers, i * IntPtr.Size, metric); }
            return tree;
        }
        public static NativeTree CreateRaw(nint[] pointers) { var tree = Empty(); nint data = tree.Alloc(IntPtr.Size * pointers.Length); for (int i = 0; i < pointers.Length; i++) Marshal.WriteIntPtr(data, i * IntPtr.Size, pointers[i]); tree.WriteArray(new PmIntrospectionObjectArray { Data = data, Size = (nuint)pointers.Length }); tree.WriteRoot(new PmIntrospectionRoot { Metrics = tree.Array }); return tree; }
        public nint Alloc(int size) { nint value = Marshal.AllocHGlobal(size); _allocations.Add(value); Span<byte> clear = new byte[size]; Marshal.Copy(clear.ToArray(), 0, value, size); return value; }
        public nint Struct<T>(T value) where T : struct { nint pointer = Alloc(Marshal.SizeOf<T>()); Marshal.StructureToPtr(value, pointer, false); return pointer; }
        public void WriteRoot(PmIntrospectionRoot value) => Marshal.StructureToPtr(value, Root, false);
        public void WriteArray(PmIntrospectionObjectArray value) => Marshal.StructureToPtr(value, Array, false);
        public void Dispose() { foreach (nint pointer in _allocations) Marshal.FreeHGlobal(pointer); }
    }
}
