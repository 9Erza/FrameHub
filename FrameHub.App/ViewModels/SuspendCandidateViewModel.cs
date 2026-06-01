using FrameHub.App.Services;
using FrameHub.Core.Models.SessionOptimization;

namespace FrameHub.App.ViewModels;

public sealed class SuspendCandidateViewModel
{
    public string RuleName { get; }
    public string ProcessName { get; }
    public int ProcessId { get; }
    public string ExecutablePath { get; }

    public SuspendCandidate Candidate { get; }

    public SuspendCandidateViewModel(SuspendCandidate candidate, LocalizationService localization)
    {
        Candidate = candidate;
        RuleName = SessionDisplayText.RuleName(candidate.RuleId, candidate.RuleName, localization);
        ProcessName = candidate.ProcessName;
        ProcessId = candidate.ProcessId;
        ExecutablePath = candidate.ExecutablePath ?? "—";
    }
}
