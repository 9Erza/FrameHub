using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Models.Library;
using FrameHub.Core.Services.Benchmarking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public sealed class BenchmarkStatisticsTests
{
    [TestMethod]
    public void KnownSequence_UsesDocumentedNearestRankAndSlowestTailDefinitions()
    {
        BenchmarkMetricSet metrics = BenchmarkStatistics.Calculate(Enumerable.Range(1, 100).Select(value => (double)value), "test");

        Assert.AreEqual(100, metrics.ValidFrameCount);
        Assert.AreEqual(1000.0 / 50.5, metrics.AverageFps!.Value, 1e-10);
        Assert.AreEqual(50.5, metrics.MedianFrameTimeMs!.Value, 1e-10);
        Assert.AreEqual(1000.0 / 50.5, metrics.MedianFps!.Value, 1e-10);
        Assert.AreEqual(10.0, metrics.OnePercentLowFps!.Value, 1e-10);
        Assert.AreEqual(10.0, metrics.PointOnePercentLowFps!.Value, 1e-10);
        Assert.AreEqual(95.0, metrics.P95FrameTimeMs);
        Assert.AreEqual(99.0, metrics.P99FrameTimeMs);
        Assert.AreEqual(1.0, metrics.MinimumFrameTimeMs);
        Assert.AreEqual(100.0, metrics.MaximumFrameTimeMs);
    }

    [TestMethod]
    public void OnePercentAndPointOnePercent_UseDifferentSlowestTailsAtScale()
    {
        BenchmarkMetricSet metrics = BenchmarkStatistics.Calculate(Enumerable.Range(1, 1000).Select(value => (double)value), "test");
        Assert.AreEqual(1000.0 / 995.5, metrics.OnePercentLowFps!.Value, 1e-12);
        Assert.AreEqual(1.0, metrics.PointOnePercentLowFps!.Value, 1e-12);
    }

    [TestMethod]
    public void ExtremePositiveFrameTime_IsRetainedRatherThanHiddenAsOutlier()
    {
        BenchmarkMetricSet metrics = BenchmarkStatistics.Calculate([16, 17, 1000], "test");
        Assert.AreEqual(1000.0, metrics.MaximumFrameTimeMs);
        Assert.AreEqual(1.0, metrics.OnePercentLowFps);
    }
}

[TestClass]
public sealed class BenchmarkAnalyzerTests
{
    [TestMethod]
    public void ExactPidFilter_ExcludesRowsFromOtherProcesses()
    {
        using var scope = new BenchmarkTestScope();
        BenchmarkSession session = scope.CreateSession(captureDurationSeconds: 30);
        BenchmarkSummary summary = new BenchmarkAnalyzer().AnalyzeSamples(session, [Frame(4242, "0x1", 16), Frame(4242, "0x1", 17), Frame(4242, "0x1", 18), Frame(9999, "0x1", 10)]);
        Assert.AreEqual(3, summary.PrimaryPresentedMetrics!.ValidFrameCount);
    }

    [TestMethod]
    public void DeterministicSequence_ProducesKnownMetrics()
    {
        using var scope = new BenchmarkTestScope();
        BenchmarkSession session = scope.CreateSession(captureDurationSeconds: 30);
        BenchmarkSummary summary = new BenchmarkAnalyzer().AnalyzeSamples(session, Enumerable.Range(1, 10).Select(value => Frame(4242, "0x1", value)));
        BenchmarkMetricSet metrics = summary.PrimaryPresentedMetrics!;
        Assert.AreEqual(10, metrics.ValidFrameCount);
        Assert.AreEqual(1000.0 / 5.5, metrics.AverageFps!.Value, 1e-10);
        Assert.AreEqual(10.0, metrics.P95FrameTimeMs);
        Assert.AreEqual(10.0, metrics.P99FrameTimeMs);
        Assert.AreEqual(100.0, metrics.OnePercentLowFps);
    }

    [TestMethod]
    public void MultipleSwapChains_AreSeparate_AndDominantChainIsDeterministic()
    {
        using var scope = new BenchmarkTestScope();
        BenchmarkSession session = scope.CreateSession(captureDurationSeconds: 30);
        IEnumerable<BenchmarkFrameSample> samples = Enumerable.Range(0, 10).Select(_ => Frame(4242, "0xMAIN", 10))
            .Concat(Enumerable.Range(0, 4).Select(_ => Frame(4242, "0xOVERLAY", 16)));
        BenchmarkSummary summary = new BenchmarkAnalyzer().AnalyzeSamples(session, samples);

        Assert.AreEqual(2, summary.SwapChains.Count);
        Assert.AreEqual("0xMAIN", summary.SelectedSwapChainAddress);
        Assert.AreEqual(10, summary.PrimaryPresentedMetrics!.ValidFrameCount);
        Assert.AreEqual(4, summary.SwapChains.Single(chain => chain.SwapChainAddress == "0xOVERLAY").UsefulFrameCount);
        StringAssert.Contains(summary.SwapChainSelectionReason, "ranked first");
    }

