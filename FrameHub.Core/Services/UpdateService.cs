using FrameHub.Core.Logging;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
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
        private readonly HttpClient? _injectedClient;

        /// <param name="httpClient">
        /// Optional client for tests. When null, a short-lived client is created
        /// per check (previous behavior). An injected client is never disposed here.
        /// </param>
        public UpdateService(HttpClient? httpClient = null)
        {
            _injectedClient = httpClient;
        }

        public async Task<UpdateCheckResult> CheckForUpdatesAsync(string currentVersion)
        {
            bool ownsClient = _injectedClient is null;
            HttpClient client = _injectedClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            if (ownsClient)
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("FrameHub");
            }

            try
            {
                string latestVersionStr = (await client.GetStringAsync($"{_versionRawUrl}?t={Guid.NewGuid()}"))
                    .Trim()
                    .TrimStart('v', 'V');

                bool hasUpdate = Version.TryParse(latestVersionStr, out Version? latestVersion) &&
                                 Version.TryParse(currentVersion.TrimStart('v', 'V'), out Version? current) &&
                                 latestVersion > current;

                if (!hasUpdate)
                {
                    return new UpdateCheckResult
                    {
                        IsUpdateAvailable = false,
                        LatestVersion = latestVersionStr,
                        ReleaseUrl = _releasesUrl
                    };
                }

                // version.txt on main moves the moment the version is bumped, but the
                // GitHub Release (installer asset) is published separately. Reporting an
                // update before the release exists would point users at a page without
                // the asset, so a newer version.txt alone never notifies: the matching
                // published (non-draft, non-prerelease) release tag must exist first
                // and must carry the expected FrameHub-Setup-X.Y.Z.exe installer asset.
                // A definitive "not ready" (no tag, draft, prerelease, tag mismatch, or
                // missing asset) suppresses silently; an inconclusive network failure
                // surfaces as Error (automatic checks stay silent, manual checks report
                // the failure honestly).
                bool? published = await IsReleasePublishedAsync(client, latestVersionStr).ConfigureAwait(false);
                if (published == true)
                {
                    return new UpdateCheckResult
                    {
                        IsUpdateAvailable = true,
                        LatestVersion = latestVersionStr,
                        ReleaseUrl = _releasesUrl
                    };
                }

                if (published == false)
                {
                    _logger.Debug($"Update {latestVersionStr} is not published yet; suppressing the notification.");
                    return new UpdateCheckResult
                    {
                        IsUpdateAvailable = false,
                        LatestVersion = latestVersionStr,
                        ReleaseUrl = _releasesUrl
                    };
                }

                return new UpdateCheckResult
                {
                    IsUpdateAvailable = false,
                    LatestVersion = latestVersionStr,
                    ReleaseUrl = _releasesUrl,
                    Error = "Could not verify the published release."
                };
            }
            catch (Exception ex)
            {
                _logger.Debug($"Update check failed: {ex.Message}");
                return new UpdateCheckResult { Error = ex.Message, ReleaseUrl = _releasesUrl };
            }
            finally
            {
                if (ownsClient)
                {
                    client.Dispose();
                }
            }
        }

        /// <returns>
        /// True when a published (non-draft, non-prerelease) release exists for the
        /// version tag, its tag matches, and it carries the expected installer asset
        /// (<c>FrameHub-Setup-X.Y.Z.exe</c>). False when it definitively does not
        /// (missing tag, draft, prerelease, tag mismatch, or no installer asset —
        /// a release without the installer is not a ready update). Null when the
        /// network answer was inconclusive.
        /// </returns>
        private async Task<bool?> IsReleasePublishedAsync(HttpClient client, string version)
        {
            string expectedAsset = $"FrameHub-Setup-{version}.exe";
            foreach (string tag in new[] { "v" + version, version })
            {
                HttpResponseMessage response;
                try
                {
                    using var request = new HttpRequestMessage(
                        HttpMethod.Get,
                        $"https://api.github.com/repos/9Erza/FrameHub/releases/tags/{tag}");
                    if (!request.Headers.Contains("User-Agent"))
                    {
                        request.Headers.UserAgent.ParseAdd("FrameHub");
                    }
                    request.Headers.Accept.ParseAdd("application/vnd.github+json");
                    response = await client.SendAsync(request).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Debug($"Release verification failed: {ex.Message}");
                    return null;
                }

                using (response)
                {
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        continue;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.Debug($"Release verification returned {(int)response.StatusCode}.");
                        return null;
                    }

                    try
                    {
                        using var document = JsonDocument.Parse(
                            await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                        JsonElement root = document.RootElement;

                        string? tagName = root.TryGetProperty("tag_name", out JsonElement tagElement) &&
                            tagElement.ValueKind == JsonValueKind.String
                            ? tagElement.GetString()
                            : null;
                        if (!string.Equals(tagName, "v" + version, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(tagName, version, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.Debug($"Release tag mismatch: expected {tag}, got {tagName ?? "<none>"}.");
                            return false;
                        }

                        if (root.TryGetProperty("draft", out JsonElement draft) &&
                            draft.ValueKind == JsonValueKind.True)
                        {
                            return false;
                        }
                        if (root.TryGetProperty("prerelease", out JsonElement prerelease) &&
                            prerelease.ValueKind == JsonValueKind.True)
                        {
                            return false;
                        }

                        if (root.TryGetProperty("assets", out JsonElement assets) &&
                            assets.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement asset in assets.EnumerateArray())
                            {
                                if (asset.ValueKind == JsonValueKind.Object &&
                                    asset.TryGetProperty("name", out JsonElement name) &&
                                    name.ValueKind == JsonValueKind.String &&
                                    string.Equals(name.GetString(), expectedAsset, StringComparison.OrdinalIgnoreCase))
                                {
                                    return true;
                                }
                            }
                        }

                        _logger.Debug($"Release {tagName} carries no {expectedAsset}; not a ready update.");
                        return false;
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"Release verification parse failed: {ex.Message}");
                        return null;
                    }
                }
            }

            return false;
        }

        public static void OpenReleasePage(string url)
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
    }
}
