using FrameHub.Core.Logging;
using System;
using System.IO;
using System.Text;

namespace FrameHub.Core.Services
{
    /// <summary>
    /// Safe text file writer using tmp + bak replacement to avoid corrupt JSON files.
    /// </summary>
    public static class AtomicFileService
    {
        private static readonly ILogger Logger = LoggerService.Instance;
        private static readonly object[] PathLocks = Enumerable.Range(0, 64).Select(_ => new object()).ToArray();

        public static void WriteAllTextAtomic(string filePath, string content)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(content);

            string fullPath = Path.GetFullPath(filePath);
            lock (GetPathLock(fullPath))
            {
                string directory = Path.GetDirectoryName(fullPath)!;
                Directory.CreateDirectory(directory);

                string tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
                string backupPath = fullPath + ".bak";

                try
                {
                    File.WriteAllText(tempPath, content, Encoding.UTF8);

                    if (File.Exists(fullPath))
                    {
                        File.Replace(tempPath, fullPath, backupPath, ignoreMetadataErrors: true);
                    }
                    else
                    {
                        File.Move(tempPath, fullPath);
                    }
                }
                finally
                {
                    if (File.Exists(tempPath))
                    {
                        try { File.Delete(tempPath); } catch { }
                    }
                }
            }
        }

        public static string? ReadAllTextWithBackup(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            string fullPath = Path.GetFullPath(filePath);
            string backupPath = fullPath + ".bak";

            lock (GetPathLock(fullPath))
            {
                try
                {
                    return File.Exists(fullPath) ? File.ReadAllText(fullPath, Encoding.UTF8) : null;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Failed to read '{fullPath}'. Trying backup. {ex.Message}");
                }

                try
                {
                    return File.Exists(backupPath) ? File.ReadAllText(backupPath, Encoding.UTF8) : null;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to read backup '{backupPath}'", ex);
                    return null;
                }
            }
        }

        private static object GetPathLock(string fullPath)
        {
            int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(fullPath) & int.MaxValue;
            return PathLocks[hash % PathLocks.Length];
        }
    }
}
