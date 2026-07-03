using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace CouchCoopMod.CouchCoopModCode.Server;

public class WebSocketHandler
{
    private readonly ConcurrentDictionary<string, CouchSession> _clients = new();
    private readonly ConcurrentDictionary<string, CouchSession> _sessions = new();
    private readonly ConcurrentDictionary<string, CouchSession> _knownSessions = new();
    private readonly Func<CouchSession, string, Task> _onAction;
    private readonly Func<CouchSession, Task>? _onConnected;
    private readonly Func<Task>? _onSessionChanged;
    private string? _latestClientId;

    public WebSocketHandler(
        Func<CouchSession, string, Task> onAction,
        Func<CouchSession, Task>? onConnected = null,
        Func<Task>? onSessionChanged = null)
    {
        _onAction = onAction;
        _onConnected = onConnected;
        _onSessionChanged = onSessionChanged;
    }

    public int ClientCount => _clients.Count;
    public IEnumerable<CouchSession> Sessions => _sessions.Values.ToArray();

    public void ClearKnownSessionAssignments()
    {
        foreach (var session in _knownSessions.Values)
        {
            session.IsHost = false;
            session.IsSpectator = false;
            session.PlayerSlot = null;
            session.CharacterId = null;
        }
    }

    public async Task AcceptConnection(HttpListenerContext context, CancellationToken ct)
    {
        var wsContext = await context.AcceptWebSocketAsync(null);
        var ws = wsContext.WebSocket;
        var clientId = Guid.NewGuid().ToString("N")[..8];
        var session = new CouchSession
        {
            ConnectionId = clientId,
            Socket = ws
        };
        _clients[clientId] = session;
        _sessions[session.SessionId] = session;
        _knownSessions[session.SessionId] = session;
        _latestClientId = clientId;
        MainFile.Logger.Info($"WebSocket client connected: {clientId}", 0);

        try
        {
            if (_onConnected != null)
                await _onConnected(session);

            await ReceiveLoop(session, ct);
        }
        finally
        {
            _clients.TryRemove(clientId, out _);
            _sessions.TryRemove(session.SessionId, out _);
            if (ws.State == WebSocketState.Open)
            {
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); }
                catch { /* already closing */ }
            }
            MainFile.Logger.Info($"WebSocket client disconnected: {clientId}", 0);
            if (_onSessionChanged != null)
                await _onSessionChanged();
        }
    }

    public async Task SendToLatestAsync(string message)
    {
        if (_latestClientId != null
            && _clients.TryGetValue(_latestClientId, out var session)
            && session.Socket.State == WebSocketState.Open)
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            await session.Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
    }

    public async Task BroadcastAsync(string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        var segment = new ArraySegment<byte>(bytes);

        foreach (var (id, session) in _clients)
        {
            var ws = session.Socket;
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
        foreach (var (id, session) in _clients)
        {
            try { session.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down", CancellationToken.None); }
            catch { /* best effort */ }
            _clients.TryRemove(id, out _);
        }
    }

    public async Task SendAsync(CouchSession session, string message)
    {
        if (session.Socket.State != WebSocketState.Open)
            return;

        var bytes = Encoding.UTF8.GetBytes(message);
        await session.SendLock.WaitAsync();
        try
        {
            if (session.Socket.State == WebSocketState.Open)
                await session.Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        finally
        {
            session.SendLock.Release();
        }
    }

    private async Task ReceiveLoop(CouchSession session, CancellationToken ct)
    {
        var buffer = new byte[4096];
        var messageBuffer = new MemoryStream();
        var ws = session.Socket;
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;
                messageBuffer.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close)
                break;

            var message = Encoding.UTF8.GetString(messageBuffer.ToArray());
            messageBuffer.SetLength(0);
            MainFile.Logger.Info($"WS [{session.ConnectionId}]: {message}", 0);

            try
            {
                if (await TryHandleSessionMessage(session, message))
                    continue;

                await _onAction(session, message);
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"Action error: {ex.Message}", 0);
            }
        }
    }

    private async Task<bool> TryHandleSessionMessage(CouchSession session, string message)
    {
        using var doc = JsonDocument.Parse(message);
        if (!doc.RootElement.TryGetProperty("type", out var typeElem))
            return false;

        var type = typeElem.GetString();
        switch (type)
        {
            case "join":
                session.Name = ReadString(doc.RootElement, "name") ?? session.Name;
                session.CharacterId = NormalizeCharacterId(ReadString(doc.RootElement, "character_id")) ?? session.CharacterId;
                _knownSessions[session.SessionId] = session;
                await SendSessionAsync(session);
                if (_onSessionChanged != null) await _onSessionChanged();
                return true;
            case "claim_slot":
                if (doc.RootElement.TryGetProperty("slot", out var slotElem) && slotElem.TryGetInt32(out var slot) && slot >= 0)
                {
                    var claimedByOther = _sessions.Values.Any(s =>
                        s.SessionId != session.SessionId
                        && s.PlayerSlot == slot
                        && !s.IsSpectator);
                    if (claimedByOther)
                    {
                        await SendAsync(session, "{\"type\":\"error\",\"message\":\"That player slot is already claimed.\"}");
                        return true;
                    }

                    session.PlayerSlot = slot;
                    session.CharacterId = NormalizeCharacterId(ReadString(doc.RootElement, "character_id")) ?? session.CharacterId;
                    session.IsSpectator = false;
                    _knownSessions[session.SessionId] = session;
                    await SendSessionAsync(session);
                    if (_onSessionChanged != null) await _onSessionChanged();
                }
                return true;
            case "rejoin":
                var sessionId = ReadString(doc.RootElement, "session_id");
                if (!string.IsNullOrWhiteSpace(sessionId) && _knownSessions.TryGetValue(sessionId, out var existing))
                {
                    _sessions.TryRemove(session.SessionId, out _);
                    session.SessionId = existing.SessionId;
                    session.Name = existing.Name;
                    session.PlayerSlot = existing.PlayerSlot;
                    session.CharacterId = existing.CharacterId;
                    session.IsHost = existing.IsHost;
                    session.IsSpectator = existing.IsSpectator;
                    _sessions[session.SessionId] = session;
                    _knownSessions[session.SessionId] = session;
                }
                await SendSessionAsync(session);
                if (_onSessionChanged != null) await _onSessionChanged();
                return true;
            case "spectate":
                session.IsSpectator = true;
                session.PlayerSlot = null;
                session.IsHost = false;
                _knownSessions[session.SessionId] = session;
                await SendSessionAsync(session);
                if (_onSessionChanged != null) await _onSessionChanged();
                return true;
            case "action":
                if (session.IsSpectator || session.PlayerSlot == null)
                {
                    await SendAsync(session, "{\"type\":\"error\",\"message\":\"Claim a player slot before acting.\"}");
                    return true;
                }
                await _onAction(session, StripMessageType(doc.RootElement));
                return true;
            default:
                return false;
        }
    }

    private async Task SendSessionAsync(CouchSession session)
    {
        var payload = new
        {
            type = "session",
            session_id = session.SessionId,
            player_slot = session.PlayerSlot,
            character_id = session.CharacterId,
            is_host = session.IsHost,
            role = session.IsSpectator ? "spectator" : "player",
            name = session.Name
        };
        await SendAsync(session, JsonSerializer.Serialize(payload));
    }

    private static string? ReadString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) ? value.GetString() : null;
    }

    private static string? NormalizeCharacterId(string? characterId)
    {
        var trimmed = characterId?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        return trimmed[..Math.Min(trimmed.Length, 64)];
    }

    private static string StripMessageType(JsonElement element)
    {
        var data = new Dictionary<string, object?>();
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals("type"))
                continue;

            data[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number when property.Value.TryGetInt32(out var intValue) => intValue,
                JsonValueKind.Number when property.Value.TryGetDouble(out var doubleValue) => doubleValue,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => JsonSerializer.Deserialize<object?>(property.Value.GetRawText())
            };
        }

        return JsonSerializer.Serialize(data);
    }
}
