using FrameHub.Companion.Models;
using FrameHub.Companion.Providers;
using FrameHub.Core.Logging;
using FrameHub.Core.Models;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Services.Benchmarking;
using FrameHub.Core.Services.SessionOptimization;

namespace FrameHub.App.Services;

public sealed class AppSessionOptimizationProvider : ICompanionSessionOptimizationProvider
{
    private readonly SessionOptimizationCoordinator _coordinator;
    private readonly IActiveGameMonitor _activeGameMonitor;
    private readonly IBenchmarkCaptureCoordinator _benchmarkCoordinator;
    private readonly Func<string, ProcessProfile?>? _gameProfileResolver;
    private readonly ILogger _logger;

    public AppSessionOptimizationProvider(
        AppRuntimeService runtime,
        SessionOptimizationCoordinator? coordinator = null,
        IActiveGameMonitor? activeGameMonitor = null,
        IBenchmarkCaptureCoordinator? benchmarkCoordinator = null,
        ILogger? logger = null,
        Func<string, ProcessProfile?>? gameProfileResolver = null)
        : this(
            coordinator ?? runtime.SessionOptimizationCoordinator,
            activeGameMonitor ?? runtime.ActiveGameMonitor,
            benchmarkCoordinator ?? runtime.BenchmarkCoordinator,
            logger,
            gameProfileResolver)
    {
    }

