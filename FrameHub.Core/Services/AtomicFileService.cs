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

        public static void WriteAllTextAtomic(string filePath, string content)
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempPath = filePath + ".tmp";
            string backupPath = filePath + ".bak";

            File.WriteAllText(tempPath, content, Encoding.UTF8);

            if (File.Exists(filePath))
            {
                File.Copy(filePath, backupPath, overwrite: true);
            }

            File.Copy(tempPath, filePath, overwrite: true);
            File.Delete(tempPath);
        }

        public static string? ReadAllTextWithBackup(string filePath)
        {
            string backupPath = filePath + ".bak";

            try
            {
                return File.Exists(filePath) ? File.ReadAllText(filePath, Encoding.UTF8) : null;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to read '{filePath}'. Trying backup. {ex.Message}");
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
}
