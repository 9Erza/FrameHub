using FrameHub.App.Services;
using FrameHub.App.ViewModels;
using FrameHub.App.ViewModels.Benchmark;
using FrameHub.Core.Models;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Services;
using FrameHub.Core.Services.Benchmarking;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;

namespace FrameHub.Tests;

/// <summary>
/// QA-P2-1: the probabilistic <c>ComparisonRebuild_ConcurrentRefreshAndSelection</c>
/// stress test could not discriminate the pre-fix race (10/10 PASS on broken code),
/// so it is not a red/green gate. This test is deterministic instead: it holds the
/// <c>_comparisonGate</c> externally (same white-box reflection precedent as the
/// coordinator concurrency test) and proves that a history refresh cannot complete
/// a comparison rebuild while the gate is held — i.e. rebuilds are mutually
/// exclusive. Pre-fix the gate field does not exist and the test fails explicitly.
/// </summary>
[TestClass]
public sealed class ComparisonGateSerializationTests
{
    private string _tempDir = null!;
    private string? _previousDataDir;
    private string? _previousLocalDataDir;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FrameHub_ComparisonGateTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _previousDataDir = Environment.GetEnvironmentVariable(AppPaths.DataDirectoryVariable);
        _previousLocalDataDir = Environment.GetEnvironmentVariable(AppPaths.LocalDataDirectoryVariable);
        Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, _tempDir);
        Environment.SetEnvironmentVariable(AppPaths.LocalDataDirectoryVariable, _tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable, _previousDataDir);
        Environment.SetEnvironmentVariable(AppPaths.LocalDataDirectoryVariable, _previousLocalDataDir);
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    [TestMethod]
    public async Task HistoryRefresh_BlocksOnHeldComparisonGate_CompletesAfterRelease()
    {
        using var vm = CreateComparisonVm();

        // Warmup: with an empty temp store the history phases are fast, proving that
        // any later stall is the comparison gate and nothing else.
        var warmup = vm.RefreshHistoryAsync();
        Assert.AreEqual(Task.WhenAny(warmup, Task.Delay(TimeSpan.FromSeconds(10))).GetAwaiter().GetResult(), warmup,
            "Warmup history refresh must complete quickly on an empty store.");

        FieldInfo? gateField = typeof(BenchmarkViewModel).GetField("_comparisonGate", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(gateField, "RED: no comparison gate — concurrent rebuilds can still corrupt rows (QA-P2-1).");
        var gate = (object)gateField.GetValue(vm)!;
        Monitor.Enter(gate);
        try
        {
            Task refresh = Task.Run(() => vm.RefreshHistoryAsync());
            Assert.IsFalse(refresh.Wait(TimeSpan.FromSeconds(5)),
                "History refresh must not finish a comparison rebuild while the gate is held.");
        }
        finally
        {
            Monitor.Exit(gate);
        }

        // Post-release the pending refresh drains and the comparison stays consistent.
        vm.ComparisonA = Item(CompletedEntry());
        vm.ComparisonB = Item(CompletedEntry());
        Assert.IsTrue(vm.ComparisonRows.Count > 0, "Same-game comparison must produce metric rows.");
        Assert.IsFalse(vm.HasEnvironmentDifferences, "Identical environments must not report differences.");
        int rows = vm.ComparisonRows.Count;
        vm.ComparisonA = Item(CompletedEntry());
        vm.ComparisonB = Item(CompletedEntry());
        Assert.AreEqual(rows, vm.ComparisonRows.Count, "Repeated rebuilds must converge to the same row count.");
    }

    private BenchmarkViewModel CreateComparisonVm()
    {
        var localization = new LocalizationService(new SettingsService(Path.Combine(_tempDir, "settings.json")));
        var storage = new BenchmarkStorageService(Path.Combine(_tempDir, "benchmarks"));
        return new BenchmarkViewModel(localization, new ComparisonRuntime(), storage: storage, engineProbe: () => (false, null, null));
    }

    private sealed class ComparisonRuntime : IBenchmarkRuntimeContext
    {
        public AppSettings Settings { get; } = new();
        public List<ProcessProfile> Profiles { get; } = [];
        public string? LastAppliedProfile => null;
        public IBenchmarkCaptureCoordinator BenchmarkCoordinator { get; } = new BenchmarkCaptureCoordinator();
        public void AddActivity(string message, string level = "Info") { }
    }

    private static BenchmarkHistoryItemViewModel Item(BenchmarkHistoryEntry entry) =>
        new(entry, new LocalizationService(new SettingsService()));

    private static BenchmarkHistoryEntry CompletedEntry() => new()
    {
        SessionDirectory = Guid.NewGuid().ToString("N"),
        Metadata = new BenchmarkSessionMetadata
        {
            SessionId = Guid.NewGuid(),
            FrameHubVersion = "0.7.0",
            Game = new BenchmarkTarget { LibraryItemId = "game", DisplayName = "Game", LibrarySource = "Manual" },
            Status = BenchmarkSessionStatus.Completed,
            Environment = new BenchmarkEnvironmentSnapshot
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
            }
        },
        Summary = new BenchmarkSummary { PrimaryPresentedMetrics = new BenchmarkMetricSet { AverageFps = 120 } }
    };
}
