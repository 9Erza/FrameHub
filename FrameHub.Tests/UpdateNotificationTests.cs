using FrameHub.App.Services;
using FrameHub.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;

namespace FrameHub.Tests;

/// <summary>
/// v0.7.1 update notification UX: once-per-process automatic check gate,
/// presentation timing (visible window only), silent automatic results,
/// ungated manual checks, styled modal wiring and EN/PL localization parity.
/// No real GitHub HTTP calls: the check delegate is synthetic.
/// </summary>
[TestClass]
public sealed class UpdateCheckSessionTests
{
    private static UpdateCheckResult Available(string version = "9.9.9") => new()
    {
        IsUpdateAvailable = true,
        LatestVersion = version,
        ReleaseUrl = "https://github.com/9Erza/FrameHub/releases"
    };

    private static UpdateCheckResult UpToDate() => new() { IsUpdateAvailable = false, LatestVersion = "0.0.1" };

    private static UpdateCheckResult Error() => new() { Error = "network unreachable" };

    [TestMethod]
    public async Task AutomaticCheck_RunsAtMostOncePerSessionInstance()
    {
        int calls = 0;
        var session = new UpdateCheckSession(() => { calls++; return Task.FromResult(UpToDate()); });

        var first = await session.TryRunAutomaticCheckAsync(updatesEnabled: true);
        var second = await session.TryRunAutomaticCheckAsync(updatesEnabled: true);
        var third = await session.TryRunAutomaticCheckAsync(updatesEnabled: true);

        Assert.IsNotNull(first, "First presentation must run the automatic check.");
        Assert.IsNull(second, "Second presentation (e.g. reopening from tray) must not repeat the automatic check.");
        Assert.IsNull(third, "Later presentations must never repeat the automatic check.");
        Assert.AreEqual(1, calls, "The update service may be invoked at most once per process session.");
        Assert.IsTrue(session.HasAutomaticCheckBegun);
    }

    [TestMethod]
    public async Task DisabledSetting_NeverRunsAutomaticCheck()
    {
        int calls = 0;
        var session = new UpdateCheckSession(() => { calls++; return Task.FromResult(Available()); });

        var result = await session.TryRunAutomaticCheckAsync(updatesEnabled: false);

        Assert.IsNull(result, "CheckForUpdates == false must never run an automatic check.");
        Assert.AreEqual(0, calls);
        Assert.IsFalse(session.HasAutomaticCheckBegun, "A disabled automatic check must not consume the process opportunity.");
    }

    [TestMethod]
    public async Task ManualCheck_RemainsAvailableAfterAutomaticCheck()
    {
        int calls = 0;
        var session = new UpdateCheckSession(() => { calls++; return Task.FromResult(UpToDate()); });

        await session.TryRunAutomaticCheckAsync(updatesEnabled: true);
        var manualOne = await session.RunManualCheckAsync();
        var manualTwo = await session.RunManualCheckAsync();

        Assert.IsNotNull(manualOne);
        Assert.IsNotNull(manualTwo);
        Assert.AreEqual(3, calls, "Manual 'Check now' is never gated by the once-per-process automatic rule.");
    }

    [TestMethod]
    public void AutomaticNoUpdate_IsSilent()
    {
        Assert.IsFalse(UpdateCheckSession.ShouldPresentUpdateDialog(UpToDate()),
            "An up-to-date automatic result must never present UI.");
    }

    [TestMethod]
    public void AutomaticError_IsSilent()
    {
        Assert.IsFalse(UpdateCheckSession.ShouldPresentUpdateDialog(Error()),
            "A failed automatic check must never present UI.");
    }

    [TestMethod]
    public void AutomaticNullResult_IsSilent()
    {
        Assert.IsFalse(UpdateCheckSession.ShouldPresentUpdateDialog(null),
            "No check result (disabled or already consumed) must never present UI.");
    }

    [TestMethod]
    public void AvailableUpdate_RequestsExactlyOneModalPresentation()
    {
        Assert.IsTrue(UpdateCheckSession.ShouldPresentUpdateDialog(Available()),
            "An available update must request the styled modal exactly once per check result.");
    }
}

[TestClass]
public sealed class UpdateNotificationPresentationTests
{
    private static string? FindRepoRoot()
    {
        string? directory = Path.GetDirectoryName(typeof(UpdateNotificationPresentationTests).Assembly.Location);
        while (directory != null && !Directory.Exists(Path.Combine(directory, "FrameHub.App")))
        {
            directory = Path.GetDirectoryName(directory);
        }
        return directory;
    }

