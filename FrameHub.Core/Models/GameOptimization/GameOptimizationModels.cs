using System;
using System.Collections.Generic;

namespace FrameHub.Core.Models.GameOptimization;

public enum GameOptimizationRiskLevel
{
    Safe,
    Preference,
    Advanced
}

public enum GameOptimizationSettingStatus
{
    MatchesBaseline,
    DifferentFromBaseline,
    OptionalPreference,
    NotDetected,
    ReadOnly
}

public enum GameOptimizationTargetFile
{
    VideoConfig,
    MachineConvars,
    Autoexec
}

public sealed class GameSettingOption
{
    public string Value { get; set; } = string.Empty;
    public string? DisplayOverride { get; set; }
}

public sealed class GameSettingChange
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CurrentValue { get; set; } = string.Empty;
    public string RecommendedValue { get; set; } = string.Empty;
    public GameOptimizationTargetFile TargetFile { get; set; } = GameOptimizationTargetFile.VideoConfig;
    public GameOptimizationRiskLevel RiskLevel { get; set; } = GameOptimizationRiskLevel.Safe;
    public GameOptimizationSettingStatus Status { get; set; } = GameOptimizationSettingStatus.MatchesBaseline;
    public bool IsSelected { get; set; } = true;
    public bool IsOptional { get; set; }
    public bool CanApply { get; set; } = true;
    public List<GameSettingOption> Options { get; set; } = new();
}


public sealed class GameOptimizationPreset
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<GameSettingChange> Changes { get; set; } = new();

    public override string ToString() => string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName;
}

public sealed class Cs2ConfigPaths
{
    public string? UserDataLocalFolder { get; set; }
    public string? UserCfgFolder { get; set; }
    public string? GameCfgFolder { get; set; }
    public string? VideoConfigPath { get; set; }
    public string? MachineConvarsPath { get; set; }
    public string? UserConvarsPath { get; set; }
    public string? UserKeysPath { get; set; }
    public bool IsComplete => !string.IsNullOrWhiteSpace(VideoConfigPath) && System.IO.File.Exists(VideoConfigPath);
}

public sealed class Cs2ConfigAnalysis
{
    public bool IsDetected { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
    public Cs2ConfigPaths Paths { get; set; } = new();
    public Dictionary<string, string> VideoSettings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> MachineConvars { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<GameOptimizationPreset> Presets { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
    public int BaselineMatchedSettings { get; set; }
    public int BaselineTotalSettings { get; set; }
}

public sealed class GameConfigBackupResult
{
    public bool Success { get; set; }
    public string BackupFolder { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class GameConfigApplyResult
{
    public bool Success { get; set; }
    public string BackupFolder { get; set; } = string.Empty;
    public int AppliedChanges { get; set; }
    public string Message { get; set; } = string.Empty;
}


public sealed class Cs2AutoexecResult
{
    public bool Success { get; set; }
    public bool Exists { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