    [TestMethod]
    public void EqualSwapChains_SurfaceAmbiguityWarning_AndUseAddressTieBreaker()
    {
        using var scope = new BenchmarkTestScope();
        BenchmarkSession session = scope.CreateSession(captureDurationSeconds: 30);
        BenchmarkSummary summary = new BenchmarkAnalyzer().AnalyzeSamples(session, [Frame(4242, "0xBBB", 16), Frame(4242, "0xAAA", 16), Frame(4242, "0xBBB", 16), Frame(4242, "0xAAA", 16)]);

        Assert.AreEqual("0xAAA", summary.SelectedSwapChainAddress);
        Assert.IsTrue(summary.Quality.Issues.Any(issue => issue.Code == "ambiguous_swap_chain"));
    }

    [TestMethod]
    public void DroppedAndDisplayedMetrics_RemainSeparate_AndFrameTypesAreRetained()
    {
        using var scope = new BenchmarkTestScope();
        BenchmarkSession session = scope.CreateSession(captureDurationSeconds: 30);
        BenchmarkSummary summary = new BenchmarkAnalyzer().AnalyzeSamples(session,
        [
            Frame(4242, "0x1", 16, 16, null, true, false, "Application"),
            Frame(4242, "0x1", 16, null, null, false, true, "Generated"),
            Frame(4242, "0x1", 16, 17, null, true, false, "Application"),
            Frame(4242, "0x1", 16, null, null, false, true, "Generated")
        ]);

        Assert.AreEqual(4, summary.PrimaryPresentedMetrics!.ValidFrameCount);
        Assert.AreEqual(2, summary.SecondaryDisplayedMetrics!.ValidFrameCount);
        Assert.AreEqual(2, summary.DroppedOrNotDisplayedCount);
        Assert.AreEqual(2, summary.SwapChains[0].FrameTypeCounts["Application"]);
        Assert.AreEqual(2, summary.SwapChains[0].FrameTypeCounts["Generated"]);
        Assert.IsTrue(summary.Quality.Issues.Any(issue => issue.Code == "mixed_frame_types"));
    }

    [TestMethod]
    public void DisplayedTimeAndBetweenDisplayChange_AreNeverSameRowFallbacks()
    {
        using var scope = new BenchmarkTestScope();
        BenchmarkSession session = scope.CreateSession(captureDurationSeconds: 30);
        BenchmarkSummary summary = new BenchmarkAnalyzer().AnalyzeSamples(session,
        [
            Frame(4242, "0x1", 16, 10, null),
            Frame(4242, "0x1", 16, 20, 10),
            Frame(4242, "0x1", 16, 30, 20)
        ]);

        Assert.AreEqual(BenchmarkDisplayTimingSource.DisplayedTimeCurrentFrame, summary.SecondaryDisplayedTimingSource);
        Assert.AreEqual(3, summary.SecondaryDisplayedMetrics!.ValidFrameCount);
        Assert.AreEqual(20.0, summary.SecondaryDisplayedMetrics.MedianFrameTimeMs);
        Assert.AreEqual(2, summary.BetweenDisplayChangeMetrics!.ValidFrameCount);
        Assert.AreEqual(BenchmarkDisplayTimingSource.MsBetweenDisplayChangePreviousFrame, summary.BetweenDisplayChangeTimingSource);
        Assert.AreEqual(15.0, summary.BetweenDisplayChangeMetrics.MedianFrameTimeMs);
        StringAssert.Contains(summary.SecondaryDisplayedMetrics.Methodology, "DisplayedTime");
        StringAssert.Contains(summary.BetweenDisplayChangeMetrics.Methodology, "Previous-frame");
    }

    [TestMethod]
    public void MissingDisplayedTime_DoesNotPromoteBetweenDisplayChangeToPrimaryDisplayMetrics()
    {
        using var scope = new BenchmarkTestScope();
        BenchmarkSession session = scope.CreateSession(captureDurationSeconds: 30);
        BenchmarkSummary summary = new BenchmarkAnalyzer().AnalyzeSamples(session, [Frame(4242, "0xAAA", 16, null, 11), Frame(4242, "0xAAA", 16, null, 22)]);

        Assert.IsNull(summary.SecondaryDisplayedMetrics);
        Assert.IsNull(summary.SecondaryDisplayedTimingSource);
        Assert.AreEqual(2, summary.BetweenDisplayChangeMetrics!.ValidFrameCount);
        Assert.AreEqual(BenchmarkDisplayTimingSource.MsBetweenDisplayChangePreviousFrame, summary.BetweenDisplayChangeTimingSource);
    }

