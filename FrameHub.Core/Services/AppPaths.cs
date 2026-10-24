using System;
using System.IO;

namespace FrameHub.Core.Services
{
    /// <summary>
    /// Centralized path handling for user-writable application files.
    /// <para>
    /// Test/measurement isolation: when the process environment defines
    /// <c>FRAMEHUB_DATA_DIR</c> (roaming) or <c>FRAMEHUB_LOCAL_DATA_DIR</c>
    /// (machine-local) as an absolute directory path, that location replaces the
    /// default %APPDATA%\FrameHub / %LOCALAPPDATA%\FrameHub root. This keeps unit
    /// tests and before/after performance measurements from appending to the real
    /// user log and session files (see QA F-2). The override is opt-in, read live
    /// on every access, and ignored when unset, blank, or relative; existing
    /// behavior is unchanged when neither variable is defined. Overrides must be
    /// set before first use (ideally process start) because callers may cache
    /// resolved file paths.
    /// </para>
    /// </summary>
    public static class AppPaths
    {
        public const string DataDirectoryVariable = "FRAMEHUB_DATA_DIR";
        public const string LocalDataDirectoryVariable = "FRAMEHUB_LOCAL_DATA_DIR";

        public static string UserDataDirectory => ResolveDataDirectory(
            DataDirectoryVariable,
            Environment.SpecialFolder.ApplicationData);

        /// <summary>Machine-local storage for large or machine-specific data.</summary>
        public static string LocalDataDirectory => ResolveDataDirectory(
            LocalDataDirectoryVariable,
            Environment.SpecialFolder.LocalApplicationData);

        public static string BenchmarkDirectory => Path.Combine(LocalDataDirectory, "Benchmarks");

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

        private static string ResolveDataDirectory(string variableName, Environment.SpecialFolder fallbackFolder)
        {
            string? overridePath = Environment.GetEnvironmentVariable(variableName);
            if (!string.IsNullOrWhiteSpace(overridePath) && Path.IsPathRooted(overridePath))
            {
                return Path.GetFullPath(overridePath);
            }

            return Path.Combine(
                Environment.GetFolderPath(fallbackFolder),
                "FrameHub");
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
