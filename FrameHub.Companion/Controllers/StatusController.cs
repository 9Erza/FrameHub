using Microsoft.AspNetCore.Mvc;
using FrameHub.Companion.Models;
using FrameHub.Companion.Providers;

namespace FrameHub.Companion.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public sealed class StatusController : ControllerBase
{
    private readonly ICompanionPresentationPreferencesProvider _preferencesProvider;

    public StatusController(ICompanionPresentationPreferencesProvider? preferencesProvider = null)
    {
        _preferencesProvider = preferencesProvider ?? new NullCompanionPresentationPreferencesProvider();
    }

    [HttpGet]
    public ActionResult<CompanionStatusDto> GetStatus()
    {
        string rawLang = _preferencesProvider.DesktopLanguage;
        string normalizedLang = string.Equals(rawLang, "pl", StringComparison.OrdinalIgnoreCase) ? "pl" : "en";
        return Ok(new CompanionStatusDto
        {
            DesktopLanguage = normalizedLang
        });
    }
}
