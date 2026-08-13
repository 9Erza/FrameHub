using System.Collections.Concurrent;
using System.Security.Cryptography;
using FrameHub.Companion.Authentication;
using FrameHub.Companion.Persistence;

namespace FrameHub.Companion.Pairing;

public sealed class WebSocketTicketStore
{
    private sealed record TicketEntry(Guid DeviceId, DateTimeOffset ExpiresAtUtc);

    private readonly object _lock = new();
    private readonly Dictionary<string, TicketEntry> _ticketsByToken = new();
    private readonly Dictionary<Guid, string> _tokenByDevice = new();
    private readonly Func<DateTimeOffset> _clock;

    public WebSocketTicketStore(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public string IssueTicket(Guid deviceId, TimeSpan? ttl = null)
    {
        lock (_lock)
        {
            // Remove existing ticket for this device (at most 1 outstanding ticket per device)
            if (_tokenByDevice.TryGetValue(deviceId, out var oldToken))
            {
                _ticketsByToken.Remove(oldToken);
                _tokenByDevice.Remove(deviceId);
            }

            // Opportunistically sweep expired entries
            CleanupExpiredInternal();

            byte[] bytes = RandomNumberGenerator.GetBytes(32);
            string ticket = PairingEngine.EncodeBase64Url(bytes);
            TimeSpan lifetime = ttl ?? TimeSpan.FromSeconds(30);

            var entry = new TicketEntry(deviceId, _clock().Add(lifetime));
            _ticketsByToken[ticket] = entry;
            _tokenByDevice[deviceId] = ticket;

            return ticket;
        }
    }

    public bool TryConsumeTicket(string? ticket, DeviceRecordStore deviceStore, out Guid deviceId)
    {
        deviceId = Guid.Empty;

        if (string.IsNullOrWhiteSpace(ticket))
        {
            return false;
        }

        string cleanTicket = ticket.Trim();
        TicketEntry? entry = null;

        lock (_lock)
        {
            if (!_ticketsByToken.Remove(cleanTicket, out entry))
            {
                return false; // Not found or already consumed (one-use)
            }

            if (_tokenByDevice.TryGetValue(entry.DeviceId, out var currentToken) && currentToken == cleanTicket)
            {
                _tokenByDevice.Remove(entry.DeviceId);
            }
        }

        if (_clock() >= entry.ExpiresAtUtc)
        {
            return false; // Expired
        }

        var device = deviceStore.GetDeviceById(entry.DeviceId);
        if (device == null || !device.Scopes.Contains(CompanionScopes.ReadTelemetry, StringComparer.OrdinalIgnoreCase))
        {
            return false; // Device revoked or scope removed
        }

        deviceId = entry.DeviceId;
        return true;
    }

    public void Clear()
    {
        lock (_lock)
        {
            _ticketsByToken.Clear();
            _tokenByDevice.Clear();
        }
    }

    private void CleanupExpiredInternal()
    {
        DateTimeOffset now = _clock();
        var expiredTokens = new List<string>();

        foreach (var kvp in _ticketsByToken)
        {
            if (now >= kvp.Value.ExpiresAtUtc)
            {
                expiredTokens.Add(kvp.Key);
            }
        }

        foreach (var token in expiredTokens)
        {
            if (_ticketsByToken.Remove(token, out var entry))
            {
                if (_tokenByDevice.TryGetValue(entry.DeviceId, out var deviceToken) && deviceToken == token)
                {
                    _tokenByDevice.Remove(entry.DeviceId);
                }
            }
        }
    }
}
