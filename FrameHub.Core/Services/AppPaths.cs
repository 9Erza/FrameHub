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
            try
            {
                Directory.CreateDirectory(UserDataDirectory);

                string legacyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
                string newPath = GetUserDataFilePath(fileName);

                if (!File.Exists(legacyPath) || File.Exists(newPath)) return;
                File.Copy(legacyPath, newPath, overwrite: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Migration is best-effort; callers can continue with defaults if user storage is unavailable.
            }
        }
    }
}
