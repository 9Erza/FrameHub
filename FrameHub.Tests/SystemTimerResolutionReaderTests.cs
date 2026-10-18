using FrameHub.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public sealed class SystemTimerResolutionReaderTests
{
    [TestMethod]
    public void Read_ConvertsHundredNanosecondUnitsToMilliseconds()
    {
        var reader = new SystemTimerResolutionReader(() => (0, 156250u, 5000u, 10000u));

        SystemTimerResolutionSnapshot result = reader.Read();

        Assert.IsTrue(result.Success);
        Assert.AreEqual(15.625, result.MinimumMilliseconds, 0.00001);
        Assert.AreEqual(0.5, result.MaximumMilliseconds, 0.00001);
        Assert.AreEqual(1.0, result.CurrentMilliseconds, 0.00001);
    }

    [TestMethod]
    public void Read_NegativeNtStatusIsFailureAndDoesNotExposeMeasurements()
    {
        var reader = new SystemTimerResolutionReader(() => (unchecked((int)0xC0000001), 156250u, 5000u, 10000u));

        SystemTimerResolutionSnapshot result = reader.Read();

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, result.CurrentMilliseconds);
        StringAssert.Contains(result.Error, "0xC0000001");
    }

    [DataTestMethod]
    [DataRow(0u, 5000u, 10000u)]
    [DataRow(156250u, 0u, 10000u)]
    [DataRow(156250u, 5000u, 0u)]
    public void Read_ZeroNativeValueIsNotPresentedAsValid(uint minimum, uint maximum, uint current)
    {
        SystemTimerResolutionSnapshot result =
            SystemTimerResolutionReader.FromNativeValues(0, minimum, maximum, current);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "zero");
    }

    [TestMethod]
    public void Read_MissingNativeEntryPointDegradesGracefully()
    {
        var reader = new SystemTimerResolutionReader(
            () => throw new EntryPointNotFoundException("Synthetic missing export"));

        SystemTimerResolutionSnapshot result = reader.Read();

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, nameof(EntryPointNotFoundException));
    }

    [TestMethod]
    public void Read_UnexpectedExceptionIsNotSwallowed()
    {
        var reader = new SystemTimerResolutionReader(
            () => throw new InvalidOperationException("Synthetic programmer error"));

        Assert.ThrowsExactly<InvalidOperationException>(() => reader.Read());
    }
}
