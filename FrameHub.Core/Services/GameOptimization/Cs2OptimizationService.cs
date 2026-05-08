using FrameHub.Core.Logging;
using FrameHub.Core.Models.GameOptimization;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FrameHub.Core.Services.GameOptimization;

public sealed class Cs2OptimizationService
{
    private readonly ILogger _logger = LoggerService.Instance;

    public bool IsCs2LibraryItem(LibraryItem? item)
    {
        if (item == null) return false;
        if (string.Equals(item.AppId, "730", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(item.ProcessName, "cs2", StringComparison.OrdinalIgnoreCase)) return true;
        if ((item.DisplayName ?? string.Empty).Contains("Counter-Strike 2", StringComparison.OrdinalIgnoreCase)) return true;
        return (item.ExecutablePath ?? string.Empty).EndsWith("cs2.exe", StringComparison.OrdinalIgnoreCase);
    }

    public Cs2ConfigAnalysis Analyze(LibraryItem item)
    {
        var paths = DetectConfigPaths(item);
        var video = ValveConfigParser.ReadKeyValues(paths.VideoConfigPath);
        var machine = ValveConfigParser.ReadKeyValues(paths.MachineConvarsPath);

        var analysis = new Cs2ConfigAnalysis
        {
            IsDetected = paths.IsComplete,
            Paths = paths,
            VideoSettings = video,
            MachineConvars = machine,
            StatusMessage = paths.IsComplete ? "CS2 configuration detected." : "CS2 video config was not found. Add Steam userdata folder manually or launch CS2 once.",
        };

        analysis.Summary = BuildSummary(video);

        var backupVideo = ReadLatestBackupVideoSettings();
        analysis.Presets.Add(BuildBackupPreset(video, machine, backupVideo));
        analysis.Presets.Add(BuildCompetitivePreset(video, machine));

        var baseline = analysis.Presets.FirstOrDefault(x => x.Id == "cs2_competitive_baseline") ?? analysis.Presets.FirstOrDefault();
        if (baseline != null)
        {
            analysis.BaselineTotalSettings = baseline.Changes.Count(x => x.Status != GameOptimizationSettingStatus.ReadOnly && x.Status != GameOptimizationSettingStatus.NotDetected);
            analysis.BaselineMatchedSettings = baseline.Changes.Count(x => x.Status == GameOptimizationSettingStatus.MatchesBaseline || x.Status == GameOptimizationSettingStatus.OptionalPreference);
        }

        return analysis;
    }

    public GameConfigBackupResult CreateBackup(Cs2ConfigAnalysis analysis)
    {
        try
        {
            if (!analysis.Paths.IsComplete)
            {
                return new GameConfigBackupResult { Success = false, Message = "CS2 config was not detected." };
            }

            string backupRoot = Path.Combine(AppPaths.UserDataDirectory, "Backups", "CS2", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            Directory.CreateDirectory(backupRoot);

            CopyIfExists(analysis.Paths.VideoConfigPath, backupRoot);
            CopyIfExists(analysis.Paths.MachineConvarsPath, backupRoot);
            CopyIfExists(analysis.Paths.UserConvarsPath, backupRoot);
            CopyIfExists(analysis.Paths.UserKeysPath, backupRoot);
            CopyIfExists(GetAutoexecPath(analysis), backupRoot);

            return new GameConfigBackupResult
            {
                Success = true,
                BackupFolder = backupRoot,
                Message = $"Backup created: {backupRoot}"
            };
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to create CS2 config backup", ex);
            return new GameConfigBackupResult { Success = false, Message = ex.Message };
        }
    }

    public GameConfigApplyResult ApplyPreset(Cs2ConfigAnalysis analysis, GameOptimizationPreset preset)
    {
        var backup = CreateBackup(analysis);
        if (!backup.Success)
        {
            return new GameConfigApplyResult { Success = false, Message = backup.Message };
        }

        try
        {
            int count = 0;
            var videoChanges = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var machineChanges = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var change in preset.Changes.Where(x => x.IsSelected && x.CanApply))
            {
                if (change.Key.Equals("cs2.resolution", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyResolutionValue(change.RecommendedValue, videoChanges);
                    count++;
                    continue;
                }

                if (change.Key.Equals("cs2.display_mode", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyDisplayModeValue(change.RecommendedValue, videoChanges);
                    count++;
                    continue;
                }

                if (change.Key.Equals("cs2.msaa_mode", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyMsaaModeValue(change.RecommendedValue, videoChanges);
                    count++;
                    continue;
                }

                if (change.Key.StartsWith("cs2.", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (change.TargetFile == GameOptimizationTargetFile.VideoConfig)
                {
                    videoChanges[change.Key] = change.RecommendedValue;
                    count++;
                }
                else if (change.TargetFile == GameOptimizationTargetFile.MachineConvars)
                {
                    machineChanges[change.Key] = change.RecommendedValue;
                    count++;
                }
            }

            if (!string.IsNullOrWhiteSpace(analysis.Paths.VideoConfigPath) && videoChanges.Count > 0)
            {
                ValveConfigParser.UpdateQuotedValueFile(analysis.Paths.VideoConfigPath, videoChanges);
            }

            if (!string.IsNullOrWhiteSpace(analysis.Paths.MachineConvarsPath) && machineChanges.Count > 0)
            {
                ValveConfigParser.UpdateQuotedValueFile(analysis.Paths.MachineConvarsPath, machineChanges);
            }

            return new GameConfigApplyResult
            {
                Success = true,
                BackupFolder = backup.BackupFolder,
                AppliedChanges = count,
                Message = $"Applied {count} selected CS2 setting(s). Backup: {backup.BackupFolder}"
            };
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to apply CS2 preset", ex);
            return new GameConfigApplyResult { Success = false, BackupFolder = backup.BackupFolder, Message = ex.Message };
        }
    }

    public GameConfigBackupResult RestoreLatestBackup(Cs2ConfigAnalysis analysis)
    {
        try
        {
            string root = Path.Combine(AppPaths.UserDataDirectory, "Backups", "CS2");
            if (!Directory.Exists(root)) return new GameConfigBackupResult { Success = false, Message = "No CS2 backup folder found." };

            string? latest = Directory.GetDirectories(root).OrderByDescending(x => x).FirstOrDefault();
            if (latest == null) return new GameConfigBackupResult { Success = false, Message = "No CS2 backup found." };

            int restored = 0;
            restored += RestoreIfAvailable(latest, analysis.Paths.VideoConfigPath);
            restored += RestoreIfAvailable(latest, analysis.Paths.MachineConvarsPath);
            restored += RestoreIfAvailable(latest, analysis.Paths.UserConvarsPath);
            restored += RestoreIfAvailable(latest, analysis.Paths.UserKeysPath);
            restored += RestoreIfAvailable(latest, GetAutoexecPath(analysis));

            return new GameConfigBackupResult
            {
                Success = restored > 0,
                BackupFolder = latest,
                Message = restored > 0 ? $"Restored {restored} CS2 config file(s) from: {latest}" : $"Latest backup found, but no matching current config path was detected: {latest}"
            };
        }
        catch (Exception ex)
        {
            return new GameConfigBackupResult { Success = false, Message = ex.Message };
        }
    }

    public string? GetAutoexecPath(Cs2ConfigAnalysis? analysis)
    {
        string? cfgFolder = analysis?.Paths.GameCfgFolder;
        return string.IsNullOrWhiteSpace(cfgFolder) ? null : Path.Combine(cfgFolder, "autoexec.cfg");
    }

    public Cs2AutoexecResult LoadAutoexec(Cs2ConfigAnalysis analysis, bool createIfMissing)
    {
        try
        {
            string? path = GetAutoexecPath(analysis);
            if (string.IsNullOrWhiteSpace(path))
            {
                return new Cs2AutoexecResult
                {
                    Success = false,
                    Message = "CS2 game cfg folder was not detected. Open CS2 from Steam Library scan first or set the executable path."
                };
            }

            if (!File.Exists(path))
            {
                if (!createIfMissing)
                {
                    return new Cs2AutoexecResult
                    {
                        Success = true,
                        Exists = false,
                        Path = path,
                        Content = string.Empty,
                        Message = "autoexec.cfg does not exist yet. Click create/open to generate an empty file."
                    };
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, string.Empty);
            }

            return new Cs2AutoexecResult
            {
                Success = true,
                Exists = true,
                Path = path,
                Content = File.ReadAllText(path),
                Message = File.Exists(path) ? $"Loaded autoexec.cfg: {path}" : $"Created autoexec.cfg: {path}"
            };
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to load CS2 autoexec.cfg", ex);
            return new Cs2AutoexecResult { Success = false, Message = ex.Message };
        }
    }

    public GameConfigApplyResult SaveAutoexec(Cs2ConfigAnalysis analysis, string content)
    {
        try
        {
            string? path = GetAutoexecPath(analysis);
            if (string.IsNullOrWhiteSpace(path))
            {
                return new GameConfigApplyResult { Success = false, Message = "CS2 game cfg folder was not detected." };
            }

            string backupFolder = string.Empty;
            if (File.Exists(path))
            {
                backupFolder = Path.Combine(AppPaths.UserDataDirectory, "Backups", "CS2", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_autoexec");
                Directory.CreateDirectory(backupFolder);
                File.Copy(path, Path.Combine(backupFolder, "autoexec.cfg"), overwrite: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, NormalizeAutoexecContent(content));

            return new GameConfigApplyResult
            {
                Success = true,
                BackupFolder = backupFolder,
                AppliedChanges = 1,
                Message = string.IsNullOrWhiteSpace(backupFolder)
                    ? $"Saved autoexec.cfg: {path}"
                    : $"Saved autoexec.cfg: {path}. Backup: {backupFolder}"
            };
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to save CS2 autoexec.cfg", ex);
            return new GameConfigApplyResult { Success = false, Message = ex.Message };
        }
    }

    public Dictionary<string, string> LoadUserConvars(Cs2ConfigAnalysis analysis)
    {
        return ValveConfigParser.ReadKeyValues(analysis.Paths.UserConvarsPath);
    }

    public GameConfigApplyResult SaveUserConvars(Cs2ConfigAnalysis analysis, IReadOnlyDictionary<string, string> values)
    {
        try
        {
            string? path = analysis.Paths.UserConvarsPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new GameConfigApplyResult { Success = false, Message = "CS2 user convars file was not detected." };
            }

            string backupFolder = Path.Combine(AppPaths.UserDataDirectory, "Backups", "CS2", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_crosshair");
            Directory.CreateDirectory(backupFolder);
            File.Copy(path, Path.Combine(backupFolder, Path.GetFileName(path)), overwrite: true);

            bool updated = ValveConfigParser.UpdateQuotedValueFile(path, values);
            return new GameConfigApplyResult
            {
                Success = updated,
                BackupFolder = backupFolder,
                AppliedChanges = updated ? values.Count : 0,
                Message = updated ? $"Saved CS2 crosshair settings. Backup: {backupFolder}" : "No CS2 crosshair values were changed."
            };
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to save CS2 crosshair settings", ex);
            return new GameConfigApplyResult { Success = false, Message = ex.Message };
        }
    }

    private static string NormalizeAutoexecContent(string content)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;
        string normalized = content.Replace("\r\n", "\n").Replace("\r", "\n");
        return normalized.Replace("\n", Environment.NewLine).TrimEnd() + Environment.NewLine;
    }

    private static int RestoreIfAvailable(string backupFolder, string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath)) return 0;
        string backupFile = Path.Combine(backupFolder, Path.GetFileName(targetPath));
        if (!File.Exists(backupFile)) return 0;
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Copy(backupFile, targetPath, overwrite: true);
        return 1;
    }

    private Cs2ConfigPaths DetectConfigPaths(LibraryItem item)
    {
        var paths = new Cs2ConfigPaths();
        paths.GameCfgFolder = DetectGameCfgFolder(item);

        foreach (string folder in FindPotentialUserCfgFolders(item))
        {
            string video = Path.Combine(folder, "cs2_video.txt");
            if (!File.Exists(video)) continue;

            paths.UserCfgFolder = folder;
            paths.UserDataLocalFolder = Directory.GetParent(folder)?.FullName;
            paths.VideoConfigPath = video;
            paths.MachineConvarsPath = Path.Combine(folder, "cs2_machine_convars.vcfg");
            paths.UserConvarsPath = Directory.EnumerateFiles(folder, "cs2_user_convars_*_slot*.vcfg", SearchOption.TopDirectoryOnly).FirstOrDefault();
            paths.UserKeysPath = Directory.EnumerateFiles(folder, "cs2_user_keys_*_slot*.vcfg", SearchOption.TopDirectoryOnly).FirstOrDefault();
            return paths;
        }

        return paths;
    }

    private static string? DetectGameCfgFolder(LibraryItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.InstallPath))
        {
            string candidate = Path.Combine(item.InstallPath, "game", "csgo", "cfg");
            if (Directory.Exists(candidate)) return candidate;
        }

        if (!string.IsNullOrWhiteSpace(item.ExecutablePath))
        {
            string? dir = Path.GetDirectoryName(item.ExecutablePath);
            while (!string.IsNullOrWhiteSpace(dir))
            {
                string candidate = Path.Combine(dir, "game", "csgo", "cfg");
                if (Directory.Exists(candidate)) return candidate;
                dir = Directory.GetParent(dir)?.FullName;
            }
        }

        return null;
    }

    private static IEnumerable<string> FindPotentialUserCfgFolders(LibraryItem item)
    {
        var roots = FindSteamRoots().ToList();

        foreach (string root in roots)
        {
            string userdata = Path.Combine(root, "userdata");
            if (!Directory.Exists(userdata)) continue;

            foreach (string user in Directory.EnumerateDirectories(userdata, "*", SearchOption.TopDirectoryOnly))
            {
                string cfg = Path.Combine(user, "730", "local", "cfg");
                if (Directory.Exists(cfg)) yield return cfg;

                string local = Path.Combine(user, "730", "local");
                if (Directory.Exists(local)) yield return local;
            }
        }

        if (!string.IsNullOrWhiteSpace(item.InstallPath))
        {
            string? drive = Path.GetPathRoot(item.InstallPath);
            if (!string.IsNullOrWhiteSpace(drive))
            {
                string programSteam = Path.Combine(drive, "Steam", "userdata");
                if (Directory.Exists(programSteam))
                {
                    foreach (string user in Directory.EnumerateDirectories(programSteam, "*", SearchOption.TopDirectoryOnly))
                    {
                        string cfg = Path.Combine(user, "730", "local", "cfg");
                        if (Directory.Exists(cfg)) yield return cfg;
                    }
                }
            }
        }
    }

    private static IEnumerable<string> FindSteamRoots()
    {
        var roots = new List<string>();
        TryAddRegistryValue(roots, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
        TryAddRegistryValue(roots, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        TryAddRegistryValue(roots, Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(pf86)) roots.Add(Path.Combine(pf86, "Steam"));
        return roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static void TryAddRegistryValue(List<string> list, RegistryKey root, string path, string valueName)
    {
        try
        {
            using var key = root.OpenSubKey(path);
            string? value = key?.GetValue(valueName)?.ToString();
            if (!string.IsNullOrWhiteSpace(value)) list.Add(value);
        }
        catch
        {
        }
    }

    private static void CopyIfExists(string? source, string targetFolder)
    {
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return;
        File.Copy(source, Path.Combine(targetFolder, Path.GetFileName(source)), overwrite: true);
    }

    private static string BuildSummary(IReadOnlyDictionary<string, string> video)
    {
        if (video.Count == 0) return "No video settings detected.";
        string res = GetResolutionRaw(video);
        string displayMode = GetDisplayModeRaw(video);
        string vsync = Get(video, "setting.mat_vsync", "?") == "0" ? "off" : "on";
        string latency = Get(video, "setting.r_low_latency", "?") == "1" ? "on" : "off/unknown";
        return $"{res}; display={displayMode}; v-sync={vsync}; low-latency={latency}";
    }

    private static GameOptimizationPreset BuildBackupPreset(Dictionary<string, string> video, Dictionary<string, string> machine, Dictionary<string, string>? backupVideo)
    {
        bool hasBackup = backupVideo != null && backupVideo.Count > 0;
        var sourceVideo = hasBackup ? backupVideo! : video;
        var changes = BuildCs2SettingChanges(video, machine, new CompetitiveBaseline(sourceVideo, machine), useCurrentAsRecommended: true);

        foreach (var change in changes)
        {
            change.IsSelected = false;
        }

        return new GameOptimizationPreset
        {
            Id = hasBackup ? "cs2_latest_backup" : "cs2_current_backup_placeholder",
            DisplayName = hasBackup ? "Backup" : "Backup / current config",
            Description = hasBackup
                ? "Preset loaded from the latest FrameHub CS2 backup. Use it to compare or restore individual values. The dedicated restore button is still the safest full rollback."
                : "No previous FrameHub backup was found. This preset mirrors the currently detected CS2 config and does not introduce changes.",
            Changes = changes
        };
    }

    private static GameOptimizationPreset BuildCompetitivePreset(Dictionary<string, string> video, Dictionary<string, string> machine)
    {
        var baseline = new CompetitiveBaseline
        {
            Resolution = "1280x960|0",
            DisplayMode = "fullscreen",
            BoostPlayerContrast = "1",
            VSync = "0",
            LowLatency = "1",
            MsaaMode = "2x",
            ShadowQuality = "0",
            DynamicShadows = "1",
            TextureDetail = "0",
            TextureFiltering = "2",
            ShaderQuality = "0",
            ParticleDetail = "0",
            AmbientOcclusion = "0",
            Hdr = "-1",
            Fsr = "0"
        };

        return new GameOptimizationPreset
        {
            Id = "cs2_competitive_baseline",
            DisplayName = "Competitive Baseline",
            Description = "FrameHub competitive baseline based on the supplied CS2 settings. It keeps the current low-latency competitive setup and recommends true fullscreen instead of borderless fullscreen.",
            Changes = BuildCs2SettingChanges(video, machine, baseline, useCurrentAsRecommended: false)
        };
    }

    private static List<GameSettingChange> BuildCs2SettingChanges(Dictionary<string, string> video, Dictionary<string, string> machine, CompetitiveBaseline baseline, bool useCurrentAsRecommended)
    {
        string resolutionCurrent = GetResolutionRaw(video) == "not found" ? "not found" : GetResolutionRaw(video) + "|" + Get(video, "setting.aspectratiomode", "0");
        string displayCurrent = GetDisplayModeRaw(video);
        string msaaCurrent = GetMsaaModeRaw(video);

        var changes = new List<GameSettingChange>
        {
            DerivedChange("cs2.resolution", resolutionCurrent, useCurrentAsRecommended ? resolutionCurrent : baseline.Resolution, canApply: true, options: ResolutionOptions()),
            DerivedChange("cs2.display_mode", displayCurrent, useCurrentAsRecommended ? displayCurrent : baseline.DisplayMode, canApply: true, optional: true, options: DisplayModeOptions()),
            Change(machine, "r_player_visibility_mode", useCurrentAsRecommended ? Get(machine, "r_player_visibility_mode", "not found") : baseline.BoostPlayerContrast, GameOptimizationTargetFile.MachineConvars, options: OnOffOptions()),
            Change(video, "setting.mat_vsync", useCurrentAsRecommended ? Get(video, "setting.mat_vsync", "not found") : baseline.VSync, GameOptimizationTargetFile.VideoConfig, options: OnOffOptions()),
            Change(video, "setting.r_low_latency", useCurrentAsRecommended ? Get(video, "setting.r_low_latency", "not found") : baseline.LowLatency, GameOptimizationTargetFile.VideoConfig, options: OnOffOptions()),
            DerivedChange("cs2.msaa_mode", msaaCurrent, useCurrentAsRecommended ? msaaCurrent : baseline.MsaaMode, canApply: true, options: MsaaOptions()),
            Change(video, "setting.videocfg_shadow_quality", useCurrentAsRecommended ? Get(video, "setting.videocfg_shadow_quality", "not found") : baseline.ShadowQuality, GameOptimizationTargetFile.VideoConfig, options: QualityOptions()),
            Change(video, "setting.videocfg_dynamic_shadows", useCurrentAsRecommended ? Get(video, "setting.videocfg_dynamic_shadows", "not found") : baseline.DynamicShadows, GameOptimizationTargetFile.VideoConfig, options: DynamicShadowsOptions()),
            Change(video, "setting.videocfg_texture_detail", useCurrentAsRecommended ? Get(video, "setting.videocfg_texture_detail", "not found") : baseline.TextureDetail, GameOptimizationTargetFile.VideoConfig, options: QualityOptions()),
            Change(video, "setting.r_texturefilteringquality", useCurrentAsRecommended ? Get(video, "setting.r_texturefilteringquality", "not found") : baseline.TextureFiltering, GameOptimizationTargetFile.VideoConfig, options: TextureFilteringOptions()),
            Change(video, "setting.shaderquality", useCurrentAsRecommended ? Get(video, "setting.shaderquality", "not found") : baseline.ShaderQuality, GameOptimizationTargetFile.VideoConfig, options: QualityOptions()),
            Change(video, "setting.videocfg_particle_detail", useCurrentAsRecommended ? Get(video, "setting.videocfg_particle_detail", "not found") : baseline.ParticleDetail, GameOptimizationTargetFile.VideoConfig, options: ParticleQualityOptions()),
            Change(video, "setting.videocfg_ao_detail", useCurrentAsRecommended ? Get(video, "setting.videocfg_ao_detail", "not found") : baseline.AmbientOcclusion, GameOptimizationTargetFile.VideoConfig, options: AmbientOcclusionOptions()),
            Change(video, "setting.videocfg_hdr_detail", useCurrentAsRecommended ? Get(video, "setting.videocfg_hdr_detail", "not found") : baseline.Hdr, GameOptimizationTargetFile.VideoConfig, options: HdrOptions()),
            Change(video, "setting.videocfg_fsr_detail", useCurrentAsRecommended ? Get(video, "setting.videocfg_fsr_detail", "not found") : baseline.Fsr, GameOptimizationTargetFile.VideoConfig, options: FsrOptions()),
        };

        return changes;
    }

    private static GameSettingChange DerivedChange(string key, string current, string recommended, bool canApply, bool optional = false, List<GameSettingOption>? options = null)
    {
        var change = new GameSettingChange
        {
            Key = key,
            DisplayName = key,
            CurrentValue = current,
            RecommendedValue = recommended,
            Description = string.Empty,
            IsOptional = optional,
            CanApply = canApply,
            IsSelected = false,
            RiskLevel = optional ? GameOptimizationRiskLevel.Preference : GameOptimizationRiskLevel.Safe,
            TargetFile = GameOptimizationTargetFile.VideoConfig,
            Options = options ?? new List<GameSettingOption>()
        };
        UpdateStatus(change);
        return change;
    }

    private static GameSettingChange Change(IReadOnlyDictionary<string, string> values, string key, string recommended, GameOptimizationTargetFile targetFile = GameOptimizationTargetFile.VideoConfig, bool optional = false, List<GameSettingOption>? options = null)
    {
        string current = Get(values, key, "not found");

        var change = new GameSettingChange
        {
            Key = key,
            DisplayName = key,
            CurrentValue = current,
            RecommendedValue = recommended,
            Description = string.Empty,
            IsOptional = optional,
            CanApply = true,
            RiskLevel = optional ? GameOptimizationRiskLevel.Preference : GameOptimizationRiskLevel.Safe,
            TargetFile = targetFile,
            Options = options ?? new List<GameSettingOption>()
        };

        UpdateStatus(change);
        return change;
    }

    private static void UpdateStatus(GameSettingChange change)
    {
        if (string.Equals(change.CurrentValue, "not found", StringComparison.OrdinalIgnoreCase))
        {
            change.Status = GameOptimizationSettingStatus.NotDetected;
            change.IsSelected = false;
            return;
        }

        if (!change.CanApply)
        {
            change.Status = GameOptimizationSettingStatus.ReadOnly;
            change.IsSelected = false;
            return;
        }

        bool matches = string.Equals(change.CurrentValue, change.RecommendedValue, StringComparison.OrdinalIgnoreCase);
        if (change.IsOptional)
        {
            change.Status = matches ? GameOptimizationSettingStatus.MatchesBaseline : GameOptimizationSettingStatus.OptionalPreference;
            change.IsSelected = !matches;
            return;
        }

        change.Status = matches ? GameOptimizationSettingStatus.MatchesBaseline : GameOptimizationSettingStatus.DifferentFromBaseline;
        change.IsSelected = !matches;
    }

    private static void ApplyResolutionValue(string value, Dictionary<string, string> videoChanges)
    {
        string[] parts = value.Split('|');
        string[] resolution = parts[0].Split('x', 'X');
        if (resolution.Length == 2)
        {
            videoChanges["setting.defaultres"] = resolution[0];
            videoChanges["setting.defaultresheight"] = resolution[1];
        }

        if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
        {
            videoChanges["setting.aspectratiomode"] = parts[1];
        }
    }

    private static void ApplyDisplayModeValue(string value, Dictionary<string, string> videoChanges)
    {
        if (value.Equals("fullscreen", StringComparison.OrdinalIgnoreCase))
        {
            videoChanges["setting.fullscreen"] = "1";
            videoChanges["setting.nowindowborder"] = "0";
            return;
        }

        if (value.Equals("borderless", StringComparison.OrdinalIgnoreCase))
        {
            videoChanges["setting.fullscreen"] = "0";
            videoChanges["setting.nowindowborder"] = "1";
            return;
        }

        videoChanges["setting.fullscreen"] = "0";
        videoChanges["setting.nowindowborder"] = "0";
    }

    private static void ApplyMsaaModeValue(string value, Dictionary<string, string> videoChanges)
    {
        switch (value.ToLowerInvariant())
        {
            case "none":
                videoChanges["setting.msaa_samples"] = "0";
                videoChanges["setting.r_csgo_cmaa_enable"] = "0";
                break;
            case "cmaa2":
                videoChanges["setting.msaa_samples"] = "0";
                videoChanges["setting.r_csgo_cmaa_enable"] = "1";
                break;
            case "2x":
                videoChanges["setting.msaa_samples"] = "2";
                videoChanges["setting.r_csgo_cmaa_enable"] = "0";
                break;
            case "4x":
                videoChanges["setting.msaa_samples"] = "4";
                videoChanges["setting.r_csgo_cmaa_enable"] = "0";
                break;
            case "8x":
                videoChanges["setting.msaa_samples"] = "8";
                videoChanges["setting.r_csgo_cmaa_enable"] = "0";
                break;
        }
    }

    private static string GetResolutionRaw(IReadOnlyDictionary<string, string> video)
    {
        string width = Get(video, "setting.defaultres", "?");
        string height = Get(video, "setting.defaultresheight", "?");
        return width == "?" || height == "?" ? "not found" : $"{width}x{height}";
    }

    private static string GetDisplayModeRaw(IReadOnlyDictionary<string, string> video)
    {
        string fullscreen = Get(video, "setting.fullscreen", "?");
        string borderless = Get(video, "setting.nowindowborder", "?");
        if (fullscreen == "1") return "fullscreen";
        if (borderless == "1") return "borderless";
        if (fullscreen == "0") return "windowed";
        return "not found";
    }

    private static string GetMsaaModeRaw(IReadOnlyDictionary<string, string> video)
    {
        string cmaa = Get(video, "setting.r_csgo_cmaa_enable", "0");
        string samples = Get(video, "setting.msaa_samples", "not found");
        if (samples == "not found") return "not found";
        if (cmaa == "1") return "cmaa2";
        return samples switch
        {
            "0" => "none",
            "2" => "2x",
            "4" => "4x",
            "8" => "8x",
            _ => samples
        };
    }

    private static Dictionary<string, string>? ReadLatestBackupVideoSettings()
    {
        try
        {
            string root = Path.Combine(AppPaths.UserDataDirectory, "Backups", "CS2");
            if (!Directory.Exists(root)) return null;
            string? latest = Directory.GetDirectories(root).OrderByDescending(x => x).FirstOrDefault();
            if (latest == null) return null;
            string video = Path.Combine(latest, "cs2_video.txt");
            return File.Exists(video) ? ValveConfigParser.ReadKeyValues(video) : null;
        }
        catch
        {
            return null;
        }
    }

    private static List<GameSettingOption> ResolutionOptions() => new()
    {
        new() { Value = "1920x1080|1", DisplayOverride = "1920x1080 (16:9)" },
        new() { Value = "1440x1080|0", DisplayOverride = "1440x1080 (4:3)" },
        new() { Value = "1280x960|0", DisplayOverride = "1280x960 (4:3)" },
        new() { Value = "1680x1050|2", DisplayOverride = "1680x1050 (16:10)" },
        new() { Value = "1024x768|0", DisplayOverride = "1024x768 (4:3)" },
        new() { Value = "1280x1024|0", DisplayOverride = "1280x1024 (5:4; CS2 shows 4:3)" },
    };

    private static List<GameSettingOption> DisplayModeOptions() => new()
    {
        new() { Value = "fullscreen" },
        new() { Value = "borderless" },
        new() { Value = "windowed" },
    };

    private static List<GameSettingOption> OnOffOptions() => new()
    {
        new() { Value = "0" },
        new() { Value = "1" },
    };

    private static List<GameSettingOption> MsaaOptions() => new()
    {
        new() { Value = "none" },
        new() { Value = "cmaa2" },
        new() { Value = "2x" },
        new() { Value = "4x" },
        new() { Value = "8x" },
    };

    private static List<GameSettingOption> QualityOptions() => new()
    {
        new() { Value = "0" },
        new() { Value = "1" },
        new() { Value = "2" },
        new() { Value = "3" },
    };

    private static List<GameSettingOption> ParticleQualityOptions() => QualityOptions();

    private static List<GameSettingOption> DynamicShadowsOptions() => new()
    {
        new() { Value = "0" },
        new() { Value = "1" },
    };

    private static List<GameSettingOption> TextureFilteringOptions() => new()
    {
        new() { Value = "0" },
        new() { Value = "1" },
        new() { Value = "2" },
        new() { Value = "3" },
        new() { Value = "4" },
        new() { Value = "5" },
    };

    private static List<GameSettingOption> AmbientOcclusionOptions() => new()
    {
        new() { Value = "0" },
        new() { Value = "1" },
        new() { Value = "2" },
    };

    private static List<GameSettingOption> HdrOptions() => new()
    {
        new() { Value = "0" },
        new() { Value = "-1" },
        new() { Value = "1" },
    };

    private static List<GameSettingOption> FsrOptions() => new()
    {
        new() { Value = "4" },
        new() { Value = "3" },
        new() { Value = "2" },
        new() { Value = "1" },
        new() { Value = "0" },
    };

    private static string Get(IReadOnlyDictionary<string, string> values, string key, string fallback)
    {
        return values.TryGetValue(key, out string? value) ? value : fallback;
    }

    private sealed class CompetitiveBaseline
    {
        public CompetitiveBaseline()
        {
        }

        public CompetitiveBaseline(Dictionary<string, string> video, Dictionary<string, string> machine)
        {
            Resolution = GetResolutionRaw(video) == "not found" ? "not found" : GetResolutionRaw(video) + "|" + Get(video, "setting.aspectratiomode", "0");
            DisplayMode = GetDisplayModeRaw(video);
            BoostPlayerContrast = Get(machine, "r_player_visibility_mode", "not found");
            VSync = Get(video, "setting.mat_vsync", "not found");
            LowLatency = Get(video, "setting.r_low_latency", "not found");
            MsaaMode = GetMsaaModeRaw(video);
            ShadowQuality = Get(video, "setting.videocfg_shadow_quality", "not found");
            DynamicShadows = Get(video, "setting.videocfg_dynamic_shadows", "not found");
            TextureDetail = Get(video, "setting.videocfg_texture_detail", "not found");
            TextureFiltering = Get(video, "setting.r_texturefilteringquality", "not found");
            ShaderQuality = Get(video, "setting.shaderquality", "not found");
            ParticleDetail = Get(video, "setting.videocfg_particle_detail", "not found");
            AmbientOcclusion = Get(video, "setting.videocfg_ao_detail", "not found");
            Hdr = Get(video, "setting.videocfg_hdr_detail", "not found");
            Fsr = Get(video, "setting.videocfg_fsr_detail", "not found");
        }

        public string Resolution { get; set; } = "not found";
        public string DisplayMode { get; set; } = "not found";
        public string BoostPlayerContrast { get; set; } = "not found";
        public string VSync { get; set; } = "not found";
        public string LowLatency { get; set; } = "not found";
        public string MsaaMode { get; set; } = "not found";
        public string ShadowQuality { get; set; } = "not found";
        public string DynamicShadows { get; set; } = "not found";
        public string TextureDetail { get; set; } = "not found";
        public string TextureFiltering { get; set; } = "not found";
        public string ShaderQuality { get; set; } = "not found";
        public string ParticleDetail { get; set; } = "not found";
        public string AmbientOcclusion { get; set; } = "not found";
        public string Hdr { get; set; } = "not found";
        public string Fsr { get; set; } = "not found";
    }
}
