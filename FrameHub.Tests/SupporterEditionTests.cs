using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace FrameHub.Tests;

/// <summary>
/// UI overhaul part C (Ember C6): Supporter edition-integration PoC.
/// Validates the pinned-SHA manifest, the extension-only separation invariant
/// (no private-only/payments/DRM/licensing code in the public tree) and the
/// MIT/notices presence. No private repo, secrets or accounts are involved.
/// </summary>
[TestClass]
public sealed class SupporterEditionTests
{
    private static string RepoRoot => FindRepoRoot();

    [TestMethod]
    public void EditionManifest_IsWellFormed()
    {
        string path = Path.Combine(RepoRoot, "supporter", "edition.manifest.json");
        Assert.IsTrue(File.Exists(path), "supporter/edition.manifest.json must exist.");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;
        Assert.AreEqual(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.AreEqual("supporter", root.GetProperty("edition").GetString());
        Assert.AreEqual("https://github.com/9Erza/FrameHub", root.GetProperty("publicRepo").GetString());
        Assert.AreEqual("MIT", root.GetProperty("publicLicense").GetString());

        string sha = root.GetProperty("pinnedPublicSha").GetString() ?? string.Empty;
        Assert.AreEqual(40, sha.Length, "Pinned SHA must be a full commit hash.");
        Assert.IsTrue(sha.All(c => Uri.IsHexDigit(c)), "Pinned SHA must be hexadecimal.");

        Assert.AreEqual(0, root.GetProperty("extensionModules").GetArrayLength(),
            "PoC ships no extension modules: the private repo was not created.");
    }

    [TestMethod]
    public void PinnedSha_ExistsInLocalHistory()
    {
        string sha = PinnedSha();
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"cat-file -e {sha}^{{commit}}",
            WorkingDirectory = RepoRoot,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        try
        {
            using var process = System.Diagnostics.Process.Start(psi);
            Assert.IsNotNull(process, "git must start.");
            process.WaitForExit(15000);
            Assert.AreEqual(0, process.ExitCode, $"Pinned SHA {sha} must resolve to a real commit.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Assert.Inconclusive("git is not available on this machine.");
        }
    }

    [TestMethod]
    public void ExtensionOnly_NoSupporterProjectsInPublicTree()
    {
        string[] projects = Directory.GetFiles(RepoRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToArray();
        Assert.IsTrue(projects.Length > 0, "Sanity: public projects must exist.");
        foreach (string project in projects)
        {
            string name = Path.GetFileNameWithoutExtension(project);
            Assert.IsFalse(name.Contains("Supporter", StringComparison.OrdinalIgnoreCase),
                $"Public tree must not contain Supporter projects: {name}.");
            string content = File.ReadAllText(project);
            Assert.IsFalse(content.Contains("Supporter", StringComparison.OrdinalIgnoreCase),
                $"Public project must not reference Supporter assemblies: {name}.");
        }
    }

    [TestMethod]
    public void PublicArtifact_ExcludesPrivateOnlyCode()
    {
        string[] forbidden =
        [
            "SupporterExtension", "LicenseServer", "DrmModule", "PaymentsSdk",
            "StripeSecret", "BEGIN PRIVATE KEY"
        ];
        var hits = new List<string>();
        foreach (string file in EnumerateProductFiles())
        {
            string content;
            try { content = File.ReadAllText(file); }
            catch { continue; }
            foreach (string token in forbidden)
            {
                if (content.Contains(token, StringComparison.Ordinal))
                {
                    hits.Add($"{Path.GetRelativePath(RepoRoot, file)}: {token}");
                }
            }
        }

        Assert.AreEqual(0, hits.Count,
            "Private-only code must never land in the public tree:\n" + string.Join("\n", hits));
    }

    [TestMethod]
    public void SupporterDir_HoldsManifestOnly_NoCodeClone()
    {
        string dir = Path.Combine(RepoRoot, "supporter");
        Assert.IsTrue(Directory.Exists(dir));
        string[] code = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.AreEqual(0, code.Length, "supporter/ must not hold a code clone, only the manifest.");
    }

    [TestMethod]
    public void BaseEdition_KeepsMitAndNotices()
    {
        string license = Path.Combine(RepoRoot, "LICENSE");
        Assert.IsTrue(File.Exists(license) && new FileInfo(license).Length > 0, "MIT LICENSE must ship.");
        string notices = Path.Combine(RepoRoot, "docs", "THIRD-PARTY-NOTICES.md");
        Assert.IsTrue(File.Exists(notices) && new FileInfo(notices).Length > 0,
            "Third-party notices must ship.");
        string about = File.ReadAllText(Path.Combine(RepoRoot, "docs", "supporter", "EDITION-INTEGRATION.md"));
        Assert.IsTrue(about.Contains("single source of truth", StringComparison.OrdinalIgnoreCase));
    }

    private static string PinnedSha()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepoRoot, "supporter", "edition.manifest.json")));
        return document.RootElement.GetProperty("pinnedPublicSha").GetString() ?? string.Empty;
    }

    private static IEnumerable<string> EnumerateProductFiles()
    {
        string[] extensions = [".cs", ".xaml", ".xaml.cs", ".js"];
        return Directory.GetFiles(RepoRoot, "*.*", SearchOption.AllDirectories)
            .Where(path =>
                extensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                && !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(segment => segment.Contains("Tests", StringComparison.OrdinalIgnoreCase)
                        || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
                        || segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                        || segment.Equals(".git", StringComparison.OrdinalIgnoreCase)));
    }

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "FrameHub.slnx")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        Assert.Fail("Repo root (FrameHub.slnx) not found from test binaries.");
        throw new InvalidOperationException("unreachable");
    }
}