    public AppSessionOptimizationProvider(
        SessionOptimizationCoordinator coordinator,
        IActiveGameMonitor activeGameMonitor,
        IBenchmarkCaptureCoordinator benchmarkCoordinator,
        ILogger? logger = null,
        Func<string, ProcessProfile?>? gameProfileResolver = null)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _activeGameMonitor = activeGameMonitor ?? throw new ArgumentNullException(nameof(activeGameMonitor));
        _benchmarkCoordinator = benchmarkCoordinator ?? throw new ArgumentNullException(nameof(benchmarkCoordinator));
        _logger = logger ?? LoggerService.Instance;
        _gameProfileResolver = gameProfileResolver;
    }

    public Task<CompanionSessionOptimizationStateDto> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var session = _coordinator.ActiveSession;
        var activeGame = _activeGameMonitor.CurrentSnapshot;

        var dto = new CompanionSessionOptimizationStateDto
        {
            IsSessionActive = session?.IsActive == true,
            SessionId = session?.SessionId,
            GameId = session?.GameId ?? activeGame?.LibraryItem.Id,
            GameDisplayName = session?.GameName ?? activeGame?.LibraryItem.DisplayName,
            StartedAtUtc = session?.StartedAtUtc,
            SuspendedProcessCount = session == null
                ? 0
                : session.SuspendedProcesses.Count
                    + session.AmbiguousProcesses.Count
                    + (session.PendingSuspension == null ? 0 : 1)
                    + (session.PendingResume == null ? 0 : 1),
            TaskbarHidden = session?.TaskbarHidden == true,
            IsRecoveryPending = session?.IsRecoveryPending == true,
            Trigger = session?.Trigger ?? "Manual"
        };

        return Task.FromResult(dto);
    }

    public async Task<CompanionOptimizationResultDto> ApplyOptimizationAsync(CancellationToken cancellationToken = default)
    {
        // 1. Early benchmark check
        if (_benchmarkCoordinator.IsActive)
        {
            return new CompanionOptimizationResultDto
            {
                Success = false,
                ErrorCode = "benchmark_active"
            };
        }

        // 2. Resolve current active game from trusted ActiveGameMonitor
        var activeGame = _activeGameMonitor.CurrentSnapshot;
        if (activeGame == null || activeGame.LibraryItem == null)
        {
            return new CompanionOptimizationResultDto
            {
                Success = false,
                ErrorCode = "no_game"
            };
        }

        // 3. Immediate benchmark recheck
        if (_benchmarkCoordinator.IsActive)
        {
            return new CompanionOptimizationResultDto
            {
                Success = false,
                ErrorCode = "benchmark_active"
            };
        }

        // 4. Delegate to authoritative coordinator
        var result = await _coordinator.StartSessionAsync("Remote", activeGame.LibraryItem, cancellationToken).ConfigureAwait(false);

        if (result.Success)
        {
            return new CompanionOptimizationResultDto
            {
                Success = true,
                ErrorCode = "applied",
                SuspendedProcessCount = result.SuspendedCount
            };
        }

        return new CompanionOptimizationResultDto
        {
            Success = false,
            ErrorCode = string.IsNullOrWhiteSpace(result.ErrorCode) ? "apply_failed" : result.ErrorCode,
            SuspendedProcessCount = result.SuspendedCount
        };
    }

    public async Task<CompanionOptimizationResultDto> RestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        // 1. Early benchmark check
        if (_benchmarkCoordinator.IsActive)
        {
            return new CompanionOptimizationResultDto
            {
                Success = false,
                ErrorCode = "benchmark_active"
            };
        }

        // 2. Delegate to authoritative coordinator
        var result = await _coordinator.StopSessionAsync(cancellationToken).ConfigureAwait(false);

        if (result.Success)
        {
            return new CompanionOptimizationResultDto
            {
                Success = true,
                ErrorCode = "restored",
                SuspendedProcessCount = result.RemainingCount
            };
        }

        return new CompanionOptimizationResultDto
        {
            Success = false,
            ErrorCode = string.IsNullOrWhiteSpace(result.ErrorCode) ? "restore_failed" : result.ErrorCode,
            SuspendedProcessCount = result.RemainingCount
        };
    }

    public Task<CompanionSessionCpuStateDto> GetCpuStateAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ToCpuStateDto(_coordinator.GetSessionCpuState()));
    }

    public Task<CompanionSessionCpuResultDto> ApplyCpuOverrideAsync(CompanionSessionCpuApplyRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request is null
            || !Guid.TryParse(request.SessionToken, out Guid sessionToken)
            || !TryParseMode(request.Mode, out OptimizationMode mode)
            || request.Indices is null || request.Indices.Count == 0)
        {
            return Task.FromResult(new CompanionSessionCpuResultDto
            {
                Success = false,
                ErrorCode = "invalid_request",
                Message = "The request must include a session token, a supported mode, and at least one processor."
            });
        }

        long mask = 0;
        foreach (int index in request.Indices)
        {
            if (index is < 0 or > 63)
            {
                return Task.FromResult(new CompanionSessionCpuResultDto
                {
                    Success = false,
                    ErrorCode = "invalid_selection",
                    Message = "The request contains an unsupported processor."
                });
            }
            mask |= 1L << index;
        }

        SessionCpuMutationResult result = _coordinator.ApplySessionCpuOverride(sessionToken, mode, mask);
        return Task.FromResult(ToCpuResultDto(result));
    }

    public Task<CompanionSessionCpuResultDto> ResetCpuOverrideAsync(CompanionSessionCpuResetRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request is null || !Guid.TryParse(request.SessionToken, out Guid sessionToken))
        {
            return Task.FromResult(new CompanionSessionCpuResultDto
            {
                Success = false,
                ErrorCode = "invalid_request",
                Message = "The request must include a session token."
            });
        }

        SessionCpuMutationResult result = _coordinator.ResetSessionCpuOverride(sessionToken);
        return Task.FromResult(ToCpuResultDto(result));
    }

    private CompanionSessionCpuResultDto ToCpuResultDto(SessionCpuMutationResult result) => new()
    {
        Success = result.Success,
        ErrorCode = result.ErrorCode,
        Message = result.Message,
        State = result.State is null ? null : ToCpuStateDto(result.State)
    };

    private CompanionSessionCpuStateDto ToCpuStateDto(SessionCpuStateResult state)
    {
        ProcessProfile? profile = _gameProfileResolver is not null && state.LibraryItemId is not null
            ? _gameProfileResolver(state.LibraryItemId)
            : null;

        return new CompanionSessionCpuStateDto
        {
            Available = state.Available,
            UnavailableReason = state.UnavailableReason,
            ProtectedGame = state.ProtectedGame,
            SessionToken = state.SessionToken?.ToString("N"),
            GameDisplayName = state.GameDisplayName,
            Source = state.TemporaryOverrideActive
                ? "temporary-override"
                : profile is not null
                    ? "profile"
                    : "system",
            ProfileName = profile?.DisplayName,
            TemporaryOverrideActive = state.TemporaryOverrideActive,
            CurrentSelection = ToSelectionDto(state.CurrentSelection),
            OverrideSelection = ToSelectionDto(state.OverrideSelection),
            Topology = state.Topology is null
                ? null
                : new CompanionSessionCpuTopologyDto
                {
                    Processors = state.Topology.Processors
                        .Select(processor => new CompanionSessionCpuProcessorDto
                        {
                            Index = processor.Index,
                            CoreIndex = processor.CoreIndex,
                            Type = processor.TypeTag,
                            IsECore = processor.IsECore,
                            IsThread = processor.IsThread
                        })
                        .ToList()
                }
        };
    }

    private static CompanionSessionCpuSelectionDto? ToSelectionDto(SessionCpuSelection? selection) =>
        selection is null
            ? null
            : new CompanionSessionCpuSelectionDto
            {
                Mode = ToModeString(selection.Mode),
                Mask = selection.Mask,
                Indices = EnumerateMaskIndices(selection.Mask).ToList()
            };

    private static IEnumerable<int> EnumerateMaskIndices(long mask)
    {
        for (int index = 0; index < 64; index++)
        {
            if ((mask & (1L << index)) != 0)
            {
                yield return index;
            }
        }
    }

    private static string ToModeString(OptimizationMode mode) =>
        mode == OptimizationMode.CpuSets ? "cpu-sets" : "affinity";

    private static bool TryParseMode(string? mode, out OptimizationMode parsed)
    {
        switch (mode?.Trim().ToLowerInvariant())
        {
            case "affinity":
                parsed = OptimizationMode.Affinity;
                return true;
            case "cpu-sets":
            case "cpusets":
                parsed = OptimizationMode.CpuSets;
                return true;
            default:
                parsed = OptimizationMode.Affinity;
                return false;
        }
    }
}
