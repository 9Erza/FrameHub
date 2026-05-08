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

                string? executablePath = null;
                if (!string.IsNullOrWhiteSpace(launchExecutable))
                {
                    executablePath = Path.IsPathRooted(launchExecutable)
                        ? launchExecutable
                        : !string.IsNullOrWhiteSpace(installLocation) ? Path.Combine(installLocation, launchExecutable) : null;
                }

                if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                {
                    executablePath = ExecutableResolver.FindBestExecutable(installLocation, displayName);
                }

                result.Items.Add(new LibraryItem
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
                });
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Epic manifest skipped: {Path.GetFileName(file)} ({ex.Message})");
            }
        }

        return result;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}
