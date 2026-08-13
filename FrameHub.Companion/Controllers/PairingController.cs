using FrameHub.Companion.Models;
using FrameHub.Companion.Pairing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FrameHub.Companion.Controllers;

[ApiController]
[Route("api/v1/pairing")]
public sealed class PairingController : ControllerBase
{
    private readonly PairingEngine _pairingEngine;

    public PairingController(PairingEngine pairingEngine)
    {
        _pairingEngine = pairingEngine ?? throw new ArgumentNullException(nameof(pairingEngine));
    }

    [HttpPost("request")]
    public async Task<IActionResult> RequestPairing(
        [FromBody] PairingRequestDto request,
        CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.PairingToken))
        {
            return BadRequest(new { message = "Pairing token is required." });
        }

        string sourceIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var result = await _pairingEngine.SubmitPairingRequestAsync(
            request.PairingToken,
            request.DisplayName ?? "Mobile Device",
            sourceIp,
            cancellationToken);

        return result.Status switch
        {
            PairingResultStatus.Approved => Ok(new PairingResponseDto
            {
                DeviceId = result.DeviceRecord!.Id,
                Credential = result.PlaintextCredential!,
                Scopes = result.DeviceRecord.Scopes
            }),

            PairingResultStatus.Denied => StatusCode(StatusCodes.Status403Forbidden, new { message = "Pairing request was denied on desktop." }),
            PairingResultStatus.Timeout => StatusCode(StatusCodes.Status408RequestTimeout, new { message = "Pairing request timed out." }),
            PairingResultStatus.Disconnected => BadRequest(new { message = "Client disconnected during pairing." }),
            PairingResultStatus.StoreFaulted => StatusCode(StatusCodes.Status500InternalServerError, new { message = "Paired device store is faulted." }),
            _ => BadRequest(new { message = "Invalid pairing token or pairing window expired." })
        };
    }
}
