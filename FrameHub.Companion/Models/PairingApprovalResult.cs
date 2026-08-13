using FrameHub.Companion.Persistence;

namespace FrameHub.Companion.Models;

public enum PairingResultStatus
{
    Approved,
    Denied,
    Timeout,
    Cancelled,
    Disconnected,
    StoreFaulted,
    InvalidToken,
    NotActive
}

public sealed record PairingApprovalResult(
    PairingResultStatus Status,
    string? PlaintextCredential = null,
    PairedDeviceRecord? DeviceRecord = null
);
