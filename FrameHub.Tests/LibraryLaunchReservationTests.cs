using FrameHub.App.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrameHub.Tests;

/// <summary>
/// Retention contract for launch reservations: a 3-second double-start shield
/// whose stale entries must not accumulate for the whole process lifetime.
/// Uses a manually advanced clock — no sleeps, no timing dependence.
/// </summary>
[TestClass]
public sealed class LibraryLaunchReservationTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void RepeatLaunch_WithinCooldown_IsBlocked()
    {
        DateTimeOffset now = T0;
        var reservations = new LibraryLaunchReservationService(() => now);

        reservations.RecordSuccessfulLaunch("game-1", now);

        Assert.IsTrue(reservations.IsCoolingDown("game-1", now.AddSeconds(2.9)));
        // Lookup is case-insensitive, matching library identity rules.
        Assert.IsTrue(reservations.IsCoolingDown("GAME-1", now.AddSeconds(1)));
    }

    [TestMethod]
    public void Launch_AtAndAfterCooldownExpiry_IsAllowed()
    {
        DateTimeOffset now = T0;
        var reservations = new LibraryLaunchReservationService(() => now);

        reservations.RecordSuccessfulLaunch("game-1", now);

        // Boundary: age < 3s cools down, age >= 3s does not.
        Assert.IsFalse(reservations.IsCoolingDown("game-1", now.AddSeconds(3)));
        Assert.IsFalse(reservations.IsCoolingDown("game-1", now.AddSeconds(30)));
        Assert.IsFalse(reservations.IsCoolingDown("never-launched", now));
    }

    [TestMethod]
    public void ExpiredEntries_AreEvicted_WhileActiveEntriesAreRetained()
    {
        DateTimeOffset now = T0;
        var reservations = new LibraryLaunchReservationService(() => now);

        reservations.RecordSuccessfulLaunch("old-1", now);
        reservations.RecordSuccessfulLaunch("old-2", now);
        Assert.AreEqual(2, reservations.Count);

        now = now.AddSeconds(2);
        reservations.RecordSuccessfulLaunch("active-1", now);
        Assert.AreEqual(3, reservations.Count);

        // Move past the old entries' window; any lookup triggers eviction.
        now = now.AddSeconds(2);
        Assert.IsFalse(reservations.IsCoolingDown("probe-new-id", now));

        Assert.AreEqual(1, reservations.Count, "Only the still-shielded entry may remain.");
        Assert.IsTrue(reservations.IsCoolingDown("active-1", now), "Active shield must not be weakened.");
        Assert.IsFalse(reservations.IsCoolingDown("old-1", now));
    }

    [TestMethod]
    public void ConcurrentRecordAndCheck_MatchesSingleThreadedContract()
    {
        DateTimeOffset now = T0;
        var reservations = new LibraryLaunchReservationService(() => now);

        Parallel.For(0, 200, i => reservations.RecordSuccessfulLaunch($"game-{i}", now));
        Assert.AreEqual(200, reservations.Count);

        bool anyFailure = false;
        Parallel.For(0, 200, i =>
        {
            try
            {
                if (!reservations.IsCoolingDown($"game-{i}", now.AddSeconds(1)))
                {
                    Interlocked.Exchange(ref anyFailure, true);
                }
            }
            catch
            {
                Interlocked.Exchange(ref anyFailure, true);
            }
        });
        Assert.IsFalse(anyFailure, "Concurrent reads within the window must all report cooling down.");
    }
}
