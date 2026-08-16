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
    private readonly ICompanionHardwareMonitoringProvider _hardwareMonitoringProvider;

    public TelemetryController(
        ITelemetrySnapshotProvider snapshotProvider,
        WebSocketTicketStore ticketStore,
        ICompanionHardwareMonitoringProvider? hardwareMonitoringProvider = null)
    {
        _snapshotProvider = snapshotProvider;
        _ticketStore = ticketStore;
        _hardwareMonitoringProvider = hardwareMonitoringProvider ?? new NullCompanionHardwareMonitoringProvider();
    }

    [HttpGet]
    public IActionResult GetTelemetry()
    {
        var snapshot = _snapshotProvider.CurrentSnapshot;
        return Ok(snapshot);
    }

    [HttpGet("hardware-monitor")]
    public IActionResult GetHardwareMonitorStatus()
    {
        return Ok(_hardwareMonitoringProvider.GetStatus());
    }

    [HttpPost("hardware-monitor")]
    public IActionResult SetHardwareMonitorStatus([FromBody] SetHardwareMonitoringRequestDto? request)
    {
        if (request == null)
        {
            return BadRequest("Invalid request payload.");
        }

        var status = _hardwareMonitoringProvider.SetEnabled(request.Enabled);
        return Ok(status);
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
