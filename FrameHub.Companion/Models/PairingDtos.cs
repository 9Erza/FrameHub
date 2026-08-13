using System.Text.Json.Serialization;

namespace FrameHub.Companion.Models;

public sealed record PairingRequestDto
{
    [JsonPropertyName("pairingToken")]
    public string PairingToken { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }
}

public sealed record PairingResponseDto
{
    [JsonPropertyName("deviceId")]
    public Guid DeviceId { get; init; }

    [JsonPropertyName("credential")]
    public string Credential { get; init; } = string.Empty;

    [JsonPropertyName("scopes")]
    public IReadOnlyList<string> Scopes { get; init; } = new List<string>();
}
