using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FrameHub.Core.Models.GameOptimization;

public sealed class GameDataManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("dataVersion")]
    public string DataVersion { get; set; } = string.Empty;

    [JsonPropertyName("minimumFrameHubVersion")]
    public string MinimumFrameHubVersion { get; set; } = string.Empty;

    [JsonPropertyName("games")]
    public List<GameDataManifestGame> Games { get; set; } = new();
}

public sealed class GameDataManifestGame
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("files")]
    public Dictionary<string, string> Files { get; set; } = new();
}

public sealed class GameDefinitionFile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("shortName")]
    public string ShortName { get; set; } = string.Empty;

    [JsonPropertyName("allowedKeys")]
    public List<string> AllowedKeys { get; set; } = new();
}

public sealed class GameSettingsFile
{
    [JsonPropertyName("gameId")]
    public string GameId { get; set; } = string.Empty;

    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;

    [JsonPropertyName("settings")]
    public List<GameSettingDefinition> Settings { get; set; } = new();
}

public sealed class GameSettingDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "single";

    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("keys")]
    public List<string> Keys { get; set; } = new();

    [JsonPropertyName("targetFile")]
    public string TargetFile { get; set; } = "video";

    [JsonPropertyName("optional")]
    public bool Optional { get; set; }

    [JsonPropertyName("canApply")]
    public bool CanApply { get; set; } = true;

    [JsonPropertyName("options")]
    public List<GameSettingOptionDefinition> Options { get; set; } = new();
}

public sealed class GameSettingOptionDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("values")]
    public Dictionary<string, string> Values { get; set; } = new();
}

public sealed class GamePresetsFile
{
    [JsonPropertyName("gameId")]
    public string GameId { get; set; } = string.Empty;

    [JsonPropertyName("presets")]
    public List<GamePresetDefinition> Presets { get; set; } = new();
}

public sealed class GamePresetDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "recommended";

    [JsonPropertyName("settings")]
    public Dictionary<string, string> Settings { get; set; } = new();
}

public sealed class GameDataBundle
{
    public GameDefinitionFile Game { get; set; } = new();
    public GameSettingsFile Settings { get; set; } = new();
    public GamePresetsFile Presets { get; set; } = new();
}
