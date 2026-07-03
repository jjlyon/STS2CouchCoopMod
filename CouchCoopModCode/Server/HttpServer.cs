using System.Net;
using System.Reflection;
using System.Text.Json;
using CouchCoopMod.CouchCoopModCode.Couch;

namespace CouchCoopMod.CouchCoopModCode.Server;

public class HttpServer
{
    private HttpListener _listener = new();
    private readonly int _port;
    private readonly WebSocketHandler _wsHandler;
    private readonly GameStateProxy _proxy = new();
    private CancellationTokenSource? _cts;
    private bool _couchSessionInitialized;
    private string? _hostSessionId;
    private int _couchPlayerCount = 2;

    public HttpServer(int port = 8080)
    {
        _port = port;
        _wsHandler = new WebSocketHandler(HandleAction, SendInitialState, BroadcastLobbyAsync);
    }

    public bool IsPubliclyReachable { get; private set; }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        try
        {
            _listener.Prefixes.Add($"http://*:{_port}/");
            _listener.Start();
            IsPubliclyReachable = true;
        }
        catch (HttpListenerException)
        {
            _listener.Close();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{_port}/");
            _listener.Start();
            IsPubliclyReachable = false;
            MainFile.Logger.Info("Could not bind to all interfaces, falling back to localhost only", 0);
        }
        MainFile.Logger.Info($"HTTP server listening on port {_port}", 0);
        Task.Run(() => ListenLoop(_cts.Token));
        Task.Run(() => PollStateLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _wsHandler.CloseAll();
        _listener.Stop();
        _listener.Close();
    }

    private async Task HandleAction(CouchSession session, string actionJson)
    {
        if (await TryHandleLocalCouchCommand(session, actionJson))
            return;

        var (success, response) = await _proxy.ExecuteActionAsync(actionJson, session.PlayerSlot);
        if (!success)
            MainFile.Logger.Warn($"Action failed: {response}", 0);

        await Task.Delay(150);
        await BroadcastPersonalStatesAsync();
        _ = BroadcastPersonalStateFollowupsAsync();
    }

    private async Task SendInitialState(CouchSession session)
    {
        await SendLobbyAsync(session);
        var state = await _proxy.GetStateAsync(session.PlayerSlot);
        if (state != null)
            await _wsHandler.SendAsync(session, WrapState(state, session));
    }

