using System.Net.WebSockets;
using System.Text.Json;
using FrameHub.Companion.Authentication;
using FrameHub.Companion.Pairing;
using FrameHub.Companion.Persistence;
using FrameHub.Companion.Providers;
using FrameHub.Core.Services;
using Microsoft.AspNetCore.Http;

namespace FrameHub.Companion.Handlers;

public static class TelemetryWebSocketHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static async Task HandleWebSocketRequestAsync(
        HttpContext context,
        WebSocketTicketStore ticketStore,
        DeviceRecordStore deviceStore,
        ITelemetrySnapshotProvider snapshotProvider,
        Func<IHardwareMonitorLease>? acquireLease = null)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("WebSocket request expected.");
            return;
        }

        // Subprotocol parsing: Sec-WebSocket-Protocol: framehub.v1, ticket.<ticket>
        string? requestedProtocols = context.Request.Headers["Sec-WebSocket-Protocol"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(requestedProtocols))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Sec-WebSocket-Protocol header required.");
            return;
        }

        var tokens = requestedProtocols.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool hasFrameHubProtocol = tokens.Any(t => t.Equals("framehub.v1", StringComparison.OrdinalIgnoreCase));
        string? ticketToken = tokens.FirstOrDefault(t => t.StartsWith("ticket.", StringComparison.OrdinalIgnoreCase));

        if (!hasFrameHubProtocol || string.IsNullOrWhiteSpace(ticketToken))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Missing required subprotocol or ticket.");
            return;
        }

        string rawTicket = ticketToken.Substring("ticket.".Length).Trim();
        if (!ticketStore.TryConsumeTicket(rawTicket, deviceStore, out Guid deviceId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Invalid, expired, or reused WebSocket ticket.");
            return;
        }

        // Accept WebSocket with non-secret subprotocol ONLY
        WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync("framehub.v1").ConfigureAwait(false);

        // Acquire hardware monitor lease for the duration of this WS connection
        using IHardwareMonitorLease? lease = acquireLease?.Invoke();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        var cancellationToken = cts.Token;

        var receiveTask = Task.Run(() => RunReceiveLoopAsync(webSocket, cts, cancellationToken));
        var sendTask = Task.Run(() => RunSendLoopAsync(webSocket, deviceId, deviceStore, snapshotProvider, cts, cancellationToken));

        await Task.WhenAll(receiveTask, sendTask).ConfigureAwait(false);

        if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived)
        {
            try
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Completed", closeCts.Token).ConfigureAwait(false);
            }
            catch
            {
                // Ignore cleanup socket exception
            }
        }
    }

    private static async Task RunReceiveLoopAsync(WebSocket webSocket, CancellationTokenSource cts, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];

        try
        {
            while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    cts.Cancel();
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text || result.MessageType == WebSocketMessageType.Binary)
                {
                    // Strict policy violation on any client application message
                    try
                    {
                        using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        await webSocket.CloseOutputAsync(
                            WebSocketCloseStatus.PolicyViolation,
                            "Client application messages are not allowed.",
                            closeCts.Token).ConfigureAwait(false);
                    }
                    catch { }

                    cts.Cancel();
                    break;
                }
            }
        }
        catch
        {
            cts.Cancel();
        }
    }

    private static async Task RunSendLoopAsync(
        WebSocket webSocket,
        Guid deviceId,
        DeviceRecordStore deviceStore,
        ITelemetrySnapshotProvider snapshotProvider,
        CancellationTokenSource cts,
        CancellationToken cancellationToken)
    {
        try
        {
            while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                // Active revalidation: DeviceId must exist and retain read:telemetry
                var device = deviceStore.GetDeviceById(deviceId);
                if (device == null || !device.Scopes.Contains(CompanionScopes.ReadTelemetry, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        await webSocket.CloseOutputAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Telemetry scope revoked.",
                            closeCts.Token).ConfigureAwait(false);
                    }
                    catch { }

                    cts.Cancel();
                    break;
                }

                var snapshot = snapshotProvider.CurrentSnapshot;
                byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);

                // Per-send timeout: bounded to 2 seconds to prevent dead/slow clients from stalling revocation checks
                using (var sendCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    sendCts.CancelAfter(TimeSpan.FromSeconds(2));
                    await webSocket.SendAsync(
                        new ArraySegment<byte>(jsonBytes),
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        sendCts.Token).ConfigureAwait(false);
                }

                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            cts.Cancel();
        }
    }
}
