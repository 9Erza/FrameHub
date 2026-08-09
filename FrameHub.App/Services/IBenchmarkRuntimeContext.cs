using FrameHub.Core.Models;

namespace FrameHub.App.Services;

public interface IBenchmarkRuntimeContext
{
    AppSettings Settings { get; }
    List<ProcessProfile> Profiles { get; }
    string? LastAppliedProfile { get; }
    void AddActivity(string message, string level = "Info");
}
