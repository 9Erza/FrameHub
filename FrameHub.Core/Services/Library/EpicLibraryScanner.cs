using FrameHub.Core.Models.Library;
using System;
using System.IO;
using System.Text.Json;

namespace FrameHub.Core.Services.Library;

public sealed class EpicLibraryScanner
{
    public LibraryScanResult Scan()
    {
        var result = new LibraryScanResult();
        string manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");

        if (!Directory.Exists(manifests))
        {
            result.Warnings.Add("Epic Games manifests folder was not found.");
            return result;
        }

        foreach (string file in Directory.EnumerateFiles(manifests, "*.item", SearchOption.TopDirectoryOnly))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;

                string displayName = GetString(root, "DisplayName") ?? GetString(root, "AppName") ?? Path.GetFileNameWithoutExtension(file);
                string? installLocation = GetString(root, "InstallLocation");
                string? launchExecutable = GetString(root, "LaunchExecutable");
                string? appName = GetString(root, "AppName") ?? GetString(root, "CatalogItemId");

                if (!IsInstalledGameManifest(root)) continue;

                string? executablePath = null;
                if (!string.IsNullOrWhiteSpace(launchExecutable))
                {
                    executablePath = Path.IsPathRooted(launchExecutable)
                        ? launchExecutable
                        : !string.IsNullOrWhiteSpace(installLocation) ? Path.Combine(installLocation, launchExecutable) : null;
                }

                // LaunchExecutable is often an Unreal bootstrap or launcher stub. Epic games launch
                // through the Epic protocol, so the stored path only identifies the running game
                // process: prefer a "-Shipping" binary when the install contains one.
                string? resolved = ExecutableResolver.FindBestExecutable(installLocation, displayName);
                bool resolvedIsShipping = resolved != null
                    && Path.GetFileNameWithoutExtension(resolved).EndsWith("-Shipping", StringComparison.OrdinalIgnoreCase);
                if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath) || resolvedIsShipping)
                {
                    executablePath = resolved ?? executablePath;
                }

                var item = new LibraryItem
                {
                    DisplayName = displayName,
                    Source = LibrarySource.Epic,
                    Type = LibraryItemType.Game,
                    AppId = appName,
                    InstallPath = Directory.Exists(installLocation) ? installLocation : null,
                    ExecutablePath = File.Exists(executablePath) ? executablePath : null,
                    ProcessName = ExecutableResolver.ProcessNameFromExecutable(executablePath),
                    IconPath = File.Exists(executablePath) ? executablePath : null,
                    IsEnabled = true,
                    WatchProcess = true
                };
                if (LibraryItemFilter.IsSupportedLibraryItem(item)) result.Items.Add(item);
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Epic manifest skipped: {Path.GetFileName(file)} ({ex.Message})");
            }
        }

        return result;
    }

    /// <summary>
    /// Epic writes manifests for engines, plugins, DLC and unfinished installs next to games.
    /// Only complete base-game installs belong in the library: categories (when present) must
    /// include "games", a DLC (MainGameAppName differs from AppName) is skipped, and an
    /// incomplete install is skipped.
    /// </summary>
    internal static bool IsInstalledGameManifest(JsonElement root)
    {
        if (root.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.ValueKind == JsonValueKind.True)
        {
            return false;
        }

        if (root.TryGetProperty("AppCategories", out var categories) && categories.ValueKind == JsonValueKind.Array && categories.GetArrayLength() > 0)
        {
            bool isGame = categories.EnumerateArray().Any(c =>
                c.ValueKind == JsonValueKind.String && string.Equals(c.GetString(), "games", StringComparison.OrdinalIgnoreCase));
            if (!isGame) return false;
        }

        string? appName = GetString(root, "AppName");
        string? mainGame = GetString(root, "MainGameAppName");
        if (!string.IsNullOrWhiteSpace(appName) && !string.IsNullOrWhiteSpace(mainGame)
            && !string.Equals(appName, mainGame, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}
