using System.Net;
using System.Reflection;

namespace CouchCoopMod.CouchCoopModCode.Server;

public class HttpServer
{
    private HttpListener _listener = new();
    private readonly int _port;
    private readonly WebSocketHandler _wsHandler;
    private readonly GameStateProxy _proxy = new();
    private CancellationTokenSource? _cts;

    public HttpServer(int port = 8080)
    {
        _port = port;
        _wsHandler = new WebSocketHandler(HandleAction, SendInitialState);
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

    private async Task HandleAction(string actionJson)
    {
        var (success, response) = await _proxy.ExecuteActionAsync(actionJson);
        if (!success)
            MainFile.Logger.Warn($"Action failed: {response}", 0);

        await Task.Delay(150);
        var state = await _proxy.GetStateAsync();
        if (state != null)
            await _wsHandler.BroadcastAsync(state);
    }

    private async Task SendInitialState()
    {
        var state = await _proxy.GetStateAsync();
        if (state != null)
            await _wsHandler.SendToLatestAsync(state);
    }

    private async Task PollStateLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(500, ct);
                if (_wsHandler.ClientCount == 0) continue;

                var (state, changed) = await _proxy.PollAsync();
                if (changed && state != null)
                    await _wsHandler.BroadcastAsync(state);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"Poll error: {ex.Message}", 0);
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
}