    [TestMethod]
    public void UpdateDialogXaml_UsesFrameHubStylingAndSingleImplementation()
    {
        string? repoRoot = FindRepoRoot();
        if (repoRoot == null) Assert.Inconclusive("Repository source root was not discoverable from the test assembly location.");

        string xaml = File.ReadAllText(Path.Combine(repoRoot!, "FrameHub.App", "Views", "UpdateAvailableDialog.xaml"));
        StringAssert.Contains(xaml, "FrameHubLogo.png", "Update dialog must reuse the official FrameHub logo asset.");
        StringAssert.Contains(xaml, "PrimaryButton", "Update dialog must reuse the FrameHub primary button style.");
        StringAssert.Contains(xaml, "SecondaryButton", "Update dialog must reuse the FrameHub secondary button style.");
        StringAssert.Contains(xaml, "CenterOwner", "Update dialog must center on its owner window.");
        StringAssert.Contains(xaml, "IsDefault=\"True\"", "Open release page must be the default (Enter) action.");
        StringAssert.Contains(xaml, "IsCancel=\"True\"", "Later must be the cancel (Escape) action.");
        StringAssert.Contains(xaml, "OpenReleaseButton", "Update dialog must expose the open-release action.");
        StringAssert.Contains(xaml, "LaterButton", "Update dialog must expose the later action.");

        string codeBehind = File.ReadAllText(Path.Combine(repoRoot!, "FrameHub.App", "Views", "UpdateAvailableDialog.xaml.cs"));
        StringAssert.Contains(codeBehind, "UpdateService.OpenReleasePage", "Open release page must reuse existing UpdateService semantics.");
    }

    [TestMethod]
    public void MainWindow_TriggersAutomaticCheckOnlyOnRealPresentation()
    {
        string? repoRoot = FindRepoRoot();
        if (repoRoot == null) Assert.Inconclusive("Repository source root was not discoverable from the test assembly location.");

        string code = File.ReadAllText(Path.Combine(repoRoot!, "FrameHub.App", "MainWindow.xaml.cs"));
        StringAssert.Contains(code, "RunAutomaticUpdateCheck();", "Window lifecycle events must trigger the automatic update check.");
        StringAssert.Contains(code, "if (!IsVisible || WindowState == WindowState.Minimized) return;",
            "Hidden tray startup and minimized startup must never trigger the automatic check or its modal.");

        int triggers = CountOccurrences(code, "RunAutomaticUpdateCheck();");
        Assert.AreEqual(3, triggers,
            "Exactly the Loaded, StateChanged (restore) and ShowFromTray paths may trigger the automatic check.");

        StringAssert.Contains(code, "shellViewModel.UpdateAvailableRequested += ShowUpdateDialog;",
            "MainWindow must host the styled update dialog for the ShellViewModel automatic check.");
    }

    [TestMethod]
    public void ShellViewModel_OwnsOncePerProcessGateAndSilentAutomaticResult()
    {
        string? repoRoot = FindRepoRoot();
        if (repoRoot == null) Assert.Inconclusive("Repository source root was not discoverable from the test assembly location.");

        string code = File.ReadAllText(Path.Combine(repoRoot!, "FrameHub.App", "ViewModels", "ShellViewModel.cs"));
        StringAssert.Contains(code, "UpdateCheckSession", "ShellViewModel must own the once-per-process automatic update check session.");
        StringAssert.Contains(code, "TryRunAutomaticCheckAsync", "ShellViewModel must route the automatic check through the once-per-process gate.");
        StringAssert.Contains(code, "ShouldPresentUpdateDialog", "Automatic results must be filtered so only available updates present UI.");
        StringAssert.Contains(code, "RunAutomaticUpdateCheckIfEligibleAsync", "ShellViewModel must expose the presentation-triggered automatic check entry point.");
    }

    [TestMethod]
    public void SettingsViewModel_ManualCheck_UsesStyledDialog()
    {
        string? repoRoot = FindRepoRoot();
        if (repoRoot == null) Assert.Inconclusive("Repository source root was not discoverable from the test assembly location.");

        string code = File.ReadAllText(Path.Combine(repoRoot!, "FrameHub.App", "ViewModels", "SettingsViewModel.cs"));
        StringAssert.Contains(code, "UpdateAvailableDialog.Present", "Manual 'Check now' must use the same styled modal as the automatic check.");
        Assert.IsFalse(code.Contains("OpenReleaseQuestion"), "The legacy MessageBox update question must be fully removed.");
    }

    [TestMethod]
    public void UpdateDialogLocalization_HasEnglishAndPolishParity()
    {
        string[] keys = ["Update.Dialog.Title", "Update.Dialog.Heading", "Update.Dialog.CurrentVersion", "Update.Dialog.OpenRelease", "Update.Dialog.Later"];
        foreach (string key in keys)
        {
            Assert.IsTrue(LocalizationService.EnglishKeys.Contains(key), $"Missing English key {key}");
            Assert.IsTrue(LocalizationService.PolishKeys.Contains(key), $"Missing Polish key {key}");
        }

        Assert.AreEqual("FrameHub update", LocalizationService.Translate("Update.Dialog.Title", "en"));
        Assert.AreEqual("Aktualizacja FrameHub", LocalizationService.Translate("Update.Dialog.Title", "pl"));
        Assert.AreEqual("A new version is available", LocalizationService.Translate("Update.Dialog.Heading", "en"));
        Assert.AreEqual("Dostępna jest nowa wersja", LocalizationService.Translate("Update.Dialog.Heading", "pl"));
        Assert.AreEqual("Open release page", LocalizationService.Translate("Update.Dialog.OpenRelease", "en"));
        Assert.AreEqual("Otwórz stronę wydania", LocalizationService.Translate("Update.Dialog.OpenRelease", "pl"));
        Assert.AreEqual("Later", LocalizationService.Translate("Update.Dialog.Later", "en"));
        Assert.AreEqual("Później", LocalizationService.Translate("Update.Dialog.Later", "pl"));
    }

    private static int CountOccurrences(string text, string fragment)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += fragment.Length;
        }
        return count;
    }
}
