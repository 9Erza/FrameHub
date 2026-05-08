using System;
using System.IO;

namespace FrameHub.Core.Services
{
    /// <summary>
    /// Centralized path handling for user-writable application files.
    /// </summary>
    public static class AppPaths
    {
        public static string UserDataDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FrameHub");

        public static string GetUserDataFilePath(string fileName)
        {
            Directory.CreateDirectory(UserDataDirectory);
            return Path.Combine(UserDataDirectory, fileName);
        }

        public static string ResolveUserLogFilePath(string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                configuredPath = "FrameHub.log";
            }

            return Path.IsPathRooted(configuredPath)
                ? configuredPath
                : GetUserDataFilePath(configuredPath);
        }

        public static void MigrateLegacyFileIfNeeded(string fileName)
        {
            Directory.CreateDirectory(UserDataDirectory);

            string legacyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            string newPath = GetUserDataFilePath(fileName);

            if (!File.Exists(legacyPath) || File.Exists(newPath))
            {
                return;
            }

            File.Copy(legacyPath, newPath, overwrite: false);
        }
    }
}
