using FrameHub.Core.Models.SessionOptimization;
using System.Collections.Generic;

namespace FrameHub.Core.Services.SessionOptimization;

public static class BackgroundProcessRuleFactory
{
    public static List<BackgroundProcessRule> CreateDefaultRules(SessionOptimizationSettings settings, SessionGameSuspendSettings? gameSettings = null)
    {
        var rules = new List<BackgroundProcessRule>
        {
            new()
            {
                Id = "explorer",
                DisplayName = "Eksplorator Windows",
                Description = "explorer.exe",
                Category = "System",
                DefaultEnabled = false,
                IsEnabled = false,
                IsAdvanced = true,
                RequiresExtraConfirmation = true,
                ProcessNames = new() { "explorer.exe" }
            },
            new()
            {
                Id = "browsers",
                DisplayName = "Przeglądarki",
                Description = "Chrome, Edge, Brave, Firefox, Opera, Vivaldi",
                Category = "Browsers",
                DefaultEnabled = true,
                IsEnabled = true,
                ProcessNames = new()
                {
                    "chrome.exe", "msedge.exe", "brave.exe", "firefox.exe", "opera.exe", "opera_gx.exe", "vivaldi.exe",
                    "chrome_crashpad_handler.exe", "firefox_crashpad_handler.exe"
                }
            },
            new()
            {
                Id = "spotify",
                DisplayName = "Spotify",
                Description = "Spotify.exe, SpotifyWebHelper.exe",
                Category = "Media",
                DefaultEnabled = true,
                IsEnabled = true,
                ProcessNames = new() { "Spotify.exe", "SpotifyWebHelper.exe" }
            },
            new()
            {
                Id = "discord",
                DisplayName = "Discord",
                Description = "Discord.exe, DiscordPTB.exe, DiscordCanary.exe",
                Category = "Communication",
                DefaultEnabled = false,
                IsEnabled = false,
                ProcessNames = new() { "Discord.exe", "DiscordPTB.exe", "DiscordCanary.exe" },
                PathContains = new() { "\\Discord\\", "\\DiscordPTB\\", "\\DiscordCanary\\" }
            },
            new()
            {
                Id = "teamspeak",
                DisplayName = "TeamSpeak",
                Description = "ts3client_win64.exe, ts3client_win32.exe, TeamSpeak.exe",
                Category = "Communication",
                DefaultEnabled = false,
                IsEnabled = false,
                ProcessNames = new() { "ts3client_win64.exe", "ts3client_win32.exe", "TeamSpeak.exe", "teamspeak.exe" }
            },
            new()
            {
                Id = "steamwebhelper",
                DisplayName = "Steam WebHelper",
                Description = "steamwebhelper.exe",
                Category = "Launchers",
                DefaultEnabled = true,
                IsEnabled = true,
                ProcessNames = new() { "steamwebhelper.exe" }
            }
        };

        foreach (var rule in rules)
        {
            if (gameSettings?.RuleEnabledStates.TryGetValue(rule.Id, out bool gameEnabled) == true)
            {
                rule.IsEnabled = gameEnabled;
            }
            else if (settings.RuleEnabledStates.TryGetValue(rule.Id, out bool legacyEnabled))
            {
                rule.IsEnabled = legacyEnabled;
            }
            else
            {
                rule.IsEnabled = rule.DefaultEnabled;
            }
        }

        return rules;
    }
}
