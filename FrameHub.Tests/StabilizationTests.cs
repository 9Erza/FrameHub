using FrameHub.App.Services;
using FrameHub.Core.Models;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Models.GameOptimization;
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
    [TestMethod]
    public void SanitizedProfile_RetainsNormalizedPathIdentity()
    {
        var sanitized = ProfileService.SanitizeProfiles(new[] { Profile(@"C:\Games\One\..\One\game.exe") }).Single();
        Assert.AreEqual(3, sanitized.SchemaVersion);
        Assert.AreEqual(Path.GetFullPath(@"C:\Games\One\game.exe"), sanitized.ExecutablePath, true);
    }

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
    [TestMethod]
    public void RememberedValidSelection_IsReused()
    {
        using var fixture = new SteamFixture("111", "222");
        Assert.AreEqual("111", Cs2OptimizationService.ResolveUserdataAccounts(new[] { fixture.Root }, "111").Selected?.UserdataId);
    }
    [TestMethod]
    public void RememberedMissingSelection_DoesNotSelectAnotherCandidate()
    {
        using var fixture = new SteamFixture("111", "222");
        Assert.IsNull(Cs2OptimizationService.ResolveUserdataAccounts(new[] { fixture.Root }, "333").Selected);
    }
    [TestMethod]
    public void DuplicateUserdataId_RequiresMatchingRememberedPath()
    {
        using var first = new SteamFixture("111");
        using var second = new SteamFixture("111");
        var unresolved = Cs2OptimizationService.ResolveUserdataAccounts(new[] { first.Root, second.Root }, "111");
        Assert.IsFalse(unresolved.IsResolved);
        string selectedPath = Path.Combine(second.Root, "userdata", "111");
        var resolved = Cs2OptimizationService.ResolveUserdataAccounts(new[] { first.Root, second.Root }, "111", selectedPath.ToUpperInvariant());
        Assert.AreEqual(Path.GetFullPath(selectedPath), resolved.Selected?.DirectoryPath, true);
    }
    [TestMethod]
    public void UnresolvedAccount_BlocksEveryWriteCapableOperation()
    {
        string root = Path.Combine(Path.GetTempPath(), "FrameHubTests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var analysis = new Cs2ConfigAnalysis
            {
                Paths = new Cs2ConfigPaths { GameCfgFolder = root },
                UserdataResolution = new Cs2UserdataResolution()
            };
            var service = new Cs2OptimizationService();
            Assert.IsFalse(service.CreateBackup(analysis).Success);
            Assert.IsFalse(service.LoadAutoexec(analysis, createIfMissing: true).Success);
            Assert.IsFalse(service.SaveAutoexec(analysis, "echo unsafe").Success);
            Assert.IsFalse(service.RestoreLatestBackup(analysis).Success);
            Assert.IsFalse(File.Exists(Path.Combine(root, "autoexec.cfg")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
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
