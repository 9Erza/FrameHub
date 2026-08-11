namespace FrameHub.Companion.Models;

public sealed record PendingPairingRequest(
    Guid RequestId,
    string DisplayName,
    string SourceIp,
    DateTimeOffset RequestedAtUtc,
    List<string> RequestedScopes
);
