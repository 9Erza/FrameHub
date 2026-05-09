using FrameHub.Core.Logging;
using FrameHub.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FrameHub.Core.Services
{
    /// <summary>
    /// Loads, validates, deduplicates, migrates and safely saves process optimization profiles.
    /// </summary>
    public class ProfileService
    {
        private static readonly ILogger _logger = LoggerService.Instance;
        private readonly string _filePath = AppPaths.GetUserDataFilePath("profiles.json");

        private static JsonSerializerOptions JsonOptions
        {
            get
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNameCaseInsensitive = true
                };
                options.Converters.Add(new JsonStringEnumConverter());
                return options;
            }
        }

        public List<ProcessProfile> LoadProfiles()
        {
            AppPaths.MigrateLegacyFileIfNeeded("profiles.json");

            if (!File.Exists(_filePath))
            {
                _logger.Debug("Profiles file not found - returning empty list");
                return new List<ProcessProfile>();
            }

            try
            {
                string? jsonContent = AtomicFileService.ReadAllTextWithBackup(_filePath);
                if (string.IsNullOrWhiteSpace(jsonContent)) return new List<ProcessProfile>();

                var loadedProfiles = JsonSerializer.Deserialize<List<ProcessProfile>>(jsonContent, JsonOptions) ?? new List<ProcessProfile>();
                var sanitized = SanitizeProfiles(loadedProfiles).ToList();

                if (sanitized.Count != loadedProfiles.Count || !ProfilesEquivalent(sanitized, loadedProfiles))
                {
                    SaveProfiles(sanitized);
                }

                _logger.Info($"Profiles loaded: {sanitized.Count}");
                return sanitized;
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to load profiles - returning empty list", ex);
                return new List<ProcessProfile>();
            }
        }

        public void SaveProfiles(IEnumerable<ProcessProfile> profiles)
        {
            try
            {
                var sanitized = SanitizeProfiles(profiles).ToList();
                string jsonContent = JsonSerializer.Serialize(sanitized, JsonOptions);

                // Validate the serialized JSON before replacing the live file.
                _ = JsonSerializer.Deserialize<List<ProcessProfile>>(jsonContent, JsonOptions);

                AtomicFileService.WriteAllTextAtomic(_filePath, jsonContent);
                _logger.Info($"Profiles saved: {sanitized.Count}");
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to save profiles", ex);
            }
        }

        public static IEnumerable<ProcessProfile> SanitizeProfiles(IEnumerable<ProcessProfile> profiles)
        {
            return profiles
                .Where(p => p != null)
                .Select(SanitizeProfile)
                .Where(p => !string.IsNullOrWhiteSpace(p.ProcessName))
                .Where(p => p.AffinityMask != 0)
                .Where(p => p.ApplyPriority || p.ApplyCoreOptimization)
                .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderBy(p => p.UpdatedAt).Last())
                .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase);
        }

        private static ProcessProfile SanitizeProfile(ProcessProfile profile)
        {
            profile.SchemaVersion = 2;
            profile.Id = string.IsNullOrWhiteSpace(profile.Id) ? Guid.NewGuid().ToString("N") : profile.Id;
            profile.ProcessName = NormalizeProcessName(profile.ProcessName);
            profile.DisplayName = string.IsNullOrWhiteSpace(profile.DisplayName) ? profile.ProcessName : profile.DisplayName;
            profile.ExecutablePath = string.IsNullOrWhiteSpace(profile.ExecutablePath) ? null : profile.ExecutablePath.Trim();
            profile.LibraryItemId = string.IsNullOrWhiteSpace(profile.LibraryItemId) ? null : profile.LibraryItemId.Trim();
            profile.Priority = PriorityService.Normalize(profile.Priority, allowRealtime: true);
            profile.CreatedAt = profile.CreatedAt == default ? DateTime.UtcNow : profile.CreatedAt;
            profile.UpdatedAt = profile.UpdatedAt == default ? DateTime.UtcNow : profile.UpdatedAt;

            // Preserve old profile behavior: legacy profiles without these fields apply both parts.
            profile.ApplyPriority = profile.ApplyPriority;
            profile.ApplyCoreOptimization = profile.ApplyCoreOptimization;

#pragma warning disable CS0618
            if ((int)profile.OptimizationMode == 2)
            {
                profile.OptimizationMode = OptimizationMode.Affinity;
            }
#pragma warning restore CS0618

            return profile;
        }

        public static string NormalizeProcessName(string? processName)
        {
            string value = processName?.Trim() ?? string.Empty;
            return value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(value)
                : value;
        }

        private static bool ProfilesEquivalent(IReadOnlyList<ProcessProfile> left, IReadOnlyList<ProcessProfile> right)
        {
            if (left.Count != right.Count) return false;

            for (int i = 0; i < left.Count; i++)
            {
                if (!string.Equals(left[i].ProcessName, NormalizeProcessName(right[i].ProcessName), StringComparison.OrdinalIgnoreCase)) return false;
                if (left[i].AffinityMask != right[i].AffinityMask) return false;
                if (!string.Equals(left[i].LibraryItemId, right[i].LibraryItemId, StringComparison.OrdinalIgnoreCase)) return false;
                if (left[i].Priority != PriorityService.Normalize(right[i].Priority, allowRealtime: true)) return false;
                if (left[i].OptimizationMode != right[i].OptimizationMode) return false;
                if (left[i].IsEnabled != right[i].IsEnabled) return false;
                if (left[i].ApplyPriority != right[i].ApplyPriority) return false;
                if (left[i].ApplyCoreOptimization != right[i].ApplyCoreOptimization) return false;
            }

            return true;
        }
    }
}
