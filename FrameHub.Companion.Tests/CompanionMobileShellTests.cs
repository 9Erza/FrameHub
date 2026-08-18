using System.Net;
using System.Net.Sockets;
using FrameHub.Companion.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Companion.Tests;

/// <summary>
/// v0.7.1 Companion mobile app-shell and browser identity regression coverage.
/// Static DOM/CSS invariants only — manual real-device smoke still applies.
/// </summary>
[TestClass]
public sealed class CompanionMobileShellTests
{
    private string _tempDirectory = null!;
    private string _tempStorePath = null!;
    private DeviceRecordStore _deviceStore = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.MobileShellTests", Guid.NewGuid().ToString("N"));
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

    private async Task<(string Html, string Css, string I18n)> GetFrontendAsync()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        string html = await (await client.GetAsync($"http://127.0.0.1:{port}/index.html")).Content.ReadAsStringAsync();
        string css = await (await client.GetAsync($"http://127.0.0.1:{port}/css/styles.css")).Content.ReadAsStringAsync();
        string i18n = await (await client.GetAsync($"http://127.0.0.1:{port}/js/i18n.js")).Content.ReadAsStringAsync();
        return (html, css, i18n);
    }

    [TestMethod]
    public async Task ViewportMeta_IncludesViewportFitCover_ForSafeAreas()
    {
        var (html, _, _) = await GetFrontendAsync();
        StringAssert.Contains(html, "viewport-fit=cover", "Viewport metadata must opt into iOS safe-area insets.");
        StringAssert.Contains(html, "width=device-width", "Viewport metadata must keep device-width scaling.");
    }

    [TestMethod]
    public async Task BrowserIdentity_FaviconAndTouchIconAndThemeColor_ExistAndAreServed()
    {
        int port = GetFreePort();
        await using var server = new CompanionServer(_deviceStore);
        bool started = await server.StartAsync(new CompanionOptions { Enabled = true, Port = port });
        Assert.IsTrue(started);

        using var client = new HttpClient();
        var (html, _, _) = await GetFrontendAsync();
        StringAssert.Contains(html, "rel=\"icon\"", "index.html must declare a favicon link.");
        StringAssert.Contains(html, "rel=\"shortcut icon\"", "index.html must declare a shortcut icon link.");
        StringAssert.Contains(html, "rel=\"apple-touch-icon\"", "index.html must declare an Apple touch icon.");
        StringAssert.Contains(html, "name=\"theme-color\"", "index.html must declare a theme-color meta.");

        foreach (string asset in new[] { "/assets/favicon-32x32.png", "/assets/apple-touch-icon.png", "/assets/logo.png" })
        {
            using var response = await client.GetAsync($"http://127.0.0.1:{port}{asset}");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"Branding asset {asset} must be served.");
            byte[] bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.IsTrue(bytes.Length > 500, $"Branding asset {asset} must have non-trivial content.");
        }
    }

    [TestMethod]
    public async Task BottomNavigation_UsesSvgIconsInsteadOfEmoji()
    {
        var (html, _, _) = await GetFrontendAsync();

        int navStart = html.IndexOf("<nav", StringComparison.Ordinal);
        int navEnd = html.IndexOf("</nav>", StringComparison.Ordinal);
        Assert.IsTrue(navStart >= 0 && navEnd > navStart, "index.html must contain the bottom nav element.");
        string nav = html.Substring(navStart, navEnd - navStart);

        StringAssert.Contains(nav, "<svg", "Bottom navigation must use inline SVG icons.");
        StringAssert.Contains(nav, "stroke=\"currentColor\"", "Nav SVG icons must inherit color from the nav item.");
        StringAssert.Contains(nav, "aria-hidden=\"true\"", "Decorative nav SVG icons must be aria-hidden when a text label exists.");
        StringAssert.Contains(nav, "viewBox=\"0 0 24 24\"", "Nav SVG icons must share a consistent viewBox.");

        string[] emojiGlyphs = ["\U0001F3E0", "\U0001F3AE", "\U0001F4CA", "\u2699", "\u2699\uFE0F"];
        foreach (string emoji in emojiGlyphs)
        {
            Assert.IsFalse(nav.Contains(emoji), $"Bottom navigation must not contain emoji icon {emoji}.");
        }
    }

    [TestMethod]
    public async Task Footer_ContainsOnlyUserFacingCopy_NoDeveloperJargon()
    {
        var (html, _, i18n) = await GetFrontendAsync();

        Assert.IsFalse(i18n.Contains("BenchmarkCaptureCoordinator"), "i18n must not leak internal coordinator names into user-facing copy.");
        Assert.IsFalse(html.Contains("BenchmarkCaptureCoordinator"), "index.html must not contain internal coordinator names.");
        StringAssert.Contains(i18n, "'footer.text': 'FrameHub Companion'", "Footer must use the simple user-facing FrameHub Companion copy.");
    }

    [TestMethod]
    public async Task AppShell_UsesSingleScrollRegionWithNavOutsideIt()
    {
        var (html, css, _) = await GetFrontendAsync();

        StringAssert.Contains(html, "class=\"app-shell\"", "index.html must contain the app shell root.");
        StringAssert.Contains(html, "app-scroll-region", "index.html must contain the dedicated vertical scroll region.");
        StringAssert.Contains(css, ".app-shell", "styles.css must style the app shell.");
        StringAssert.Contains(css, ".app-scroll-region", "styles.css must style the scroll region.");

        int scrollRegion = html.IndexOf("app-scroll-region", StringComparison.Ordinal);
        int footer = html.IndexOf("<footer", StringComparison.Ordinal);
        int nav = html.IndexOf("<nav", StringComparison.Ordinal);
        Assert.IsTrue(scrollRegion >= 0 && footer > scrollRegion, "The footer must live inside the scrolling content region.");
        Assert.IsTrue(nav > footer, "The bottom nav must come after the footer, outside the scroll region.");

        string betweenFooterAndNav = html.Substring(footer, nav - footer);
        StringAssert.Contains(betweenFooterAndNav, "</footer>", "Footer must be closed before the nav element.");
        StringAssert.Contains(betweenFooterAndNav.Substring(betweenFooterAndNav.IndexOf("</footer>", StringComparison.Ordinal)), "</div>",
            "The scroll region must be closed before the nav: the nav is a sibling, not a child of the scroller.");

        Assert.IsFalse(CssRuleContains(css, ".bottom-nav", "position: fixed"),
            "Bottom nav must be laid out inside the shell (flex child), not position:fixed against a scrolling document.");
    }

    [TestMethod]
    public async Task Css_UsesDvhLayoutWithFallback()
    {
        var (_, css, _) = await GetFrontendAsync();
        StringAssert.Contains(css, "height: 100vh;", "A vh fallback must exist for browsers without dvh support.");
        StringAssert.Contains(css, "height: 100dvh;", "The app shell must use dynamic viewport height for Safari toolbar collapse stability.");
    }

    [TestMethod]
    public async Task Css_HandlesSafeAreaInsets()
    {
        var (_, css, _) = await GetFrontendAsync();
        StringAssert.Contains(css, "env(safe-area-inset-bottom", "Bottom nav/content must respect the iOS home indicator inset.");
        StringAssert.Contains(css, "env(safe-area-inset-left", "Shell must respect left safe area.");
        StringAssert.Contains(css, "env(safe-area-inset-right", "Shell must respect right safe area.");
        StringAssert.Contains(css, "env(safe-area-inset-top", "Header must respect top safe area when non-zero.");
    }

    [TestMethod]
    public async Task Css_PreventsRootHorizontalOverflow()
    {
        var (_, css, _) = await GetFrontendAsync();
        Assert.IsTrue(CssRuleContains(css, ".app-shell", "overflow-x: clip"), "The app shell root must clip accidental horizontal overflow.");
        Assert.IsTrue(CssRuleContains(css, ".app-scroll-region", "overflow-x: clip"), "The scroll region must never scroll horizontally.");
        StringAssert.Contains(css, "min-width: 0", "Flex/grid children must use min-width: 0 to prevent widening the root.");
    }

    [TestMethod]
    public async Task Css_KeepsIntentionalHorizontalScrollLocal()
    {
        var (_, css, _) = await GetFrontendAsync();
        Assert.IsTrue(CssRuleContains(css, ".table-responsive", "overflow-x: auto"), "Benchmark tables must keep their own horizontal scroll container.");
        StringAssert.Contains(css, "overscroll-behavior-x: contain", "Local horizontal scrollers must contain overscroll so gestures never drag the whole page.");
    }

    [TestMethod]
    public async Task Css_HasMobileDensityBreakpoint()
    {
        var (_, css, _) = await GetFrontendAsync();
        int mobileBreakpoint = css.IndexOf("@media (max-width: 640px)", StringComparison.Ordinal);
        Assert.IsTrue(mobileBreakpoint >= 0, "A phone-density breakpoint must exist.");

        string mobileBlock = css.Substring(mobileBreakpoint);
        StringAssert.Contains(mobileBlock, "padding: 0 0.85rem", "Phone viewports must use compact outer horizontal padding.");
        StringAssert.Contains(mobileBlock, ".card-body", "Card body density must be compact on phones.");
        StringAssert.Contains(mobileBlock, ".card-header", "Card header density must be compact on phones.");
    }

    [TestMethod]
    public async Task Css_ProtectsHeaderFromHorizontalOverflow()
    {
        var (_, css, _) = await GetFrontendAsync();
        Assert.IsTrue(CssRuleContains(css, ".app-header", "flex-wrap: wrap"), "Header must allow wrapping instead of overflowing on narrow phones.");
        Assert.IsTrue(CssRuleContains(css, ".app-header", "min-width: 0"), "Header must not widen the root.");
        Assert.IsTrue(CssRuleContains(css, ".auth-status-badge", "overflow: hidden"), "The pairing status badge must shrink instead of overflowing.");
    }

    [TestMethod]
    public async Task Css_GuardsCpuChipsAndValuesAgainstWideningRoot()
    {
        var (_, css, _) = await GetFrontendAsync();
        Assert.IsTrue(CssRuleContains(css, ".cpu-chip-grid", "min-width: 0"), "CPU chip grid must not widen its card or the root.");
        Assert.IsTrue(CssRuleContains(css, ".metric-value", "overflow-wrap: break-word"), "Long metric values must wrap inside their cards.");
        Assert.IsTrue(CssRuleContains(css, ".stat-value", "overflow-wrap: break-word"), "Long hardware values must wrap inside their cards.");
    }

    [TestMethod]
    public async Task Css_LongLibraryTitlesCannotWidenRoot()
    {
        var (_, css, _) = await GetFrontendAsync();
        Assert.IsTrue(CssRuleContains(css, ".library-card-title", "word-break: break-word"), "Long game names must wrap instead of widening the page.");
        Assert.IsTrue(CssRuleContains(css, ".library-card-info", "min-width: 0"), "Library card info must be allowed to shrink.");
    }

    [TestMethod]
    public async Task Body_DocumentItselfNeverScrolls()
    {
        var (_, css, _) = await GetFrontendAsync();
        Assert.IsTrue(CssRuleContains(css, "body", "overflow: hidden"), "The document must never be the scrolling element; the app shell owns scrolling.");
        StringAssert.Contains(css, ".app-scroll-region", "A dedicated scroll region must own vertical scrolling.");
        StringAssert.Contains(css, "-webkit-overflow-scrolling: touch", "The scroll region must use native momentum scrolling on iOS.");
    }

    private static bool CssRuleContains(string css, string selector, string declaration)
    {
        int selectorIndex = css.IndexOf(selector, StringComparison.Ordinal);
        while (selectorIndex >= 0)
        {
            int braceOpen = css.IndexOf('{', selectorIndex);
            int braceClose = css.IndexOf('}', selectorIndex);
            if (braceOpen < 0 || braceClose < braceOpen) return false;
            string ruleBody = css.Substring(braceOpen, braceClose - braceOpen);
            if (ruleBody.Contains(declaration, StringComparison.Ordinal)) return true;
            selectorIndex = css.IndexOf(selector, braceClose, StringComparison.Ordinal);
        }
        return false;
    }
}
