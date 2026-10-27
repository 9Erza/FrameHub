using System.Net;
using System.Net.Sockets;
using FrameHub.Companion.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Companion.Tests;

/// <summary>
/// FrameHub 1.0 branding regression coverage: served web assets are real PNGs,
/// the shell carries the graphite theme color with cache-busted brand refs, the
/// desktop side rail mirrors the bottom-bar tabs, and version markers exist.
/// </summary>
[TestClass]
public sealed class CompanionBrandingTests
{
    private string _tempDirectory = null!;
    private string _tempStorePath = null!;
    private DeviceRecordStore _deviceStore = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.BrandingTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _tempStorePath = Path.Combine(_tempDirectory, "paired-devices.json");
        _deviceStore = new DeviceRecordStore(_tempStorePath);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { }
        }
    }

    private static int GetFreePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    private async Task<(string Html, string Css, Dictionary<string, byte[]> Assets)> GetFrontendAsync()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        string html = await (await client.GetAsync($"http://127.0.0.1:{port}/index.html")).Content.ReadAsStringAsync();
        string css = await (await client.GetAsync($"http://127.0.0.1:{port}/css/styles.css")).Content.ReadAsStringAsync();
        var assets = new Dictionary<string, byte[]>();
        foreach (string asset in new[] { "/assets/logo.png", "/assets/favicon-32x32.png", "/assets/apple-touch-icon.png" })
        {
            using var response = await client.GetAsync($"http://127.0.0.1:{port}{asset}");
            assets[asset] = (response.StatusCode, await response.Content.ReadAsByteArrayAsync()) switch
            {
                (HttpStatusCode.OK, var bytes) => bytes,
                (var status, _) => throw new AssertFailedException($"Branding asset {asset} must be served (got {status})."),
            };
        }

        return (html, css, assets);
    }

    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47];

    [TestMethod]
    public async Task BrandingAssets_AreRealPngsWithNonTrivialContent()
    {
        var (_, _, assets) = await GetFrontendAsync();
        foreach (var (asset, bytes) in assets)
        {
            Assert.IsTrue(bytes.Length > 500, $"Branding asset {asset} must have non-trivial content.");
            Assert.IsTrue(bytes.Length >= 4 && bytes[..4].SequenceEqual(PngMagic),
                $"Branding asset {asset} must be a real PNG file.");
        }
    }

    [TestMethod]
    public async Task ThemeColor_UsesFrameCoreGraphiteWithCacheBustedRefs()
    {
        var (html, _, _) = await GetFrontendAsync();
        StringAssert.Contains(html, "content=\"#151D25\"", "theme-color must be Frame Core graphite, not the legacy navy.");
        foreach (string asset in new[] { "/assets/logo.png", "/assets/favicon-32x32.png", "/assets/apple-touch-icon.png" })
        {
            StringAssert.Contains(html, asset + "?v=", $"Brand asset {asset} must carry a cache-busting query.");
        }
    }

    [TestMethod]
    public async Task SideRail_MirrorsBottomBarTabsWithMatchingViews()
    {
        var (html, _, _) = await GetFrontendAsync();
        foreach (string tab in new[] { "home", "library", "benchmarks", "settings" })
        {
            StringAssert.Contains(html, $"id=\"nav-side-{tab}\"", $"Desktop side rail must contain a {tab} tab.");
            StringAssert.Contains(html, $"id=\"nav-tab-{tab}\"", $"Bottom bar must keep its {tab} tab.");
        }

        int sideNavStart = html.IndexOf("id=\"side-nav\"", StringComparison.Ordinal);
        int sideNavEnd = html.IndexOf("</nav>", sideNavStart, StringComparison.Ordinal);
        Assert.IsTrue(sideNavStart >= 0 && sideNavEnd > sideNavStart, "Side nav must be a closed nav element.");
        string sideNav = html.Substring(sideNavStart, sideNavEnd - sideNavStart);
        foreach (string view in new[] { "home-view", "library-view", "benchmarks-view", "settings-view" })
        {
            StringAssert.Contains(sideNav, $"aria-controls=\"{view}\"",
                $"Side rail tabs must target the same view containers as the bottom bar ({view}).");
            StringAssert.Contains(html, $"id=\"{view}\"", $"View container {view} must exist.");
        }
    }

    [TestMethod]
    public async Task VersionMarkers_ExistInAboutFooterAndRail()
    {
        var (html, _, _) = await GetFrontendAsync();
        int markers = 0;
        int index = 0;
        while ((index = html.IndexOf("data-app-version", index, StringComparison.Ordinal)) >= 0)
        {
            markers++;
            index += "data-app-version".Length;
        }

        Assert.IsTrue(markers >= 3, $"About, footer and rail must carry version markers (found {markers}).");
        StringAssert.Contains(html, "settings.aboutVersionLabel", "About must label the version row.");
    }

    [TestMethod]
    public async Task InitialHardwareState_ShowsDisabledNoticeNotEmptyTiles()
    {
        var (html, _, _) = await GetFrontendAsync();
        int gridIndex = html.IndexOf("id=\"hw-grid-container\"", StringComparison.Ordinal);
        Assert.IsTrue(gridIndex >= 0, "Hardware grid container must exist.");
        int gridTagEnd = html.IndexOf('>', gridIndex);
        string gridTag = html.Substring(gridIndex, gridTagEnd - gridIndex);
        StringAssert.Contains(gridTag, "hidden", "Hardware grid must start hidden until telemetry enables it.");

        int noticeIndex = html.IndexOf("id=\"hw-disabled-notice\"", StringComparison.Ordinal);
        Assert.IsTrue(noticeIndex >= 0, "Hardware disabled notice must exist.");
        int noticeTagEnd = html.IndexOf('>', noticeIndex);
        string noticeTag = html.Substring(noticeIndex, noticeTagEnd - noticeIndex);
        Assert.IsFalse(noticeTag.Contains("hidden"), "Hardware disabled notice must be visible by default.");
    }

    [TestMethod]
    public async Task Shell_HasSkipLinkAndFrameCoreTokens()
    {
        var (html, css, _) = await GetFrontendAsync();
        StringAssert.Contains(html, "class=\"skip-link\"", "Shell must offer a skip link for keyboard users.");
        StringAssert.Contains(css, "--fh-bg", "CSS must define Frame Core tokens.");
        StringAssert.Contains(css, "--fh-accent", "CSS must define the Frame Core accent token.");
        StringAssert.Contains(css, "prefers-reduced-motion", "CSS must honor reduced-motion requests.");
    }
}
