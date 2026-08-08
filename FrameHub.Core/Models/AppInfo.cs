using System.Reflection;

namespace FrameHub.Core.Models;

public sealed class AppInfo
{
    public string Name { get; init; } = "FrameHub";
    public string Version { get; init; } = GetApplicationVersion();
    public string Tagline { get; init; } = "Windows Gaming Performance Hub";

    private static string GetApplicationVersion()
    {
        Assembly assembly = Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;

        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString(3)
               ?? "0.0.0";
    }
}
