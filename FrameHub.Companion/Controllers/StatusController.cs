using Microsoft.AspNetCore.Mvc;
using FrameHub.Companion.Models;

namespace FrameHub.Companion.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public sealed class StatusController : ControllerBase
{
    [HttpGet]
    public ActionResult<CompanionStatusDto> GetStatus()
    {
        return Ok(new CompanionStatusDto());
    }
}