    [TestMethod]
    public void InsufficientSampleCount_IsAQualityWarning()
    {
        using var scope = new BenchmarkTestScope();
        BenchmarkSession session = scope.CreateSession(captureDurationSeconds: 30);
        BenchmarkSummary summary = new BenchmarkAnalyzer().AnalyzeSamples(session, [Frame(4242, "0x1", 16), Frame(4242, "0x1", 17)]);
        Assert.AreEqual(BenchmarkQualityLevel.Warning, summary.Quality.Level);
        Assert.IsTrue(summary.Quality.Issues.Any(issue => issue.Code == "low_sample_count"));
    }

    private static BenchmarkFrameSample Frame(int processId, string swapChain, double betweenPresents, double? displayedTime = null, double? betweenDisplayChange = null, bool? displayed = null, bool? dropped = null, string frameType = "Application") => new()
    {
        ProcessId = processId,
        Application = "game.exe",
        SwapChainAddress = swapChain,
        MsBetweenPresents = betweenPresents,
        DisplayedTime = displayedTime,
        MsBetweenDisplayChange = betweenDisplayChange,
        WasDisplayed = displayed,
        WasDropped = dropped,
        FrameType = frameType
    };
}

[TestClass]
public sealed class BenchmarkIdentityTests
{
    [TestMethod]
    public void ConfiguredPathMismatch_IsRejectedWithoutNameFallback()
    {
        var target = new BenchmarkTarget { ConfiguredExecutablePath = @"C:\Games\One\game.exe" };
        var process = new BenchmarkProcessIdentity
        {
            ProcessId = 42,
            ProcessName = "game",
            ExecutablePath = @"C:\Games\Two\game.exe",
            StartTimeUtc = DateTime.UtcNow
        };
        BenchmarkTargetException ex = Assert.ThrowsExactly<BenchmarkTargetException>(() => BenchmarkGameResolver.ValidateConfiguredPath(target, process));
        Assert.AreEqual("target_path_mismatch", ex.Code);
    }

    [TestMethod]
    public void ReusedPidWithDifferentStartTime_IsRejected()
    {
        DateTime firstStart = new(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc);
        var expected = new BenchmarkProcessIdentity { ProcessId = 42, ProcessName = "game", ExecutablePath = @"C:\Games\game.exe", StartTimeUtc = firstStart };
        var reused = new BenchmarkProcessIdentity { ProcessId = 42, ProcessName = "game", ExecutablePath = @"C:\Games\game.exe", StartTimeUtc = firstStart.AddMinutes(1) };
        BenchmarkTargetException ex = Assert.ThrowsExactly<BenchmarkTargetException>(() => BenchmarkGameResolver.ValidateSameProcessInstance(expected, reused));
        Assert.AreEqual("target_identity_changed", ex.Code);
    }

    [TestMethod]
    public void PathlessIdentity_StillRejectsPidReuseByStartTime()
    {
        DateTime firstStart = new(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc);
        var expected = new BenchmarkProcessIdentity
        {
            ProcessId = 10236,
            ProcessName = "League of Legends",
            ExecutablePathResolution = BenchmarkProcessPathResolution.Unavailable,
            StartTimeUtc = firstStart
        };
        var reused = new BenchmarkProcessIdentity
        {
            ProcessId = 10236,
            ProcessName = "League of Legends",
            ExecutablePathResolution = BenchmarkProcessPathResolution.Unavailable,
            StartTimeUtc = firstStart.AddSeconds(1)
        };
        Assert.ThrowsExactly<BenchmarkTargetException>(() => BenchmarkGameResolver.ValidateSameProcessInstance(expected, reused));
    }
}

[TestClass]
public sealed class ProcessExecutablePathResolverTests
{
    [TestMethod]
    public void ManagedPathSuccess_IsNormalizedAndRetained()
    {
        var resolver = new ProcessExecutablePathResolver();
        ProcessExecutablePathResult result = resolver.Resolve(() => @"C:\Managed\game.exe");
        Assert.AreEqual(Path.GetFullPath(@"C:\Managed\game.exe"), result.ExecutablePath);
        Assert.AreEqual(BenchmarkProcessPathResolution.Managed, result.Resolution);
    }

