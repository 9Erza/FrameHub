using FrameHub.Core.Models;
using FrameHub.Core.Logging;

namespace FrameHub.Core.Services;

public sealed record StartupOperationResult(bool Success, bool ElevationRequired = false, bool ElevationCancelled = false, string? Error = null);
public sealed record StartupApplyResult(bool Success, bool WasElevationRequired, bool WasElevationCancelled, IReadOnlyList<StartupOperation> OperationsAttempted, StartupConfigurationEvaluation FinalEvaluation, string? Error = null);

public interface IStartupConfigurationBackend
{
    Task<ActualStartupConfiguration> ReadActualAsync(DesiredStartupConfiguration desired, CancellationToken cancellationToken = default);
    Task<StartupOperationResult> ExecuteAsync(StartupOperation operation, DesiredStartupConfiguration desired, CancellationToken cancellationToken = default);
}

public sealed class StartupConfigurationExecutor(IStartupConfigurationBackend backend, ILogger? logger = null)
{
    public async Task<StartupApplyResult> ApplyAsync(DesiredStartupConfiguration desired, CancellationToken cancellationToken = default)
    {
        var initial = StartupConfigurationPlanner.Evaluate(desired, await backend.ReadActualAsync(desired, cancellationToken));
        logger?.Info($"Startup planner. State={initial.State}; Reasons={string.Join(',', initial.Reasons)}; Operations={string.Join(',', initial.RequiredOperations)}.");
        if (initial.Reasons.Contains(StartupConfigurationReason.ReadFailed))
            return new(false, false, false, Array.Empty<StartupOperation>(), initial, "Startup configuration could not be read safely.");

        var attempted = new List<StartupOperation>();
        bool elevationRequired = false;
        bool elevationCancelled = false;
        foreach (var operation in initial.RequiredOperations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempted.Add(operation);
            var result = await backend.ExecuteAsync(operation, desired, cancellationToken);
            elevationRequired |= result.ElevationRequired;
            elevationCancelled |= result.ElevationCancelled;
            if (!result.Success)
            {
                var finalAfterFailure = StartupConfigurationPlanner.Evaluate(desired, await backend.ReadActualAsync(desired, cancellationToken));
                return new(false, elevationRequired, elevationCancelled, attempted, finalAfterFailure, result.Error);
            }
        }

        var final = StartupConfigurationPlanner.Evaluate(desired, await backend.ReadActualAsync(desired, cancellationToken));
        bool healthy = !final.Reasons.Contains(StartupConfigurationReason.ReadFailed) &&
            (desired.StartWithWindows ? desired.RunElevated ? final.State == StartupConfigurationState.ElevatedScheduledTask : final.State == StartupConfigurationState.Registry : final.State == StartupConfigurationState.Disabled);
        logger?.Info($"Startup final evaluation. State={final.State}; Reasons={string.Join(',', final.Reasons)}; Success={healthy}.");
        return new(healthy, elevationRequired, elevationCancelled, attempted, final, healthy ? null : "Startup configuration differs from the requested configuration.");
    }
}
