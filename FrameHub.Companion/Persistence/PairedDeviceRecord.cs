using FrameHub.Companion.Authentication;

namespace FrameHub.Companion.Persistence;

public sealed record PairedDeviceRecord
{
    private readonly string[] _scopes = new[] { CompanionScopes.ReadStatus };

    public Guid Id { get; init; } = Guid.NewGuid();
    public string DisplayName { get; init; } = string.Empty;
    public string CredentialHash { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsedAtUtc { get; init; }

    public IReadOnlyList<string> Scopes
    {
        get => (string[])_scopes.Clone();
        init => _scopes = (value ?? new[] { CompanionScopes.ReadStatus }).Select(s => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