    private async Task PollStateLoop(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var delay = TimeSpan.FromMilliseconds(500);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(delay, ct);
                if (_wsHandler.ClientCount == 0) continue;

                await BroadcastPersonalStatesAsync();
                delay = TimeSpan.FromMilliseconds(500);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"Poll error: {ex.Message}", 0);
                delay = TimeSpan.FromSeconds(2);
            }
        }
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = HandleRequest(context, ct);
            }
            catch when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"HTTP error: {ex.Message}", 0);
            }
        }
    }

    private async Task HandleRequest(HttpListenerContext context, CancellationToken ct)
    {
        if (context.Request.IsWebSocketRequest)
        {
            await _wsHandler.AcceptConnection(context, ct);
            return;
        }

        var path = context.Request.Url?.AbsolutePath ?? "/";
        var (content, contentType) = path switch
        {
            "/" or "/index.html" => (LoadResource("index.html"), "text/html"),
            "/app.js" => (LoadResource("app.js"), "application/javascript"),
            "/styles.css" => (LoadResource("styles.css"), "text/css"),
            _ => ((string?)null, "text/plain")
        };

        if (content == null)
        {
            context.Response.StatusCode = 404;
            context.Response.Close();
            return;
        }

        context.Response.ContentType = contentType;
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, ct);
        context.Response.Close();
    }

    private static string? LoadResource(string filename)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith($".{filename}", StringComparison.Ordinal));
        if (name == null) return null;
        using var stream = assembly.GetManifestResourceStream(name);
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private async Task BroadcastPersonalStatesAsync()
    {
        foreach (var session in _wsHandler.Sessions)
        {
            var state = await _proxy.GetStateAsync(session.PlayerSlot);
            if (state != null)
                await _wsHandler.SendAsync(session, WrapState(state, session));
        }
    }

    private async Task BroadcastPersonalStateFollowupsAsync()
    {
        try
        {
            foreach (var delay in new[] { 500, 1000, 2000 })
            {
                await Task.Delay(delay);
                await BroadcastPersonalStatesAsync();
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"Follow-up state broadcast failed: {ex.Message}", 0);
        }
    }

    private async Task BroadcastLobbyAsync()
    {
        foreach (var session in _wsHandler.Sessions)
            await SendLobbyAsync(session);
    }

    private async Task SendLobbyAsync(CouchSession session)
    {
        var sessions = _wsHandler.Sessions
            .Select(s => new
            {
                session_id = s.SessionId,
                name = s.Name,
                player_slot = s.PlayerSlot,
                character_id = s.CharacterId,
                is_host = s.IsHost,
                role = s.IsSpectator ? "spectator" : "player"
            })
            .ToArray();

        var payload = JsonSerializer.Serialize(new
        {
            type = "lobby",
            couch_initialized = _couchSessionInitialized,
            host_session_id = _hostSessionId,
            player_count = _couchPlayerCount,
            players = sessions,
            slots = Enumerable.Range(0, 4).Select(slot => new
            {
                slot,
                claimed = sessions.Any(s => s.player_slot == slot),
                name = sessions.FirstOrDefault(s => s.player_slot == slot)?.name,
                character_id = sessions.FirstOrDefault(s => s.player_slot == slot)?.character_id
            })
        });
        await _wsHandler.SendAsync(session, payload);
    }

    private static string WrapState(string rawState, CouchSession session)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawState);
            var stateType = doc.RootElement.TryGetProperty("state_type", out var stateTypeElem)
                ? stateTypeElem.GetString()
                : null;
            var payload = new Dictionary<string, object?>
            {
                ["type"] = "state",
                ["state_type"] = stateType,
                ["session_id"] = session.SessionId,
                ["player_slot"] = session.PlayerSlot,
                ["character_id"] = session.CharacterId,
                ["role"] = session.IsSpectator ? "spectator" : "player",
                ["state"] = JsonSerializer.Deserialize<object?>(rawState)
            };
            return JsonSerializer.Serialize(payload);
        }
        catch
        {
            return rawState;
        }
    }

    private async Task<bool> TryHandleLocalCouchCommand(CouchSession session, string actionJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(actionJson);
            if (!doc.RootElement.TryGetProperty("type", out var typeElem)
                || (typeElem.GetString() != "init_couch"
                    && typeElem.GetString() != "start_couch"
                    && typeElem.GetString() != "reset_couch"))
            {
                return false;
            }

            var playerCount = doc.RootElement.TryGetProperty("player_count", out var countElem)
                && countElem.TryGetInt32(out var count)
                ? count
                : 2;
            playerCount = Math.Clamp(playerCount, 2, 4);
            _couchPlayerCount = playerCount;

            if (typeElem.GetString() == "init_couch")
            {
                InitializeCouchSession(session);
                await SendSessionAsync(session);
                await _wsHandler.SendAsync(session, JsonSerializer.Serialize(new
                {
                    type = "notice",
                    message = $"Local co-op session ready for {playerCount} players."
                }));
                await BroadcastLobbyAsync();
                await BroadcastPersonalStatesAsync();
                return true;
            }

            if (typeElem.GetString() == "reset_couch")
            {
                if (!EnsureHostAccess(session))
                {
                    await _wsHandler.SendAsync(session, JsonSerializer.Serialize(new
                    {
                        type = "error",
                        message = "Only the local co-op host can reset the run."
                    }));
                    return true;
                }
                await SendSessionAsync(session);

                var resetResult = CouchRunBootstrapper.Instance == null
                    ? new Dictionary<string, object?> { ["status"] = "error", ["error"] = "Couch bootstrapper is not ready." }
                    : await CouchRunBootstrapper.Instance.ResetCouchRunAsync();

                await _wsHandler.SendAsync(session, JsonSerializer.Serialize(new
                {
                    type = resetResult.TryGetValue("status", out var resetStatus) && resetStatus?.ToString() == "ok" ? "notice" : "error",
                    message = resetResult.TryGetValue("message", out var resetMessage) ? resetMessage?.ToString() : resetResult.GetValueOrDefault("error")?.ToString(),
                    result = resetResult
                }));
                if (resetResult.TryGetValue("status", out var resetStatusValue)
                    && resetStatusValue?.ToString() == "ok")
                {
                    ResetCouchLobbyState();
                    await SendSessionAsync(session);
                }
                await BroadcastLobbyAsync();
                await BroadcastPersonalStatesAsync();
                return true;
            }

            if (!_couchSessionInitialized)
            {
                InitializeCouchSession(session);
                await SendSessionAsync(session);
            }

            if (!EnsureHostAccess(session))
            {
                await _wsHandler.SendAsync(session, JsonSerializer.Serialize(new
                {
                    type = "error",
                    message = "Only the local co-op host can start the run."
                }));
                return true;
            }
            await SendSessionAsync(session);

            var characters = ReadCharacterIds(doc.RootElement);
            if (characters.Count == 0)
                characters = ReadSessionCharacterIds(playerCount);
            var seed = ReadOptionalString(doc.RootElement, "seed");

            var result = CouchRunBootstrapper.Instance == null
                ? new Dictionary<string, object?> { ["status"] = "error", ["error"] = "Couch bootstrapper is not ready." }
                : await CouchRunBootstrapper.Instance.StartCouchRunAsync(playerCount, characters, seed);

            await _wsHandler.SendAsync(session, JsonSerializer.Serialize(new
            {
                type = result.TryGetValue("status", out var status) && status?.ToString() == "ok" ? "notice" : "error",
                message = result.TryGetValue("message", out var message) ? message?.ToString() : result.GetValueOrDefault("error")?.ToString(),
                result
            }));
            await BroadcastPersonalStatesAsync();
            return true;
        }
        catch (Exception ex)
        {
            await _wsHandler.SendAsync(session, JsonSerializer.Serialize(new
            {
                type = "error",
                message = ex.Message
            }));
            return true;
        }
    }

    private void InitializeCouchSession(CouchSession session)
    {
        if (_couchSessionInitialized)
            return;

        _couchSessionInitialized = true;
        _hostSessionId = session.SessionId;
        session.IsHost = true;
        session.IsSpectator = false;
        session.PlayerSlot = 0;
        session.CharacterId ??= "Ironclad";
    }

    private void ResetCouchLobbyState()
    {
        _couchSessionInitialized = false;
        _hostSessionId = null;
        _couchPlayerCount = 2;
        _wsHandler.ClearKnownSessionAssignments();

        foreach (var connectedSession in _wsHandler.Sessions)
        {
            connectedSession.IsHost = false;
            connectedSession.IsSpectator = false;
            connectedSession.PlayerSlot = null;
            connectedSession.CharacterId = null;
        }
    }

    private bool EnsureHostAccess(CouchSession session)
    {
        if (_hostSessionId == null || session.SessionId == _hostSessionId)
        {
            _hostSessionId = session.SessionId;
            session.IsHost = true;
            session.IsSpectator = false;
            session.PlayerSlot ??= 0;
            session.CharacterId ??= "Ironclad";
            return true;
        }

        var hostStillConnected = _wsHandler.Sessions.Any(s => s.SessionId == _hostSessionId);
        if (hostStillConnected)
            return false;

        _hostSessionId = session.SessionId;
        session.IsHost = true;
        session.IsSpectator = false;
        session.PlayerSlot ??= 0;
        session.CharacterId ??= "Ironclad";
        return true;
    }

    private async Task SendSessionAsync(CouchSession session)
    {
        await _wsHandler.SendAsync(session, JsonSerializer.Serialize(new
        {
            type = "session",
            session_id = session.SessionId,
            player_slot = session.PlayerSlot,
            character_id = session.CharacterId,
            is_host = session.IsHost,
            role = session.IsSpectator ? "spectator" : "player",
            name = session.Name
        }));
    }

    private static List<string> ReadCharacterIds(JsonElement element)
    {
        var result = new List<string>();
        if (!element.TryGetProperty("characters", out var characters) || characters.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var character in characters.EnumerateArray())
        {
            if (character.ValueKind == JsonValueKind.String)
                result.Add(character.GetString() ?? "");
        }

        return result;
    }

    private List<string> ReadSessionCharacterIds(int playerCount)
    {
        var bySlot = _wsHandler.Sessions
            .Where(s => s.PlayerSlot.HasValue && !s.IsSpectator)
            .GroupBy(s => s.PlayerSlot!.Value)
            .ToDictionary(g => g.Key, g => g.First().CharacterId ?? "");

        var result = new List<string>();
        for (var slot = 0; slot < playerCount; slot++)
            result.Add(bySlot.GetValueOrDefault(slot) ?? "");

        return result;
    }

    private static string? ReadOptionalString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return null;

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
