using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.App.ViewModels.Benchmark;
using FrameHub.Core.Models;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Benchmarking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

[TestClass]
public sealed class BenchmarkEnvironmentStorageTests
{
    private string _root = null!;
    [TestInitialize] public void Initialize() => _root = Path.Combine(Path.GetTempPath(), "FrameHubEnvTests", Guid.NewGuid().ToString("N"));
    [TestCleanup] public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private static BenchmarkEnvironmentSnapshot SampleEnvironment { get; } = new()
    {
        OsDescription = "Microsoft Windows 10.0.26100",
        OsBuild = "26100",
        CpuName = "AMD Ryzen 9 7950X",
        GpuName = "NVIDIA GeForce RTX 4080",
        GpuDriverVersion = "32.0.15.6094",
        TotalMemoryBytes = 34_359_738_368UL,
        DisplayWidth = 2560,
        DisplayHeight = 1440,
        DisplayRefreshRateHz = 165
    };

    [TestMethod]
    public void CreateSession_WithEnvironment_PersistsAndRoundTrips()
    {
        BenchmarkStorageService storage = new(_root);
        BenchmarkSession session = storage.CreateSession(Target(), Identity(), "0.7.0", DateTime.UtcNow, environment: SampleEnvironment);

        BenchmarkSessionMetadata reloaded = storage.LoadSessionMetadata(session.SessionDirectory);

        Assert.IsNotNull(reloaded.Environment);
        Assert.AreEqual(SampleEnvironment.CpuName, reloaded.Environment.CpuName);
        Assert.AreEqual(SampleEnvironment.GpuName, reloaded.Environment.GpuName);
        Assert.AreEqual(SampleEnvironment.GpuDriverVersion, reloaded.Environment.GpuDriverVersion);
        Assert.AreEqual(SampleEnvironment.TotalMemoryBytes, reloaded.Environment.TotalMemoryBytes);
        Assert.AreEqual(SampleEnvironment.DisplayWidth, reloaded.Environment.DisplayWidth);
        Assert.AreEqual(SampleEnvironment.DisplayHeight, reloaded.Environment.DisplayHeight);
        Assert.AreEqual(SampleEnvironment.DisplayRefreshRateHz, reloaded.Environment.DisplayRefreshRateHz);
        Assert.AreEqual(SampleEnvironment.OsBuild, reloaded.Environment.OsBuild);
    }

    [TestMethod]
    public void SessionJson_WithoutEnvironmentProperty_StillLoads()
    {
        string directory = WriteLegacySession(@"{""schemaVersion"":1,""analysisVersion"":1,""sessionId"":""6f6f41c2-65b2-4e01-a0dd-090a5a68b6dc"",""frameHubVersion"":""0.6.0""}");

        BenchmarkSessionMetadata metadata = new BenchmarkStorageService(_root).LoadSessionMetadata(directory);

        Assert.IsNull(metadata.Environment);
        Assert.AreEqual("0.6.0", metadata.FrameHubVersion);
    }

    [TestMethod]
    public void LegacySessionJson_WithEmptyEnvironmentObject_LoadsWithAllFieldsUnavailable()
    {
        string directory = WriteLegacySession(@"{""schemaVersion"":1,""sessionId"":""6f6f41c2-65b2-4e01-a0dd-090a5a68b6dc"",""frameHubVersion"":""0.6.0"",""environment"":{}}");

        BenchmarkSessionMetadata metadata = new BenchmarkStorageService(_root).LoadSessionMetadata(directory);

        Assert.IsNotNull(metadata.Environment, "The schema-v1 empty environment placeholder must keep loading.");
        Assert.IsFalse(metadata.Environment.HasAnyValue, "A legacy empty environment must never fabricate values.");
    }

