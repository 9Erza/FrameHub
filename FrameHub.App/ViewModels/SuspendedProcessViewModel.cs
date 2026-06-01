using FrameHub.App.Services;
using FrameHub.Core.Models.SessionOptimization;

namespace FrameHub.App.ViewModels;

public sealed class SuspendedProcessViewModel
{
    public string RuleName { get; }
    public string ProcessName { get; }
    public int ProcessId { get; }
    public string SuspendedAt { get; }

    public SuspendedProcessViewModel(SuspendedProcessRecord record, LocalizationService localization)
    {
        RuleName = SessionDisplayText.RuleName(record.RuleId, record.RuleName, localization);
        ProcessName = record.ProcessName;
        ProcessId = record.ProcessId;
        SuspendedAt = record.SuspendedAtUtc.ToLocalTime().ToString("HH:mm:ss");
    }
}
