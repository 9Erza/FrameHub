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

    public bool IsCoolingDown(string itemId, DateTimeOffset now)
    {
        lock (_sync)
        {
            return _recentLaunches.TryGetValue(itemId, out DateTimeOffset lastLaunchTime)
                && now - lastLaunchTime < Cooldown;
        }
    }

    public void RecordSuccessfulLaunch(string itemId, DateTimeOffset launchedAt)
    {
        lock (_sync)
        {
            _recentLaunches[itemId] = launchedAt;
        }
    }
}
