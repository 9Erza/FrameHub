using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.RegularExpressions;

namespace FrameHub.Companion.Tests;

/// <summary>
/// QA R1 P3: global Companion i18n parity/fallback test. Parses the shipped
/// wwwroot/js/i18n.js (same file the server serves): every EN key must have a
/// non-empty PL twin, no PL-only orphans, and t() must keep its EN fallback.
/// </summary>
[TestClass]
public sealed class CompanionI18nParityTests
{
    [TestMethod]
    public void I18nJs_HasFullEnPlParityWithFallback()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "wwwroot", "js", "i18n.js");
        Assert.IsTrue(File.Exists(path), $"Shipped i18n.js missing at {path}.");
        string content = File.ReadAllText(path);

        int plStart = content.IndexOf("pl: {", StringComparison.Ordinal);
        Assert.IsTrue(plStart > 0, "i18n.js must contain a 'pl' dictionary.");
        var plEndMatch = Regex.Match(content[plStart..], @"\n\s{8}\}\r?\n");
        Assert.IsTrue(plEndMatch.Success, "i18n.js 'pl' dictionary must terminate.");
        int plEnd = plStart + plEndMatch.Index;

        var keyPattern = new Regex(@"'(?<key>[A-Za-z0-9_.]+)'\s*:", RegexOptions.Compiled);
        var enKeys = keyPattern.Matches(content[..plStart]).Select(m => m.Groups["key"].Value)
            .ToHashSet(StringComparer.Ordinal);
        var plKeys = keyPattern.Matches(content.Substring(plStart, plEnd - plStart))
            .Select(m => m.Groups["key"].Value).ToHashSet(StringComparer.Ordinal);

        Assert.IsTrue(enKeys.Count >= 200, $"EN dictionary unexpectedly small: {enKeys.Count}.");
        var missingInPl = enKeys.Except(plKeys, StringComparer.Ordinal).ToList();
        var orphanInPl = plKeys.Except(enKeys, StringComparer.Ordinal).ToList();
        Assert.AreEqual(0, missingInPl.Count,
            "EN keys without PL translation:\n" + string.Join("\n", missingInPl.Take(20)));
        Assert.AreEqual(0, orphanInPl.Count,
            "PL-only keys without EN source:\n" + string.Join("\n", orphanInPl.Take(20)));

        var emptyPattern = new Regex(@"'[A-Za-z0-9_.]+'\s*:\s*''", RegexOptions.Compiled);
        Assert.AreEqual(0, emptyPattern.Matches(content).Count, "Empty translations are not allowed.");

        Assert.IsTrue(content.Contains("translations.en[key]"),
            "t() must keep the EN fallback for missing keys.");
        Assert.IsTrue(content.Contains("companion_language"),
            "Language persistence key must stay 'companion_language'.");
    }
}
