using FrameHub.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

/// <summary>
/// QA-P2-2 / F-2: process-level data directory isolation for tests and
/// before/after performance measurements. Without an override, tests and the
/// app share %APPDATA%\FrameHub (log + session json), contaminating
/// measurements. Each test restores the environment, so sequential MSTest
/// execution is unaffected.
/// </summary>
[TestClass]
public sealed class AppPathsDataDirectoryTests
{
    private string? _previousDataDir;
    private string? _previousLocalDataDir;

    [TestInitialize]
    public void SaveEnvironment()
    {
        _previousDataDir = Environment.GetEnvironmentVariable(AppPaths.DataDirectoryVariable);
        _previousLocalDataDir = Environment.GetEnvironmentVariable(AppPaths.LocalDataDirectoryVariable);
        Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, null);
        Environment.SetEnvironmentVariable(AppPaths.LocalDataDirectoryVariable, null);
    }

    [TestCleanup]
    public void RestoreEnvironment()
    {
        Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, _previousDataDir);
        Environment.SetEnvironmentVariable(AppPaths.LocalDataDirectoryVariable, _previousLocalDataDir);
    }

    [TestMethod]
    public void UserDataDirectory_WithoutOverride_UsesRoamingAppData()
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FrameHub");

        Assert.AreEqual(expected, AppPaths.UserDataDirectory);
    }

    [TestMethod]
    public void UserDataDirectory_WithAbsoluteOverride_UsesOverride()
    {
        string temp = Path.Combine(Path.GetTempPath(), "FrameHub_DataDir_" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, temp);

        try
        {
            Assert.AreEqual(Path.GetFullPath(temp), AppPaths.UserDataDirectory);
            Assert.AreEqual(
                Path.Combine(Path.GetFullPath(temp), "settings.json"),
                AppPaths.GetUserDataFilePath("settings.json"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, null);
        }
    }

    [TestMethod]
    public void UserDataDirectory_WithBlankOrRelativeOverride_FallsBackToDefault()
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FrameHub");

        Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, "   ");
        Assert.AreEqual(expected, AppPaths.UserDataDirectory);

        Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, "relative\\path");
        Assert.AreEqual(expected, AppPaths.UserDataDirectory);
    }

    [TestMethod]
    public void LocalDataDirectory_WithAbsoluteOverride_UsesOverrideIncludingBenchmarks()
    {
        string temp = Path.Combine(Path.GetTempPath(), "FrameHub_LocalDir_" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(AppPaths.LocalDataDirectoryVariable, temp);

        try
        {
            Assert.AreEqual(Path.GetFullPath(temp), AppPaths.LocalDataDirectory);
            Assert.AreEqual(
                Path.Combine(Path.GetFullPath(temp), "Benchmarks"),
                AppPaths.BenchmarkDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.LocalDataDirectoryVariable, null);
        }
    }

    [TestMethod]
    public void DataDirectoryOverride_RedirectsLogResolution()
    {
        string temp = Path.Combine(Path.GetTempPath(), "FrameHub_LogDir_" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, temp);

        try
        {
            Assert.AreEqual(
                Path.Combine(Path.GetFullPath(temp), "FrameHub.log"),
                AppPaths.ResolveUserLogFilePath("FrameHub.log"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, null);
        }
    }
}
