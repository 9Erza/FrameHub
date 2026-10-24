using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using FrameHub.Core.Models.SessionOptimization;
using FrameHub.Core.Services.SessionOptimization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public class ProcessSuspendServiceTests
{
    [TestMethod]
    public void SuspendProcesses_WhenCandidateIsForegroundProcess_BlocksSuspensionFailClosed()
    {
        // Arrange
        var suspendedHandles = new List<int>();
        int targetPid = 5001;
        var startTime = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

        var service = new ProcessSuspendService(
            observationProvider: null,
            getForegroundProcessId: () => targetPid,
            openProcess: pid => new IntPtr(pid),
            ntSuspendProcess: handle =>
            {
                suspendedHandles.Add(handle.ToInt32());
                return 0;
            },
            closeHandle: _ => { },
            getProcessStartTimeUtc: _ => startTime);

        var candidate = CreateCandidate(targetPid, startTime);

        // Act
        SessionActionResult result = service.SuspendProcesses([candidate]);

        // Assert
        Assert.AreEqual(0, result.SuccessCount);
        Assert.AreEqual(1, result.FailedCount);
        Assert.AreEqual(0, result.Records.Count);
        Assert.AreEqual(0, suspendedHandles.Count);
        Assert.IsTrue(result.Messages[0].Contains("foreground window", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void SuspendProcesses_WhenCandidateIsNotForegroundProcess_SuspendsSuccessfully()
    {
        // Arrange
        var suspendedHandles = new List<int>();
        int targetPid = 5002;
        int otherForegroundPid = 9999;
        var startTime = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

        var service = new ProcessSuspendService(
            observationProvider: null,
            getForegroundProcessId: () => otherForegroundPid,
            openProcess: pid => new IntPtr(pid),
            ntSuspendProcess: handle =>
            {
                suspendedHandles.Add(handle.ToInt32());
                return 0;
            },
            closeHandle: _ => { },
            getProcessStartTimeUtc: _ => startTime);

        var candidate = CreateCandidate(targetPid, startTime);

        // Act
        SessionActionResult result = service.SuspendProcesses([candidate]);

        // Assert
        Assert.AreEqual(1, result.SuccessCount);
        Assert.AreEqual(0, result.FailedCount);
        Assert.AreEqual(1, result.Records.Count);
        Assert.AreEqual(targetPid, result.Records[0].ProcessId);
        Assert.AreEqual(1, suspendedHandles.Count);
        Assert.AreEqual(targetPid, suspendedHandles[0]);
    }

    [TestMethod]
    public void SuspendProcesses_WhenNoForegroundWindowActive_SuspendsSuccessfully()
    {
        // Arrange
        var suspendedHandles = new List<int>();
        int targetPid = 5003;
        var startTime = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

        var service = new ProcessSuspendService(
            observationProvider: null,
            getForegroundProcessId: () => null, // null represents IntPtr.Zero (no foreground window active)
            openProcess: pid => new IntPtr(pid),
            ntSuspendProcess: handle =>
            {
                suspendedHandles.Add(handle.ToInt32());
                return 0;
            },
            closeHandle: _ => { },
            getProcessStartTimeUtc: _ => startTime);

        var candidate = CreateCandidate(targetPid, startTime);

        // Act
        SessionActionResult result = service.SuspendProcesses([candidate]);

        // Assert
        Assert.AreEqual(1, result.SuccessCount);
        Assert.AreEqual(0, result.FailedCount);
        Assert.AreEqual(1, result.Records.Count);
        Assert.AreEqual(1, suspendedHandles.Count);
        Assert.AreEqual(targetPid, suspendedHandles[0]);
    }

    [TestMethod]
    public void SuspendProcesses_WhenForegroundChangesBetweenCandidates_EvaluatesFreshlyPerCandidate()
    {
        // Arrange
        var suspendedHandles = new List<int>();
        int pid1 = 6001;
        int pid2 = 6002;
        var startTime = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

        var candidate1 = CreateCandidate(pid1, startTime, "worker1.exe");
        var candidate2 = CreateCandidate(pid2, startTime, "worker2.exe");

        // Foreground focus sequence: candidate1 is focused first, then focus shifts away to 9999 for candidate2
        var foregroundSequence = new Queue<int?>([pid1, 9999]);

        var service = new ProcessSuspendService(
            observationProvider: null,
            getForegroundProcessId: () => foregroundSequence.Count > 0 ? foregroundSequence.Dequeue() : null,
            openProcess: pid => new IntPtr(pid),
            ntSuspendProcess: handle =>
            {
                suspendedHandles.Add(handle.ToInt32());
                return 0;
            },
            closeHandle: _ => { },
            getProcessStartTimeUtc: _ => startTime);

        // Act
        SessionActionResult result = service.SuspendProcesses([candidate1, candidate2]);

        // Assert: candidate1 blocked due to foreground; candidate2 suspended after focus change
        Assert.AreEqual(1, result.SuccessCount);
        Assert.AreEqual(1, result.FailedCount);
        Assert.AreEqual(1, result.Records.Count);
        Assert.AreEqual(pid2, result.Records[0].ProcessId);
        Assert.AreEqual(1, suspendedHandles.Count);
        Assert.AreEqual(pid2, suspendedHandles[0]);
    }

    [TestMethod]
    public void SuspendProcesses_WhenForegroundDetectionThrows_OperatesFailClosed()
    {
        // Arrange
        var suspendedHandles = new List<int>();
        int targetPid = 5004;
        var startTime = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

        var service = new ProcessSuspendService(
            observationProvider: null,
            getForegroundProcessId: () => throw new Win32Exception(5, "Simulated access denied"),
            openProcess: pid => new IntPtr(pid),
            ntSuspendProcess: handle =>
            {
                suspendedHandles.Add(handle.ToInt32());
                return 0;
            },
            closeHandle: _ => { },
            getProcessStartTimeUtc: _ => startTime);

        var candidate = CreateCandidate(targetPid, startTime);

        // Act
        SessionActionResult result = service.SuspendProcesses([candidate]);

        // Assert: fail-closed prevents suspension when detection encounters an unexpected error
        Assert.AreEqual(0, result.SuccessCount);
        Assert.AreEqual(1, result.FailedCount);
        Assert.AreEqual(0, result.Records.Count);
        Assert.AreEqual(0, suspendedHandles.Count);
        Assert.IsTrue(result.Messages[0].Contains("fail-closed", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void SuspendProcesses_WhenIdentityValidationFails_BlocksSuspension()
    {
        // Arrange
        var suspendedHandles = new List<int>();
        int targetPid = 5005;
        var candidateStartTime = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        var actualStartTime = new DateTime(2026, 10, 18, 12, 5, 0, DateTimeKind.Utc); // Mismatch!

        var service = new ProcessSuspendService(
            observationProvider: null,
            getForegroundProcessId: () => 9999, // Not foreground
            openProcess: pid => new IntPtr(pid),
            ntSuspendProcess: handle =>
            {
                suspendedHandles.Add(handle.ToInt32());
                return 0;
            },
            closeHandle: _ => { },
            getProcessStartTimeUtc: _ => actualStartTime);

        var candidate = CreateCandidate(targetPid, candidateStartTime);

        // Act
        SessionActionResult result = service.SuspendProcesses([candidate]);

        // Assert
        Assert.AreEqual(0, result.SuccessCount);
        Assert.AreEqual(1, result.FailedCount);
        Assert.AreEqual(0, result.Records.Count);
        Assert.AreEqual(0, suspendedHandles.Count);
        Assert.IsTrue(result.Messages[0].Contains("identity changed", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void SuspendProcesses_WhenFocusChangesToCandidateDuringOpenAndIdentityCheck_BlocksSuspension()
    {
        // Arrange: Focus was elsewhere during initial state, but switched to candidate during open/identity check.
        // The fresh check placed after start time validation directly before NtSuspendProcess must catch this.
        var suspendedHandles = new List<int>();
        int targetPid = 5006;
        var startTime = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        bool identityChecked = false;

        var service = new ProcessSuspendService(
            observationProvider: null,
            getForegroundProcessId: () => identityChecked ? targetPid : 9999,
            openProcess: pid => new IntPtr(pid),
            ntSuspendProcess: handle =>
            {
                suspendedHandles.Add(handle.ToInt32());
                return 0;
            },
            closeHandle: _ => { },
            getProcessStartTimeUtc: _ =>
            {
                identityChecked = true;
                return startTime;
            });

        var candidate = CreateCandidate(targetPid, startTime);

        // Act
        SessionActionResult result = service.SuspendProcesses([candidate]);

        // Assert: fresh check caught focus change right before mutation and blocked suspension
        Assert.AreEqual(0, result.SuccessCount);
        Assert.AreEqual(1, result.FailedCount);
        Assert.AreEqual(0, result.Records.Count);
        Assert.AreEqual(0, suspendedHandles.Count);
        Assert.IsTrue(result.Messages[0].Contains("foreground window", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void SuspendProcesses_WhenForegroundWindowLookupFailsOnNonZeroHwnd_BlocksSuspensionFailClosed()
    {
        // Arrange: user32!GetForegroundWindow returns a non-zero HWND (window exists), but GetWindowThreadProcessId
        // fails (threadId==0 / pid==0). Must operate fail-closed and block suspension instead of treating as null/no window.
        var suspendedHandles = new List<int>();
        int targetPid = 5007;
        var startTime = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

        var service = new ProcessSuspendService(
            observationProvider: null,
            openProcess: pid => new IntPtr(pid),
            ntSuspendProcess: handle =>
            {
                suspendedHandles.Add(handle.ToInt32());
                return 0;
            },
            closeHandle: _ => { },
            getProcessStartTimeUtc: _ => startTime,
            getForegroundWindow: () => new IntPtr(0xCAFE),
            getWindowThreadProcessId: (IntPtr hwnd, out uint pid) =>
            {
                pid = 0;
                return 0; // Failure
            });

        var candidate = CreateCandidate(targetPid, startTime);

        // Act
        SessionActionResult result = service.SuspendProcesses([candidate]);

        // Assert: fail-closed blocks suspension when lookup fails on active window
        Assert.AreEqual(0, result.SuccessCount);
        Assert.AreEqual(1, result.FailedCount);
        Assert.AreEqual(0, result.Records.Count);
        Assert.AreEqual(0, suspendedHandles.Count);
        Assert.IsTrue(result.Messages[0].Contains("fail-closed", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void SuspendProcesses_WhenForegroundWindowIsZeroViaUser32Seam_SuspendsSuccessfully()
    {
        // Arrange: user32!GetForegroundWindow returns IntPtr.Zero (no foreground window active). Safe to proceed.
        var suspendedHandles = new List<int>();
        int targetPid = 5008;
        var startTime = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

        var service = new ProcessSuspendService(
            observationProvider: null,
            openProcess: pid => new IntPtr(pid),
            ntSuspendProcess: handle =>
            {
                suspendedHandles.Add(handle.ToInt32());
                return 0;
            },
            closeHandle: _ => { },
            getProcessStartTimeUtc: _ => startTime,
            getForegroundWindow: () => IntPtr.Zero,
            getWindowThreadProcessId: (IntPtr hwnd, out uint pid) =>
            {
                pid = 0;
                return 0;
            });

        var candidate = CreateCandidate(targetPid, startTime);

        // Act
        SessionActionResult result = service.SuspendProcesses([candidate]);

        // Assert: suspends safely when no foreground window exists
        Assert.AreEqual(1, result.SuccessCount);
        Assert.AreEqual(0, result.FailedCount);
        Assert.AreEqual(1, result.Records.Count);
        Assert.AreEqual(1, suspendedHandles.Count);
        Assert.AreEqual(targetPid, suspendedHandles[0]);
    }

    [TestMethod]
    public void SuspendProcesses_WhenForegroundMatchesCandidateViaUser32Seam_BlocksSuspension()
    {
        // Arrange: user32 seam resolves candidate PID as current foreground process
        var suspendedHandles = new List<int>();
        int targetPid = 5009;
        var startTime = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

        var service = new ProcessSuspendService(
            observationProvider: null,
            openProcess: pid => new IntPtr(pid),
            ntSuspendProcess: handle =>
            {
                suspendedHandles.Add(handle.ToInt32());
                return 0;
            },
            closeHandle: _ => { },
            getProcessStartTimeUtc: _ => startTime,
            getForegroundWindow: () => new IntPtr(0xBEEF),
            getWindowThreadProcessId: (IntPtr hwnd, out uint pid) =>
            {
                pid = (uint)targetPid;
                return 42;
            });

        var candidate = CreateCandidate(targetPid, startTime);

        // Act
        SessionActionResult result = service.SuspendProcesses([candidate]);

        // Assert
        Assert.AreEqual(0, result.SuccessCount);
        Assert.AreEqual(1, result.FailedCount);
        Assert.AreEqual(0, result.Records.Count);
        Assert.AreEqual(0, suspendedHandles.Count);
        Assert.IsTrue(result.Messages[0].Contains("foreground window", StringComparison.OrdinalIgnoreCase));
    }

    private static SuspendCandidate CreateCandidate(int pid, DateTime startTime, string name = "worker.exe") => new()
    {
        ProcessId = pid,
        ProcessName = name,
        ProcessStartTimeUtc = startTime,
        RuleId = "test-rule",
        RuleName = "Test Rule",
        ExecutablePath = @"C:\Program Files\Worker\worker.exe"
    };

    // REA F9: TryResume previously called the static OpenProcess/NtResumeProcess/
    // CloseHandle directly, so the resume path could not be tested through the
    // injected seams. The tests below use the real current test process for the
    // read-only identity pre-check (no OS mutation: every mutating native call is
    // faked) and prove the resume decision matrix through the delegates.

    private static SuspendedProcessRecord CreateResumeRecord(int pid, string name, DateTime startTimeUtc) => new()
    {
        ProcessId = pid,
        ProcessName = name,
        ProcessStartTimeUtc = startTimeUtc,
        RuleId = "test-rule",
        RuleName = "Test Rule"
    };

    private static (int CurrentPid, string CurrentName, DateTime CurrentStartUtc) CurrentProcessIdentity()
    {
        using var current = Process.GetCurrentProcess();
        return (current.Id, current.ProcessName, current.StartTime.ToUniversalTime());
    }

    [TestMethod]
    public void ResumeProcesses_WhenIdentityMatches_ResumesThroughInjectedDelegates()
    {
        // Arrange
        var (pid, name, startUtc) = CurrentProcessIdentity();
        var resumedHandles = new List<int>();
        var closedHandles = new List<int>();

        var service = new ProcessSuspendService(
            observationProvider: null,
            openProcess: _ => new IntPtr(0xCAFE),
            ntResumeProcess: handle =>
            {
                resumedHandles.Add(handle.ToInt32());
                return 0;
            },
            closeHandle: handle => closedHandles.Add(handle.ToInt32()),
            getProcessStartTimeUtc: _ => startUtc);

        // Act
        SessionActionResult result = service.ResumeProcesses([CreateResumeRecord(pid, name, startUtc)]);

        // Assert
        Assert.AreEqual(1, result.SuccessCount);
        Assert.AreEqual(0, result.FailedCount);
        Assert.AreEqual(1, resumedHandles.Count);
        Assert.AreEqual(0xCAFE, resumedHandles[0]);
        Assert.AreEqual(1, closedHandles.Count, "The resume handle must always be released.");
        Assert.AreEqual(0xCAFE, closedHandles[0]);
    }

    [TestMethod]
    public void ResumeProcesses_WhenHandleStartTimeDiffers_DoesNotResumeFailClosed()
    {
        // Arrange: pre-check passes on the real identity, but the handle recheck
        // sees a different instance -> fail closed without calling NtResumeProcess.
        var (pid, name, startUtc) = CurrentProcessIdentity();
        var resumedHandles = new List<int>();
        DateTime staleStartUtc = startUtc.AddSeconds(-30);

        var service = new ProcessSuspendService(
            observationProvider: null,
            openProcess: _ => new IntPtr(0xCAFE),
            ntResumeProcess: handle =>
            {
                resumedHandles.Add(handle.ToInt32());
                return 0;
            },
            closeHandle: _ => { },
            getProcessStartTimeUtc: _ => staleStartUtc);

        // Act
        SessionActionResult result = service.ResumeProcesses([CreateResumeRecord(pid, name, startUtc)]);

        // Assert
        Assert.AreEqual(0, result.SuccessCount);
        Assert.AreEqual(0, resumedHandles.Count, "NtResumeProcess must not run after an identity mismatch.");
        Assert.AreEqual(1, result.StaleProcessCount);
    }

    [TestMethod]
    public void ResumeProcesses_WhenNtResumeFails_ReportsFailureWithStatusCode()
    {
        // Arrange
        var (pid, name, startUtc) = CurrentProcessIdentity();
        const int denied = unchecked((int)0xC0000022);

        var service = new ProcessSuspendService(
            observationProvider: null,
            openProcess: _ => new IntPtr(0xCAFE),
            ntResumeProcess: _ => denied,
            closeHandle: _ => { },
            getProcessStartTimeUtc: _ => startUtc);

        // Act
        SessionActionResult result = service.ResumeProcesses([CreateResumeRecord(pid, name, startUtc)]);

        // Assert
        Assert.AreEqual(0, result.SuccessCount);
        Assert.AreEqual(1, result.FailedCount);
        StringAssert.Contains(result.Messages[0], "C0000022");
    }

    [TestMethod]
    public void ResumeProcesses_WhenOpenFailsButProcessExists_ReportsFailureNotNotFound()
    {
        // Arrange: OpenProcess denied, but the read-only identity probe still sees
        // the live current process -> Failed (not ProcessNotFound).
        var (pid, name, startUtc) = CurrentProcessIdentity();

        var service = new ProcessSuspendService(
            observationProvider: null,
            openProcess: _ => IntPtr.Zero,
            ntResumeProcess: _ => 0,
            closeHandle: _ => { },
            getProcessStartTimeUtc: _ => startUtc);

        // Act
        SessionActionResult result = service.ResumeProcesses([CreateResumeRecord(pid, name, startUtc)]);

        // Assert
        Assert.AreEqual(0, result.SuccessCount);
        Assert.AreEqual(1, result.FailedCount);
        Assert.AreEqual(0, result.ResolvedCount);
    }
}