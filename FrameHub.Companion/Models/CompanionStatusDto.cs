using System.Text.Json.Serialization;
using FrameHub.Core.Models;

namespace FrameHub.Companion.Models;

public sealed record CompanionStatusDto
{
    [JsonPropertyName("service")]
    public string Service { get; init; } = "FrameHub Companion";

    [JsonPropertyName("apiVersion")]
    public string ApiVersion { get; init; } = "1";

    [JsonPropertyName("appVersion")]
    public string AppVersion { get; init; } = new AppInfo().Version;

    [JsonPropertyName("state")]
    public string State { get; init; } = "ready";

    [JsonPropertyName("desktopLanguage")]
    public string DesktopLanguage { get; init; } = "en";
}
