using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Companion.Tests;

[TestClass]
public sealed class CompanionFrontendEncodingTests
{
    private static string? FindRepoRoot()
    {
        string? directory = Path.GetDirectoryName(typeof(CompanionFrontendEncodingTests).Assembly.Location);
        while (directory != null && !Directory.Exists(Path.Combine(directory, "FrameHub.Companion")))
        {
            directory = Path.GetDirectoryName(directory);
        }
        return directory;
    }

    [TestMethod]
    public void FrontendJsAssets_ContainNoMojibakeLiterals()
    {
        string? repoRoot = FindRepoRoot();
        if (repoRoot == null)
        {
            Assert.Inconclusive("Repository root could not be located.");
        }

        string jsDir = Path.Combine(repoRoot, "FrameHub.Companion", "wwwroot", "js");
        Assert.IsTrue(Directory.Exists(jsDir), "Companion js directory must exist.");

        string[] jsFiles = Directory.GetFiles(jsDir, "*.js");
        Assert.IsTrue(jsFiles.Length > 0, "At least one JS file must be found.");

        string[] forbiddenMojibakePatterns = new[]
        {
            "Â°",
            "â–",
            "â€",
            "Ã©",
            "Ã³",
            "Ã³"
        };

        foreach (string file in jsFiles)
        {
            string fileName = Path.GetFileName(file);
            string content = File.ReadAllText(file, System.Text.Encoding.UTF8);

            foreach (string pattern in forbiddenMojibakePatterns)
            {
                Assert.IsFalse(content.Contains(pattern, StringComparison.Ordinal),
                    $"File {fileName} contains known mojibake sequence '{pattern}'.");
            }
        }
    }

    [TestMethod]
    public void TelemetryJs_ContainsCorrectDegreeSymbol()
    {
        string? repoRoot = FindRepoRoot();
        if (repoRoot == null)
        {
            Assert.Inconclusive("Repository root could not be located.");
        }

        string telemetryJsPath = Path.Combine(repoRoot, "FrameHub.Companion", "wwwroot", "js", "telemetry.js");
        string content = File.ReadAllText(telemetryJsPath, System.Text.Encoding.UTF8);

        Assert.IsTrue(content.Contains("Math.round(val) + '°C'", StringComparison.Ordinal),
            "telemetry.js must contain the correct '°C' literal without double encoding.");
        Assert.IsFalse(content.Contains("Â°C", StringComparison.Ordinal),
            "telemetry.js must not contain 'Â°C'.");
    }

    [TestMethod]
    public void BenchmarksJs_ContainsCorrectComparisonArrows()
    {
        string? repoRoot = FindRepoRoot();
        if (repoRoot == null)
        {
            Assert.Inconclusive("Repository root could not be located.");
        }

        string benchmarksJsPath = Path.Combine(repoRoot, "FrameHub.Companion", "wwwroot", "js", "benchmarks.js");
        string content = File.ReadAllText(benchmarksJsPath, System.Text.Encoding.UTF8);

        Assert.IsTrue(content.Contains("'▲ Better'", StringComparison.Ordinal),
            "benchmarks.js must contain '▲ Better'.");
        Assert.IsTrue(content.Contains("'▼ Worse'", StringComparison.Ordinal),
            "benchmarks.js must contain '▼ Worse'.");
        Assert.IsFalse(content.Contains("â–", StringComparison.Ordinal),
            "benchmarks.js must not contain corrupted arrow mojibake.");
    }
}
