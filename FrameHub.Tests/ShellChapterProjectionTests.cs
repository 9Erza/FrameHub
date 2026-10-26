using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

/// <summary>
/// Session Chapters shell projections: chapter/utility groupings are pure presentation
/// over the same 9 navigation keys, Navigate stays the single activation owner, and all
/// chapter/supporter strings exist in EN+PL (parity is additionally guarded globally).
/// </summary>
[TestClass]
public sealed class ShellChapterProjectionTests
{
    [TestMethod]
    public void ChapterItems_ProjectFirstFourKeysInSessionOrder()
    {
        using var shell = new ShellViewModel();
        CollectionAssert.AreEqual(
            new[] { "Dashboard", "Library", "Session", "Benchmarks" },
            shell.ChapterItems.Select(item => item.Key).ToArray());
    }

    [TestMethod]
    public void UtilityItems_ProjectRemainingFiveKeys()
    {
        using var shell = new ShellViewModel();
        CollectionAssert.AreEqual(
            new[] { "Processes", "Profiles", "Hardware", "Logs", "Settings" },
            shell.UtilityItems.Select(item => item.Key).ToArray());
    }

    [TestMethod]
    public void ChapterAndUtility_TogetherCoverAllNavigationKeysExactlyOnce()
    {
        using var shell = new ShellViewModel();
        var projected = shell.ChapterItems.Concat(shell.UtilityItems).Select(item => item.Key).ToArray();
        CollectionAssert.AreEquivalent(shell.NavigationItems.Select(item => item.Key).ToArray(), projected);
        Assert.AreEqual(projected.Length, projected.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [TestMethod]
    public void Navigate_UpdatesCurrentKeyAndSelection()
    {
        using var shell = new ShellViewModel();
        Assert.AreEqual("Dashboard", shell.CurrentKey);

        shell.NavigateTo("Benchmarks");
        Assert.AreEqual("Benchmarks", shell.CurrentKey);
        Assert.IsTrue(shell.NavigationItems.Single(item => item.Key == "Benchmarks").IsSelected);
        Assert.IsFalse(shell.NavigationItems.Single(item => item.Key == "Dashboard").IsSelected);

        shell.NavigateTo("Settings");
        Assert.AreEqual("Settings", shell.CurrentKey);
    }

    [TestMethod]
    public void ChapterTitles_AreDistinctFromScreenTitlesAndLocalized()
    {
        using var shell = new ShellViewModel();
        var chapters = shell.ChapterItems.ToArray();
        Assert.AreEqual("Overview", chapters[0].ChapterTitle);
        Assert.AreEqual("Prepare", chapters[1].ChapterTitle);
        Assert.AreEqual("Play", chapters[2].ChapterTitle);
        Assert.AreEqual("Verify", chapters[3].ChapterTitle);
        // Non-chapter destinations fall back to the screen title.
        Assert.AreEqual(shell.UtilityItems.First().Title, shell.UtilityItems.First().ChapterTitle);
    }

    [TestMethod]
    public void NewChapterAndSupporterKeys_ExistInBothLanguages()
    {
        string[] required =
        [
            "Shell.Chapter.Overview", "Shell.Chapter.Prepare", "Shell.Chapter.Play", "Shell.Chapter.Verify",
            "Settings.Supporter.Title", "Settings.Supporter.Text", "Settings.Supporter.Action",
        ];
        foreach (string key in required)
        {
            Assert.IsTrue(LocalizationService.EnglishKeys.Contains(key), $"Missing English key {key}");
            Assert.IsTrue(LocalizationService.PolishKeys.Contains(key), $"Missing Polish key {key}");
            Assert.AreNotEqual(key, LocalizationService.Translate(key, "en"));
            Assert.AreNotEqual(key, LocalizationService.Translate(key, "pl"));
        }

        Assert.AreEqual("Przegląd", LocalizationService.Translate("Shell.Chapter.Overview", "pl"));
    }
}