    [TestMethod]
    public void ManagedPathUnavailable_ReturnsPathlessWithoutAnyNativeProcessProbe()
    {
        var resolver = new ProcessExecutablePathResolver();
        int managedAttempts = 0;
        ProcessExecutablePathResult result = resolver.Resolve(() =>
        {
            managedAttempts++;
            return null;
        });
        Assert.IsNull(result.ExecutablePath);
        Assert.AreEqual(BenchmarkProcessPathResolution.Unavailable, result.Resolution);
        Assert.AreEqual(1, managedAttempts);
    }

    [TestMethod]
    public void ManagedPathAccessDenied_ReturnsUnavailableWithoutFallbackProbe()
    {
        var resolver = new ProcessExecutablePathResolver();
        ProcessExecutablePathResult result = resolver.Resolve(() => throw new System.ComponentModel.Win32Exception(5));
        Assert.IsNull(result.ExecutablePath);
        Assert.AreEqual(BenchmarkProcessPathResolution.Unavailable, result.Resolution);
    }
}

[TestClass]
public sealed class BenchmarkQualityTests
{
    [TestMethod]
    public void ValidCapture_HasNoManufacturedAccuracyScore()
    {
        var evaluator = new BenchmarkQualityEvaluator();
        BenchmarkQualityResult result = evaluator.Evaluate(
            new BenchmarkMetricSet { ValidFrameCount = 120 }, 30, new BenchmarkDataDiagnostics(), false, true, true, false);
        Assert.AreEqual(BenchmarkQualityLevel.Valid, result.Level);
        Assert.AreEqual(0, result.Issues.Count);
    }

    [TestMethod]
    public void ZeroFramesAndChangedIdentity_AreInvalid()
    {
        var evaluator = new BenchmarkQualityEvaluator();
        BenchmarkQualityResult result = evaluator.Evaluate(
            new BenchmarkMetricSet(), 30, new BenchmarkDataDiagnostics(), false, false, false, false);
        Assert.AreEqual(BenchmarkQualityLevel.Invalid, result.Level);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == "zero_usable_frames"));
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == "target_identity_invalid"));
    }
}

[TestClass]
public sealed class BenchmarkStorageTests
{
    [TestMethod]
    public void MetadataAndSummary_RoundTrip_WithStableSafeDirectoryScheme()
    {
        using var scope = new BenchmarkTestScope();
        BenchmarkSession session = scope.CreateSession(captureDurationSeconds: 30, libraryId: "unsafe:/library*id");
        session.Metadata.Status = BenchmarkSessionStatus.Completed;
        scope.Storage.SaveSession(session);
        var summary = new BenchmarkSummary { SessionId = session.Metadata.SessionId, CaptureDurationSeconds = 30, AnalyzedDurationSeconds = 20 };
        scope.Storage.SaveSummary(session, summary);

        BenchmarkSessionMetadata loadedMetadata = scope.Storage.LoadSessionMetadata(session.SessionDirectory);
        BenchmarkSummary loadedSummary = scope.Storage.LoadSummary(session.SessionDirectory);
        Assert.AreEqual(BenchmarkSessionStatus.Completed, loadedMetadata.Status);
        Assert.AreEqual(session.Metadata.SessionId, loadedSummary.SessionId);
        StringAssert.StartsWith(Path.GetFullPath(session.SessionDirectory), Path.GetFullPath(scope.Root));
        Assert.IsFalse(new DirectoryInfo(session.SessionDirectory).Parent!.Name.Contains('/'));
        Assert.IsFalse(new DirectoryInfo(session.SessionDirectory).Parent!.Name.Contains(':'));
    }
}

[TestClass]
public sealed class PresentMonApiCaptureBackendTests
{
    [TestMethod]
    public async Task NormalCapture_PersistsRawFramesSummaryAndCompletedStatus()
    {
        using var scope = new BenchmarkTestScope(); BenchmarkSession session = scope.CreateSession(null);
        var backend = new PresentMonApiCaptureBackend(new FakeFrameSource(FakeFrameSourceBehavior.Success), storage: scope.Storage, identityProvider: new FixedIdentityProvider(session.Metadata.Process));
        BenchmarkCaptureResult result = await backend.CaptureAsync(session);
        Assert.AreEqual(BenchmarkSessionStatus.Completed, result.Session.Metadata.Status);
        Assert.IsTrue(File.Exists(session.RawDataPath)); Assert.IsTrue(File.Exists(Path.Combine(session.SessionDirectory, BenchmarkFormat.SummaryFileName)));
        Assert.AreEqual("3.3.0", result.Session.Metadata.PresentMonVersion);
    }

