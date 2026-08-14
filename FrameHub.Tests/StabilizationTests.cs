using FrameHub.App.Services;
using FrameHub.Core.Models;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Models.GameOptimization;
using FrameHub.Core.Models.SessionOptimization;
using FrameHub.Core.Services;
using FrameHub.Core.Services.GameOptimization;
using FrameHub.Core.Services.Library;
using FrameHub.Core.Services.SessionOptimization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;

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

    [TestMethod]
    public void MalformedExecutablePath_DoesNotDiscardOtherPersistedLibraryItems()
    {
        string root = Path.Combine(Path.GetTempPath(), "FrameHub.LibraryPathTests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "library.json");
        try
        {
            Directory.CreateDirectory(root);
            string json = System.Text.Json.JsonSerializer.Serialize(new[]
            {
                new LibraryItem { Id = "valid", DisplayName = "Valid", ExecutablePath = @"C:\Games\valid.exe" },
                new LibraryItem { Id = "malformed", DisplayName = "Malformed", ExecutablePath = "\0broken.exe" }
            });
            File.WriteAllText(path, json);

            List<LibraryItem> items = new LibraryService(path).LoadItems();

            Assert.IsTrue(items.Any(item => item.Id == "valid"));
            Assert.IsTrue(items.Any(item => item.Id == "malformed"));
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }
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

    [TestMethod]
    public void OptimizationIdentity_RejectsReusedPidWithDifferentStartTime()
    {
        using var process = Process.GetCurrentProcess();
        var profile = new ProcessProfile
        {
            ProcessName = process.ProcessName,
            AffinityMask = 1,
            ApplyCoreOptimization = true
        };
        var staleKey = new ProcessInstanceKey(process.Id, process.StartTime.ToUniversalTime().AddSeconds(-1));
        var method = typeof(OptimizationService).GetMethod(
            "MatchesRunningProcessIdentity",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.IsNotNull(method);
        bool matches = (bool)method.Invoke(null, new object[] { staleKey, process.ProcessName, profile })!;
        Assert.IsFalse(matches, "A reused PID must not authorize mutation of a different process instance.");
    }

    private static ProcessProfile Profile(string? path) => new() { ProcessName = "game", ExecutablePath = path, AffinityMask = 1, ApplyCoreOptimization = true };
}

[TestClass]
public sealed class ProcessSuspensionRecoveryIdentityTests
{
    [TestMethod]
    public void AmbiguousMatchingProcess_RemainsUnresolvedWithoutResume()
    {
        using Process process = Process.GetCurrentProcess();
        var record = CurrentProcessRecord(process);

        SessionActionResult result = new ProcessSuspendService().ResolveProcessesWithoutResume([record]);

        Assert.AreEqual(0, result.ResolvedCount);
        Assert.AreEqual(1, result.FailedCount);
        Assert.AreEqual(0, result.Records.Count);
    }

    [TestMethod]
    public void AmbiguousRecovery_ResolvesReusedPidOnlyAfterIdentityMismatch()
    {
        using Process process = Process.GetCurrentProcess();
        SuspendedProcessRecord current = CurrentProcessRecord(process);
        var stale = new SuspendedProcessRecord
        {
            ProcessId = current.ProcessId,
            ProcessName = current.ProcessName,
            ProcessStartTimeUtc = current.ProcessStartTimeUtc.AddMinutes(-1),
            ExecutablePath = current.ExecutablePath
        };

        SessionActionResult result = new ProcessSuspendService().ResolveProcessesWithoutResume([stale]);

        Assert.AreEqual(1, result.ResolvedCount);
        Assert.AreEqual(1, result.StaleProcessCount);
        Assert.AreEqual(stale.ProcessId, result.Records.Single().ProcessId);
    }

    [TestMethod]
    public void AmbiguousRecovery_NameOrPathMismatch_IsStaleAndNeverMutated()
    {
        using Process process = Process.GetCurrentProcess();
        SuspendedProcessRecord current = CurrentProcessRecord(process);
        var wrongName = new SuspendedProcessRecord
        {
            ProcessId = current.ProcessId,
            ProcessName = current.ProcessName + "-other",
            ProcessStartTimeUtc = current.ProcessStartTimeUtc,
            ExecutablePath = current.ExecutablePath
        };
        var wrongPath = new SuspendedProcessRecord
        {
            ProcessId = current.ProcessId,
            ProcessName = current.ProcessName,
            ProcessStartTimeUtc = current.ProcessStartTimeUtc,
            ExecutablePath = Path.Combine(Path.GetTempPath(), "different.exe")
        };

        SessionActionResult nameResult = new ProcessSuspendService().ResolveProcessesWithoutResume([wrongName]);
        SessionActionResult pathResult = new ProcessSuspendService().ResolveProcessesWithoutResume([wrongPath]);

        Assert.AreEqual(1, nameResult.StaleProcessCount);
        Assert.AreEqual(1, pathResult.StaleProcessCount);
        Assert.AreEqual(1, nameResult.ResolvedCount);
        Assert.AreEqual(1, pathResult.ResolvedCount);
    }

    private static SuspendedProcessRecord CurrentProcessRecord(Process process) => new()
    {
        ProcessId = process.Id,
        ProcessName = process.ProcessName,
        ProcessStartTimeUtc = process.StartTime.ToUniversalTime(),
        ExecutablePath = Environment.ProcessPath
    };
}

[TestClass]
public sealed class ProcessScannerIsolationTests
{
    [TestMethod]
    public async Task ProfileScan_DoesNotEraseUiCpuSamplingHistory()
    {
        var scanner = new ProcessScannerService(new ProcessService());
        var field = typeof(ProcessScannerService).GetField(
            "_lastCpuTimes",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.IsNotNull(field);

        var samples = (Dictionary<ProcessInstanceKey, TimeSpan>)field.GetValue(scanner)!;
        var key = new ProcessInstanceKey(12345, DateTime.UtcNow);
        samples[key] = TimeSpan.FromSeconds(1);

        await scanner.ScanProfileProcessesAsync(Array.Empty<ProcessProfile>());

        Assert.IsTrue(samples.ContainsKey(key), "The lightweight profile scan must not reset the independent UI CPU sampler.");
    }
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
