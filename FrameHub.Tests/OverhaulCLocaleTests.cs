using FrameHub.App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Globalization;

namespace FrameHub.Tests;

/// <summary>
/// UI overhaul part C (Ember C7): global PL/EN locale parity — one owner per
/// client (LocalizationService), full key parity, no empty/missing values and
/// culture-aware number/date formatting for user-facing strings.
/// </summary>
[TestClass]
public sealed class OverhaulCLocaleTests
{
    private string _tempDirectory = null!;
    private string? _previousDataDir;

    [TestInitialize]
    public void Setup()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "FrameHub.EmberC7", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _previousDataDir = Environment.GetEnvironmentVariable("FRAMEHUB_DATA_DIR");
        Environment.SetEnvironmentVariable("FRAMEHUB_DATA_DIR", _tempDirectory);
    }

    [TestCleanup]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("FRAMEHUB_DATA_DIR", _previousDataDir);
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { }
        }
    }

    [TestMethod]
    public void LocaleDictionaries_HaveFullParity()
    {
        var en = LocalizationService.EnglishKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pl = LocalizationService.PolishKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingInPl = en.Except(pl, StringComparer.OrdinalIgnoreCase).ToList();
        var missingInEn = pl.Except(en, StringComparer.OrdinalIgnoreCase).ToList();

        Assert.AreEqual(0, missingInPl.Count,
            "EN keys without PL translation:\n" + string.Join("\n", missingInPl.Take(20)));
        Assert.AreEqual(0, missingInEn.Count,
            "PL keys without EN source:\n" + string.Join("\n", missingInEn.Take(20)));
        Assert.IsTrue(en.Count >= 900, $"Dictionary unexpectedly small: {en.Count}.");
    }

    [TestMethod]
    public void LocaleValues_AreNeverEmptyOrEcho()
    {
        using var runtime = CreateRuntime();
        var localization = new LocalizationService(runtime.SettingsService);
        var bad = new List<string>();
        foreach (string key in LocalizationService.EnglishKeys)
        {
            localization.SetLanguage("en");
            string en = localization.T(key);
            localization.SetLanguage("pl");
            string pl = localization.T(key);
            if (string.IsNullOrWhiteSpace(en) || en == key) bad.Add($"en:{key}");
            if (string.IsNullOrWhiteSpace(pl) || pl == key) bad.Add($"pl:{key}");
        }

        Assert.AreEqual(0, bad.Count,
            "Empty or key-echo values:\n" + string.Join("\n", bad.Take(20)));
    }

    [TestMethod]
    public void CultureFormatting_UsesUiCultureSeparators()
    {
        using var runtime = CreateRuntime();
        var localization = new LocalizationService(runtime.SettingsService);
        localization.SetLanguage("pl");

        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pl-PL");
            string formatted = string.Format(CultureInfo.CurrentCulture, localization.T("Dns.LatencyValue"), 12.5);
            Assert.IsTrue(formatted.Contains(',', StringComparison.Ordinal),
                $"pl-PL must use comma decimals, got '{formatted}'.");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [TestMethod]
    public void LanguageSwitch_PersistsAcrossReload()
    {
        string settingsPath = Path.Combine(_tempDirectory, "persist-settings.json");
        using (var first = new AppRuntimeService(settingsPath))
        {
            var localization = new LocalizationService(first.SettingsService);
            localization.SetLanguage("pl");
            Assert.AreEqual("pl", localization.CurrentLanguage);
            Assert.AreEqual("Dziennik", localization.T("Nav.Logs"));
        }

        using (var second = new AppRuntimeService(settingsPath))
        {
            var reloaded = new LocalizationService(second.SettingsService);
            Assert.AreEqual("pl", reloaded.CurrentLanguage, "Language choice must persist via settings.");
            Assert.AreEqual("Dziennik", reloaded.T("Nav.Logs"));

            reloaded.SetLanguage("en");
            Assert.AreEqual("Logs", reloaded.T("Nav.Logs"));
        }
    }

    private AppRuntimeService CreateRuntime()
    {
        string settingsPath = Path.Combine(_tempDirectory, $"settings-{Guid.NewGuid():N}.json");
        return new AppRuntimeService(settingsPath);
    }

    [TestMethod]
    public void CompanionKeys_UseCanonicalPartCWording_NoDuplicates()
    {
        // QA R1 P3: the PL dictionary had 12 duplicated Companion keys (older
        // wording earlier, part-C wording at the end, last-wins). The earlier
        // block is removed; this locks the canonical winners.
        using var runtime = CreateRuntime();
        var localization = new LocalizationService(runtime.SettingsService);
        localization.SetLanguage("pl");
        Assert.AreEqual("Włącz usługę Companion", localization.T("Settings.CompanionEnable"));
        Assert.AreEqual("Wyłączona", localization.T("Settings.CompanionStateDisabled"));
        localization.SetLanguage("en");
        Assert.AreEqual("Enable Companion", localization.T("Settings.CompanionEnable"));
        Assert.AreEqual("Disabled", localization.T("Settings.CompanionStateDisabled"));
    }

    [TestMethod]
    public void RestartHandshakeKeys_HaveEnAndPlValues()
    {
        using var runtime = CreateRuntime();
        var localization = new LocalizationService(runtime.SettingsService);
        foreach (string key in new[]
        {
            "Shell.Restart.SuccessorReady", "Shell.Restart.SuccessorTimeout",
            "Shell.Restart.SuccessorDied", "Shell.Restart.Aborted", "Shell.Restart.Incomplete"
        })
        {
            localization.SetLanguage("en");
            string en = localization.T(key);
            localization.SetLanguage("pl");
            string pl = localization.T(key);
            Assert.IsFalse(string.IsNullOrWhiteSpace(en) || en == key, key);
            Assert.IsFalse(string.IsNullOrWhiteSpace(pl) || pl == key, key);
        }
    }
}
