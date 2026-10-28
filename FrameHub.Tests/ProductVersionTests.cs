using FrameHub.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

/// <summary>
/// FrameHub 1.0.0 single-source-of-truth: version.txt is the only maintained
/// version definition; MSBuild props flow it into assembly metadata and
/// <see cref="AppInfo"/> exposes it to WPF, Companion DTOs and the installer.
/// Historical release numbers stay in CHANGELOG.md and are not asserted here.
/// </summary>
[TestClass]
public sealed class ProductVersionTests
{
    private static string FindVersionFile()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && dir is not null; i++)
        {
            string candidate = Path.Combine(dir, "version.txt");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        Assert.Fail("version.txt was not found above the test output directory.");
        throw new InvalidOperationException("unreachable");
    }

    [TestMethod]
    public void DesktopMetadata_UsesPublicProductNameAndCentralBenchmarkVersion()
    {
        string root = Path.GetDirectoryName(FindVersionFile())!;
        string project = File.ReadAllText(Path.Combine(root, "FrameHub.App", "FrameHub.App.csproj"));
        StringAssert.Contains(project, "<AssemblyTitle>FrameHub</AssemblyTitle>");
        StringAssert.Contains(project, "<Product>FrameHub</Product>");

        string provider = File.ReadAllText(Path.Combine(root, "FrameHub.App", "Services", "AppBenchmarkProvider.cs"));
        StringAssert.Contains(provider, "new AppInfo().Version");
        Assert.IsFalse(provider.Contains("\"0.7.0\"", StringComparison.Ordinal),
            "Benchmarks must use the shared app version, never an obsolete fallback.");
    }

    [TestMethod]
    public void VersionFile_DeclaresCurrentRelease()
    {
        string version = File.ReadAllText(FindVersionFile()).Trim();
        Assert.AreEqual("1.0.0", version, "version.txt must declare the current 1.0.0 release.");
    }

    [TestMethod]
    public void VersionFile_UsesMajorMinorPatchFormat()
    {
        string version = File.ReadAllText(FindVersionFile()).Trim();
        string[] parts = version.Split('.');
        Assert.AreEqual(3, parts.Length, "Product version must use major.minor.patch format.");
        Assert.IsTrue(parts.All(p => p.All(char.IsDigit)), "Product version parts must be numeric.");
    }

    [TestMethod]
    public void AssemblyMetadata_MatchesVersionFile()
    {
        string expected = File.ReadAllText(FindVersionFile()).Trim();
        string? informational = typeof(AppInfo).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion;
        Assert.AreEqual(expected, informational,
            "FrameHub.Core assembly metadata must match version.txt (props -> assembly chain).");
    }
}
