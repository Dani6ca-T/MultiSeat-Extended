using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MultiSeat.Shared.Models;

namespace MultiSeat.Service.Api;

/// <summary>
/// Lightweight WebSocket hub that pushes seat state changes to connected dashboard clients.
/// </summary>
public static class WebSocketHub
{
    private static readonly ConcurrentDictionary<string, WebSocket> _clients = new();

    public static void Map(WebApplication app)
    {
        app.Map("/ws/seats", async (HttpContext context) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                return;
            }

            var ws = await context.WebSockets.AcceptWebSocketAsync();
            var clientId = Guid.NewGuid().ToString("N");
            _clients.TryAdd(clientId, ws);

            try
            {
                // Keep connection alive — read loop (we only push, but must drain client frames)
                var buffer = new byte[256];
                while (ws.State == WebSocketState.Open)
                {
                    var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                        break;
                }
            }
            finally
            {
                _clients.TryRemove(clientId, out _);
                if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            }
        });
    }

    /// <summary>
    /// Broadcast a seat state change to all connected WebSocket clients.
    /// Called by SeatManager whenever a seat transitions state.
    /// </summary>
    public static async Task BroadcastSeatUpdateAsync(SeatInfo seat)
    {
        var json = JsonSerializer.Serialize(seat,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var bytes = Encoding.UTF8.GetBytes(json);

        foreach (var (id, ws) in _clients)
        {
            if (ws.State != WebSocketState.Open)
            {
                _clients.TryRemove(id, out _);
                continue;
            }

            try
            {
                await ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true,
                    CancellationToken.None);
            }
            catch
            {
                _clients.TryRemove(id, out _);
            }
        }
    }
}
