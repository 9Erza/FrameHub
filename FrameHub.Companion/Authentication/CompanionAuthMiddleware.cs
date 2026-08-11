using System.Net;
using System.Text.Json;
using FrameHub.Companion.Pairing;
using FrameHub.Companion.Persistence;
using FrameHub.Companion.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace FrameHub.Companion.Authentication;

public sealed class CompanionAuthMiddleware
{
    private readonly RequestDelegate _next;

    public CompanionAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        DeviceRecordStore deviceStore,
        PairingEngine pairingEngine,
        CompanionOptions options,
        PairingRateLimiter rateLimiter)
    {
        // 1. Host Header Validation
        string hostHeader = context.Request.Host.Value ?? string.Empty;
        if (!IsHostAllowed(hostHeader, options))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Invalid Host header.");
            return;
        }

        string path = context.Request.Path.Value?.TrimEnd('/') ?? string.Empty;
        IPAddress? localIp = context.Connection.LocalIpAddress;
        IPAddress? remoteIp = context.Connection.RemoteIpAddress;
        bool isLoopbackLocal = localIp != null && IPAddress.IsLoopback(localIp);
        bool isLoopbackRemote = remoteIp != null && IPAddress.IsLoopback(remoteIp);

        // 2. Pairing Request Endpoint
        if (path.Equals("/api/v1/pairing/request", StringComparison.OrdinalIgnoreCase))
        {
            var sessionStatus = pairingEngine.GetCurrentStatus();
            if (!sessionStatus.IsActive)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Pairing window is not active.");
                return;
            }

            string clientIpStr = remoteIp?.ToString() ?? "unknown";
            if (rateLimiter.IsRateLimited(clientIpStr))
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.Response.WriteAsync("Too many pairing attempts.");
                return;
            }

            await _next(context);
            return;
        }

        // 3. Status Endpoint
        if (path.Equals("/api/v1/status", StringComparison.OrdinalIgnoreCase))
        {
            // Unauthenticated ONLY on 127.0.0.1 / loopback
            if (isLoopbackLocal && isLoopbackRemote)
            {
                await _next(context);
                return;
            }

            // On LAN: Authentication is REQUIRED with read:status scope
            if (!TryAuthenticateBearer(context, deviceStore, out var device, out var authErrorStatusCode))
            {
                context.Response.StatusCode = authErrorStatusCode;
                return;
            }

            if (!device.Scopes.Contains("read:status", StringComparer.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            deviceStore.UpdateLastUsed(device.Id, DateTimeOffset.UtcNow);
            context.Items["PairedDevice"] = device;
            await _next(context);
            return;
        }

        // 4. Default for any other endpoint: Require Authentication
        if (!TryAuthenticateBearer(context, deviceStore, out var authenticatedDevice, out var defaultErrorStatusCode))
        {
            context.Response.StatusCode = defaultErrorStatusCode;
            return;
        }

        deviceStore.UpdateLastUsed(authenticatedDevice.Id, DateTimeOffset.UtcNow);
        context.Items["PairedDevice"] = authenticatedDevice;
        await _next(context);
    }

    private static bool IsHostAllowed(string hostHeader, CompanionOptions options)
    {
        if (string.IsNullOrWhiteSpace(hostHeader)) return false;

        string hostWithoutPort = hostHeader;
        int colonIdx = hostHeader.IndexOf(':');
        if (colonIdx >= 0)
        {
            hostWithoutPort = hostHeader.Substring(0, colonIdx);
        }

        // Always allow 127.0.0.1 and localhost
        if (hostWithoutPort.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            hostWithoutPort.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // If LAN enabled and selected LAN IPv4 set, allow exact LAN IPv4
        if (options.LanEnabled && !string.IsNullOrWhiteSpace(options.LanAddress))
        {
            if (hostWithoutPort.Equals(options.LanAddress.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryAuthenticateBearer(
        HttpContext context,
        DeviceRecordStore deviceStore,
        out PairedDeviceRecord device,
        out int statusCode)
    {
        device = null!;
        statusCode = StatusCodes.Status401Unauthorized;

        string authHeader = context.Request.Headers["Authorization"].FirstOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string token = authHeader.Substring("Bearer ".Length).Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (deviceStore.IsFaulted)
        {
            return false;
        }

        string hash = PairingEngine.HashCredential(token);
        var found = deviceStore.FindByCredentialHash(hash);
        if (found == null)
        {
            return false;
        }

        device = found;
        return true;
    }
}
