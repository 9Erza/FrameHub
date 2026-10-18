using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using FrameHub.Core.Models.GameOptimization;

namespace FrameHub.Core.Services.GameOptimization;

public sealed partial class Cs2OptimizationService
{
    private const string SnapshotManifestName = "snapshot.json";

    private static readonly Regex UserConvarsRegex =
        new(@"^cs2_user_convars_[0-9A-Za-z_-]*_slot[0-9A-Za-z_-]*\.vcfg$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex UserKeysRegex =
        new(@"^cs2_user_keys_[0-9A-Za-z_-]*_slot[0-9A-Za-z_-]*\.vcfg$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool IsAllowedConfigFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (Path.GetFileName(name) != name) return false;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        if (name.Contains('/') || name.Contains('\\') || name.Contains(':') || name.Contains("..")) return false;

        if (string.Equals(name, "cs2_video.txt", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(name, "cs2_machine_convars.vcfg", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(name, "autoexec.cfg", StringComparison.OrdinalIgnoreCase)) return true;

        if (UserConvarsRegex.IsMatch(name)) return true;
        if (UserKeysRegex.IsMatch(name)) return true;

        return false;
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            var fileInfo = new FileInfo(path);
            if (fileInfo.LinkTarget is not null || (fileInfo.Attributes != (FileAttributes)(-1) && (fileInfo.Attributes & FileAttributes.ReparsePoint) != 0))
                return true;

            var dirInfo = new DirectoryInfo(path);
            if (dirInfo.LinkTarget is not null || (dirInfo.Attributes != (FileAttributes)(-1) && (dirInfo.Attributes & FileAttributes.ReparsePoint) != 0))
                return true;

            FileAttributes attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return false;
        }
    }

    private string? ResolveRestoreTarget(Cs2ConfigAnalysis analysis, string fileName)
    {
        if (!IsAllowedConfigFileName(fileName)) return null;

        string? userCfg = !string.IsNullOrWhiteSpace(analysis.Paths.UserCfgFolder)
            ? analysis.Paths.UserCfgFolder
            : analysis.UserdataResolution.Selected?.ConfigFolderPath;

        string? gameCfg = analysis.Paths.GameCfgFolder;

        string? target = null;
        string? expectedDir = null;

        if (string.Equals(fileName, "cs2_video.txt", StringComparison.OrdinalIgnoreCase))
        {
            target = !string.IsNullOrWhiteSpace(analysis.Paths.VideoConfigPath)
                ? analysis.Paths.VideoConfigPath
                : (!string.IsNullOrWhiteSpace(userCfg) ? Path.Combine(userCfg, fileName) : null);
            expectedDir = userCfg;
        }
        else if (string.Equals(fileName, "cs2_machine_convars.vcfg", StringComparison.OrdinalIgnoreCase))
        {
            target = !string.IsNullOrWhiteSpace(analysis.Paths.MachineConvarsPath)
                ? analysis.Paths.MachineConvarsPath
                : (!string.IsNullOrWhiteSpace(userCfg) ? Path.Combine(userCfg, fileName) : null);
            expectedDir = userCfg;
        }
        else if (string.Equals(fileName, "autoexec.cfg", StringComparison.OrdinalIgnoreCase))
        {
            target = GetAutoexecPath(analysis);
            expectedDir = gameCfg;
        }
        else if (UserConvarsRegex.IsMatch(fileName))
        {
            if (!string.IsNullOrWhiteSpace(analysis.Paths.UserConvarsPath) &&
                string.Equals(Path.GetFileName(analysis.Paths.UserConvarsPath), fileName, StringComparison.OrdinalIgnoreCase))
            {
                target = analysis.Paths.UserConvarsPath;
            }
            else if (!string.IsNullOrWhiteSpace(userCfg))
            {
                target = Path.Combine(userCfg, fileName);
            }
            expectedDir = userCfg;
        }
        else if (UserKeysRegex.IsMatch(fileName))
        {
            if (!string.IsNullOrWhiteSpace(analysis.Paths.UserKeysPath) &&
                string.Equals(Path.GetFileName(analysis.Paths.UserKeysPath), fileName, StringComparison.OrdinalIgnoreCase))
            {
                target = analysis.Paths.UserKeysPath;
            }
            else if (!string.IsNullOrWhiteSpace(userCfg))
            {
                target = Path.Combine(userCfg, fileName);
            }
            expectedDir = userCfg;
        }

        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(expectedDir))
            return null;

        string fullTarget = Path.GetFullPath(target);
        string fullExpectedDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(expectedDir));

        string targetDir = Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(fullTarget) ?? string.Empty);
        if (!string.Equals(targetDir, fullExpectedDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Restore target '{fullTarget}' escapes expected directory '{fullExpectedDir}'.");
        }

        if (!string.Equals(Path.GetFileName(fullTarget), fileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Restore target '{fullTarget}' does not match expected file name '{fileName}'.");
        }

        if (IsReparsePoint(fullExpectedDir))
        {
            throw new InvalidDataException($"Restore target directory '{fullExpectedDir}' is a reparse point or link.");
        }

        if (IsReparsePoint(fullTarget))
        {
            throw new InvalidDataException($"Restore target '{fullTarget}' is a reparse point or link.");
        }

        return fullTarget;
    }

    private static void UpdateAnalysisPathsAfterRestore(Cs2ConfigAnalysis analysis, List<string> completed)
    {
        foreach (string path in completed)
        {
            string name = Path.GetFileName(path);
            bool exists = File.Exists(path);
            if (UserConvarsRegex.IsMatch(name))
            {
                analysis.Paths.UserConvarsPath = exists ? path : null;
            }
            else if (UserKeysRegex.IsMatch(name))
            {
                analysis.Paths.UserKeysPath = exists ? path : null;
            }
            else if (string.Equals(name, "cs2_video.txt", StringComparison.OrdinalIgnoreCase))
            {
                analysis.Paths.VideoConfigPath = exists ? path : null;
            }
            else if (string.Equals(name, "cs2_machine_convars.vcfg", StringComparison.OrdinalIgnoreCase))
            {
                analysis.Paths.MachineConvarsPath = exists ? path : null;
            }
        }
    }

    private string[] GetConfigFiles(Cs2ConfigAnalysis analysis) => new[]
    {
        analysis.Paths.VideoConfigPath, analysis.Paths.MachineConvarsPath,
        analysis.Paths.UserConvarsPath, analysis.Paths.UserKeysPath, GetAutoexecPath(analysis)
    }.Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => Path.GetFullPath(path!))
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private string CreateVerifiedBackup(Cs2ConfigAnalysis analysis)
    {
        string accountRoot = GetAccountBackupRoot(analysis);
        if (IsReparsePoint(_backupRoot) || IsReparsePoint(accountRoot))
            throw new InvalidDataException($"Backup root '{accountRoot}' is a reparse point or link.");

        Directory.CreateDirectory(accountRoot);
        if (IsReparsePoint(accountRoot))
            throw new InvalidDataException($"Backup root '{accountRoot}' is a reparse point or link.");
        string pending = Path.Combine(accountRoot, $".pending-{Guid.NewGuid():N}");
        Directory.CreateDirectory(pending);
        try
        {
            var manifest = new ConfigBackupManifest { AccountKey = Path.GetFileName(accountRoot) };
            foreach (string path in GetConfigFiles(analysis))
            {
                string name = Path.GetFileName(path);
                if (!File.Exists(path) && !string.Equals(path, Path.GetFullPath(analysis.Paths.VideoConfigPath!), StringComparison.OrdinalIgnoreCase))
                {
                    manifest.MissingFiles.Add(name);
                    continue;
                }
                if (IsReparsePoint(path))
                {
                    throw new InvalidDataException($"Source config file '{path}' is a reparse point or link.");
                }
                byte[] bytes = File.ReadAllBytes(path);
                File.WriteAllBytes(Path.Combine(pending, name), bytes);
                manifest.Files.Add(name, Convert.ToHexString(SHA256.HashData(bytes)));
            }
            File.WriteAllText(Path.Combine(pending, SnapshotManifestName), JsonSerializer.Serialize(manifest));
            VerifySnapshot(pending, analysis);
            string published = Path.Combine(accountRoot, $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fffffff}_{Guid.NewGuid():N}");
            Directory.Move(pending, published);
            return published;
        }
        finally
        {
            if (Directory.Exists(pending))
            {
                try { Directory.Delete(pending, recursive: true); }
                catch (Exception ex) { _logger.Error("Failed to remove an incomplete configuration backup", ex); }
            }
        }
    }

