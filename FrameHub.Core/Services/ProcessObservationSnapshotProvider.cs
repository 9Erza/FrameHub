using System.Diagnostics;
using FrameHub.Core.Services.Benchmarking;

namespace FrameHub.Core.Services;

/// <summary>
/// Immutable metadata captured during one non-destructive system process enumeration.
/// This data is suitable for discovery and presentation only; callers must revalidate
/// process identity before any operating-system mutation.
/// </summary>
public sealed record ProcessObservation(
    int ProcessId,
    string ProcessName,
    string? ExecutablePath,
    DateTime StartTimeUtc);

public sealed record ProcessObservationSnapshot(
    long Generation,
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<ProcessObservation> Processes);

public interface IProcessObservationSnapshotProvider
{
    TimeSpan TimeToLive { get; }
    Task<ProcessObservationSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// On-demand, short-lived and single-flight process observation. It owns no timer and
/// performs no work until a consumer requests a snapshot.
/// </summary>
public sealed class ProcessObservationSnapshotProvider : IProcessObservationSnapshotProvider
{
    public static readonly TimeSpan DefaultTimeToLive = TimeSpan.FromMilliseconds(250);

    private readonly object _sync = new();
    private readonly Func<IReadOnlyList<ProcessObservation>> _enumerate;
    private readonly Func<DateTimeOffset> _clock;
    private ProcessObservationSnapshot? _current;
    private Task<ProcessObservationSnapshot>? _inFlight;
    private long _generation;

    public ProcessObservationSnapshotProvider(
        TimeSpan? timeToLive = null,
        Func<IReadOnlyList<ProcessObservation>>? enumerate = null,
        Func<DateTimeOffset>? clock = null)
    {
        TimeToLive = timeToLive ?? DefaultTimeToLive;
        if (TimeToLive < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeToLive));
        _enumerate = enumerate ?? EnumerateSystemProcesses;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public TimeSpan TimeToLive { get; }

    public async Task<ProcessObservationSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        Task<ProcessObservationSnapshot> sharedTask;
        lock (_sync)
        {
            DateTimeOffset now = _clock();
            if (_current != null && now - _current.CapturedAtUtc <= TimeToLive)
            {
                return _current;
            }

            if (_inFlight is { IsCompleted: true } completedRefresh)
            {
                if (completedRefresh.IsFaulted) _ = completedRefresh.Exception;
                _inFlight = null;
            }

            if (_inFlight == null)
            {
                Task<ProcessObservationSnapshot> refreshTask = Task.Run(Refresh);
                _inFlight = refreshTask;
                _ = refreshTask.ContinueWith(
                    completedTask => ClearCompletedRefresh(completedTask),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            sharedTask = _inFlight;
        }

        return await sharedTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private ProcessObservationSnapshot Refresh()
    {
        IReadOnlyList<ProcessObservation> processes = _enumerate();
        var snapshot = new ProcessObservationSnapshot(
            Interlocked.Increment(ref _generation),
            _clock(),
            processes);

        lock (_sync)
        {
            _current = snapshot;
        }

        return snapshot;
    }

    private void ClearCompletedRefresh(Task<ProcessObservationSnapshot> completedTask)
    {
        if (completedTask.IsFaulted) _ = completedTask.Exception;
        lock (_sync)
        {
            if (ReferenceEquals(_inFlight, completedTask)) _inFlight = null;
        }
    }

    private static IReadOnlyList<ProcessObservation> EnumerateSystemProcesses()
    {
        var observations = new List<ProcessObservation>();
        var pathResolver = new ProcessExecutablePathResolver();
        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                if (process.HasExited) continue;
                string processName = process.ProcessName;
                if (string.IsNullOrWhiteSpace(processName)) continue;

                observations.Add(new ProcessObservation(
                    process.Id,
                    processName,
                    pathResolver.Resolve(process).ExecutablePath,
                    TryGetStartTimeUtc(process)));
            }
            catch
            {
                // Protected or exiting processes are unavailable to non-destructive observation.
            }
            finally
            {
                process.Dispose();
            }
        }

        return observations;
    }

    private static DateTime TryGetStartTimeUtc(Process process)
    {
        try { return process.StartTime.ToUniversalTime(); }
        catch { return default; }
    }
}
