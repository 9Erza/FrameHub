using FrameHub.Core.Logging;
using FrameHub.Core.Models;

namespace FrameHub.Core.Services;

/// <summary>Serializes startup applies and guarantees that the newest desired state is eventually applied.</summary>
public sealed class StartupApplyCoordinator(IStartupConfigurationBackend backend, ILogger logger)
{
    private readonly StartupConfigurationExecutor _executor = new(backend, logger);
    private readonly SemaphoreSlim _applyGate = new(1, 1);
    private readonly object _sync = new();
    private DesiredStartupConfiguration? _latestDesired;
    private long _desiredVersion;

    public async Task<StartupApplyResult> ApplyLatestAsync(DesiredStartupConfiguration desired, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _latestDesired = desired;
            _desiredVersion++;
        }

        await _applyGate.WaitAsync(cancellationToken);
        try
        {
            while (true)
            {
                DesiredStartupConfiguration snapshot;
                long version;
                lock (_sync)
                {
                    snapshot = _latestDesired!;
                    version = _desiredVersion;
                }

                logger.Info($"Startup apply begin. Desired: Enabled={snapshot.StartWithWindows}; Mode={snapshot.WindowMode}; Elevated={snapshot.RunElevated}.");
                var result = await _executor.ApplyAsync(snapshot, cancellationToken);
                logger.Info($"Startup apply result. Success={result.Success}; State={result.FinalEvaluation.State}; Reasons={string.Join(',', result.FinalEvaluation.Reasons)}; Operations={string.Join(',', result.OperationsAttempted)}; Error={result.Error ?? "none"}.");

                lock (_sync)
                {
                    if (version == _desiredVersion) return result;
                }
                logger.Info("Startup desired state changed during apply; applying the latest snapshot.");
            }
        }
        finally
        {
            _applyGate.Release();
        }
    }
}
