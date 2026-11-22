using System.Reflection;

namespace FrameHub.Core.Models;

public sealed class AppInfo
{
    public string Name { get; init; } = "FrameHub";
    public string Version { get; init; } = GetApplicationVersion();
    public string Tagline { get; init; } = "Windows Gaming Performance Hub";

    private static string GetApplicationVersion()
    {
        // FrameHub.Core carries the product version from version.txt (Directory.Build.props). The
        // entry assembly is the test host or another tool when FrameHub is hosted, which showed
        // e.g. "FrameHub 17.14.1" in renders.
        Assembly assembly = typeof(AppInfo).Assembly;

        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString(3)
               ?? "0.0.0";
    }
}
