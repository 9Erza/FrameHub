using FrameHub.BenchmarkHarness;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Models.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public sealed class BenchmarkHarnessOptionsTests
{
    [TestMethod]
    public void ApiBackend_DoesNotRequireStandalonePath()
    {
        Assert.IsTrue(BenchmarkHarnessOptions.TryParse(["--backend", "api", "--pid", "4242"], out BenchmarkHarnessOptions? options, out string? error), error);
        Assert.AreEqual(4242, options!.ProcessId);
    }

    [TestMethod]
    public void ApiBackend_AcceptsAbsoluteApiDllOverride()
    {
        Assert.IsTrue(BenchmarkHarnessOptions.TryParse(["--backend", "api", "--pid", "4242", "--presentmon-api-dll", @"C:\Program Files\Intel\PresentMonSharedService\PresentMonAPI2.dll"], out BenchmarkHarnessOptions? options, out string? error), error);
        Assert.AreEqual(@"C:\Program Files\Intel\PresentMonSharedService\PresentMonAPI2.dll", options!.PresentMonApiDllPath);
    }

    [TestMethod]
    public void RequiredPid_WithNoDuration_UsesThirtySecondDefault()
    {
        bool parsed = BenchmarkHarnessOptions.TryParse(["--pid", "4242"], out BenchmarkHarnessOptions? options, out string? error);
        Assert.IsTrue(parsed, error);
        Assert.AreEqual(4242, options!.ProcessId);
        Assert.AreEqual(30, options.DurationSeconds);
    }

    [TestMethod]
    public void DurationOutsideTenToSixHundredSeconds_IsRejected()
    {
        Assert.IsFalse(BenchmarkHarnessOptions.TryParse(["--pid", "42", "--seconds", "9"], out _, out string? tooShort));
        StringAssert.Contains(tooShort, "10 through 600");
        Assert.IsFalse(BenchmarkHarnessOptions.TryParse(["--pid", "42", "--seconds", "601"], out _, out _));
    }

    [TestMethod]
    public void OptionalApiPathOutputAndGameIdentity_AreParsedWithoutShellInterpretation()
    {
        bool parsed = BenchmarkHarnessOptions.TryParse(
            ["--pid", "42", "--seconds", "60", "--game-id", "steam-730", "--presentmon-api-dll", @"C:\Program Files\Intel\PresentMonSharedService\PresentMonAPI2.dll", "--output", @"D:\Bench Output"],
            out BenchmarkHarnessOptions? options,
            out string? error);
        Assert.IsTrue(parsed, error);
        Assert.AreEqual(@"C:\Program Files\Intel\PresentMonSharedService\PresentMonAPI2.dll", options!.PresentMonApiDllPath);
        Assert.AreEqual(@"D:\Bench Output", options.OutputRoot);
        Assert.AreEqual("steam-730", options.GameId);
    }

    [TestMethod]
    public void RetiredCsvBackend_IsRejected()
    {
        Assert.IsFalse(BenchmarkHarnessOptions.TryParse(["--backend", "csv", "--pid", "42"], out _, out string? error));
        StringAssert.Contains(error, "retired");
    }
}

[TestClass]
public sealed class HarnessTargetResolverTests
{
    private static readonly BenchmarkProcessIdentity ProcessIdentity = new()
    {
        ProcessId = 4242,
        ProcessName = "game",
        ExecutablePath = @"C:\Games\Exact\game.exe",
        StartTimeUtc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
    };

    [TestMethod]
    public void ExactConfiguredPath_UsesStableLibraryIdentity()
    {
        var item = Game("library-1", "Exact Game", @"c:\games\exact\GAME.exe");
        HarnessTargetResolution result = new HarnessTargetResolver().Resolve(ProcessIdentity, [item]);
        Assert.AreEqual(HarnessIdentityConfidence.ExactPath, result.Confidence);
        Assert.AreEqual("library-1", result.Target.LibraryItemId);
    }

    [TestMethod]
    public void SameExecutableNameAtDifferentPath_IsNotSelected()
    {
        var item = Game("wrong", "Wrong Game", @"C:\Games\Other\game.exe");
        HarnessTargetResolution result = new HarnessTargetResolver().Resolve(ProcessIdentity, [item]);
        Assert.AreEqual(HarnessIdentityConfidence.AdHocExactProcess, result.Confidence);
        Assert.AreEqual("AdHoc", result.Target.LibrarySource);
        StringAssert.StartsWith(result.Target.LibraryItemId, "adhoc-");
    }

    [TestMethod]
    public void MultipleExactPathMatches_FailClearly()
    {
        BenchmarkTargetException exception = Assert.ThrowsExactly<BenchmarkTargetException>(() =>
            new HarnessTargetResolver().Resolve(ProcessIdentity,
            [
                Game("one", "One", ProcessIdentity.ExecutablePath),
                Game("two", "Two", ProcessIdentity.ExecutablePath)
            ]));
        Assert.AreEqual("ambiguous_library_match", exception.Code);
        StringAssert.Contains(exception.Message, "--game-id");
    }

