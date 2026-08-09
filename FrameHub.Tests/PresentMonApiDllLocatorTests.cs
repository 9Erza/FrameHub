using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Services.Benchmarking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public sealed class PresentMonApiDllLocatorTests
{
    [TestMethod]
    public void ExplicitPath_WinsOverServiceAndFallback()
    {
        const string path = @"C:\Dev\PresentMonAPI2.dll";
        string result = new PresentMonApiDllLocator(path, new FakeServiceReader(@"C:\Service\PresentMonSharedService.exe"), value => value == path).Locate();
        Assert.AreEqual(path, result);
    }

    [TestMethod]
    public void ServiceDirectory_IsUsedBeforeOfficialFallback()
    {
        const string path = @"C:\Service Folder\PresentMonAPI2.dll";
        string result = new PresentMonApiDllLocator(null, new FakeServiceReader("\"C:\\Service Folder\\PresentMonSharedService.exe\" --service"), value => value == path).Locate();
        Assert.AreEqual(path, result);
    }

    [TestMethod]
    public void OfficialPath_IsUsedAsFallback()
    {
        string result = new PresentMonApiDllLocator(null, new FakeServiceReader(null), value => value == PresentMonApiDllLocator.OfficialPath).Locate();
        Assert.AreEqual(PresentMonApiDllLocator.OfficialPath, result);
    }

    [TestMethod]
    public void MissingDll_ProducesTypedUnavailableError()
    {
        PresentMonUnavailableException exception = Assert.ThrowsExactly<PresentMonUnavailableException>(() => new PresentMonApiDllLocator(null, new FakeServiceReader(null), _ => false).Locate());
        Assert.AreEqual("presentmon_unavailable", exception.Code);
    }

    [TestMethod]
    public void DllLoadFailure_ProducesTypedUnavailableError()
    {
        const string path = @"C:\Dev\PresentMonAPI2.dll";
        PresentMonUnavailableException exception = Assert.ThrowsExactly<PresentMonUnavailableException>(() => new PresentMonApi(new PresentMonApiDllLocator(path, fileExists: value => value == path), _ => throw new BadImageFormatException("test load failure")));
        Assert.AreEqual("presentmon_unavailable", exception.Code);
        StringAssert.Contains(exception.Message, path);
    }

    private sealed class FakeServiceReader(string? path) : IPresentMonServiceConfigReader { public string? TryGetBinaryPath(string serviceName) => path; }
}
