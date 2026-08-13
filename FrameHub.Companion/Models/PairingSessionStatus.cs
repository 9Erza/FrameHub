namespace FrameHub.Companion.Models;

public sealed record PairingSessionStatus
{
    public bool IsActive { get; init; }
    public string? PairingToken { get; init; }
    public string? PairingUrl { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public PendingPairingRequest? PendingRequest { get; init; }
}
