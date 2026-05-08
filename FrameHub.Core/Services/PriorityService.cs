using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace FrameHub.Core.Services
{
    /// <summary>
    /// Keeps process priority names canonical and language-independent in saved profiles.
    /// </summary>
    public static class PriorityService
    {
        public static readonly string[] SafePriorityNames =
        {
            "Idle", "BelowNormal", "Normal", "AboveNormal", "High"
        };

        public static readonly string[] AllPriorityNames =
        {
            "Idle", "BelowNormal", "Normal", "AboveNormal", "High", "RealTime"
        };

        public static IEnumerable<string> GetDisplayPriorities(string languageCode, bool allowRealtime)
        {
            foreach (string priority in allowRealtime ? AllPriorityNames : SafePriorityNames)
            {
                yield return Translate(priority, languageCode);
            }
        }

        public static string Normalize(string? priority, bool allowRealtime = true)
        {
            string normalized = priority?.Trim() switch
            {
                "Idle" or "Bezczynny" => "Idle",
                "BelowNormal" or "Below Normal" or "Poniżej Normalnego" => "BelowNormal",
                "Normal" or "Normalny" => "Normal",
                "AboveNormal" or "Above Normal" or "Powyżej Normalnego" => "AboveNormal",
                "High" or "Wysoki" => "High",
                "RealTime" or "Real Time" or "Czas Rzeczywisty" => "RealTime",
                _ => "Normal"
            };

            return normalized == "RealTime" && !allowRealtime ? "High" : normalized;
        }

        public static string Translate(string? priority, string languageCode)
        {
            string normalized = Normalize(priority, allowRealtime: true);

            if (languageCode == "pl")
            {
                return normalized switch
                {
                    "Idle" => "Bezczynny",
                    "BelowNormal" => "Poniżej Normalnego",
                    "Normal" => "Normalny",
                    "AboveNormal" => "Powyżej Normalnego",
                    "High" => "Wysoki",
                    "RealTime" => "Czas Rzeczywisty",
                    _ => "Normalny"
                };
            }

            return normalized switch
            {
                "BelowNormal" => "Below Normal",
                "AboveNormal" => "Above Normal",
                "RealTime" => "Real Time",
                _ => normalized
            };
        }

        public static bool TryParse(string? priority, bool allowRealtime, out ProcessPriorityClass processPriority)
        {
            string normalized = Normalize(priority, allowRealtime);
            return Enum.TryParse(normalized, out processPriority);
        }
    }
}
