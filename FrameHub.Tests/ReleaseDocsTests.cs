using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

/// <summary>
/// FrameHub 1.0.1 release-docs consistency: both READMEs point at the new logo,
/// declare 1.0.1 as current (1.0.0 history stays intact), and the release notes,
/// changelogs and installer identity stay consistent.
/// </summary>
[TestClass]
public sealed class ReleaseDocsTests
{
    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "version.txt"))) return dir;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        Assert.Fail("Repo root (version.txt) was not found above the test output directory.");
        throw new InvalidOperationException("unreachable");
    }

    private static string ReadRootFile(string name)
    {
        string path = Path.Combine(FindRepoRoot(), name);
        Assert.IsTrue(File.Exists(path), $"{name} must exist at the repo root.");
        return File.ReadAllText(path);
    }

    [TestMethod]
    public void Readme_DeclaresCurrentReleaseAndNewLogo()
    {
        string readme = ReadRootFile("README.md");
        // Relative asset path is intentional: GitHub serves it as the raw logo URL on main.
        StringAssert.Contains(readme, "FrameHub.App/Assets/FrameHubLogo.png", "README must reference the official logo asset.");
        StringAssert.Contains(readme, "v1.0.1", "README must declare 1.0.1.");
        Assert.IsFalse(readme.Contains("v0.7.1"), "README must not present the retired version as current.");
    }

    [TestMethod]
    public void ReadmePl_MatchesEnglishReleaseAndLogo()
    {
        string readmePl = ReadRootFile("README.pl.md");
        // Relative asset path is intentional: GitHub serves it as the raw logo URL on main.
        StringAssert.Contains(readmePl, "FrameHub.App/Assets/FrameHubLogo.png", "README.pl must reference the official logo asset.");
        StringAssert.Contains(readmePl, "v1.0.1", "README.pl must declare 1.0.1.");
        Assert.IsFalse(readmePl.Contains("v0.7.1"), "README.pl must not present the retired version as current.");
    }

    [TestMethod]
    public void Changelog_RecordsOneDotOhWithoutRewritingHistory()
    {
        string changelog = ReadRootFile("CHANGELOG.md");
        StringAssert.Contains(changelog, "## [1.0.0]", "CHANGELOG must contain the 1.0.0 section.");
        StringAssert.Contains(changelog, "## [1.0.0] - 2026-10-28", "1.0.0 entry must have its intended release date.");
        StringAssert.Contains(changelog, "docs/RELEASE_NOTES_1.0.0.md", "Release notes must be linked.");
        Assert.IsFalse(changelog.Contains("prepared, not yet released", StringComparison.OrdinalIgnoreCase), "Do not keep the outdated release-preparation notice.");
        StringAssert.Contains(changelog, "## [0.7.1]", "History of the 0.7.1 release must stay intact.");
        Assert.IsTrue(changelog.IndexOf("## [1.0.0]", StringComparison.Ordinal)
            < changelog.IndexOf("## [0.7.1]", StringComparison.Ordinal),
            "The 1.0.0 section must lead the release history.");
    }

    [TestMethod]
    public void BilingualReleaseNotes_AndPolishChangelog_AreLinked()
    {
        string readme = ReadRootFile("README.md");
        string readmePl = ReadRootFile("README.pl.md");
        string notes = ReadRootFile("docs/RELEASE_NOTES_1.0.0.md");
        string changelogPl = ReadRootFile("CHANGELOG.pl.md");

        StringAssert.Contains(readme, "docs/RELEASE_NOTES_1.0.0.md");
        StringAssert.Contains(readmePl, "docs/RELEASE_NOTES_1.0.0.md");
        StringAssert.Contains(readmePl, "CHANGELOG.pl.md");
        StringAssert.Contains(notes, "## English");
        StringAssert.Contains(notes, "## Polski");
        StringAssert.Contains(notes, "FrameHub-Setup-1.0.0.exe");
        StringAssert.Contains(changelogPl, "## [1.0.0]");
    }

    [TestMethod]
    public void Installer_PreservesExistingUpgradeIdentity()
    {
        string installer = ReadRootFile("installer/FrameHub.iss");
        StringAssert.Contains(installer, "AppId={{A073F5F3-E7D0-45C7-A233-8A98B033385B}");
        StringAssert.Contains(installer, "UsePreviousAppDir=yes");
        StringAssert.Contains(installer, "PrivilegesRequired=admin");
    }

    [TestMethod]
    public void Readmes_AgreeWithVersionTxt()
    {
        string version = ReadRootFile("version.txt").Trim();
        Assert.AreEqual("1.0.1", version, "version.txt is the single source for the current release.");
        foreach (string name in new[] { "README.md", "README.pl.md" })
        {
            string readme = ReadRootFile(name);
            StringAssert.Contains(readme, "v" + version, $"{name} badge must match version.txt.");
        }
    }

    [TestMethod]
    public void Documentation_OneDotOne_IsBilingualAndLinked()
    {
        string notes = ReadRootFile("docs/RELEASE_NOTES_1.0.1.md");
        StringAssert.Contains(notes, "## English", "1.0.1 notes must carry the English section.");
        StringAssert.Contains(notes, "## Polski", "1.0.1 notes must carry the Polish section.");
        StringAssert.Contains(notes, "Known limitations", "1.0.1 notes must document known limitations.");
        StringAssert.Contains(notes, "Znane ograniczenia", "1.0.1 notes must document known limitations in Polish.");
        StringAssert.Contains(notes, "Application data & reset", "1.0.1 notes must cover data & reset in English.");
        StringAssert.Contains(notes, "Dane aplikacji i reset", "1.0.1 notes must cover data & reset in Polish.");

        foreach (string guide in new[] { "docs/USER_GUIDE.md", "docs/USER_GUIDE.pl.md" })
        {
            string text = ReadRootFile(guide);
            StringAssert.Contains(text, "1.0.1", $"{guide} must describe the current release.");
            StringAssert.Contains(text, "RESET", $"{guide} must document the wipe confirmation.");
            StringAssert.Contains(text, "PresentMon", $"{guide} must document the optional prerequisite.");
        }

        foreach (string readme in new[] { "README.md", "README.pl.md" })
        {
            string text = ReadRootFile(readme);
            StringAssert.Contains(text, "docs/RELEASE_NOTES_1.0.1.md", $"{readme} must link the 1.0.1 notes.");
        }
    }

    [TestMethod]
    public void Documentation_LocalMarkdownLinks_Resolve()
    {
        string root = FindRepoRoot();
        var broken = new List<string>();
        foreach (string doc in new[]
        {
            "README.md", "README.pl.md", "CHANGELOG.md", "CHANGELOG.pl.md",
            "docs/RELEASE_NOTES_1.0.0.md", "docs/RELEASE_NOTES_1.0.1.md",
            "docs/USER_GUIDE.md", "docs/USER_GUIDE.pl.md"
        })
        {
            string text = File.ReadAllText(Path.Combine(root, doc));
            string? docDir = Path.GetDirectoryName(Path.Combine(root, doc));
            foreach (System.Text.RegularExpressions.Match match in
                System.Text.RegularExpressions.Regex.Matches(text, @"\[[^\]]*\]\(([^)]+)\)"))
            {
                string target = match.Groups[1].Value.Trim();
                if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    || target.StartsWith('#')
                    || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string path = target.Split('#')[0];
                if (string.IsNullOrWhiteSpace(path)) continue;
                string resolved = Path.GetFullPath(Path.Combine(docDir!, path.Replace('/', Path.DirectorySeparatorChar)));
                if (!File.Exists(resolved) && !Directory.Exists(resolved))
                {
                    broken.Add($"{doc} -> {target}");
                }
            }
        }

        Assert.AreEqual(0, broken.Count,
            "Broken local Markdown links:\n" + string.Join("\n", broken.Take(20)));
    }
    [TestMethod]
    public void CurrentRelease_IsOneDotOne_WithHistoryIntact()
    {
        string changelog = ReadRootFile("CHANGELOG.md");
        StringAssert.Contains(changelog, "## [1.0.1]", "CHANGELOG must contain the 1.0.1 section.");
        StringAssert.Contains(changelog, "FrameHub-Setup-1.0.1.exe", "1.0.1 section must name its installer asset.");
        Assert.IsTrue(changelog.IndexOf("## [1.0.1]", StringComparison.Ordinal)
            < changelog.IndexOf("## [1.0.0]", StringComparison.Ordinal),
            "The 1.0.1 section must lead the release history without rewriting 1.0.0.");
        StringAssert.Contains(changelog, "## [1.0.0] - 2026-10-28", "1.0.0 history must stay intact.");

        string changelogPl = ReadRootFile("CHANGELOG.pl.md");
        StringAssert.Contains(changelogPl, "## [1.0.1]", "Polish changelog must contain the 1.0.1 section.");

        string notes = ReadRootFile("docs/RELEASE_NOTES_1.0.1.md");
        StringAssert.Contains(notes, "FrameHub-Setup-1.0.1.exe", "1.0.1 release notes must name the installer asset.");

        string companionHtml = ReadRootFile("FrameHub.Companion/wwwroot/index.html");
        StringAssert.Contains(companionHtml, ">1.0.1<", "Companion static version placeholders must match version.txt.");
        Assert.IsFalse(companionHtml.Contains("1.0.0", StringComparison.Ordinal),
            "No stale 1.0.0 reference may remain in Companion index.html.");
    }
}
