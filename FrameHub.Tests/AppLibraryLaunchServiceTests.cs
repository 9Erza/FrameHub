using System.Diagnostics;
using System.IO;
using FrameHub.App.Services;
using FrameHub.Core.Models.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public sealed class AppLibraryLaunchServiceTests
{
    private string _tempDirectory = null!;
    private string _fakeExecutablePath = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.LaunchTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _fakeExecutablePath = Path.Combine(_tempDirectory, "testgame.exe");
        File.WriteAllText(_fakeExecutablePath, "binary content");
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { }
        }
    }

    [TestMethod]
    public void Launch_ValidGameItem_InvokesProcessStarterWithCorrectParameters()
    {
        ProcessStartInfo? capturedPsi = null;
        var launchService = new AppLibraryLaunchService(psi =>
        {
            capturedPsi = psi;
            return true;
        });

        var item = new LibraryItem
        {
            Id = "game-1",
            DisplayName = "Test Game",
            Type = LibraryItemType.Game,
            IsEnabled = true,
            ExecutablePath = _fakeExecutablePath
        };

        var result = launchService.Launch(item);

        Assert.IsTrue(result.Success);
        Assert.AreEqual("launched", result.ErrorCode);
        Assert.IsNotNull(capturedPsi);
        Assert.AreEqual(Path.GetFullPath(_fakeExecutablePath), capturedPsi.FileName);
        Assert.AreEqual(_tempDirectory, capturedPsi.WorkingDirectory);
        Assert.IsTrue(capturedPsi.UseShellExecute);
    }

    [TestMethod]
    public void Launch_ValidAppItem_Succeeds()
    {
        ProcessStartInfo? capturedPsi = null;
        var launchService = new AppLibraryLaunchService(psi =>
        {
            capturedPsi = psi;
            return true;
        });

        var item = new LibraryItem
        {
            Id = "app-1",
            DisplayName = "Test App",
            Type = LibraryItemType.App,
            IsEnabled = true,
            ExecutablePath = _fakeExecutablePath
        };

        var result = launchService.Launch(item);

        Assert.IsTrue(result.Success);
        Assert.AreEqual("launched", result.ErrorCode);
        Assert.IsNotNull(capturedPsi);
    }

    [TestMethod]
    public void Launch_NullOrDisabledItem_ReturnsNotLaunchable()
    {
        bool invoked = false;
        var launchService = new AppLibraryLaunchService(_ => { invoked = true; return true; });

        var nullResult = launchService.Launch(null);
        Assert.IsFalse(nullResult.Success);
        Assert.AreEqual("not_launchable", nullResult.ErrorCode);

        var disabledItem = new LibraryItem
        {
            Id = "disabled-1",
            DisplayName = "Disabled Game",
            Type = LibraryItemType.Game,
            IsEnabled = false,
            ExecutablePath = _fakeExecutablePath
        };

        var disabledResult = launchService.Launch(disabledItem);
        Assert.IsFalse(disabledResult.Success);
        Assert.AreEqual("not_launchable", disabledResult.ErrorCode);
        Assert.IsFalse(invoked);
    }

    [TestMethod]
    public void Launch_NonGameOrAppType_ReturnsNotLaunchable()
    {
        bool invoked = false;
        var launchService = new AppLibraryLaunchService(_ => { invoked = true; return true; });

        var backgroundItem = new LibraryItem
        {
            Id = "bg-1",
            DisplayName = "Background Service",
            Type = LibraryItemType.BackgroundApp,
            IsEnabled = true,
            ExecutablePath = _fakeExecutablePath
        };

        var launcherItem = new LibraryItem
        {
            Id = "launcher-1",
            DisplayName = "Game Launcher",
            Type = LibraryItemType.Launcher,
            IsEnabled = true,
            ExecutablePath = _fakeExecutablePath
        };

        Assert.AreEqual("not_launchable", launchService.Launch(backgroundItem).ErrorCode);
        Assert.AreEqual("not_launchable", launchService.Launch(launcherItem).ErrorCode);
        Assert.IsFalse(invoked);
    }

    [TestMethod]
    public void Launch_ExplicitlyEligibleBackgroundApp_UsesTrustedExecutable()
    {
        ProcessStartInfo? captured = null;
        var service = new AppLibraryLaunchService(info => { captured = info; return true; });
        var item = new LibraryItem
        {
            Id = "background-1", DisplayName = "Background", Type = LibraryItemType.BackgroundApp,
            IsEnabled = true, AllowRemoteControl = true, ExecutablePath = _fakeExecutablePath,
            ProcessName = Path.GetFileNameWithoutExtension(_fakeExecutablePath)
        };

        LibraryLaunchResult result = service.Launch(item);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(Path.GetFullPath(_fakeExecutablePath), captured!.FileName);
    }

    [TestMethod]
    public void Launch_MissingExecutablePath_ReturnsNotLaunchable()
    {
        bool invoked = false;
        var launchService = new AppLibraryLaunchService(_ => { invoked = true; return true; });

        var item = new LibraryItem
        {
            Id = "game-no-path",
            DisplayName = "No Path Game",
            Type = LibraryItemType.Game,
            IsEnabled = true,
            ExecutablePath = ""
        };

        var result = launchService.Launch(item);
        Assert.IsFalse(result.Success);
        Assert.AreEqual("not_launchable", result.ErrorCode);
        Assert.IsFalse(invoked);
    }

    [TestMethod]
    public void Launch_NonExistentFile_ReturnsExecutableMissing()
    {
        bool invoked = false;
        var launchService = new AppLibraryLaunchService(_ => { invoked = true; return true; });

        var item = new LibraryItem
        {
            Id = "game-missing-file",
            DisplayName = "Missing File Game",
            Type = LibraryItemType.Game,
            IsEnabled = true,
            ExecutablePath = Path.Combine(_tempDirectory, "non_existent.exe")
        };

        var result = launchService.Launch(item);
        Assert.IsFalse(result.Success);
        Assert.AreEqual("executable_missing", result.ErrorCode);
        Assert.IsFalse(invoked);
    }

    [TestMethod]
    public void Launch_TrustedShortcutItem_LaunchesShortcutInsteadOfExecutable()
    {
        string shortcutPath = Path.Combine(_tempDirectory, "official-game.lnk");
        File.WriteAllText(shortcutPath, "shortcut content");
        string gameExe = Path.Combine(_tempDirectory, "actual-game.exe");
        File.WriteAllText(gameExe, "game binary");

        ProcessStartInfo? capturedPsi = null;
        var launchService = new AppLibraryLaunchService(psi => { capturedPsi = psi; return true; });

        var item = new LibraryItem
        {
            Id = "riot-1",
            DisplayName = "Shortcut Game",
            Type = LibraryItemType.Game,
            IsEnabled = true,
            ExecutablePath = gameExe,
            LaunchPath = shortcutPath
        };

        var result = launchService.Launch(item);

        Assert.IsTrue(result.Success);
        Assert.AreEqual("launched", result.ErrorCode);
        Assert.IsNotNull(capturedPsi);
        Assert.AreEqual(shortcutPath, capturedPsi.FileName, "The trusted shortcut itself must be executed via the shell.");
        Assert.IsTrue(capturedPsi.UseShellExecute);
        Assert.IsTrue(string.IsNullOrEmpty(capturedPsi.Arguments), "FrameHub must never add launch arguments.");
    }

    [TestMethod]
    public void Launch_ShortcutPathWithWrongExtension_IsRejected()
    {
        string fakeShortcut = Path.Combine(_tempDirectory, "not-a-shortcut.exe");
        File.WriteAllText(fakeShortcut, "exe content");
        bool invoked = false;
        var launchService = new AppLibraryLaunchService(_ => { invoked = true; return true; });

        var item = new LibraryItem
        {
            Id = "bad-launch",
            DisplayName = "Bad Launch Target",
            Type = LibraryItemType.Game,
            IsEnabled = true,
            ExecutablePath = fakeShortcut,
            LaunchPath = fakeShortcut
        };

        var result = launchService.Launch(item);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("not_launchable", result.ErrorCode, "LaunchPath must accept trusted .lnk shell shortcuts only.");
        Assert.IsFalse(invoked);
    }

    [TestMethod]
    public void Launch_MissingShortcutFile_ReturnsLaunchTargetMissing()
    {
        bool invoked = false;
        var launchService = new AppLibraryLaunchService(_ => { invoked = true; return true; });

        var item = new LibraryItem
        {
            Id = "missing-shortcut",
            DisplayName = "Missing Shortcut",
            Type = LibraryItemType.Game,
            IsEnabled = true,
            ExecutablePath = _fakeExecutablePath,
            LaunchPath = Path.Combine(_tempDirectory, "missing.lnk")
        };

        var result = launchService.Launch(item);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("launch_target_missing", result.ErrorCode);
        Assert.IsFalse(invoked);
    }

    [TestMethod]
    public void Launch_StarterThrowsException_ReturnsLaunchFailed()
    {
        var launchService = new AppLibraryLaunchService(_ => throw new System.ComponentModel.Win32Exception(5, "Access Denied"));

        var item = new LibraryItem
        {
            Id = "game-fail",
            DisplayName = "Failing Game",
            Type = LibraryItemType.Game,
            IsEnabled = true,
            ExecutablePath = _fakeExecutablePath
        };

        var result = launchService.Launch(item);
        Assert.IsFalse(result.Success);
        Assert.AreEqual("launch_failed", result.ErrorCode);
    }

    [TestMethod]
    public void Launch_SteamGameWithValidAppId_LaunchesThroughSteamProtocolUri()
    {
        ProcessStartInfo? capturedPsi = null;
        var launchService = new AppLibraryLaunchService(psi =>
        {
            capturedPsi = psi;
            return true;
        });

        var item = new LibraryItem
        {
            Id = "steam-cs2",
            DisplayName = "Counter-Strike 2",
            Source = LibrarySource.Steam,
            AppId = "730",
            Type = LibraryItemType.Game,
            IsEnabled = true,
            ExecutablePath = _fakeExecutablePath
        };

        var result = launchService.Launch(item);

        Assert.IsTrue(result.Success);
        Assert.AreEqual("launched", result.ErrorCode);
        Assert.IsNotNull(capturedPsi);
        Assert.AreEqual("steam://run/730", capturedPsi.FileName);
        Assert.IsTrue(capturedPsi.UseShellExecute);
        Assert.IsTrue(string.IsNullOrEmpty(capturedPsi.Arguments));
    }

    [TestMethod]
    public void Launch_SteamGameWithArbitraryNumericAppId_PreservesDiscoveredAppId()
    {
        ProcessStartInfo? capturedPsi = null;
        var launchService = new AppLibraryLaunchService(psi =>
        {
            capturedPsi = psi;
            return true;
        });

        var item = new LibraryItem
        {
            Id = "steam-dota",
            DisplayName = "Dota 2",
            Source = LibrarySource.Steam,
            AppId = "570",
            Type = LibraryItemType.Game,
            IsEnabled = true,
            ExecutablePath = _fakeExecutablePath
        };

        var result = launchService.Launch(item);

        Assert.IsTrue(result.Success);
        Assert.AreEqual("launched", result.ErrorCode);
        Assert.IsNotNull(capturedPsi);
        Assert.AreEqual("steam://run/570", capturedPsi.FileName);
        Assert.IsTrue(capturedPsi.UseShellExecute);
    }

    [TestMethod]
    public void Launch_SteamGameMissingAppId_ReturnsSteamAppIdMissingAndDoesNotDirectLaunch()
    {
        bool invoked = false;
        var launchService = new AppLibraryLaunchService(_ =>
        {
            invoked = true;
            return true;
        });

        var item = new LibraryItem
        {
            Id = "steam-no-appid",
            DisplayName = "Unindexed Steam Game",
            Source = LibrarySource.Steam,
            AppId = null,
            Type = LibraryItemType.Game,
            IsEnabled = true,
            ExecutablePath = _fakeExecutablePath
        };

        var result = launchService.Launch(item);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("steam_appid_missing", result.ErrorCode);
        Assert.IsFalse(invoked, "Direct executable launch must be refused when Steam AppId is missing to preserve VAC/Steam launch context.");
    }

    [TestMethod]
    public void Launch_SteamGameNonNumericAppId_ReturnsSteamAppIdMissing()
    {
        bool invoked = false;
        var launchService = new AppLibraryLaunchService(_ =>
        {
            invoked = true;
            return true;
        });

        var item = new LibraryItem
        {
            Id = "steam-bad-appid",
            DisplayName = "Corrupted Steam Game",
            Source = LibrarySource.Steam,
            AppId = "invalid_id_not_numeric",
            Type = LibraryItemType.Game,
            IsEnabled = true,
            ExecutablePath = _fakeExecutablePath
        };

        var result = launchService.Launch(item);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("steam_appid_missing", result.ErrorCode);
        Assert.IsFalse(invoked);
    }
}
