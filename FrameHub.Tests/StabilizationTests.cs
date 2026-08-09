using FrameHub.App.Services;
using FrameHub.Core.Models;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Services;
using FrameHub.Core.Services.GameOptimization;
using FrameHub.Core.Services.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public sealed class LibraryStabilizationTests
{
    [TestMethod]
    public void SteamworksRedistributables_AreNotSupportedLibraryItems() =>
        Assert.IsFalse(LibraryItemFilter.IsSupportedLibraryItem(new LibraryItem { Source = LibrarySource.Steam, AppId = "228980", DisplayName = "Steamworks Common Redistributables" }));

    [TestMethod]
    public void OtherSteamItems_AreNotHiddenByTheConservativeFilter() =>
        Assert.IsTrue(LibraryItemFilter.IsSupportedLibraryItem(new LibraryItem { Source = LibrarySource.Steam, AppId = "730", DisplayName = "Counter-Strike 2" }));
}

[TestClass]
public sealed class ProfileIdentityTests
{
    [TestMethod] public void SameNameAndPath_Matches() => Assert.IsTrue(ProfileService.MatchesIdentity(Profile(@"C:\Games\One\game.exe"), "game", @"c:\games\one\GAME.exe"));
    [TestMethod] public void SameNameDifferentPath_DoesNotMatch() => Assert.IsFalse(ProfileService.MatchesIdentity(Profile(@"C:\Games\One\game.exe"), "game", @"C:\Games\Two\game.exe"));
    [TestMethod] public void LegacyProfileWithoutPath_UsesNameFallback() => Assert.IsTrue(ProfileService.MatchesIdentity(Profile(null), "game", @"C:\Elsewhere\game.exe"));

    private static ProcessProfile Profile(string? path) => new() { ProcessName = "game", ExecutablePath = path, AffinityMask = 1, ApplyCoreOptimization = true };
}

[TestClass]
public sealed class Cs2BackupTests
{
    [TestMethod]
    public void EqualTimestamps_ProduceDifferentBackupDirectories()
    {
        string root = Path.Combine(Path.GetTempPath(), "FrameHubTests", Guid.NewGuid().ToString("N"));
        try
        {
            var now = new DateTime(2026, 1, 1, 1, 2, 3, 4);
            string first = Cs2OptimizationService.CreateUniqueBackupDirectory(root, now);
            string second = Cs2OptimizationService.CreateUniqueBackupDirectory(root, now);
            Assert.AreNotEqual(first, second);
            Assert.IsTrue(Directory.Exists(first)); Assert.IsTrue(Directory.Exists(second));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}

[TestClass]
public sealed class Cs2UserdataResolutionTests
{
    [TestMethod] public void ZeroCandidates_IsUnresolved() => Assert.IsFalse(Cs2OptimizationService.ResolveUserdataAccounts(Array.Empty<string>(), null).IsResolved);
    [TestMethod]
    public void OneCandidate_IsSelectedAutomatically()
    {
        using var fixture = new SteamFixture("111");
        Assert.AreEqual("111", Cs2OptimizationService.ResolveUserdataAccounts(new[] { fixture.Root }, null).Selected?.UserdataId);
    }
    [TestMethod]
    public void MultipleCandidates_RequireExplicitAndValidChoice()
    {
        using var fixture = new SteamFixture("111", "222");
        Assert.IsFalse(Cs2OptimizationService.ResolveUserdataAccounts(new[] { fixture.Root }, null).IsResolved);
        Assert.AreEqual("222", Cs2OptimizationService.ResolveUserdataAccounts(new[] { fixture.Root }, "222").Selected?.UserdataId);
        Assert.IsFalse(Cs2OptimizationService.ResolveUserdataAccounts(new[] { fixture.Root }, "999").IsResolved);
    }
    private sealed class SteamFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "FrameHubTests", Guid.NewGuid().ToString("N"));
        public SteamFixture(params string[] ids) { foreach (string id in ids) { string cfg = Path.Combine(Root, "userdata", id, "730", "local", "cfg"); Directory.CreateDirectory(cfg); File.WriteAllText(Path.Combine(cfg, "cs2_video.txt"), "test"); } }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}

[TestClass]
public sealed class LocalizationTests
{
    [TestMethod]
    public void PolishAndEnglishHaveTheSameLocalizationKeys() =>
        CollectionAssert.AreEquivalent(LocalizationService.EnglishKeys.ToArray(), LocalizationService.PolishKeys.ToArray());
}