    [TestMethod]
    public void ExplicitGameId_WithConfiguredPathMismatch_IsRejected()
    {
        BenchmarkTargetException exception = Assert.ThrowsExactly<BenchmarkTargetException>(() =>
            new HarnessTargetResolver().Resolve(ProcessIdentity, [Game("chosen", "Chosen", @"C:\Wrong\game.exe")], "chosen"));
        Assert.AreEqual("target_path_mismatch", exception.Code);
    }

    [TestMethod]
    public void PathUnavailable_WithOneCredibleProcessNameMatch_UsesDegradedLibraryIdentity()
    {
        BenchmarkProcessIdentity pathless = PathlessIdentity("League of Legends");
        LibraryItem league = Game("lol", "League of Legends", @"C:\Riot Games\League of Legends\Game\League of Legends.exe");

        HarnessTargetResolution result = new HarnessTargetResolver().Resolve(pathless, [league]);

        Assert.AreEqual("lol", result.Target.LibraryItemId);
        Assert.AreEqual(HarnessIdentityConfidence.PathUnavailableUniqueName, result.Confidence);
    }

    [TestMethod]
    public void PathUnavailable_WithAmbiguousNameMatches_DoesNotGuess()
    {
        BenchmarkProcessIdentity pathless = PathlessIdentity("League of Legends");
        LibraryItem first = Game("one", "League One", @"C:\Riot One\League of Legends.exe");
        LibraryItem second = Game("two", "League Two", @"D:\Riot Two\League of Legends.exe");

        BenchmarkTargetException exception = Assert.ThrowsExactly<BenchmarkTargetException>(() =>
            new HarnessTargetResolver().Resolve(pathless, [first, second]));

        Assert.AreEqual("ambiguous_name_match", exception.Code);
    }

    [TestMethod]
    public void PathUnavailable_WithoutNameMatch_UsesClearlyMarkedAdHocIdentity()
    {
        BenchmarkProcessIdentity pathless = PathlessIdentity("League of Legends");
        HarnessTargetResolution result = new HarnessTargetResolver().Resolve(pathless, []);
        Assert.AreEqual(HarnessIdentityConfidence.PathUnavailableAdHoc, result.Confidence);
        Assert.AreEqual("AdHocPathUnavailable", result.Target.LibrarySource);
        StringAssert.StartsWith(result.Target.LibraryItemId, "adhoc-pathless-");
    }

    private static LibraryItem Game(string id, string name, string? path) => new()
    {
        Id = id,
        DisplayName = name,
        Source = LibrarySource.Steam,
        Type = LibraryItemType.Game,
        AppId = "730",
        ExecutablePath = path
    };

    private static BenchmarkProcessIdentity PathlessIdentity(string processName) => new()
    {
        ProcessId = 10236,
        ProcessName = processName,
        ExecutablePath = null,
        ExecutablePathResolution = BenchmarkProcessPathResolution.Unavailable,
        StartTimeUtc = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc)
    };
}

[TestClass]
public sealed class BenchmarkReportWriterTests
{
    [TestMethod]
    public void DisplayReport_LabelsCurrentAndPreviousFrameSourcesSeparately()
    {
        var selected = new BenchmarkSwapChainSummary
        {
            SwapChainAddress = "0xAAA",
            FrameTypeAvailable = true,
            FrameTypeCounts = new Dictionary<string, int> { ["Application"] = 100 },
            PresentedMetrics = new BenchmarkMetricSet { ValidFrameCount = 100 },
            DisplayedMetrics = new BenchmarkMetricSet { ValidFrameCount = 90, AverageFps = 60, MedianFrameTimeMs = 16.6, P95FrameTimeMs = 17, P99FrameTimeMs = 18 },
            DisplayedTimingSource = BenchmarkDisplayTimingSource.DisplayedTimeCurrentFrame,
            BetweenDisplayChangeMetrics = new BenchmarkMetricSet { ValidFrameCount = 89, AverageFps = 59 }
        };
        var result = new BenchmarkCaptureResult
        {
            Session = new BenchmarkSession
            {
                SessionDirectory = @"C:\Bench\session",
                Metadata = new BenchmarkSessionMetadata
                {
                    Game = new BenchmarkTarget { DisplayName = "Game", LibrarySource = "Steam" },
                    Process = new BenchmarkProcessIdentity { ProcessId = 42, ExecutablePath = @"C:\Game\game.exe" }
                }
            },
            Summary = new BenchmarkSummary
            {
                SelectedSwapChainAddress = "0xAAA",
                SecondaryDisplayedMetrics = selected.DisplayedMetrics,
            SecondaryDisplayedTimingSource = selected.DisplayedTimingSource,
            BetweenDisplayChangeMetrics = selected.BetweenDisplayChangeMetrics,
            BetweenDisplayChangeTimingSource = BenchmarkDisplayTimingSource.MsBetweenDisplayChangePreviousFrame,
                SwapChains = [selected],
                Quality = new BenchmarkQualityResult { Level = BenchmarkQualityLevel.Valid }
            }
        };
        using var writer = new StringWriter();
        BenchmarkReportWriter.WriteSummary(writer, result);
        string report = writer.ToString();
        StringAssert.Contains(report, "DisplayedTime (current frame duration");
        StringAssert.Contains(report, "MsBetweenDisplayChange (previous-frame duration");
        StringAssert.Contains(report, "Dropped/not displayed: Unavailable");
    }
}
