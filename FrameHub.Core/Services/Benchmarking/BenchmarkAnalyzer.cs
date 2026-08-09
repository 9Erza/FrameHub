using FrameHub.Core.Models.Benchmarking;

namespace FrameHub.Core.Services.Benchmarking;

public sealed class BenchmarkAnalyzer
{
    private const string PresentedMethodology = "Presented intervals: positive MsBetweenPresents (or PresentMon FrameTime alias), unfiltered by FrameType; FPS is interval-derived.";
    private const string DisplayedMethodology = "Current-frame display duration: positive DisplayedTime values only. NA remains unavailable; MsBetweenDisplayChange is never substituted.";
    private const string BetweenDisplayChangeMethodology = "Previous-frame display interval: positive MsBetweenDisplayChange values attached to the current-present row. Kept separate from current-frame DisplayedTime.";
    private readonly BenchmarkQualityEvaluator _qualityEvaluator;

    public BenchmarkAnalyzer(BenchmarkQualityEvaluator? qualityEvaluator = null) => _qualityEvaluator = qualityEvaluator ?? new BenchmarkQualityEvaluator();

    public BenchmarkSummary AnalyzeSamples(BenchmarkSession session, IEnumerable<BenchmarkFrameSample> samples, BenchmarkDataDiagnostics? diagnostics = null, bool targetIdentityValid = true)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(samples);
        diagnostics ??= new BenchmarkDataDiagnostics();
        var chains = new Dictionary<string, SwapChainAccumulator>(StringComparer.OrdinalIgnoreCase);
        foreach (BenchmarkFrameSample sample in samples)
        {
            if (sample.ProcessId != session.Metadata.Process.ProcessId) continue;
            if (!chains.TryGetValue(sample.SwapChainAddress, out SwapChainAccumulator? chain))
            {
                chain = new SwapChainAccumulator(sample.SwapChainAddress);
                chains.Add(sample.SwapChainAddress, chain);
            }
            chain.Add(sample);
        }

        List<BenchmarkSwapChainSummary> summaries = chains.Values
            .Select(accumulator => accumulator.ToSummary())
            .OrderByDescending(summary => summary.UsefulFrameCount)
            .ThenByDescending(summary => summary.ActiveDurationSeconds)
            .ThenByDescending(summary => summary.ContinuityRatio)
            .ThenBy(summary => summary.SwapChainAddress, StringComparer.OrdinalIgnoreCase)
            .ToList();

        BenchmarkSwapChainSummary? selected = summaries.FirstOrDefault();
        bool ambiguous = selected is not null && summaries.Count > 1 && IsAmbiguous(selected, summaries[1]);
        string selectionReason = selected is null
            ? "No swap chain contained a usable presented-frame interval for the target PID."
            : ambiguous
                ? $"Selected {selected.SwapChainAddress} by deterministic ordering: useful frame count, active duration, continuity, then address; the top two scores were materially similar."
                : $"Selected {selected.SwapChainAddress} because it ranked first by useful frame count, then active duration and continuity.";

        double captureDuration = session.Metadata.CaptureDurationSeconds
            ?? Math.Max(0, ((session.Metadata.EndUtc ?? DateTime.UtcNow) - session.Metadata.StartUtc).TotalSeconds);
        bool frameTypeAvailable = selected?.FrameTypeAvailable ?? false;
        bool mixedFrameTypes = selected is not null && selected.FrameTypeCounts.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any();
        BenchmarkQualityResult quality = _qualityEvaluator.Evaluate(
            selected?.PresentedMetrics,
            captureDuration,
            diagnostics,
            ambiguous,
            targetIdentityValid,
            frameTypeAvailable,
            mixedFrameTypes);

