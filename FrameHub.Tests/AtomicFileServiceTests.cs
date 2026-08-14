using FrameHub.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public sealed class AtomicFileServiceTests
{
    private string _tempDirectory = null!;
    private string _filePath = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.AtomicFileTests", Guid.NewGuid().ToString("N"));
        _filePath = Path.Combine(_tempDirectory, "state.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, recursive: true);
        }
        catch { }
    }

    [TestMethod]
    public void ReplacingFile_PreservesPreviousCompleteVersionAsBackup()
    {
        AtomicFileService.WriteAllTextAtomic(_filePath, "first");
        AtomicFileService.WriteAllTextAtomic(_filePath, "second");

        Assert.AreEqual("second", File.ReadAllText(_filePath));
        Assert.AreEqual("first", File.ReadAllText(_filePath + ".bak"));
    }

    [TestMethod]
    public void ConcurrentWriters_LeaveOneCompletePayloadAndNoTemporaryFiles()
    {
        string[] payloads = Enumerable.Range(0, 40)
            .Select(i => $"{{\"writer\":{i},\"payload\":\"{new string((char)('a' + i % 26), 4096)}\"}}")
            .ToArray();

        Parallel.ForEach(payloads, payload => AtomicFileService.WriteAllTextAtomic(_filePath, payload));

        string actual = File.ReadAllText(_filePath);
        Assert.IsTrue(payloads.Contains(actual), "The destination must contain one whole writer payload, never an interleaved or partial file.");
        Assert.AreEqual(0, Directory.GetFiles(_tempDirectory, "*.tmp").Length);
    }
}
