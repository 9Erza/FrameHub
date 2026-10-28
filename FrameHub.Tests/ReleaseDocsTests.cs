using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

/// <summary>
/// FrameHub 1.0 release-docs consistency: both READMEs point at the new logo,
/// declare 1.0.0, and the release notes, changelogs and installer identity stay consistent.
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
        StringAssert.Contains(readme, "v1.0.0", "README must declare 1.0.0.");
        Assert.IsFalse(readme.Contains("v0.7.1"), "README must not present the retired version as current.");
    }

    [TestMethod]
    public void ReadmePl_MatchesEnglishReleaseAndLogo()
    {
        string readmePl = ReadRootFile("README.pl.md");
        // Relative asset path is intentional: GitHub serves it as the raw logo URL on main.
        StringAssert.Contains(readmePl, "FrameHub.App/Assets/FrameHubLogo.png", "README.pl must reference the official logo asset.");
        StringAssert.Contains(readmePl, "v1.0.0", "README.pl must declare 1.0.0.");
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
        Assert.AreEqual("1.0.0", version, "version.txt is the single source for the current release.");
        foreach (string name in new[] { "README.md", "README.pl.md" })
        {
            string readme = ReadRootFile(name);
            StringAssert.Contains(readme, "v" + version, $"{name} badge must match version.txt.");
        }
    }
}
