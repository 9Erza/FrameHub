using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using FrameHub.Core.Models.GameOptimization;

namespace FrameHub.Core.Services.GameOptimization;

public sealed partial class Cs2OptimizationService
{
    private const string SnapshotManifestName = "snapshot.json";

    private string[] GetConfigFiles(Cs2ConfigAnalysis analysis) => new[]
    {
        analysis.Paths.VideoConfigPath, analysis.Paths.MachineConvarsPath,
        analysis.Paths.UserConvarsPath, analysis.Paths.UserKeysPath, GetAutoexecPath(analysis)
    }.Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => Path.GetFullPath(path!))
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private string CreateVerifiedBackup(Cs2ConfigAnalysis analysis)
    {
        string accountRoot = GetAccountBackupRoot(analysis);
        Directory.CreateDirectory(accountRoot);
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
        string path = Path.Combine(folder, SnapshotManifestName);
        if (!File.Exists(path)) return null; // Legacy snapshots predate integrity metadata.
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
                || !string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder, file.Key)))), file.Value, StringComparison.Ordinal))
                throw new InvalidDataException("Backup integrity verification failed.");
        }
        if (manifest.MissingFiles.Any(name => string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name || manifest.Files.ContainsKey(name)))
            throw new InvalidDataException("Backup absence metadata is invalid.");
        return manifest;
    }

    private int RestoreVerifiedBackup(Cs2ConfigAnalysis analysis, string folder)
    {
        ConfigBackupManifest? manifest = VerifySnapshot(folder, analysis);
        var originals = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var writes = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
        foreach (string target in GetConfigFiles(analysis))
        {
            string name = Path.GetFileName(target);
            string source = Path.Combine(folder, name);
            if (manifest is not null && manifest.MissingFiles.Contains(name)) writes[target] = null;
            else if ((manifest is null || manifest.Files.ContainsKey(name)) && File.Exists(source))
            {
                byte[] bytes = File.ReadAllBytes(source);
                if (manifest is not null && Convert.ToHexString(SHA256.HashData(bytes)) != manifest.Files[name])
                    throw new InvalidDataException("Backup changed after integrity verification.");
                writes[target] = bytes;
            }
            if (writes.ContainsKey(target) && File.Exists(target)) originals[target] = File.ReadAllBytes(target);
        }
        var completed = new List<string>();
        try
        {
            foreach (var write in writes)
            {
                if (write.Value is null) File.Delete(write.Key);
                else AtomicFileService.WriteAllBytesAtomic(write.Key, write.Value);
                completed.Add(write.Key);
            }
            return completed.Count;
        }
        catch (Exception ex)
        {
            bool restored = RollBackFiles(completed, originals);
            throw new IOException($"Restore failed. Rollback: {(restored ? "completed" : "incomplete; retained backup requires manual recovery")}. {ex.Message}", ex);
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
