namespace FrameHub.Companion.Persistence;

public sealed record PairedDeviceRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string DisplayName { get; init; } = string.Empty;
    public string CredentialHash { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsedAtUtc { get; init; }
    public List<string> Scopes { get; init; } = new() { "read:status" };
}
