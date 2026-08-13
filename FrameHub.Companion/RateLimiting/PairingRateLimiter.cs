using System.Collections.Concurrent;

namespace FrameHub.Companion.RateLimiting;

public sealed class PairingRateLimiter
{
    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> _attempts = new();
    private readonly int _maxAttempts;
    private readonly TimeSpan _window;
    private readonly Func<DateTimeOffset> _clock;

    public PairingRateLimiter(int maxAttempts = 5, TimeSpan? window = null, Func<DateTimeOffset>? clock = null)
    {
        _maxAttempts = maxAttempts;
        _window = window ?? TimeSpan.FromMinutes(1);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public bool IsRateLimited(string clientIp)
    {
        if (string.IsNullOrWhiteSpace(clientIp)) return false;

        DateTimeOffset now = _clock();
        DateTimeOffset cutoff = now - _window;

        foreach (var kvp in _attempts)
        {
            lock (kvp.Value)
            {
                kvp.Value.RemoveAll(t => t < cutoff);
                if (kvp.Value.Count == 0)
                {
                    _attempts.TryRemove(kvp.Key, out _);
                }
            }
        }

        var list = _attempts.GetOrAdd(clientIp, _ => new List<DateTimeOffset>());
        lock (list)
        {
            list.RemoveAll(t => t < cutoff);
            if (list.Count >= _maxAttempts)
            {
                return true;
            }
            list.Add(now);
            return false;
        }
    }
}
