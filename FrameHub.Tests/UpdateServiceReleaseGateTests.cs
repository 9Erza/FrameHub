using FrameHub.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;

namespace FrameHub.Tests;

/// <summary>
/// 1.0.1 update-gate: a newer version.txt alone must never notify while the
/// matching GitHub Release is unpublished (draft, prerelease or missing tag).
/// No real HTTP calls: a fake handler answers both version.txt and the
/// releases API.
/// </summary>
[TestClass]
public sealed class UpdateServiceReleaseGateTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<string> RequestedUrls { get; } = new();
        public string VersionTxtPayload { get; set; } = "1.0.1";
        public Func<string, HttpResponseMessage> ReleaseResponder { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri?.ToString() ?? string.Empty;
            RequestedUrls.Add(url);
            if (url.Contains("version.txt", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(VersionTxtPayload)
                });
            }

            return Task.FromResult(ReleaseResponder(url));
        }
    }

    private static UpdateService CreateService(FakeHandler handler)
        => new(new HttpClient(handler));

    private static HttpResponseMessage PublishedRelease(
        string tag = "v1.0.1", string asset = "FrameHub-Setup-1.0.1.exe")
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $"{{\"tag_name\":\"{tag}\",\"draft\":false,\"prerelease\":false,\"assets\":[{{\"name\":\"{asset}\"}}]}}")
        };

    [TestMethod]
    public async Task NewerVersion_WithPublishedRelease_ReportsUpdate()
    {
        var handler = new FakeHandler { ReleaseResponder = _ => PublishedRelease() };
        var result = await CreateService(handler).CheckForUpdatesAsync("1.0.0");

        Assert.IsTrue(result.IsUpdateAvailable, "A published release must notify.");
        Assert.AreEqual("1.0.1", result.LatestVersion);
        Assert.IsTrue(string.IsNullOrWhiteSpace(result.Error));
        Assert.IsTrue(handler.RequestedUrls.Any(u => u.Contains("api.github.com", StringComparison.OrdinalIgnoreCase)),
            "The release tag must be verified through the releases API.");
    }

    [TestMethod]
    public async Task NewerVersion_ReleaseWithoutInstaller_SuppressesSilently()
    {
        var handler = new FakeHandler
        {
            ReleaseResponder = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"tag_name\":\"v1.0.1\",\"draft\":false,\"prerelease\":false,\"assets\":[]}")
            }
        };
        var result = await CreateService(handler).CheckForUpdatesAsync("1.0.0");

        Assert.IsFalse(result.IsUpdateAvailable, "A release without the installer is not a ready update.");
        Assert.IsTrue(string.IsNullOrWhiteSpace(result.Error));
    }

    [TestMethod]
    public async Task NewerVersion_WrongAssetName_SuppressesSilently()
    {
        var handler = new FakeHandler
        {
            ReleaseResponder = _ => PublishedRelease(asset: "FrameHub-Setup-1.0.0.exe")
        };
        var result = await CreateService(handler).CheckForUpdatesAsync("1.0.0");

        Assert.IsFalse(result.IsUpdateAvailable, "An unrelated asset must not count as the 1.0.1 installer.");
        Assert.IsTrue(string.IsNullOrWhiteSpace(result.Error));
    }

    [TestMethod]
    public async Task NewerVersion_WrongTag_SuppressesSilently()
    {
        var handler = new FakeHandler
        {
            ReleaseResponder = _ => PublishedRelease(tag: "v9.9.9", asset: "FrameHub-Setup-9.9.9.exe")
        };
        var result = await CreateService(handler).CheckForUpdatesAsync("1.0.0");

        Assert.IsFalse(result.IsUpdateAvailable, "A mismatched tag must not notify.");
        Assert.IsTrue(string.IsNullOrWhiteSpace(result.Error));
    }

    [TestMethod]
    public async Task NewerVersion_ReleaseApiError_SurfacesError()
    {
        var handler = new FakeHandler
        {
            ReleaseResponder = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        };
        var result = await CreateService(handler).CheckForUpdatesAsync("1.0.0");

        Assert.IsFalse(result.IsUpdateAvailable);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Error), "Inconclusive verification must surface honestly.");
    }

    [TestMethod]
    public async Task NewerVersion_ReleaseApiTimeout_SurfacesError()
    {
        var handler = new FakeHandler
        {
            ReleaseResponder = _ => throw new TaskCanceledException("Simulated timeout.")
        };
        var result = await CreateService(handler).CheckForUpdatesAsync("1.0.0");

        Assert.IsFalse(result.IsUpdateAvailable);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Error), "A timeout must surface honestly, never as an update.");
    }

    [TestMethod]
    public async Task NewerVersion_WithoutPublishedRelease_SuppressesSilently()
    {
        var handler = new FakeHandler();
        var result = await CreateService(handler).CheckForUpdatesAsync("1.0.0");

        Assert.IsFalse(result.IsUpdateAvailable, "An unpublished version must not notify.");
        Assert.IsTrue(string.IsNullOrWhiteSpace(result.Error), "Suppression is expected, not an error.");
        Assert.AreEqual("1.0.1", result.LatestVersion);
    }

    [TestMethod]
    public async Task NewerVersion_DraftRelease_SuppressesSilently()
    {
        var handler = new FakeHandler
        {
            ReleaseResponder = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"tag_name\":\"v1.0.1\",\"draft\":true,\"prerelease\":false}")
            }
        };
        var result = await CreateService(handler).CheckForUpdatesAsync("1.0.0");

        Assert.IsFalse(result.IsUpdateAvailable, "A draft release must not notify.");
        Assert.IsTrue(string.IsNullOrWhiteSpace(result.Error));
    }

    [TestMethod]
    public async Task NewerVersion_PrereleaseRelease_SuppressesSilently()
    {
        var handler = new FakeHandler
        {
            ReleaseResponder = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"tag_name\":\"v1.0.1\",\"draft\":false,\"prerelease\":true}")
            }
        };
        var result = await CreateService(handler).CheckForUpdatesAsync("1.0.0");

        Assert.IsFalse(result.IsUpdateAvailable, "A prerelease must not notify stable clients.");
        Assert.IsTrue(string.IsNullOrWhiteSpace(result.Error));
    }

    [TestMethod]
    public async Task UpToDateVersion_DoesNotQueryReleaseApi()
    {
        var handler = new FakeHandler { VersionTxtPayload = "1.0.0" };
        var result = await CreateService(handler).CheckForUpdatesAsync("1.0.0");

        Assert.IsFalse(result.IsUpdateAvailable);
        Assert.IsFalse(handler.RequestedUrls.Any(u => u.Contains("api.github.com", StringComparison.OrdinalIgnoreCase)),
            "An up-to-date client must not touch the releases API.");
        Assert.AreEqual(1, handler.RequestedUrls.Count, "Only version.txt should be fetched.");
    }
}