    [TestMethod]
    public void PartialEnvironmentSnapshot_RoundTripsWithMissingFieldsAsNull()
    {
        BenchmarkStorageService storage = new(_root);
        BenchmarkSession session = storage.CreateSession(Target(), Identity(), "0.7.0", DateTime.UtcNow,
            environment: new BenchmarkEnvironmentSnapshot { CpuName = "Intel Core i7-13700K" });

        BenchmarkEnvironmentSnapshot? reloaded = storage.LoadSessionMetadata(session.SessionDirectory).Environment;

        Assert.IsNotNull(reloaded);
        Assert.AreEqual("Intel Core i7-13700K", reloaded.CpuName);
        Assert.IsNull(reloaded.GpuName);
        Assert.IsNull(reloaded.GpuDriverVersion);
        Assert.IsNull(reloaded.TotalMemoryBytes);
        Assert.IsNull(reloaded.DisplayWidth);
    }

    [TestMethod]
    public void EnvironmentMetadata_DoesNotAlterBenchmarkMetrics()
    {
        BenchmarkStorageService storage = new(_root);
        BenchmarkSession withEnvironment = storage.CreateSession(Target(), Identity(), "0.7.0", DateTime.UtcNow, environment: SampleEnvironment);
        BenchmarkSession withoutEnvironment = storage.CreateSession(Target(), Identity(), "0.7.0", DateTime.UtcNow);
        BenchmarkSummary summary = new()
        {
            SessionId = withEnvironment.Metadata.SessionId,
            PrimaryPresentedMetrics = new BenchmarkMetricSet { ValidFrameCount = 600, AverageFps = 123.4, OnePercentLowFps = 98.6, P99FrameTimeMs = 14.2 }
        };
        storage.SaveSummary(withEnvironment, summary);
        storage.SaveSummary(withoutEnvironment, new BenchmarkSummary
        {
            SessionId = withoutEnvironment.Metadata.SessionId,
            PrimaryPresentedMetrics = new BenchmarkMetricSet { ValidFrameCount = 600, AverageFps = 123.4, OnePercentLowFps = 98.6, P99FrameTimeMs = 14.2 }
        });

        var metricsWith = storage.EnumerateSessions().Sessions.Single(entry => entry.Metadata.SessionId == withEnvironment.Metadata.SessionId).Summary!.PrimaryPresentedMetrics!;
        var metricsWithout = storage.EnumerateSessions().Sessions.Single(entry => entry.Metadata.SessionId == withoutEnvironment.Metadata.SessionId).Summary!.PrimaryPresentedMetrics!;

        Assert.AreEqual(metricsWithout.AverageFps, metricsWith.AverageFps);
        Assert.AreEqual(metricsWithout.P99FrameTimeMs, metricsWith.P99FrameTimeMs);
        Assert.AreEqual(metricsWithout.ValidFrameCount, metricsWith.ValidFrameCount);
    }

