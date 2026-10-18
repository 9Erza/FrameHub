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