        return new BenchmarkSummary
        {
            SessionId = session.Metadata.SessionId,
            CaptureDurationSeconds = captureDuration,
            AnalyzedDurationSeconds = selected?.PresentedMetrics.DurationSeconds ?? 0,
            SelectedSwapChainAddress = selected?.SwapChainAddress,
            SwapChainSelectionReason = selectionReason,
            PrimaryPresentedMetrics = selected?.PresentedMetrics,
            SecondaryDisplayedMetrics = selected?.DisplayedMetrics,
            SecondaryDisplayedTimingSource = selected?.DisplayedTimingSource,
            BetweenDisplayChangeMetrics = selected?.BetweenDisplayChangeMetrics,
            BetweenDisplayChangeTimingSource = selected?.BetweenDisplayChangeTimingSource,
            DroppedOrNotDisplayedCount = selected?.DroppedOrNotDisplayedCount,
            SwapChains = summaries,
            Quality = quality,
            DataDiagnostics = diagnostics
        };
    }

    internal static bool IsAmbiguous(BenchmarkSwapChainSummary first, BenchmarkSwapChainSummary second)
    {
        double countDifference = first.UsefulFrameCount == 0 ? 0 : (double)(first.UsefulFrameCount - second.UsefulFrameCount) / first.UsefulFrameCount;
        double maximumDuration = Math.Max(first.ActiveDurationSeconds, second.ActiveDurationSeconds);
        double durationDifference = maximumDuration <= 0 ? 0 : Math.Abs(first.ActiveDurationSeconds - second.ActiveDurationSeconds) / maximumDuration;
        double continuityDifference = Math.Abs(first.ContinuityRatio - second.ContinuityRatio);
        return countDifference <= 0.05 && durationDifference <= 0.10 && continuityDifference <= 0.05;
    }

    private sealed class SwapChainAccumulator
    {
        private readonly List<double> _presentedFrameTimes = new();
        private readonly List<double> _displayedFrameTimes = new();
        private readonly List<double> _betweenDisplayChangeTimes = new();
        private readonly List<double> _cpuStartTimes = new();
        private readonly Dictionary<string, int> _frameTypes = new(StringComparer.OrdinalIgnoreCase);
        private int _stateKnownCount;
        private int _droppedCount;

        public SwapChainAccumulator(string address) => Address = address;
        public string Address { get; }

        public void Add(BenchmarkFrameSample sample)
        {
            if (sample.MsBetweenPresents is > 0 and var frameTime && double.IsFinite(frameTime)) _presentedFrameTimes.Add(frameTime);
            if (sample.WasDisplayed != false && sample.DisplayedTime is > 0 and var displayed && double.IsFinite(displayed))
            {
                _displayedFrameTimes.Add(displayed);
            }
            if (sample.MsBetweenDisplayChange is > 0 and var previousFrameDisplayed && double.IsFinite(previousFrameDisplayed))
            {
                _betweenDisplayChangeTimes.Add(previousFrameDisplayed);
            }
            if (sample.CpuStartTime is double start
                && double.IsFinite(start)
                && sample.CpuStartTimeUnit == "Milliseconds")
            {
                _cpuStartTimes.Add(start / 1000.0);
            }

            if (sample.WasDropped.HasValue || sample.WasDisplayed.HasValue)
            {
                _stateKnownCount++;
                if (sample.WasDropped == true || sample.WasDisplayed == false) _droppedCount++;
            }

            if (!string.IsNullOrWhiteSpace(sample.FrameType))
            {
                _frameTypes[sample.FrameType] = _frameTypes.GetValueOrDefault(sample.FrameType) + 1;
            }
        }

        public BenchmarkSwapChainSummary ToSummary()
        {
            BenchmarkMetricSet presented = BenchmarkStatistics.Calculate(_presentedFrameTimes, PresentedMethodology);
            BenchmarkMetricSet? displayed = _displayedFrameTimes.Count == 0 ? null : BenchmarkStatistics.Calculate(_displayedFrameTimes, DisplayedMethodology);
            BenchmarkMetricSet? betweenDisplayChange = _betweenDisplayChangeTimes.Count == 0
                ? null
                : BenchmarkStatistics.Calculate(_betweenDisplayChangeTimes, BetweenDisplayChangeMethodology);
            return new BenchmarkSwapChainSummary
            {
                SwapChainAddress = Address,
                UsefulFrameCount = presented.ValidFrameCount,
                ActiveDurationSeconds = ActiveDurationSeconds(presented.DurationSeconds),
                ContinuityRatio = ContinuityRatio(),
                PresentedMetrics = presented,
                DisplayedMetrics = displayed,
                DisplayedTimingSource = displayed is null ? null : BenchmarkDisplayTimingSource.DisplayedTimeCurrentFrame,
                BetweenDisplayChangeMetrics = betweenDisplayChange,
                BetweenDisplayChangeTimingSource = betweenDisplayChange is null ? null : BenchmarkDisplayTimingSource.MsBetweenDisplayChangePreviousFrame,
                DroppedOrNotDisplayedCount = _stateKnownCount == 0 ? null : _droppedCount,
                FrameTypeAvailable = _frameTypes.Count > 0,
                FrameTypeCounts = new Dictionary<string, int>(_frameTypes, StringComparer.OrdinalIgnoreCase)
            };
        }

        private double ActiveDurationSeconds(double intervalDurationSeconds)
        {
            if (_cpuStartTimes.Count < 2) return intervalDurationSeconds;
            double timestampDuration = _cpuStartTimes.Max() - _cpuStartTimes.Min();
            return Math.Max(timestampDuration, intervalDurationSeconds);
        }

        private double ContinuityRatio()
        {
            if (_cpuStartTimes.Count < 2) return _presentedFrameTimes.Count > 0 ? 1 : 0;
            double medianSeconds = _presentedFrameTimes.Count == 0
                ? 0.0167
                : _presentedFrameTimes.OrderBy(value => value).ElementAt(_presentedFrameTimes.Count / 2) / 1000.0;
            double maximumContinuousGap = Math.Max(0.25, medianSeconds * 10);
            double[] ordered = _cpuStartTimes.OrderBy(value => value).ToArray();
            int continuous = 0;
            for (int index = 1; index < ordered.Length; index++)
            {
                double gap = ordered[index] - ordered[index - 1];
                if (gap > 0 && gap <= maximumContinuousGap) continuous++;
            }
            return (double)continuous / (ordered.Length - 1);
        }
    }
}
