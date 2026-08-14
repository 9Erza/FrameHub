using FrameHub.Companion.Models;
using FrameHub.Companion.Providers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FrameHub.Companion.Controllers;

[ApiController]
[Route("api/v1/session-optimization")]
public sealed class SessionOptimizationController : ControllerBase
{
    private readonly ICompanionSessionOptimizationProvider? _provider;

    public SessionOptimizationController(IServiceProvider serviceProvider)
    {
        _provider = serviceProvider.GetService(typeof(ICompanionSessionOptimizationProvider)) as ICompanionSessionOptimizationProvider;
    }

    [HttpGet]
    public async Task<IActionResult> GetState(CancellationToken cancellationToken = default)
    {
        if (_provider == null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new CompanionOptimizationResultDto
            {
                Success = false,
                ErrorCode = "optimization_provider_unavailable"
            });
        }

        var state = await _provider.GetStateAsync(cancellationToken).ConfigureAwait(false);
        return Ok(state);
    }

    [HttpPost("apply")]
    public async Task<IActionResult> Apply(CancellationToken cancellationToken = default)
    {
        if (_provider == null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new CompanionOptimizationResultDto
            {
                Success = false,
                ErrorCode = "optimization_provider_unavailable"
            });
        }

        var result = await _provider.ApplyOptimizationAsync(cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            return Ok(result);
        }

        return result.ErrorCode switch
        {
            "no_game" or "not_running" => StatusCode(StatusCodes.Status422UnprocessableEntity, result),
            "already_active" or "benchmark_active" or "operation_in_progress" => StatusCode(StatusCodes.Status409Conflict, result),
            "apply_failed" or "state_persist_failed" => StatusCode(StatusCodes.Status500InternalServerError, result),
            _ => StatusCode(StatusCodes.Status400BadRequest, result)
        };
    }

    [HttpPost("restore")]
    public async Task<IActionResult> Restore(CancellationToken cancellationToken = default)
    {
        if (_provider == null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new CompanionOptimizationResultDto
            {
                Success = false,
                ErrorCode = "optimization_provider_unavailable"
            });
        }

        var result = await _provider.RestoreSessionAsync(cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            return Ok(result);
        }

        return result.ErrorCode switch
        {
            "not_active" or "benchmark_active" or "operation_in_progress" or "restore_partial" or "restore_manual_required" => StatusCode(StatusCodes.Status409Conflict, result),
            "restore_failed" or "state_persist_failed" or "state_clear_failed" => StatusCode(StatusCodes.Status500InternalServerError, result),
            _ => StatusCode(StatusCodes.Status400BadRequest, result)
        };
    }
}