    private string WriteLegacySession(string sessionJson)
    {
        string gameDirectory = Path.Combine(_root, "manual-game-abc123");
        string directory = Path.Combine(gameDirectory, "20260101T000000000Z_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, BenchmarkFormat.SessionFileName), sessionJson);
        return directory;
    }

    private static BenchmarkTarget Target() => new() { LibraryItemId = "game", DisplayName = "Game", LibrarySource = "Manual" };
    private static BenchmarkProcessIdentity Identity() => new() { ProcessId = 42, ProcessName = "game", StartTimeUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
}

[TestClass]
public sealed class BenchmarkEnvironmentCaptureTests
{
    private string _tempDir = null!;
    private static readonly DateTime FixedStartTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [TestInitialize] public void Initialize() => _tempDir = Path.Combine(Path.GetTempPath(), "FrameHubEnvCaptureTests", Guid.NewGuid().ToString("N"));
    [TestCleanup] public void Cleanup() { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }

    private sealed class CountingEnvironmentProvider(BenchmarkEnvironmentSnapshot snapshot) : IBenchmarkEnvironmentProvider
    {
        public int CaptureCalls;
        public BenchmarkEnvironmentSnapshot Capture() { Interlocked.Increment(ref CaptureCalls); return snapshot; }
    }

    private sealed class ThrowingEnvironmentProvider : IBenchmarkEnvironmentProvider
    {
        public BenchmarkEnvironmentSnapshot Capture() => throw new InvalidOperationException("Synthetic environment failure");
    }

    private static BenchmarkCaptureRequest CreateRequest(int countdown = 0) => new()
    {
        Target = new BenchmarkTarget { LibraryItemId = "game-id", DisplayName = "Test Game", LibrarySource = "Manual" },
        Process = new BenchmarkProcessIdentity { ProcessId = 9999, ProcessName = "testgame", StartTimeUtc = FixedStartTime },
        AppVersion = "0.7.0",
        DurationSeconds = 1,
        CountdownSeconds = countdown
    };

    private sealed class FakeIdentityProvider : IBenchmarkProcessIdentityProvider
    {
        public BenchmarkProcessIdentity GetCurrentIdentity(int processId, BenchmarkTarget target) => new()
        {
            ProcessId = processId,
            ProcessName = "testgame",
            ExecutablePath = target.ConfiguredExecutablePath,
            StartTimeUtc = FixedStartTime
        };
    }

    private sealed class FakeBackend(BenchmarkStorageService storage) : IBenchmarkCaptureBackend
    {
        public Task<BenchmarkCaptureResult> CaptureAsync(BenchmarkSession session, CancellationToken cancellationToken = default)
        {
            session.Metadata.Status = BenchmarkSessionStatus.Completed;
            session.Metadata.CaptureDurationSeconds = 1;
            session.Metadata.AnalyzedDurationSeconds = 1;
            var summary = new BenchmarkSummary { SessionId = session.Metadata.SessionId, CaptureDurationSeconds = 1, AnalyzedDurationSeconds = 1, SelectedSwapChainAddress = "0x1", PrimaryPresentedMetrics = new BenchmarkMetricSet { ValidFrameCount = 60, AverageFps = 60, OnePercentLowFps = 50, P99FrameTimeMs = 20 }, Quality = new BenchmarkQualityResult { Level = BenchmarkQualityLevel.Valid } };
            storage.SaveSession(session);
            storage.SaveSummary(session, summary);
            return Task.FromResult(new BenchmarkCaptureResult { Session = session, Summary = summary });
        }
    }

    [TestMethod]
    public async Task Capture_UsesEnvironmentProviderExactlyOncePerBenchmark()
    {
        var storage = new BenchmarkStorageService(_tempDir);
        var provider = new CountingEnvironmentProvider(new BenchmarkEnvironmentSnapshot { CpuName = "CPU A" });
        var coordinator = new BenchmarkCaptureCoordinator(storage, () => new FakeBackend(storage), new FakeIdentityProvider(), delayProvider: (_, _) => Task.CompletedTask, environmentProvider: provider);

        BenchmarkCaptureOutcome outcome = await coordinator.StartCaptureAsync(CreateRequest(countdown: 2));

        Assert.AreEqual(CoordinatorStatus.Completed, outcome.Status);
        Assert.AreEqual(1, provider.CaptureCalls, "Environment must be captured exactly once per benchmark and never polled.");
        Assert.AreEqual("CPU A", outcome.Result!.Session.Metadata.Environment!.CpuName);
        Assert.AreEqual("CPU A", storage.EnumerateSessions().Sessions.Single().Metadata.Environment!.CpuName);
    }

    [TestMethod]
    public async Task SequentialCaptures_CaptureFreshEnvironmentEachTime()
    {
        var storage = new BenchmarkStorageService(_tempDir);
        var provider = new CountingEnvironmentProvider(new BenchmarkEnvironmentSnapshot { GpuDriverVersion = "32.0" });
        var coordinator = new BenchmarkCaptureCoordinator(storage, () => new FakeBackend(storage), environmentProvider: provider);

        await coordinator.StartCaptureAsync(CreateRequest());
        await coordinator.StartCaptureAsync(CreateRequest());

        Assert.AreEqual(2, provider.CaptureCalls);
        Assert.AreEqual(2, storage.EnumerateSessions().Sessions.Count);
        Assert.IsTrue(storage.EnumerateSessions().Sessions.All(entry => entry.Metadata.Environment?.GpuDriverVersion == "32.0"));
    }

    [TestMethod]
    public async Task EnvironmentProviderFailure_DoesNotAbortBenchmarkCapture()
    {
        var storage = new BenchmarkStorageService(_tempDir);
        var coordinator = new BenchmarkCaptureCoordinator(storage, () => new FakeBackend(storage), environmentProvider: new ThrowingEnvironmentProvider());

        BenchmarkCaptureOutcome outcome = await coordinator.StartCaptureAsync(CreateRequest());

        Assert.AreEqual(CoordinatorStatus.Completed, outcome.Status);
        Assert.IsNull(outcome.Result!.Session.Metadata.Environment, "A failed environment snapshot must not be attached and must not fail the capture.");
    }

    [TestMethod]
    public async Task CoordinatorWithoutProvider_CompletesWithoutEnvironment()
    {
        var storage = new BenchmarkStorageService(_tempDir);
        var coordinator = new BenchmarkCaptureCoordinator(storage, () => new FakeBackend(storage));

        BenchmarkCaptureOutcome outcome = await coordinator.StartCaptureAsync(CreateRequest());

        Assert.AreEqual(CoordinatorStatus.Completed, outcome.Status);
        Assert.IsNull(outcome.Result!.Session.Metadata.Environment);
    }
}

[TestClass]
public sealed class BenchmarkEnvironmentComparisonTests
{
    [TestMethod]
    public void IdenticalEnvironments_ProduceNoDifferences()
    {
        BenchmarkEnvironmentSnapshot environment = new()
        {
            OsDescription = "Microsoft Windows 10.0.26100", OsBuild = "26100",
            CpuName = "AMD Ryzen 9 7950X", GpuName = "NVIDIA GeForce RTX 4080", GpuDriverVersion = "32.0.15.6094",
            TotalMemoryBytes = 34_359_738_368UL, DisplayWidth = 2560, DisplayHeight = 1440, DisplayRefreshRateHz = 165
        };

        IReadOnlyList<BenchmarkEnvironmentDifference> differences = BenchmarkComparisonService.CompareEnvironments(Entry(environment, "0.7.0"), Entry(environment, "0.7.0"));

        Assert.AreEqual(0, differences.Count);
    }

    [TestMethod]
    public void GpuDriverDifference_IsDetected()
    {
        IReadOnlyList<BenchmarkEnvironmentDifference> differences = BenchmarkComparisonService.CompareEnvironments(
            Entry(Environment(gpuDriver: "32.0.15.6094")),
            Entry(Environment(gpuDriver: "33.0.15.5222")));

        BenchmarkEnvironmentDifference difference = differences.Single();
        Assert.AreEqual("gpu_driver", difference.Key);
        Assert.AreEqual("32.0.15.6094", difference.FirstValue);
        Assert.AreEqual("33.0.15.5222", difference.SecondValue);
    }

    [TestMethod]
    public void DisplayResolutionDifference_IsDetected()
    {
        IReadOnlyList<BenchmarkEnvironmentDifference> differences = BenchmarkComparisonService.CompareEnvironments(
            Entry(Environment(width: 1920, height: 1080, refresh: 144)),
            Entry(Environment(width: 2560, height: 1440, refresh: 144)));

        Assert.AreEqual(1, differences.Count);
        Assert.AreEqual("display_resolution", differences[0].Key);
        StringAssert.Contains(differences[0].FirstValue, "1920");
        StringAssert.Contains(differences[0].SecondValue, "2560");
    }

    [TestMethod]
    public void RefreshRateDifference_IsDetected()
    {
        IReadOnlyList<BenchmarkEnvironmentDifference> differences = BenchmarkComparisonService.CompareEnvironments(
            Entry(Environment(width: 1920, height: 1080, refresh: 144)),
            Entry(Environment(width: 1920, height: 1080, refresh: 165)));

        Assert.AreEqual(1, differences.Count);
        Assert.AreEqual("display_refresh_rate", differences[0].Key);
        StringAssert.Contains(differences[0].FirstValue, "144");
        StringAssert.Contains(differences[0].SecondValue, "165");
    }

    [TestMethod]
    public void OsBuildDifference_IsDetectedWithSameDescription()
    {
        IReadOnlyList<BenchmarkEnvironmentDifference> differences = BenchmarkComparisonService.CompareEnvironments(
            Entry(Environment(osBuild: "26100")),
            Entry(Environment(osBuild: "26200")));

        Assert.AreEqual(1, differences.Count);
        Assert.AreEqual("os", differences[0].Key);
        StringAssert.Contains(differences[0].FirstValue, "26100");
        StringAssert.Contains(differences[0].SecondValue, "26200");
    }

    [TestMethod]
    public void HardwareDifferences_AreDetected()
    {
        BenchmarkEnvironmentSnapshot first = Environment(cpu: "AMD Ryzen 9 7950X", gpu: "RTX 4080", memory: 34_359_738_368UL);
        BenchmarkEnvironmentSnapshot second = Environment(cpu: "Intel Core i9-14900K", gpu: "RTX 5080", memory: 68_719_476_736UL);

        IReadOnlyList<BenchmarkEnvironmentDifference> differences = BenchmarkComparisonService.CompareEnvironments(Entry(first), Entry(second));

        Assert.AreEqual(3, differences.Count);
        CollectionAssert.AreEquivalent(new[] { "cpu", "gpu", "memory" }, differences.Select(difference => difference.Key).ToArray());
    }

    [TestMethod]
    public void FrameHubVersionDifference_IsDetected()
    {
        IReadOnlyList<BenchmarkEnvironmentDifference> differences = BenchmarkComparisonService.CompareEnvironments(
            Entry(Environment(), "0.6.0"),
            Entry(Environment(), "0.7.0"));

        Assert.AreEqual(1, differences.Count);
        Assert.AreEqual("framehub_version", differences[0].Key);
    }

    [TestMethod]
    public void OldBenchmarkWithoutEnvironment_ComparesSafelyWithoutFabricatedDifferences()
    {
        IReadOnlyList<BenchmarkEnvironmentDifference> differences = BenchmarkComparisonService.CompareEnvironments(
            Entry(null),
            Entry(Environment(gpuDriver: "32.0")));

        Assert.AreEqual(0, differences.Count, "Unavailable environment metadata must never fabricate differences.");
    }

    [TestMethod]
    public void OneMissingField_DoesNotCreateMisleadingDifference()
    {
        BenchmarkEnvironmentSnapshot first = Environment(gpuDriver: "32.0.15.6094");
        BenchmarkEnvironmentSnapshot second = Environment(gpuDriver: null);

        IReadOnlyList<BenchmarkEnvironmentDifference> differences = BenchmarkComparisonService.CompareEnvironments(Entry(first), Entry(second));

        Assert.AreEqual(0, differences.Count, "A field recorded on only one side is not a comparable difference.");
    }

    [TestMethod]
    public void PerformanceComparison_StillWorksWhenEnvironmentsDiffer()
    {
        BenchmarkHistoryEntry first = Entry(Environment(gpuDriver: "32.0"), averageFps: 100);
        BenchmarkHistoryEntry second = Entry(Environment(gpuDriver: "33.0"), averageFps: 110);

        IReadOnlyList<BenchmarkComparisonMetric> metrics = BenchmarkComparisonService.Compare(first, second);
        IReadOnlyList<BenchmarkEnvironmentDifference> differences = BenchmarkComparisonService.CompareEnvironments(first, second);

        Assert.AreEqual(10.0, metrics.Single(metric => metric.Key == "average_fps").Delta);
        Assert.AreEqual(1, differences.Count, "Differing environments are advisory only and never block the performance comparison.");
    }

    private static BenchmarkEnvironmentSnapshot Environment(
        string? osDescription = "Microsoft Windows 10.0.26100", string? osBuild = "26100",
        string? cpu = "AMD Ryzen 9 7950X", string? gpu = "NVIDIA GeForce RTX 4080", string? gpuDriver = "32.0.15.6094",
        ulong? memory = 34_359_738_368UL, int? width = 2560, int? height = 1440, int? refresh = 165) => new()
    {
        OsDescription = osDescription,
        OsBuild = osBuild,
        CpuName = cpu,
        GpuName = gpu,
        GpuDriverVersion = gpuDriver,
        TotalMemoryBytes = memory,
        DisplayWidth = width,
        DisplayHeight = height,
        DisplayRefreshRateHz = refresh
    };

    private static BenchmarkHistoryEntry Entry(BenchmarkEnvironmentSnapshot? environment, string frameHubVersion = "0.7.0", double averageFps = 120) => new()
    {
        SessionDirectory = Guid.NewGuid().ToString("N"),
        Metadata = new BenchmarkSessionMetadata
        {
            SessionId = Guid.NewGuid(),
            FrameHubVersion = frameHubVersion,
            Game = new BenchmarkTarget { LibraryItemId = "game", DisplayName = "Game", LibrarySource = "Manual" },
            Status = BenchmarkSessionStatus.Completed,
            Environment = environment
        },
        Summary = new BenchmarkSummary { PrimaryPresentedMetrics = new BenchmarkMetricSet { AverageFps = averageFps } }
    };
}

[TestClass]
public sealed class BenchmarkEnvironmentPresentationTests
{
    [TestMethod]
    public void ResultEnvironment_IsExposedWithLocalizedLabels()
    {
        var localization = new LocalizationService(new SettingsService());
        var result = new BenchmarkResultViewModel(CompletedEntry(new BenchmarkEnvironmentSnapshot
        {
            CpuName = "AMD Ryzen 9 7950X",
            GpuName = "NVIDIA GeForce RTX 4080",
            GpuDriverVersion = "32.0.15.6094",
            TotalMemoryBytes = 34_359_738_368UL,
            DisplayWidth = 2560,
            DisplayHeight = 1440,
            DisplayRefreshRateHz = 165
        }), [], localization);
        string language = localization.CurrentLanguage;

        Assert.IsTrue(result.EnvironmentRows.Count >= 7);
        Assert.AreEqual("AMD Ryzen 9 7950X", ValueOf(result, Label("Benchmark.Environment.Cpu", language)));
        Assert.AreEqual("NVIDIA GeForce RTX 4080", ValueOf(result, Label("Benchmark.Environment.Gpu", language)));
        StringAssert.Contains(ValueOf(result, Label("Benchmark.Environment.Display", language)), "2560");
        StringAssert.Contains(ValueOf(result, Label("Benchmark.Environment.Display", language)), "165");
        StringAssert.Contains(ValueOf(result, Label("Benchmark.Environment.Memory", language)), "GB");
    }

    [TestMethod]
    public void MissingEnvironment_ShowsExistingUnavailablePresentation()
    {
        var localization = new LocalizationService(new SettingsService());
        var result = new BenchmarkResultViewModel(CompletedEntry(null), [], localization);
        string unavailable = LocalizationService.Translate("Benchmark.Unavailable", localization.CurrentLanguage);
        string frameHubLabel = LocalizationService.Translate("Benchmark.Environment.FrameHub", localization.CurrentLanguage);

        Assert.IsTrue(result.EnvironmentRows.All(row => row.Value == unavailable || row.Label == frameHubLabel),
            "Old sessions without environment must use the standard unavailable presentation.");
        Assert.AreEqual("0.7.0", result.EnvironmentRows.Single(row => row.Label == frameHubLabel).Value,
            "FrameHub version is recorded in session metadata independently of the environment snapshot.");
    }

    [TestMethod]
    public void ComparisonEnvironmentDifferences_AreExposedForDifferingSessions()
    {
        using var vm = CreateComparisonVm();
        vm.ComparisonA = Item(CompletedEntry(Environment(gpuDriver: "32.0.15.6094", refresh: 144, width: 1920, height: 1080)));
        vm.ComparisonB = Item(CompletedEntry(Environment(gpuDriver: "33.0.15.5222", refresh: 144, width: 1920, height: 1080)));

        Assert.IsTrue(vm.HasEnvironmentDifferences);
        Assert.AreEqual(1, vm.EnvironmentDifferenceRows.Count);
        StringAssert.Contains(vm.EnvironmentDifferenceRows[0].ChangeText, "32.0.15.6094");
        StringAssert.Contains(vm.EnvironmentDifferenceRows[0].ChangeText, "33.0.15.5222");
    }

    [TestMethod]
    public void ComparisonIdenticalEnvironments_HasNoEnvironmentDifferenceState()
    {
        using var vm = CreateComparisonVm();
        vm.ComparisonA = Item(CompletedEntry(Environment()));
        vm.ComparisonB = Item(CompletedEntry(Environment()));

        Assert.IsFalse(vm.HasEnvironmentDifferences);
        Assert.AreEqual(0, vm.EnvironmentDifferenceRows.Count);
    }

    [TestMethod]
    public void EnvironmentLocalization_HasEnglishAndPolishParity()
    {
        string[] keys =
        [
            "Benchmark.Result.Environment",
            "Benchmark.Environment.Cpu",
            "Benchmark.Environment.Gpu",
            "Benchmark.Environment.GpuDriver",
            "Benchmark.Environment.Os",
            "Benchmark.Environment.OsBuildFormat",
            "Benchmark.Environment.Memory",
            "Benchmark.Environment.Display",
            "Benchmark.Environment.RefreshRate",
            "Benchmark.Environment.FrameHub",
            "Benchmark.Compare.EnvironmentDifferences",
            "Benchmark.Compare.EnvironmentDiffers"
        ];

        foreach (string key in keys)
        {
            string english = LocalizationService.Translate(key, "en");
            string polish = LocalizationService.Translate(key, "pl");
            Assert.AreNotEqual(key, english, $"Missing English localization for '{key}'.");
            Assert.AreNotEqual(key, polish, $"Missing Polish localization for '{key}'.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(english));
            Assert.IsFalse(string.IsNullOrWhiteSpace(polish));
        }
    }

    private static string ValueOf(BenchmarkResultViewModel result, string label) =>
        result.EnvironmentRows.Single(row => row.Label == label).Value;

    private static string Label(string key, string language) => LocalizationService.Translate(key, language);

    private static BenchmarkEnvironmentSnapshot Environment(string? gpuDriver = "32.0.15.6094", int? width = 2560, int? height = 1440, int? refresh = 165) => new()
    {
        OsDescription = "Microsoft Windows 10.0.26100", OsBuild = "26100",
        CpuName = "AMD Ryzen 9 7950X", GpuName = "NVIDIA GeForce RTX 4080", GpuDriverVersion = gpuDriver,
        TotalMemoryBytes = 34_359_738_368UL, DisplayWidth = width, DisplayHeight = height, DisplayRefreshRateHz = refresh
    };

    private static BenchmarkHistoryEntry CompletedEntry(BenchmarkEnvironmentSnapshot? environment) => new()
    {
        SessionDirectory = Guid.NewGuid().ToString("N"),
        Metadata = new BenchmarkSessionMetadata
        {
            SessionId = Guid.NewGuid(),
            FrameHubVersion = "0.7.0",
            Game = new BenchmarkTarget { LibraryItemId = "game", DisplayName = "Game", LibrarySource = "Manual" },
            Status = BenchmarkSessionStatus.Completed,
            Environment = environment
        },
        Summary = new BenchmarkSummary { PrimaryPresentedMetrics = new BenchmarkMetricSet { AverageFps = 120 }, Quality = new BenchmarkQualityResult { Level = BenchmarkQualityLevel.Valid } }
    };

    private static BenchmarkHistoryItemViewModel Item(BenchmarkHistoryEntry entry) => new(entry, new LocalizationService(new SettingsService()));

    private static BenchmarkViewModel CreateComparisonVm() => new(new LocalizationService(new SettingsService()), new ComparisonRuntime(), engineProbe: () => (false, null, null));

    private sealed class ComparisonRuntime : IBenchmarkRuntimeContext
    {
        public AppSettings Settings { get; } = new();
        public List<ProcessProfile> Profiles { get; } = [];
        public string? LastAppliedProfile => null;
        public IBenchmarkCaptureCoordinator BenchmarkCoordinator { get; } = new BenchmarkCaptureCoordinator();
        public void AddActivity(string message, string level = "Info") { }
    }
}
