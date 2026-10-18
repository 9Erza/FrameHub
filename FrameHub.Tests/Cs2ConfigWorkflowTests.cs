using FrameHub.Core.Models.GameOptimization;
using FrameHub.Core.Services.GameOptimization;
using FrameHub.Core.Services.Benchmarking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public sealed class Cs2ConfigWorkflowTests
{
    [TestMethod]
    public void ApplyPreset_MissingSelectedKey_RejectsWithoutWritingOrBackingUp()
    {
        using var fixture = new ConfigFixture();
        byte[] original = File.ReadAllBytes(fixture.Video);
        GameConfigApplyResult result = fixture.Service.ApplyPreset(fixture.Analysis, fixture.Preset("missing.setting", "0"));
        Assert.IsFalse(result.Success, "A selected setting that is absent from the file must not be reported as applied.");
        Assert.AreEqual(0, result.AppliedChanges);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(fixture.Video));
        Assert.IsFalse(Directory.Exists(fixture.Backups), "Validation must finish before any backup is published.");
    }

    [TestMethod]
    public void ApplyPreset_UnchangedSelection_DoesNotCreateBackupOrCountChanges()
    {
        using var fixture = new ConfigFixture();
        GameConfigApplyResult result = fixture.Service.ApplyPreset(fixture.Analysis, fixture.Preset(value: "1"));
        Assert.IsTrue(result.Success);
        Assert.AreEqual(0, result.AppliedChanges);
        Assert.IsFalse(Directory.Exists(fixture.Backups));
    }

    [DataTestMethod]
    [DataRow("0\"\n\"injected\" \"1")]
    [DataRow("invalid")]
    public void ApplyPreset_InvalidSelectedValue_RejectsBeforeBackup(string value)
    {
        using var fixture = new ConfigFixture();
        GameOptimizationPreset preset = fixture.Preset(value: value);
        preset.Changes[0].Options = [new GameSettingOption { Value = "0" }, new GameSettingOption { Value = "1" }];
        byte[] original = File.ReadAllBytes(fixture.Video);
        Assert.IsFalse(fixture.Service.ApplyPreset(fixture.Analysis, preset).Success);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(fixture.Video));
        Assert.IsFalse(Directory.Exists(fixture.Backups));
    }

    [TestMethod]
    public void ApplyPreset_MissingSecondFile_RejectsWholeSelection()
    {
        using var fixture = new ConfigFixture();
        byte[] original = File.ReadAllBytes(fixture.Video);
        GameOptimizationPreset preset = fixture.Preset();
        preset.Changes.Add(new GameSettingChange { Key = "r_player_visibility_mode", TargetValue = "1", TargetFile = GameOptimizationTargetFile.MachineConvars });
        File.Delete(fixture.Machine);
        Assert.IsFalse(fixture.Service.ApplyPreset(fixture.Analysis, preset).Success);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(fixture.Video));
        Assert.IsFalse(Directory.Exists(fixture.Backups));
    }

    [TestMethod]
    public void ApplyPreset_ValidSelection_ChangesOnlyRequestedValueAndCanRestore()
    {
        using var fixture = new ConfigFixture();
        byte[] original = File.ReadAllBytes(fixture.Video);
        GameConfigApplyResult result = fixture.Service.ApplyPreset(fixture.Analysis, fixture.Preset());
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(1, result.AppliedChanges);
        Assert.AreEqual("0", ValveConfigParser.ReadKeyValues(fixture.Video)["setting.mat_vsync"]);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(Path.Combine(result.BackupFolder, "cs2_video.txt")));
        Assert.IsTrue(fixture.Service.RestoreLatestBackup(fixture.Analysis).Success);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(fixture.Video));
    }

    [TestMethod]
    public void ApplyPreset_LockedSecondFile_RestoresFirstFileExactly()
    {
        using var fixture = new ConfigFixture();
        byte[] originalVideo = File.ReadAllBytes(fixture.Video);
        byte[] originalMachine = File.ReadAllBytes(fixture.Machine);
        GameOptimizationPreset preset = fixture.Preset();
        preset.Changes.Add(new GameSettingChange { Key = "r_player_visibility_mode", TargetValue = "1", TargetFile = GameOptimizationTargetFile.MachineConvars });
        using var lockedFile = new FileStream(fixture.Machine, FileMode.Open, FileAccess.Read, FileShare.Read);

        GameConfigApplyResult result = fixture.Service.ApplyPreset(fixture.Analysis, preset);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, result.AppliedChanges);
        CollectionAssert.AreEqual(originalVideo, File.ReadAllBytes(fixture.Video), "The completed first write must be rolled back after a later failure.");
        CollectionAssert.AreEqual(originalMachine, File.ReadAllBytes(fixture.Machine));
        CollectionAssert.AreEqual(originalVideo, File.ReadAllBytes(Path.Combine(result.BackupFolder, "cs2_video.txt")));
    }

    [TestMethod]
    public void SaveAutoexec_BackupIncludesGraphicsAndOriginalAutoexec()
    {
        using var fixture = new ConfigFixture();
        string autoexec = Path.Combine(fixture.Analysis.Paths.GameCfgFolder!, "autoexec.cfg");
        File.WriteAllText(autoexec, "echo original");
        GameConfigApplyResult result = fixture.Service.SaveAutoexec(fixture.Analysis, "echo changed");
        Assert.IsTrue(result.Success, result.Message);
        Assert.IsTrue(File.Exists(Path.Combine(result.BackupFolder, "cs2_video.txt")), "An autoexec save must not hide the latest full configuration snapshot.");
        Assert.AreEqual("echo original", File.ReadAllText(Path.Combine(result.BackupFolder, "autoexec.cfg")));
    }

    [TestMethod]
    public void RestoreLatestBackup_CorruptedBackup_IsRejectedBeforeWriting()
    {
        using var fixture = new ConfigFixture();
        GameConfigBackupResult backup = fixture.Service.CreateBackup(fixture.Analysis);
        Assert.IsTrue(backup.Success);
        File.WriteAllText(Path.Combine(backup.BackupFolder, "cs2_video.txt"), "corrupted snapshot");
        byte[] original = File.ReadAllBytes(fixture.Video);
        Assert.IsFalse(fixture.Service.RestoreLatestBackup(fixture.Analysis).Success);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(fixture.Video));
    }

    [TestMethod]
    public void CreateBackup_UnreadableSource_DoesNotPublishPartialSnapshot()
    {
        using var fixture = new ConfigFixture();
        string keys = Path.Combine(fixture.Analysis.Paths.UserCfgFolder!, "cs2_user_keys_0_slot0.vcfg");
        File.WriteAllText(keys, "keys");
        fixture.Analysis.Paths.UserKeysPath = keys;
        Assert.IsTrue(fixture.Service.CreateBackup(fixture.Analysis).Success);
        int publishedBefore = Directory.GetDirectories(fixture.Backups, "*", SearchOption.AllDirectories).Length;
        using var locked = new FileStream(keys, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.IsFalse(fixture.Service.CreateBackup(fixture.Analysis).Success);
        Assert.AreEqual(publishedBefore, Directory.GetDirectories(fixture.Backups, "*", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public void RestoreLatestBackup_LockedSecondTarget_RollsBackFirstTarget()
    {
        using var fixture = new ConfigFixture();
        Assert.IsTrue(fixture.Service.CreateBackup(fixture.Analysis).Success);
        File.WriteAllText(fixture.Video, "current video");
        File.WriteAllText(fixture.Machine, "current machine");
        byte[] video = File.ReadAllBytes(fixture.Video);
        byte[] machine = File.ReadAllBytes(fixture.Machine);
        using var locked = new FileStream(fixture.Machine, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.IsFalse(fixture.Service.RestoreLatestBackup(fixture.Analysis).Success);
        CollectionAssert.AreEqual(video, File.ReadAllBytes(fixture.Video));
        CollectionAssert.AreEqual(machine, File.ReadAllBytes(fixture.Machine));
    }

    [TestMethod]
    public void RestoreLatestBackup_DeletedUserConvarsMissingFromCurrentAnalysis_RestoresFileAndData()
    {
        using var fixture = new ConfigFixture();
        string userConvars = Path.Combine(fixture.Analysis.Paths.UserCfgFolder!, "cs2_user_convars_0_slot0.vcfg");
        File.WriteAllText(userConvars, "\"convars\"\n{\n\t\"cl_radar_scale\"\t\"0.4\"\n}\n");
        fixture.Analysis.Paths.UserConvarsPath = userConvars;

        GameConfigBackupResult backup = fixture.Service.CreateBackup(fixture.Analysis);
        Assert.IsTrue(backup.Success, backup.Message);

        File.Delete(userConvars);
        Assert.IsFalse(File.Exists(userConvars));
        fixture.Analysis.Paths.UserConvarsPath = null;

        GameConfigBackupResult restore = fixture.Service.RestoreLatestBackup(fixture.Analysis);
        Assert.IsTrue(restore.Success, restore.Message);
        Assert.IsTrue(File.Exists(userConvars), "Deleted user convars file must be restored from the backup manifest.");
        Assert.AreEqual("\"convars\"\n{\n\t\"cl_radar_scale\"\t\"0.4\"\n}\n", File.ReadAllText(userConvars));
    }

    [TestMethod]
    public void RestoreLatestBackup_DeletedUserKeysMissingFromCurrentAnalysis_RestoresFileAndData()
    {
        using var fixture = new ConfigFixture();
        string userKeys = Path.Combine(fixture.Analysis.Paths.UserCfgFolder!, "cs2_user_keys_0_slot0.vcfg");
        File.WriteAllText(userKeys, "\"bindings\"\n{\n\t\"SPACE\"\t\"+jump\"\n}\n");
        fixture.Analysis.Paths.UserKeysPath = userKeys;

        GameConfigBackupResult backup = fixture.Service.CreateBackup(fixture.Analysis);
        Assert.IsTrue(backup.Success, backup.Message);

        File.Delete(userKeys);
        Assert.IsFalse(File.Exists(userKeys));
        fixture.Analysis.Paths.UserKeysPath = null;

        GameConfigBackupResult restore = fixture.Service.RestoreLatestBackup(fixture.Analysis);
        Assert.IsTrue(restore.Success, restore.Message);
        Assert.IsTrue(File.Exists(userKeys), "Deleted user keys file must be restored from the backup manifest.");
        Assert.AreEqual("\"bindings\"\n{\n\t\"SPACE\"\t\"+jump\"\n}\n", File.ReadAllText(userKeys));
    }

    [DataTestMethod]
    [DataRow(@"..\..\evil.vcfg")]
    [DataRow(@"subdir\cs2_user_convars_0_slot0.vcfg")]
    [DataRow(@"cs2_user_convars_../../slot0.vcfg")]
    [DataRow(@"C:\Windows\System32\malicious.dll")]
    public void RestoreLatestBackup_ManifestWithTraversingFileName_IsRejected(string maliciousFileName)
    {
        using var fixture = new ConfigFixture();
        GameConfigBackupResult backup = fixture.Service.CreateBackup(fixture.Analysis);
        Assert.IsTrue(backup.Success);

        string manifestPath = Path.Combine(backup.BackupFolder, "snapshot.json");
        string manifestJson = File.ReadAllText(manifestPath);
        manifestJson = manifestJson.Replace("cs2_machine_convars.vcfg", maliciousFileName.Replace(@"\", @"\\"));
        File.WriteAllText(manifestPath, manifestJson);

        GameConfigBackupResult restore = fixture.Service.RestoreLatestBackup(fixture.Analysis);
        Assert.IsFalse(restore.Success, "Restoring a manifest with path traversal must fail.");
    }

    [TestMethod]
    public void RestoreLatestBackup_ManifestWithUnrecognizedFileName_IsRejected()
    {
        using var fixture = new ConfigFixture();
        GameConfigBackupResult backup = fixture.Service.CreateBackup(fixture.Analysis);
        Assert.IsTrue(backup.Success);

        string manifestPath = Path.Combine(backup.BackupFolder, "snapshot.json");
        string manifestJson = File.ReadAllText(manifestPath);
        manifestJson = manifestJson.Replace("cs2_machine_convars.vcfg", "malicious_script.bat");
        File.WriteAllText(manifestPath, manifestJson);

        GameConfigBackupResult restore = fixture.Service.RestoreLatestBackup(fixture.Analysis);
        Assert.IsFalse(restore.Success, "Restoring a manifest with unrecognized file name must fail.");
    }

    [TestMethod]
    public void RestoreLatestBackup_MissingFileInManifest_RemovesExtraneousFileOnDisk()
    {
        using var fixture = new ConfigFixture();
        GameConfigBackupResult backup = fixture.Service.CreateBackup(fixture.Analysis);
        Assert.IsTrue(backup.Success);

        string autoexec = Path.Combine(fixture.Analysis.Paths.GameCfgFolder!, "autoexec.cfg");
        File.WriteAllText(autoexec, "echo extraneous");
        Assert.IsTrue(File.Exists(autoexec));

        GameConfigBackupResult restore = fixture.Service.RestoreLatestBackup(fixture.Analysis);
        Assert.IsTrue(restore.Success, restore.Message);
        Assert.IsFalse(File.Exists(autoexec), "Extraneous autoexec should be deleted according to manifest absence metadata.");
    }

    [TestMethod]
    public void RestoreLatestBackup_LockedTarget_RollsBackAllRestoredFiles()
    {
        using var fixture = new ConfigFixture();
        string userConvars = Path.Combine(fixture.Analysis.Paths.UserCfgFolder!, "cs2_user_convars_0_slot0.vcfg");
        File.WriteAllText(userConvars, "original user convars");
        fixture.Analysis.Paths.UserConvarsPath = userConvars;

        GameConfigBackupResult backup = fixture.Service.CreateBackup(fixture.Analysis);
        Assert.IsTrue(backup.Success);

        File.WriteAllText(fixture.Video, "modified video");
        byte[] preRestoreVideo = File.ReadAllBytes(fixture.Video);

        using var locked = new FileStream(userConvars, FileMode.Open, FileAccess.Read, FileShare.Read);

        GameConfigBackupResult restore = fixture.Service.RestoreLatestBackup(fixture.Analysis);
        Assert.IsFalse(restore.Success);
        Assert.IsTrue(restore.RollbackAttempted, "RollbackAttempted should be true when partial restore fails.");
        Assert.IsTrue(restore.RollbackSucceeded, "RollbackSucceeded should be true when rollback succeeds.");
        CollectionAssert.AreEqual(preRestoreVideo, File.ReadAllBytes(fixture.Video), "Modified video must be rolled back to pre-restore state.");
    }

    [TestMethod]
    public void RestoreLatestBackup_ReparsePointTargetDirectory_RejectsRestore()
    {
        using var fixture = new ConfigFixture();
        GameConfigBackupResult backup = fixture.Service.CreateBackup(fixture.Analysis);
        Assert.IsTrue(backup.Success);

        string junctionDir = Path.Combine(fixture.Root, "junction_cfg");
        if (!TryCreateDirectoryJunction(junctionDir, fixture.Analysis.Paths.UserCfgFolder!))
        {
            Assert.Inconclusive("NTFS junctions not supported or permitted in test environment.");
            return;
        }

        try
        {
            fixture.Analysis.Paths.UserCfgFolder = junctionDir;
            fixture.Analysis.Paths.VideoConfigPath = Path.Combine(junctionDir, "cs2_video.txt");
            GameConfigBackupResult restore = fixture.Service.RestoreLatestBackup(fixture.Analysis);
            Assert.IsFalse(restore.Success);
            StringAssert.Contains(restore.Message, "reparse point");
        }
        finally
        {
            if (Directory.Exists(junctionDir)) Directory.Delete(junctionDir);
        }
    }

    [TestMethod]
    public void RestoreLatestBackup_TargetEscapingDirectory_RejectsRestore()
    {
        using var fixture = new ConfigFixture();
        GameConfigBackupResult backup = fixture.Service.CreateBackup(fixture.Analysis);
        Assert.IsTrue(backup.Success);

        fixture.Analysis.Paths.VideoConfigPath = Path.Combine(fixture.Analysis.Paths.UserCfgFolder!, "..", "escaped_video.txt");
        GameConfigBackupResult restore = fixture.Service.RestoreLatestBackup(fixture.Analysis);
        Assert.IsFalse(restore.Success);
        StringAssert.Contains(restore.Message, "escapes expected directory");
    }

    [TestMethod]
    public void CreateBackup_ReparsePointAccountRoot_RejectsBackup()
    {
        using var fixture = new ConfigFixture();
        string realBackupRoot = Path.Combine(fixture.Root, "real_backups");
        Directory.CreateDirectory(realBackupRoot);
        string junctionBackupRoot = Path.Combine(fixture.Root, "junction_backups");
        if (!TryCreateDirectoryJunction(junctionBackupRoot, realBackupRoot))
        {
            Assert.Inconclusive("NTFS junctions not supported or permitted in test environment.");
            return;
        }

        try
        {
            var service = new Cs2OptimizationService(backupRoot: junctionBackupRoot);
            GameConfigBackupResult backup = service.CreateBackup(fixture.Analysis);
            Assert.IsFalse(backup.Success);
            StringAssert.Contains(backup.Message, "reparse point");
        }
        finally
        {
            if (Directory.Exists(junctionBackupRoot)) Directory.Delete(junctionBackupRoot);
        }
    }

    [TestMethod]
    public void RestoreLatestBackup_LegacySnapshotWithoutManifest_RestoresFilesSuccessfully()
    {
        using var fixture = new ConfigFixture();
        string legacySnapshotDir = Path.Combine(fixture.Root, "backups", "20240101_120000");
        Directory.CreateDirectory(legacySnapshotDir);
        File.WriteAllText(Path.Combine(legacySnapshotDir, "cs2_video.txt"), "\"video\"\n{\n\t\"setting.defaultres\"\t\"1280\"\n}\n");
        File.WriteAllText(Path.Combine(legacySnapshotDir, "cs2_machine_convars.vcfg"), "\"convars\"\n{\n\t\"fps_max\"\t\"300\"\n}\n");
        File.WriteAllText(Path.Combine(legacySnapshotDir, "rogue_file.exe"), "malicious bytes");

        File.WriteAllText(fixture.Video, "modified video");
        File.WriteAllText(fixture.Machine, "modified machine");

        GameConfigBackupResult restore = fixture.Service.RestoreLatestBackup(fixture.Analysis);
        Assert.IsTrue(restore.Success, restore.Message);
        Assert.AreEqual("\"video\"\n{\n\t\"setting.defaultres\"\t\"1280\"\n}\n", File.ReadAllText(fixture.Video));
        Assert.AreEqual("\"convars\"\n{\n\t\"fps_max\"\t\"300\"\n}\n", File.ReadAllText(fixture.Machine));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Analysis.Paths.UserCfgFolder!, "rogue_file.exe")), "Rogue files from legacy snapshots must not be restored.");
    }

    [TestMethod]
    public void RestoreLatestBackup_UserCfgFolderWithTrailingSlash_Succeeds()
    {
        using var fixture = new ConfigFixture();
        GameConfigBackupResult backup = fixture.Service.CreateBackup(fixture.Analysis);
        Assert.IsTrue(backup.Success);

        fixture.Analysis.Paths.UserCfgFolder = fixture.Analysis.Paths.UserCfgFolder!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        File.WriteAllText(fixture.Video, "modified video");

        GameConfigBackupResult restore = fixture.Service.RestoreLatestBackup(fixture.Analysis);
        Assert.IsTrue(restore.Success, restore.Message);
    }

    [TestMethod]
    public void RestoreLatestBackup_TargetFileNameMismatch_RejectsRestore()
    {
        using var fixture = new ConfigFixture();
        GameConfigBackupResult backup = fixture.Service.CreateBackup(fixture.Analysis);
        Assert.IsTrue(backup.Success);

        fixture.Analysis.Paths.VideoConfigPath = Path.Combine(fixture.Analysis.Paths.UserCfgFolder!, "cs2_video_mismatch.txt");
        GameConfigBackupResult restore = fixture.Service.RestoreLatestBackup(fixture.Analysis);
        Assert.IsFalse(restore.Success);
        StringAssert.Contains(restore.Message, "does not match expected file name");
    }

    [TestMethod]
    public void RestoreLatestBackup_ReparsePointBackupFolder_RejectsRestore()
    {
        using var fixture = new ConfigFixture();
        GameConfigBackupResult backup = fixture.Service.CreateBackup(fixture.Analysis);
        Assert.IsTrue(backup.Success);

        string junctionBackup = Path.Combine(Path.GetDirectoryName(backup.BackupFolder)!, "junction_snapshot");
        if (!TryCreateDirectoryJunction(junctionBackup, backup.BackupFolder))
        {
            Assert.Inconclusive("NTFS junctions not supported or permitted in test environment.");
            return;
        }

        try
        {
            Directory.Delete(backup.BackupFolder, recursive: true);
            GameConfigBackupResult restore = fixture.Service.RestoreLatestBackup(fixture.Analysis);
            Assert.IsFalse(restore.Success);
        }
        finally
        {
            if (Directory.Exists(junctionBackup)) Directory.Delete(junctionBackup);
        }
    }

    [DataTestMethod]
    [DataRow("apply")]
    [DataRow("save")]
    [DataRow("restore")]
    [DataRow("create")]
    public void ConfigurationMutation_BenchmarkReserved_DoesNotWrite(string operation)
    {
        var arbiter = new RecordingArbiter { Allow = false };
        using var fixture = new ConfigFixture(arbiter);
        Assert.IsTrue(fixture.Service.CreateBackup(fixture.Analysis).Success);
        byte[] original = File.ReadAllBytes(fixture.Video);
        bool success = operation switch
        {
            "apply" => fixture.Service.ApplyPreset(fixture.Analysis, fixture.Preset()).Success,
            "save" => fixture.Service.SaveAutoexec(fixture.Analysis, "echo test").Success,
            "restore" => fixture.Service.RestoreLatestBackup(fixture.Analysis).Success,
            _ => fixture.Service.LoadAutoexec(fixture.Analysis, createIfMissing: true).Success
        };
        Assert.IsFalse(success);
        Assert.AreEqual(1, arbiter.Attempts);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(fixture.Video));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Analysis.Paths.GameCfgFolder!, "autoexec.cfg")));
    }

    [TestMethod]
    public void LibraryView_RestoreBackup_IsBoundToExistingCommand()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "FrameHub.App", "Views"))) root = root.Parent;
        Assert.IsNotNull(root);
        string xaml = File.ReadAllText(Path.Combine(root.FullName, "FrameHub.App", "Views", "LibraryView.xaml"));
        StringAssert.Contains(xaml, "Command=\"{Binding RestoreCs2BackupCommand}\"");
        StringAssert.Contains(xaml, "Content=\"{Binding RestoreCs2Text}\"");
    }

    private static bool TryCreateDirectoryJunction(string junctionPath, string targetPath)
    {
        try
        {
            if (Directory.Exists(junctionPath)) Directory.Delete(junctionPath);
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{junctionPath}\" \"{targetPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return false;
            proc.WaitForExit(5000);
            return proc.ExitCode == 0 && Directory.Exists(junctionPath);
        }
        catch
        {
            return false;
        }
    }

    private sealed class RecordingArbiter : IBenchmarkOperationArbiter
    {
        public bool Allow { get; set; }
        public int Attempts { get; private set; }
        public bool TryAcquireExternalMutation(out IDisposable? lease)
        {
            Attempts++;
            lease = null;
            return Allow;
        }
    }

    private sealed class ConfigFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "FrameHub.Config.Tests", Guid.NewGuid().ToString("N"));
        public string Video { get; }
        public string Machine { get; }
        public string Backups => Path.Combine(Root, "backups");
        public Cs2OptimizationService Service { get; }
        public Cs2ConfigAnalysis Analysis { get; }

        public ConfigFixture(IBenchmarkOperationArbiter? arbiter = null)
        {
            string cfg = Path.Combine(Root, "account", "730", "local", "cfg");
            Directory.CreateDirectory(cfg);
            Video = Path.Combine(cfg, "cs2_video.txt");
            Machine = Path.Combine(cfg, "cs2_machine_convars.vcfg");
            File.WriteAllText(Video, "\"video.cfg\"\n{\n\t\"setting.mat_vsync\"\t\"1\"\n}\n");
            File.WriteAllText(Machine, "\"convars\"\n{\n\t\"r_player_visibility_mode\"\t\"0\"\n}\n");
            var account = new SteamUserdataAccountCandidate
            {
                UserdataId = "111",
                DirectoryPath = Path.Combine(Root, "account"),
                ConfigFolderPath = cfg
            };
            Analysis = new Cs2ConfigAnalysis
            {
                Paths = new Cs2ConfigPaths { VideoConfigPath = Video, MachineConvarsPath = Machine, UserCfgFolder = cfg, GameCfgFolder = cfg },
                UserdataResolution = new Cs2UserdataResolution { Selected = account, Candidates = [account] }
            };
            Service = new Cs2OptimizationService(backupRoot: Backups, arbiter: arbiter);
        }

        public GameOptimizationPreset Preset(string key = "setting.mat_vsync", string value = "0", GameOptimizationTargetFile target = GameOptimizationTargetFile.VideoConfig) => new()
        {
            Changes = [new GameSettingChange { Key = key, TargetValue = value, TargetFile = target, IsSelected = true, CanApply = true }]
        };

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
