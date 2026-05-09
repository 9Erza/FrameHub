using FrameHub.Core.Models;

namespace FrameHub.Core.Services;

public sealed class AppStatusService
{
    public AppInfo GetAppInfo() => new();

    public IReadOnlyList<string> GetStartupActivity()
    {
        return new[]
        {
            "Działanie FrameHub uruchomione.",
            "Monitor profili w tle aktywny.",
            "Usługi optymalizacji procesów gotowe.",
            "Monitor sprzętu pozostaje opcjonalny i wyłączony do ręcznego włączenia."
        };
    }
}
