using FrameHub.Companion.Authentication;
using FrameHub.Companion.Models;
using FrameHub.Companion.Pairing;
using FrameHub.Companion.Persistence;
using FrameHub.Companion.Providers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FrameHub.Companion.Controllers;

[ApiController]
[Route("api/v1/telemetry")]
public sealed class TelemetryController : ControllerBase
{
    private readonly ITelemetrySnapshotProvider _snapshotProvider;
    private readonly WebSocketTicketStore _ticketStore;

    public TelemetryController(
        ITelemetrySnapshotProvider snapshotProvider,
        WebSocketTicketStore ticketStore)
    {
        _snapshotProvider = snapshotProvider;
        _ticketStore = ticketStore;
    }

    [HttpGet]
    public IActionResult GetTelemetry()
    {
        var snapshot = _snapshotProvider.CurrentSnapshot;
        return Ok(snapshot);
    }

    [HttpPost("ws-ticket")]
    public IActionResult CreateWebSocketTicket()
    {
        // Must be authenticated paired device with read:telemetry scope (handled in middleware)
        if (HttpContext.Items["PairedDevice"] is not PairedDeviceRecord device)
        {
            return Unauthorized("Authentication required.");
        }

        if (!device.Scopes.Contains(CompanionScopes.ReadTelemetry, StringComparer.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "read:telemetry scope required.");
        }

        string ticket = _ticketStore.IssueTicket(device.Id, TimeSpan.FromSeconds(30));
        return Ok(new WebSocketTicketResponseDto(ticket, 30));
    }
}