    [TestMethod]
    public async Task Cancellation_PersistsCancelledTerminalMetadata()
    {
        using var scope = new BenchmarkTestScope(); BenchmarkSession session = scope.CreateSession(null);
        var backend = new PresentMonApiCaptureBackend(new FakeFrameSource(FakeFrameSourceBehavior.Cancel), storage: scope.Storage, identityProvider: new FixedIdentityProvider(session.Metadata.Process));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => backend.CaptureAsync(session));
        BenchmarkSessionMetadata stored = scope.Storage.LoadSessionMetadata(session.SessionDirectory);
        Assert.AreEqual(BenchmarkSessionStatus.Cancelled, stored.Status); Assert.AreEqual("capture_cancelled", stored.ErrorCode); Assert.IsNotNull(stored.EndUtc);
    }

    [TestMethod]
    public async Task ApiException_PersistsFailedTerminalMetadata()
    {
        using var scope = new BenchmarkTestScope(); BenchmarkSession session = scope.CreateSession(null);
        var backend = new PresentMonApiCaptureBackend(new FakeFrameSource(FakeFrameSourceBehavior.Fail), storage: scope.Storage, identityProvider: new FixedIdentityProvider(session.Metadata.Process));
        await Assert.ThrowsExactlyAsync<BenchmarkException>(() => backend.CaptureAsync(session));
        BenchmarkSessionMetadata stored = scope.Storage.LoadSessionMetadata(session.SessionDirectory);
        Assert.AreEqual(BenchmarkSessionStatus.Failed, stored.Status); Assert.AreEqual("presentmon_api_status", stored.ErrorCode); Assert.IsNotNull(stored.EndUtc);
    }

    private enum FakeFrameSourceBehavior { Success, Cancel, Fail }
    private sealed class FakeFrameSource(FakeFrameSourceBehavior behavior) : IPresentMonFrameSource
    {
        public Task<PresentMonApiCapture> CaptureAsync(int processId, TimeSpan duration, CancellationToken cancellationToken = default)
        {
            if (behavior == FakeFrameSourceBehavior.Cancel) throw new OperationCanceledException(cancellationToken);
            if (behavior == FakeFrameSourceBehavior.Fail) throw new BenchmarkException("presentmon_api_status", "test failure");
            IReadOnlyList<BenchmarkFrameSample> frames = Enumerable.Range(0, 60).Select(index => new BenchmarkFrameSample { ProcessId = processId, SwapChainAddress = "0x1", MsBetweenPresents = 16, FrameType = "Application" }).ToArray();
            return Task.FromResult(new PresentMonApiCapture { Frames = frames, ApiVersion = "3.3.0", Warnings = [], Diagnostics = new PresentMonApiCaptureDiagnostics() });
        }
    }
    private sealed class FixedIdentityProvider(BenchmarkProcessIdentity identity) : IBenchmarkProcessIdentityProvider { public BenchmarkProcessIdentity GetCurrentIdentity(int processId, BenchmarkTarget target) => identity; }
}

internal sealed class BenchmarkTestScope : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "FrameHubTests", Guid.NewGuid().ToString("N"));
    public BenchmarkStorageService Storage { get; }

    public BenchmarkTestScope()
    {
        Directory.CreateDirectory(Root);
        Storage = new BenchmarkStorageService(Root);
    }

    public BenchmarkSession CreateSession(double? captureDurationSeconds, string libraryId = "library-item-1")
    {
        var target = new BenchmarkTarget
        {
            LibraryItemId = libraryId,
            DisplayName = "Fixture Game",
            LibrarySource = LibrarySource.Steam.ToString(),
            SourceId = "730",
            ConfiguredExecutablePath = @"C:\Games\Fixture\game.exe"
        };
        var identity = new BenchmarkProcessIdentity
        {
            ProcessId = 4242,
            ProcessName = "game",
            ExecutablePath = @"C:\Games\Fixture\game.exe",
            StartTimeUtc = new DateTime(2026, 1, 1, 11, 59, 0, DateTimeKind.Utc)
        };
        BenchmarkSession session = Storage.CreateSession(target, identity, "0.6.0-dev", new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        session.Metadata.CaptureDurationSeconds = captureDurationSeconds;
        session.Metadata.EndUtc = captureDurationSeconds.HasValue ? session.Metadata.StartUtc.AddSeconds(captureDurationSeconds.Value) : null;
        Storage.SaveSession(session);
        return session;
    }

    public void Dispose()
    {
        if (Directory.Exists(Root)) Directory.Delete(Root, true);
    }
}
