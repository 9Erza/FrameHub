using FrameHub.Core.Models.GameOptimization;
using FrameHub.Core.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FrameHub.Core.Services.GameData;

public sealed class GameDataService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public GameDataBundle? LoadGameBundle(string gameId, string languageCode)
    {
        string normalizedLanguage = NormalizeLanguage(languageCode);
        foreach (string root in EnumerateGameDataRoots())
        {
            string gameFolder = Path.Combine(root, "games", gameId);
            if (!Directory.Exists(gameFolder)) continue;

            string gamePath = Path.Combine(gameFolder, "game.json");
            string settingsPath = Path.Combine(gameFolder, $"settings.{normalizedLanguage}.json");
            if (!File.Exists(settingsPath)) settingsPath = Path.Combine(gameFolder, "settings.en.json");
            string presetsPath = Path.Combine(gameFolder, "presets.json");

            if (!File.Exists(gamePath) || !File.Exists(settingsPath) || !File.Exists(presetsPath)) continue;

            var game = ReadJson<GameDefinitionFile>(gamePath);
            var settings = ReadJson<GameSettingsFile>(settingsPath);
            var presets = ReadJson<GamePresetsFile>(presetsPath);
            if (game == null || settings == null || presets == null) continue;

            return new GameDataBundle
            {
                Game = game,
                Settings = settings,
                Presets = presets
            };
        }

        return null;
    }

    public IReadOnlyList<string> EnumerateGameDataRoots()
    {
        var roots = new List<string>();

        string userRoot = Path.Combine(AppPaths.UserDataDirectory, "GameData");
        roots.Add(userRoot);

        string appRoot = Path.Combine(AppContext.BaseDirectory, "GameData");
        roots.Add(appRoot);

        string? current = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrWhiteSpace(current); i++)
        {
            string repoRoot = Path.Combine(current, "FrameHub.GameData");
            roots.Add(repoRoot);
            current = Directory.GetParent(current)?.FullName;
        }

        return roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string NormalizeLanguage(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode)) return "en";
        return languageCode.StartsWith("pl", StringComparison.OrdinalIgnoreCase) ? "pl" : "en";
    }

    private static T? ReadJson<T>(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch
        {
            return default;
        }
    }
}
