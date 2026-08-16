using FrameHub.Core.Models.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FrameHub.Core.Services.Library;

/// <summary>
/// Passive Riot Games discovery through official Riot-created Windows Start Menu shortcuts.
/// Shortcuts are only read (target path and stored arguments) to classify the product; launching
/// later executes the shortcut itself via the shell, equivalent to the user double-clicking it.
/// No Riot metadata, lockfiles, client protocols, or network endpoints are used.
/// </summary>
public sealed class RiotLibraryScanner
{
    private readonly string[] _startMenuRoots;
    private readonly Func<string, (string? TargetPath, string? Arguments)> _shortcutResolver;

    public RiotLibraryScanner(
        IEnumerable<string>? startMenuRoots = null,
        Func<string, (string? TargetPath, string? Arguments)>? shortcutResolver = null)
    {
        _startMenuRoots = (startMenuRoots ?? DefaultStartMenuRoots()).ToArray();
        _shortcutResolver = shortcutResolver ?? ResolveShortcutThroughShell;
    }

    public LibraryScanResult Scan()
    {
        var result = new LibraryScanResult();
        var seenProducts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string root in _startMenuRoots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (string shortcut in EnumerateShortcutsSafe(root))
            {
                try
                {
                    (string? targetPath, string? arguments) = _shortcutResolver(shortcut);

                    if (string.IsNullOrWhiteSpace(targetPath)
                        || !Path.GetFileNameWithoutExtension(targetPath)
                            .Equals(RiotGameProcesses.RiotClientExecutableName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string? productId = ExtractLaunchProduct(arguments);
                    RiotGameProcesses.RiotProductKnowledge? product = RiotGameProcesses.FindProduct(productId);
                    if (product == null)
                    {
                        if (productId != null && !seenProducts.Contains(productId))
                        {
                            result.Warnings.Add($"Riot product '{productId}' was found but is not supported for discovery.");
                            seenProducts.Add(productId);
                        }
                        continue;
                    }

                    if (!seenProducts.Add(product.ProductId)) continue;

                    string? installRoot = TryGetParentDirectory(Path.GetDirectoryName(targetPath.Trim()));
                    string? gameExecutablePath = null;
                    if (installRoot != null)
                    {
                        string candidate = Path.Combine(installRoot, product.RelativeGameExecutablePath);
                        if (File.Exists(candidate))
                        {
                            gameExecutablePath = candidate;
                        }
                    }

                    if (gameExecutablePath == null)
                    {
                        result.Warnings.Add($"Riot game executable for '{product.DisplayName}' was not found; item uses name-based process identity only.");
                    }

                    result.Items.Add(new LibraryItem
                    {
                        DisplayName = product.DisplayName,
                        Source = LibrarySource.Riot,
                        Type = LibraryItemType.Game,
                        AppId = product.ProductId,
                        InstallPath = installRoot != null && Directory.Exists(installRoot) ? installRoot : null,
                        ExecutablePath = gameExecutablePath,
                        LaunchPath = Path.GetFullPath(shortcut),
                        ProcessName = product.GameProcessName,
                        IconPath = gameExecutablePath,
                        IsEnabled = true,
                        WatchProcess = true,
                        AllowRemoteControl = false,
                        AllowBenchmark = true
                    });
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Riot shortcut skipped: {shortcut} ({ex.Message})");
                }
            }
        }

        return result;
    }

    private static IEnumerable<string> EnumerateShortcutsSafe(string root)
    {
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            IEnumerable<string> files = Array.Empty<string>();
            IEnumerable<string> directories = Array.Empty<string>();

            try { files = Directory.EnumerateFiles(current.Path, "*.lnk", SearchOption.TopDirectoryOnly); }
            catch { }

            foreach (string file in files)
            {
                yield return file;
            }

            if (current.Depth >= 4) continue;

            try { directories = Directory.EnumerateDirectories(current.Path, "*", SearchOption.TopDirectoryOnly); }
            catch { }

            foreach (string directory in directories)
            {
                queue.Enqueue((directory, current.Depth + 1));
            }
        }
    }

    public static string? ExtractLaunchProduct(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments)) return null;

        var match = System.Text.RegularExpressions.Regex.Match(
            arguments,
            @"--launch-product=([A-Za-z0-9_\-]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? TryGetParentDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return null;
        try
        {
            string? parent = Path.GetFullPath(directory);
            parent = Path.GetDirectoryName(parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return parent;
        }
        catch
        {
            return null;
        }
    }

    private static (string? TargetPath, string? Arguments) ResolveShortcutThroughShell(string shortcutPath)
    {
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
        {
            return (null, null);
        }

        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell == null) return (null, null);

            shortcut = shell.GetType().InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                shell,
                [shortcutPath]);

            string? targetPath = shortcut!.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, shortcut, null) as string;
            string? arguments = shortcut.GetType().InvokeMember("Arguments", System.Reflection.BindingFlags.GetProperty, null, shortcut, null) as string;
            return (targetPath, arguments);
        }
        finally
        {
            if (shortcut is IDisposable disposableShortcut) disposableShortcut.Dispose();
            if (shell is IDisposable disposableShell) disposableShell.Dispose();
        }
    }

    private static IEnumerable<string> DefaultStartMenuRoots()
    {
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            "Programs");
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs");
    }
}
