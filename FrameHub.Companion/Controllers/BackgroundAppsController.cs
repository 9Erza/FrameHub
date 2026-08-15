using System.Text.RegularExpressions;
using FrameHub.Companion.Models;
using FrameHub.Companion.Providers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FrameHub.Companion.Controllers;

[ApiController]
[Route("api/v1/background-apps")]
public sealed partial class BackgroundAppsController : ControllerBase
{
    private readonly ICompanionBackgroundAppsProvider _provider;

    public BackgroundAppsController(ICompanionBackgroundAppsProvider provider)
    {
        _provider = provider;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken = default) =>
        Ok(await _provider.GetBackgroundAppsAsync(cancellationToken).ConfigureAwait(false));

    [HttpPost("{id}/start")]
    public Task<IActionResult> Start([FromRoute] string id, CancellationToken cancellationToken = default) =>
        ExecuteAsync(id, _provider.StartBackgroundAppAsync, cancellationToken);

    [HttpPost("{id}/stop")]
    public Task<IActionResult> Stop([FromRoute] string id, CancellationToken cancellationToken = default) =>
        ExecuteAsync(id, _provider.StopBackgroundAppAsync, cancellationToken);

    private async Task<IActionResult> ExecuteAsync(
        string id,
        Func<string, CancellationToken, Task<CompanionBackgroundAppOperationDto>> operation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id) || !OpaqueIdPattern().IsMatch(id))
        {
            return BadRequest(new CompanionBackgroundAppOperationDto { Success = false, ErrorCode = "invalid_id" });
        }

        CompanionBackgroundAppOperationDto result = await operation(id, cancellationToken).ConfigureAwait(false);
        if (result.Success) return Ok(result);
        return result.ErrorCode switch
        {
            "not_found" => NotFound(result),
            "operation_busy" or "benchmark_active" or "already_running" or "not_running" => Conflict(result),
            "not_eligible" or "executable_missing" => UnprocessableEntity(result),
            "launch_failed" or "stop_failed" => StatusCode(StatusCodes.Status500InternalServerError, result),
            "background_apps_provider_unavailable" => StatusCode(StatusCodes.Status503ServiceUnavailable, result),
            _ => BadRequest(result)
        };
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex OpaqueIdPattern();
}
