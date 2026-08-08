using FrameHub.Core.Models;
using FrameHub.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public class ScheduledTaskStartupReaderTests
{
    private static readonly DesiredStartupConfiguration Desired = new(true, StartupWindowMode.Minimized, false);

    [TestMethod]
    public void TaskSchedulerReportsMissing_TaskSnapshotIsReadableAndAbsent()
    {
        var reader = new ScheduledTaskStartupReader(new FakeQuery(new(true, false)), "C:\\FrameHub.exe");
        var snapshot = reader.Read(Desired);
        Assert.IsTrue(snapshot.ReadSucceeded);
        Assert.IsFalse(snapshot.Exists);
    }

    [TestMethod]
    public void TaskSchedulerReportsQueryFailure_TaskSnapshotIsReadFailed()
    {
        var reader = new ScheduledTaskStartupReader(new FakeQuery(new(false, false, Error: "COM failure")), "C:\\FrameHub.exe");
        var snapshot = reader.Read(Desired);
        Assert.IsFalse(snapshot.ReadSucceeded);
        Assert.IsFalse(snapshot.Exists);
    }

    [TestMethod]
    public void MissingTaskResult_DoesNotDependOnLocalizedMessage()
    {
        var reader = new ScheduledTaskStartupReader(new FakeQuery(new(true, false, Error: "System nie może odnaleźć określonego pliku.")), "C:\\FrameHub.exe");
        var snapshot = reader.Read(Desired);
        Assert.IsTrue(snapshot.ReadSucceeded);
        Assert.IsFalse(snapshot.Exists);
    }

    [DataTestMethod]
    [DataRow(unchecked((int)0x80070002), true)]
    [DataRow(unchecked((int)0x80070003), true)]
    [DataRow(unchecked((int)0x80070005), false)]
    public void TaskNotFoundRecognition_UsesStableHResultsOnly(int hresult, bool expected) =>
        Assert.AreEqual(expected, TaskSchedulerComQuery.IsTaskNotFoundHResult(hresult));

    private sealed class FakeQuery(ScheduledTaskQueryResult result) : ITaskSchedulerQuery
    {
        public ScheduledTaskQueryResult Query(string taskName) => result;
    }
}
