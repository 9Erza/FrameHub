using System.Text.Json;
using FrameHub.Core.Models.Benchmarking;

namespace FrameHub.Core.Services.Benchmarking;

/// <summary>Production benchmark backend using the installed PresentMon Shared Service/API.</summary>
public sealed class PresentMonApiCaptureBackend : IBenchmarkCaptureBackend
{
    private readonly IPresentMonFrameSource _source;
    private readonly BenchmarkAnalyzer _analyzer;
    private readonly BenchmarkStorageService _storage;
    private readonly IBenchmarkProcessIdentityProvider _identityProvider;
    public PresentMonApiCaptureDiagnostics? LastDiagnostics { get; private set; }

    public PresentMonApiCaptureBackend(IPresentMonFrameSource? source = null, BenchmarkAnalyzer? analyzer = null, BenchmarkStorageService? storage = null, IBenchmarkProcessIdentityProvider? identityProvider = null)
    {
        _source = source ?? new PresentMonApiFrameSource(); _analyzer = analyzer ?? new BenchmarkAnalyzer(); _storage = storage ?? new BenchmarkStorageService(); _identityProvider = identityProvider ?? new BenchmarkProcessIdentityProvider();
    }

    public async Task<BenchmarkCaptureResult> CaptureAsync(BenchmarkSession session, CancellationToken cancellationToken = default)
    {
        if (session.Metadata.Status != BenchmarkSessionStatus.Created) throw new BenchmarkException("invalid_session_state", $"Capture requires Created status, not {session.Metadata.Status}.");
        DateTime? captureStart = null;
        try
        {
            BenchmarkGameResolver.ValidateSameProcessInstance(session.Metadata.Process, _identityProvider.GetCurrentIdentity(session.Metadata.Process.ProcessId, session.Metadata.Game));
            session.Metadata.Status = BenchmarkSessionStatus.Capturing; _storage.SaveSession(session);
            DateTime start = DateTime.UtcNow; captureStart = start;
            PresentMonApiCapture capture = await _source.CaptureAsync(session.Metadata.Process.ProcessId, TimeSpan.FromSeconds(session.Metadata.RequestedCaptureDurationSeconds ?? 30), cancellationToken).ConfigureAwait(false);
            LastDiagnostics = capture.Diagnostics;
            session.Metadata.StartUtc = start; session.Metadata.EndUtc = DateTime.UtcNow; session.Metadata.CaptureDurationSeconds = (session.Metadata.EndUtc.Value - start).TotalSeconds; session.Metadata.PresentMonVersion = capture.ApiVersion;
            await File.WriteAllTextAsync(session.RawDataPath, JsonSerializer.Serialize(capture.Frames), cancellationToken).ConfigureAwait(false);
            bool identityValid = true;
            try { BenchmarkGameResolver.ValidateSameProcessInstance(session.Metadata.Process, _identityProvider.GetCurrentIdentity(session.Metadata.Process.ProcessId, session.Metadata.Game)); }
            catch (BenchmarkTargetException ex) { identityValid = false; session.Metadata.DiagnosticMessage = ex.Message; }
            var diagnostics = new BenchmarkDataDiagnostics { Warnings = capture.Warnings.ToList() };
            BenchmarkSummary summary = _analyzer.AnalyzeSamples(session, capture.Frames, diagnostics, identityValid);
            session.Metadata.AnalyzedDurationSeconds = summary.AnalyzedDurationSeconds; session.Metadata.Status = BenchmarkSessionStatus.Completed;
            _storage.SaveSummary(session, summary); _storage.SaveSession(session);
            return new BenchmarkCaptureResult { Session = session, Summary = summary };
        }
        catch (OperationCanceledException) { FinalizeInterruptedSession(session, captureStart, BenchmarkSessionStatus.Cancelled, "capture_cancelled", "Benchmark capture was cancelled."); _storage.SaveSession(session); throw; }
        catch (Exception ex) { FinalizeInterruptedSession(session, captureStart, BenchmarkSessionStatus.Failed, ex is BenchmarkException benchmark ? benchmark.Code : "capture_failed", ex.Message); _storage.SaveSession(session); throw; }
    }

    private static void FinalizeInterruptedSession(BenchmarkSession session, DateTime? captureStart, BenchmarkSessionStatus status, string errorCode, string diagnostic)
    {
        session.Metadata.Status = status;
        session.Metadata.EndUtc ??= DateTime.UtcNow;
        if (captureStart.HasValue) session.Metadata.CaptureDurationSeconds ??= Math.Max(0, (session.Metadata.EndUtc.Value - captureStart.Value).TotalSeconds);
        session.Metadata.ErrorCode = errorCode;
        session.Metadata.DiagnosticMessage = string.IsNullOrWhiteSpace(session.Metadata.DiagnosticMessage) ? diagnostic : $"{session.Metadata.DiagnosticMessage}{Environment.NewLine}{diagnostic}";
    }
}
