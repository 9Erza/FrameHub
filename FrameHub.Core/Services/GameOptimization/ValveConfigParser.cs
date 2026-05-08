using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace FrameHub.Core.Services.GameOptimization;

public static class ValveConfigParser
{
    private static readonly Regex KeyValueRegex = new("\"(?<key>[^\"]+)\"\\s+\"(?<value>[^\"]*)\"", RegexOptions.Compiled);

    public static Dictionary<string, string> ReadKeyValues(string? filePath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return result;

        string text = File.ReadAllText(filePath);
        foreach (Match match in KeyValueRegex.Matches(text))
        {
            string key = match.Groups["key"].Value;
            string value = match.Groups["value"].Value;
            if (!result.ContainsKey(key)) result[key] = value;
        }

        return result;
    }

    public static bool UpdateQuotedValueFile(string filePath, IReadOnlyDictionary<string, string> replacements)
    {
        if (!File.Exists(filePath) || replacements.Count == 0) return false;

        string original = File.ReadAllText(filePath);
        string updated = KeyValueRegex.Replace(original, match =>
        {
            string key = match.Groups["key"].Value;
            if (!replacements.TryGetValue(key, out string? replacement)) return match.Value;
            return $"\"{key}\"\t\t\"{replacement}\"";
        });

        if (string.Equals(original, updated, StringComparison.Ordinal)) return false;
        File.WriteAllText(filePath, updated);
        return true;
    }
}