    private ConfigBackupManifest? VerifySnapshot(string folder, Cs2ConfigAnalysis analysis)
    {
        string fullFolder = Path.GetFullPath(folder);
        if (IsReparsePoint(fullFolder))
            throw new InvalidDataException($"Backup directory '{fullFolder}' is a reparse point or link.");

        string path = Path.Combine(fullFolder, SnapshotManifestName);
        if (!File.Exists(path)) return null; // Legacy snapshots predate integrity metadata.
        if (IsReparsePoint(path))
            throw new InvalidDataException($"Backup manifest '{path}' is a reparse point or link.");
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Backup manifest is too large.");
        ConfigBackupManifest manifest = JsonSerializer.Deserialize<ConfigBackupManifest>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Backup manifest is empty.");
        if (manifest.SchemaVersion != 1 || manifest.AccountKey != Path.GetFileName(GetAccountBackupRoot(analysis))
            || manifest.Files is null || manifest.MissingFiles is null
            || manifest.Files.Count == 0 || manifest.Files.Count + manifest.MissingFiles.Count > 5)
            throw new InvalidDataException("Backup manifest does not match the selected account or schema.");
        foreach (var file in manifest.Files)
        {
            if (Path.GetFileName(file.Key) != file.Key || string.IsNullOrWhiteSpace(file.Key)
                || !IsAllowedConfigFileName(file.Key))
                throw new InvalidDataException("Backup integrity verification failed.");

            string source = Path.Combine(fullFolder, file.Key);
            if (IsReparsePoint(source))
                throw new InvalidDataException($"Backup file '{file.Key}' is a reparse point or link.");

            if (!string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))), file.Value, StringComparison.Ordinal))
                throw new InvalidDataException("Backup integrity verification failed.");
        }
        if (manifest.MissingFiles.Any(name => string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name || !IsAllowedConfigFileName(name) || manifest.Files.ContainsKey(name)))
            throw new InvalidDataException("Backup absence metadata is invalid.");
        return manifest;
    }

    private int RestoreVerifiedBackup(Cs2ConfigAnalysis analysis, string folder)
    {
        string fullFolder = Path.GetFullPath(folder);
        string accountRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(GetAccountBackupRoot(analysis)));
        string? parent = Path.GetDirectoryName(fullFolder);
        if (parent is not null) parent = Path.TrimEndingDirectorySeparator(parent);
        string backupRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_backupRoot));

        bool isAccountBackup = string.Equals(parent, accountRoot, StringComparison.OrdinalIgnoreCase);
        bool isLegacyBackup = analysis.UserdataResolution.Candidates.Count == 1
            && string.Equals(parent, backupRoot, StringComparison.OrdinalIgnoreCase);

        if (!isAccountBackup && !isLegacyBackup)
        {
            throw new InvalidDataException($"Backup directory '{fullFolder}' is not within an authorized backup root.");
        }

        if (IsReparsePoint(fullFolder))
        {
            throw new InvalidDataException($"Backup directory '{fullFolder}' is a reparse point or link.");
        }

        ConfigBackupManifest? manifest = VerifySnapshot(fullFolder, analysis);
        var originals = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var writes = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);

        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string target in GetConfigFiles(analysis))
        {
            string name = Path.GetFileName(target);
            if (IsAllowedConfigFileName(name)) fileNames.Add(name);
        }

        if (manifest is not null)
        {
            foreach (string name in manifest.Files.Keys)
            {
                if (IsAllowedConfigFileName(name)) fileNames.Add(name);
            }
            foreach (string name in manifest.MissingFiles)
            {
                if (IsAllowedConfigFileName(name)) fileNames.Add(name);
            }
        }
        else if (Directory.Exists(fullFolder))
        {
            foreach (string entry in Directory.EnumerateFiles(fullFolder, "*", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(entry);
                if (IsAllowedConfigFileName(name)) fileNames.Add(name);
            }
        }

        foreach (string name in fileNames)
        {
            string? target = ResolveRestoreTarget(analysis, name);
            if (target is null) continue;

            string source = Path.Combine(fullFolder, name);
            if (manifest is not null && manifest.MissingFiles.Contains(name))
            {
                if (File.Exists(target)) writes[target] = null;
            }
            else if ((manifest is null || manifest.Files.ContainsKey(name)) && File.Exists(source))
            {
                if (IsReparsePoint(source))
                    throw new InvalidDataException($"Backup source file '{name}' is a reparse point or link.");

                byte[] bytes = File.ReadAllBytes(source);
                if (manifest is not null && Convert.ToHexString(SHA256.HashData(bytes)) != manifest.Files[name])
                    throw new InvalidDataException("Backup changed after integrity verification.");
                writes[target] = bytes;
            }

            if (writes.ContainsKey(target) && File.Exists(target))
            {
                if (IsReparsePoint(target))
                    throw new InvalidDataException($"Restore target '{target}' is a reparse point or link.");
                originals[target] = File.ReadAllBytes(target);
            }
        }

        var completed = new List<string>();
        try
        {
            foreach (var write in writes)
            {
                if (write.Value is null)
                {
                    if (File.Exists(write.Key))
                    {
                        File.Delete(write.Key);
                        completed.Add(write.Key);
                    }
                }
                else
                {
                    AtomicFileService.WriteAllBytesAtomic(write.Key, write.Value);
                    completed.Add(write.Key);
                }
            }
            UpdateAnalysisPathsAfterRestore(analysis, completed);
            return completed.Count;
        }
        catch (Exception ex)
        {
            bool rollbackAttempted = completed.Count > 0;
            bool rollbackSucceeded = rollbackAttempted ? RollBackFiles(completed, originals) : true;
            throw new BackupRestoreException(
                $"Restore failed. Rollback: {(rollbackAttempted ? (rollbackSucceeded ? "completed" : "incomplete; retained backup requires manual recovery") : "not needed")}. {ex.Message}",
                ex,
                rollbackAttempted,
                rollbackSucceeded);
        }
    }

    private sealed class BackupRestoreException : IOException
    {
        public bool RollbackAttempted { get; }
        public bool RollbackSucceeded { get; }

        public BackupRestoreException(string message, Exception inner, bool rollbackAttempted, bool rollbackSucceeded)
            : base(message, inner)
        {
            RollbackAttempted = rollbackAttempted;
            RollbackSucceeded = rollbackSucceeded;
        }
    }

    private sealed class ConfigBackupManifest
    {
        public ConfigBackupManifest() { }
        public int SchemaVersion { get; set; } = 1;
        public string AccountKey { get; set; } = string.Empty;
        public Dictionary<string, string> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> MissingFiles { get; set; } = new();
    }
}
