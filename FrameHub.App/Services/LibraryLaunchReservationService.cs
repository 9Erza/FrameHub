namespace FrameHub.App.Services;

public sealed class LibraryLaunchReservationService
{
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(3);
    private readonly object _sync = new();
    private readonly Dictionary<string, DateTimeOffset> _recentLaunches = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<DateTimeOffset> _clock;

    public LibraryLaunchReservationService(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public DateTimeOffset Now => _clock();

    /// <summary>Total tracked reservations (active shield plus not-yet-evicted stale entries).</summary>
    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _recentLaunches.Count;
            }
        }
    }

    public bool IsCoolingDown(string itemId, DateTimeOffset now)
    {
        lock (_sync)
        {
            EvictExpiredInternal(now);
            return _recentLaunches.TryGetValue(itemId, out DateTimeOffset lastLaunchTime)
                && now - lastLaunchTime < Cooldown;
        }
    }

    public void RecordSuccessfulLaunch(string itemId, DateTimeOffset launchedAt)
    {
        lock (_sync)
        {
            EvictExpiredInternal(launchedAt);
            _recentLaunches[itemId] = launchedAt;
        }
    }

    private void EvictExpiredInternal(DateTimeOffset now)
    {
        // Drop only entries whose shield window has fully elapsed. Entries stamped
        // in the future (clock adjustments) are retained: fail closed, never weaken
        // the double-start shield. No timer needed; every lookup/record sweeps.
        List<string>? expired = null;
        foreach (var pair in _recentLaunches)
        {
            if (now - pair.Value >= Cooldown)
            {
                expired ??= new List<string>();
                expired.Add(pair.Key);
            }
        }

        if (expired == null)
        {
            return;
        }

        foreach (string key in expired)
        {
            _recentLaunches.Remove(key);
        }
    }
}
