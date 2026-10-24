using FrameHub.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;

namespace FrameHub.Tests;

/// <summary>
/// Researcher R1 / dyrektywa §B: cooperative restart. Contract under test:
/// only the verified current executable is ever started (never a composed
/// command), only whitelisted flags are forwarded, concurrent requests collapse
/// into <c>AlreadyInProgress</c>, and any non-<c>Started</c> result means the
/// caller must NOT exit. Pre-fix none of this surface exists (the only restart
/// was elevation-bound <c>RestartAsAdmin</c>), so the negative control is the
/// missing API (CS error), documented in the commit evidence.
/// </summary>
[TestClass]
public sealed class RestartSuccessorTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FrameHub_RestartTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    [TestMethod]
    public void BuildSuccessorArguments_ForwardsOnlyWhitelistedFlags()
    {
        Assert.AreEqual(string.Empty, SettingsService.BuildSuccessorArguments(null));
        Assert.AreEqual(string.Empty, SettingsService.BuildSuccessorArguments([]));
        Assert.AreEqual(
            "--tray --minimized",
            SettingsService.BuildSuccessorArguments(["--tray", "--bogus", "--MINIMIZED", " --tray ", "--startup-helper", "--tray --minimized"]));
    }

    [TestMethod]
    public void TryStartSuccessorProcess_InvalidExecutable_DoesNotStartOrExit()
    {
        using var service = new SettingsService(Path.Combine(_tempDir, "settings.json"));
        SetExePath(service, Path.Combine(_tempDir, "does-not-exist.exe"));

        SettingsService.RestartResult first = service.TryStartSuccessorProcess(["--tray"]);
        Assert.AreEqual(SettingsService.RestartStatus.InvalidExecutable, first.Status);
        Assert.IsNull(first.ProcessId);

        // Failure releases the single-flight: a retry is a fresh rejection, not a stuck gate.
        SettingsService.RestartResult second = service.TryStartSuccessorProcess(["--tray"]);
        Assert.AreEqual(SettingsService.RestartStatus.InvalidExecutable, second.Status);
    }

    [TestMethod]
    public void TryStartSuccessorProcess_WhenRestartInFlight_ReturnsAlreadyInProgress()
    {
        using var service = new SettingsService(Path.Combine(_tempDir, "settings.json"));
        SetRestartInFlight(service, 1);

        try
        {
            SettingsService.RestartResult result = service.TryStartSuccessorProcess(["--tray"]);
            Assert.AreEqual(SettingsService.RestartStatus.AlreadyInProgress, result.Status);
            Assert.IsNull(result.ProcessId);
        }
        finally
        {
            service.ResetRestartState();
        }
    }

    [TestMethod]
    public void TryStartSuccessorProcess_StartsVerifiedExecutableOnce()
    {
        string harmlessExe = Path.Combine(Environment.SystemDirectory, "whoami.exe");
        if (!File.Exists(harmlessExe))
        {
            Assert.Inconclusive("whoami.exe is not available on this machine.");
        }

        using var service = new SettingsService(Path.Combine(_tempDir, "settings.json"));
        SetExePath(service, harmlessExe);

        try
        {
            SettingsService.RestartResult started = service.TryStartSuccessorProcess(["--tray", "--not-a-flag"]);
            Assert.AreEqual(SettingsService.RestartStatus.Started, started.Status);
            Assert.IsNotNull(started.ProcessId);

            // Single-flight stays armed after a successful start: the process is
            // expected to shut down gracefully next, not to start a third instance.
            SettingsService.RestartResult concurrent = service.TryStartSuccessorProcess(["--tray"]);
            Assert.AreEqual(SettingsService.RestartStatus.AlreadyInProgress, concurrent.Status);

            EnsureExited(started.ProcessId.Value);
        }
        finally
        {
            service.ResetRestartState();
        }
    }

    [TestMethod]
    public void ResetRestartState_ReleasesSingleFlight()
    {
        using var service = new SettingsService(Path.Combine(_tempDir, "settings.json"));
        SetRestartInFlight(service, 1);
        service.ResetRestartState();
        SetExePath(service, Path.Combine(_tempDir, "does-not-exist.exe"));

        // After reset a new attempt runs the executable validation instead of
        // reporting a stuck restart.
        Assert.AreEqual(SettingsService.RestartStatus.InvalidExecutable,
            service.TryStartSuccessorProcess(null).Status);
    }

    private static void SetExePath(SettingsService service, string exePath) =>
        typeof(SettingsService).GetField("_exePath", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(service, exePath);

    private static void SetRestartInFlight(SettingsService service, int value) =>
        typeof(SettingsService).GetField("_restartInFlight", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(service, value);

    private static void EnsureExited(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            if (!process.WaitForExit(5000))
            {
                try { process.Kill(); } catch { }
            }
        }
        catch (ArgumentException)
        {
            // Already exited — the expected case for the probe executable.
        }
    }
}
