using FrameHub.Core.Models;

namespace FrameHub.Core.Services;

public sealed class AppStatusService
{
    public AppInfo GetAppInfo() => new();

    public IReadOnlyList<string> GetStartupActivity()
    {
        return new[]
        {
            "FrameHub runtime initialized.",
            "Background profile watcher active.",
            "Process optimizer core migrated from PCO into FrameHub.Core.",
            "Hardware monitor remains opt-in and disabled until enabled."
        };
    }
}
