using FrameHub.Core.Models;
using FrameHub.Core.Services;

namespace FrameHub.App.Services;

/// <summary>
/// Owns the once-per-process automatic update check gate and the update notification decision.
/// Reuses <see cref="UpdateService"/> for the actual version check; no parallel check implementation.
/// </summary>
public sealed class UpdateCheckSession
{
    private readonly Func<Task<UpdateCheckResult>> _checkAsync;
    private int _automaticCheckBegun;

    public UpdateCheckSession(Func<Task<UpdateCheckResult>>? checkAsync = null)
    {
        _checkAsync = checkAsync ?? DefaultCheckAsync;
    }

    public bool HasAutomaticCheckBegun => Volatile.Read(ref _automaticCheckBegun) == 1;

    /// <summary>
    /// Runs the automatic update check at most once per process lifetime.
    /// Returns null when updates are disabled, the check already ran, or the caller never presented a trigger.
    /// </summary>
    public async Task<UpdateCheckResult?> TryRunAutomaticCheckAsync(bool updatesEnabled)
    {
        if (!updatesEnabled) return null;
        if (Interlocked.Exchange(ref _automaticCheckBegun, 1) == 1) return null;
        return await _checkAsync();
    }

    /// <summary>Manual checks are never gated by the automatic once-per-process rule.</summary>
    public Task<UpdateCheckResult> RunManualCheckAsync() => _checkAsync();

    /// <summary>Automatic results are silent unless an update is actually available; errors and up-to-date results never present UI.</summary>
    public static bool ShouldPresentUpdateDialog(UpdateCheckResult? result)
        => result is not null && string.IsNullOrWhiteSpace(result.Error) && result.IsUpdateAvailable;

    private static Task<UpdateCheckResult> DefaultCheckAsync()
        => new UpdateService().CheckForUpdatesAsync(new AppInfo().Version);
}
