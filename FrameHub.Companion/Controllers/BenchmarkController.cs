using FrameHub.Companion.Models;
using FrameHub.Companion.Providers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FrameHub.Companion.Controllers;

[ApiController]
[Route("api/v1/benchmarks")]
public sealed class BenchmarkController : ControllerBase
{
    private readonly ICompanionBenchmarkProvider? _benchmarkProvider;

    public BenchmarkController(IServiceProvider serviceProvider)
    {
        _benchmarkProvider = serviceProvider.GetService(typeof(ICompanionBenchmarkProvider)) as ICompanionBenchmarkProvider;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        if (_benchmarkProvider == null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new CompanionBenchmarkErrorDto
            {
                ErrorCode = "benchmark_provider_unavailable",
                Message = "Benchmark provider service is not configured on this server."
            });
        }

        var status = _benchmarkProvider.GetStatus();
        return Ok(status);
    }

    [HttpGet("targets")]
    public IActionResult GetTargets()
    {
        if (_benchmarkProvider == null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new CompanionBenchmarkErrorDto
            {
                ErrorCode = "benchmark_provider_unavailable",
                Message = "Benchmark provider service is not configured on this server."
            });
        }

        var targets = _benchmarkProvider.GetEligibleTargets();
        return Ok(targets);
    }

    [HttpGet("history")]
    public IActionResult GetHistory([FromQuery] int limit = 20)
    {
        if (_benchmarkProvider == null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new CompanionBenchmarkErrorDto
            {
                ErrorCode = "benchmark_provider_unavailable",
                Message = "Benchmark provider service is not configured on this server."
            });
        }

        if (limit < 1 || limit > 100)
        {
            return BadRequest(new CompanionBenchmarkErrorDto
            {
                ErrorCode = "invalid_limit",
                Message = "Limit parameter must be between 1 and 100."
            });
        }

        var history = _benchmarkProvider.GetHistory(limit);
        return Ok(history);
    }

    [HttpGet("history/compare")]
    public IActionResult Compare([FromQuery] string? sessionA, [FromQuery] string? sessionB)
    {
        if (_benchmarkProvider == null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new CompanionBenchmarkErrorDto
            {
                ErrorCode = "benchmark_provider_unavailable",
                Message = "Benchmark provider service is not configured on this server."
            });
        }

        if (string.IsNullOrWhiteSpace(sessionA) || !Guid.TryParse(sessionA, out Guid sessionAGuid) ||
            string.IsNullOrWhiteSpace(sessionB) || !Guid.TryParse(sessionB, out Guid sessionBGuid))
        {
            return BadRequest(new CompanionBenchmarkErrorDto
            {
                ErrorCode = "invalid_session_ids",
                Message = "Both sessionA and sessionB query parameters must be valid GUIDs."
            });
        }

        try
        {
            var comparison = _benchmarkProvider.CompareHistorySessions(sessionAGuid, sessionBGuid);
            return Ok(comparison);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new CompanionBenchmarkErrorDto
            {
                ErrorCode = "session_not_found",
                Message = ex.Message
            });
        }
        catch (FrameHub.Core.Models.Benchmarking.BenchmarkException ex)
        {
            return BadRequest(new CompanionBenchmarkErrorDto
            {
                ErrorCode = ex.Code,
                Message = ex.Message
            });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new CompanionBenchmarkErrorDto
            {
                ErrorCode = "comparison_error",
                Message = "Failed to perform benchmark session comparison."
            });
        }
    }

    [HttpGet("history/{sessionId}")]
    public IActionResult GetHistoryDetail([FromRoute] string sessionId)
    {
        if (_benchmarkProvider == null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new CompanionBenchmarkErrorDto
            {
                ErrorCode = "benchmark_provider_unavailable",
                Message = "Benchmark provider service is not configured on this server."
            });
        }

        if (!Guid.TryParse(sessionId, out Guid sessionGuid))
        {
            return BadRequest(new CompanionBenchmarkErrorDto
            {
                ErrorCode = "invalid_session_id",
                Message = "SessionId must be a valid GUID."
            });
        }

        var detail = _benchmarkProvider.GetHistoryDetail(sessionGuid);
        if (detail == null)
        {
            return NotFound(new CompanionBenchmarkErrorDto
            {
                ErrorCode = "session_not_found",
                Message = $"Benchmark session '{sessionGuid}' was not found."
            });
        }

        return Ok(detail);
    }

    [HttpGet("history/{sessionId}/chart")]
    public IActionResult GetHistoryChart([FromRoute] string sessionId, [FromQuery] int buckets = 200)
    {
        if (_benchmarkProvider == null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new CompanionBenchmarkErrorDto
            {
                ErrorCode = "benchmark_provider_unavailable",
                Message = "Benchmark provider service is not configured on this server."
            });
        }

        if (!Guid.TryParse(sessionId, out Guid sessionGuid))
        {
            return BadRequest(new CompanionBenchmarkErrorDto
            {
                ErrorCode = "invalid_session_id",
                Message = "SessionId must be a valid GUID."
            });
        }

        if (buckets < 10 || buckets > 1000)
        {
            return BadRequest(new CompanionBenchmarkErrorDto
            {
                ErrorCode = "invalid_buckets",
                Message = "Buckets parameter must be between 10 and 1000."
            });
        }

        var chart = _benchmarkProvider.GetHistoryChart(sessionGuid, buckets);
        if (chart == null)
        {
            return NotFound(new CompanionBenchmarkErrorDto
            {
                ErrorCode = "chart_unavailable",
                Message = $"Chart data for session '{sessionGuid}' was not found or is unreadable."
            });
        }

        return Ok(chart);
    }

    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] CompanionBenchmarkStartRequestDto request)
    {
        if (_benchmarkProvider == null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new CompanionBenchmarkErrorDto
            {
                ErrorCode = "benchmark_provider_unavailable",
                Message = "Benchmark provider service is not configured on this server."
            });
        }

        if (request == null || string.IsNullOrWhiteSpace(request.TargetId))
        {
            return BadRequest(new CompanionBenchmarkErrorDto
            {
                ErrorCode = "invalid_target",
                Message = "TargetId must be specified."
            });
        }

        if (request.DurationSeconds <= 0)
        {
            return BadRequest(new CompanionBenchmarkErrorDto
            {
                ErrorCode = "invalid_duration",
                Message = "DurationSeconds must be greater than zero."
            });
        }

        if (request.CountdownSeconds < 0)
        {
            return BadRequest(new CompanionBenchmarkErrorDto
            {
                ErrorCode = "invalid_countdown",
                Message = "CountdownSeconds cannot be negative."
            });
        }

        var result = await _benchmarkProvider.StartBenchmarkAsync(request).ConfigureAwait(false);

        if (!result.Accepted)
        {
            if (string.Equals(result.ErrorCode, "already_running", StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(StatusCodes.Status409Conflict, new CompanionBenchmarkErrorDto
                {
                    ErrorCode = "already_running",
                    Message = "A benchmark capture is already in progress."
                });
            }

            return BadRequest(new CompanionBenchmarkErrorDto
            {
                ErrorCode = result.ErrorCode ?? "start_rejected",
                Message = $"Benchmark start request rejected: {result.ErrorCode}"
            });
        }

        return StatusCode(StatusCodes.Status202Accepted, result);
    }

    [HttpPost("stop")]
    public async Task<IActionResult> Stop()
    {
        if (_benchmarkProvider == null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new CompanionBenchmarkErrorDto
            {
                ErrorCode = "benchmark_provider_unavailable",
                Message = "Benchmark provider service is not configured on this server."
            });
        }

        var result = await _benchmarkProvider.StopBenchmarkAsync().ConfigureAwait(false);
        return Ok(result);
    }
}
