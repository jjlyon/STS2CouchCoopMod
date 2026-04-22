using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace CouchCoopMod.CouchCoopModCode.Server;

public class WebSocketHandler
{
    private readonly ConcurrentDictionary<string, WebSocket> _clients = new();

    public int ClientCount => _clients.Count;

    public async Task AcceptConnection(HttpListenerContext context, CancellationToken ct)
    {
        var wsContext = await context.AcceptWebSocketAsync(null);
        var ws = wsContext.WebSocket;
        var clientId = Guid.NewGuid().ToString("N")[..8];
        _clients[clientId] = ws;
        MainFile.Logger.Log($"WebSocket client connected: {clientId}");

        try
        {
            await ReceiveLoop(clientId, ws, ct);
        }
        finally
        {
            _clients.TryRemove(clientId, out _);
            if (ws.State == WebSocketState.Open)
            {
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); }
                catch { /* already closing */ }
            }
            MainFile.Logger.Log($"WebSocket client disconnected: {clientId}");
        }
    }

    public async Task BroadcastAsync(string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        var segment = new ArraySegment<byte>(bytes);

        foreach (var (id, ws) in _clients)
        {
            if (ws.State != WebSocketState.Open)
            {
                _clients.TryRemove(id, out _);
                continue;
            }
            try
            {
                await ws.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch
            {
                _clients.TryRemove(id, out _);
            }
        }
    }

    public void CloseAll()
    {
        foreach (var (id, ws) in _clients)
        {
            try { ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down", CancellationToken.None); }
            catch { /* best effort */ }
            _clients.TryRemove(id, out _);
        }
    }

    private async Task ReceiveLoop(string clientId, WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[4096];
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
                break;

            var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
            MainFile.Logger.Log($"WS [{clientId}]: {message}");

            var echo = Encoding.UTF8.GetBytes($"echo: {message}");
            await ws.SendAsync(new ArraySegment<byte>(echo), WebSocketMessageType.Text, true, ct);
        }
    }
}
