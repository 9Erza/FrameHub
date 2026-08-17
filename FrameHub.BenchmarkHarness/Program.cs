using System.Diagnostics;
using FrameHub.BenchmarkHarness;
using FrameHub.Core.Models;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Services.Benchmarking;
using FrameHub.Core.Services.Library;

if (!BenchmarkHarnessOptions.TryParse(args, out BenchmarkHarnessOptions? options, out string? argumentError))
{
    Console.Error.WriteLine(argumentError);
    if (argumentError != BenchmarkHarnessOptions.Usage) Console.Error.WriteLine(BenchmarkHarnessOptions.Usage);
    return 2;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

BenchmarkSession? session = null;
try
{
    using Process process = Process.GetProcessById(options!.ProcessId);
    var gameResolver = new BenchmarkGameResolver();
    var probeTarget = new BenchmarkTarget { LibraryItemId = "process-probe", DisplayName = "Process probe", LibrarySource = "AdHoc" };
    BenchmarkProcessIdentity identity = gameResolver.ResolveProcessIdentity(process, probeTarget);
    HarnessTargetResolution resolution = new HarnessTargetResolver().Resolve(identity, new LibraryService().LoadItems(), options.GameId);

    Console.WriteLine("Resolved benchmark target:");
    BenchmarkReportWriter.WriteResolvedIdentity(Console.Out, resolution, identity);
    Console.WriteLine();

    Console.WriteLine("PresentMon backend: Shared Service/API");
    Console.WriteLine($"Capture duration: {options.DurationSeconds} seconds");
    Console.WriteLine("Press Ctrl+C to test cancellation.");

    var storage = new BenchmarkStorageService(options.OutputRoot);
    session = storage.CreateSession(
        resolution.Target,
        identity,
        new AppInfo().Version,
        DateTime.UtcNow,
        requestedCaptureDurationSeconds: options.DurationSeconds,
        environment: new BenchmarkEnvironmentProvider().Capture());
    var backend = new PresentMonApiCaptureBackend(new PresentMonApiFrameSource(() => new PresentMonApi(new PresentMonApiDllLocator(options.PresentMonApiDllPath)), identity.ProcessName), storage: storage);
    BenchmarkCaptureResult result = await backend.CaptureAsync(session, cancellation.Token);
    BenchmarkReportWriter.WriteSummary(Console.Out, result);
    if (backend.LastDiagnostics is { } apiDiagnostics) BenchmarkReportWriter.WriteApiDiagnostics(Console.Out, apiDiagnostics, result.Session.Metadata.PresentMonVersion);
    return result.Summary?.Quality.Level == BenchmarkQualityLevel.Invalid ? 6 : 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Capture cancelled. Partial raw data was retained and no completed summary was created.");
    if (session is not null) Console.Error.WriteLine($"Storage path: {session.SessionDirectory}");
    return 5;
}
catch (PresentMonUnavailableException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine("Install or repair the official PresentMon prerequisite so PresentMonSharedService and its matching PresentMonAPI2.dll are available, then retry.");
    return 4;
}
catch (BenchmarkTargetException ex)
{
    Console.Error.WriteLine($"Target resolution failed [{ex.Code}]: {ex.Message}");
    return 3;
}
catch (BenchmarkException ex)
{
    Console.Error.WriteLine($"Benchmark failed [{ex.Code}]: {ex.Message}");
    if (session is not null) Console.Error.WriteLine($"Storage path: {session.SessionDirectory}");
    return 5;
}
catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"Target resolution failed: {ex.Message}");
    return 3;
}
