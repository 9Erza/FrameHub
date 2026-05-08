using FrameHub.Core.Logging;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;

namespace FrameHub.Core.Services
{
    public sealed class UpdateCheckResult
    {
        public bool IsUpdateAvailable { get; set; }
        public string LatestVersion { get; set; } = string.Empty;
        public string ReleaseUrl { get; set; } = string.Empty;
        public string? Error { get; set; }
    }

    public sealed class UpdateService
    {
        private readonly string _versionRawUrl = "https://raw.githubusercontent.com/9Erza/FrameHub/refs/heads/main/version.txt";
        private readonly string _releasesUrl = "https://github.com/9Erza/FrameHub/releases";
        private readonly ILogger _logger = LoggerService.Instance;

        public async Task<UpdateCheckResult> CheckForUpdatesAsync(string currentVersion)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("FrameHub");

                string latestVersionStr = (await client.GetStringAsync($"{_versionRawUrl}?t={Guid.NewGuid()}"))
                    .Trim()
                    .TrimStart('v', 'V');

                bool hasUpdate = Version.TryParse(latestVersionStr, out Version? latestVersion) &&
                                 Version.TryParse(currentVersion.TrimStart('v', 'V'), out Version? current) &&
                                 latestVersion > current;

                return new UpdateCheckResult
                {
                    IsUpdateAvailable = hasUpdate,
                    LatestVersion = latestVersionStr,
                    ReleaseUrl = _releasesUrl
                };
            }
            catch (Exception ex)
            {
                _logger.Debug($"Update check failed: {ex.Message}");
                return new UpdateCheckResult { Error = ex.Message, ReleaseUrl = _releasesUrl };
            }
        }

        public static void OpenReleasePage(string url)
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
    }
}
