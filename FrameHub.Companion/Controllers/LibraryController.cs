using FrameHub.Companion.Models;
using FrameHub.Companion.Providers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FrameHub.Companion.Controllers;

[ApiController]
[Route("api/v1/library")]
public sealed class LibraryController : ControllerBase
{
    private readonly ICompanionLibraryProvider? _libraryProvider;

    public LibraryController(IServiceProvider serviceProvider)
    {
        _libraryProvider = serviceProvider.GetService(typeof(ICompanionLibraryProvider)) as ICompanionLibraryProvider;
    }

    [HttpGet]
    public async Task<IActionResult> GetLibrary(CancellationToken cancellationToken = default)
    {
        if (_libraryProvider == null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new CompanionLaunchResultDto
            {
                Success = false,
                ErrorCode = "library_provider_unavailable"
            });
        }

        var items = await _libraryProvider.GetLibraryItemsAsync(cancellationToken).ConfigureAwait(false);
        return Ok(items);
    }

    [HttpPost("{id}/launch")]
    public async Task<IActionResult> Launch([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        if (_libraryProvider == null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new CompanionLaunchResultDto
            {
                Success = false,
                ErrorCode = "library_provider_unavailable"
            });
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new CompanionLaunchResultDto
            {
                Success = false,
                ErrorCode = "not_found"
            });
        }

        var result = await _libraryProvider.LaunchItemAsync(id, cancellationToken).ConfigureAwait(false);

        if (result.Success)
        {
            return Ok(result);
        }

        return result.ErrorCode switch
        {
            "not_found" => NotFound(result),
            "already_running" or "benchmark_active" or "launch_in_progress" => StatusCode(StatusCodes.Status409Conflict, result),
            "not_launchable" or "executable_missing" => StatusCode(StatusCodes.Status422UnprocessableEntity, result),
            "launch_failed" => StatusCode(StatusCodes.Status500InternalServerError, result),
            _ => StatusCode(StatusCodes.Status400BadRequest, result)
        };
    }
}
