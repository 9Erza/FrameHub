using FrameHub.Core.Models;
using FrameHub.Core.Services.Benchmarking;

namespace FrameHub.App.Services;

public interface IBenchmarkRuntimeContext
{
    AppSettings Settings { get; }
    List<ProcessProfile> Profiles { get; }
    string? LastAppliedProfile { get; }
    IBenchmarkCaptureCoordinator BenchmarkCoordinator { get; }
    void AddActivity(string message, string level = "Info");
}
