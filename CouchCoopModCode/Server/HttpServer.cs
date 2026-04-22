using System.Net;
using System.Reflection;

namespace CouchCoopMod.CouchCoopModCode.Server;

public class HttpServer
{
    private HttpListener _listener = new();
    private readonly int _port;
    private readonly WebSocketHandler _wsHandler = new();
    private CancellationTokenSource? _cts;

    public HttpServer(int port = 8080)
    {
        _port = port;
    }

    public WebSocketHandler WebSocketHandler => _wsHandler;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        try
        {
            _listener.Prefixes.Add($"http://*:{_port}/");
            _listener.Start();
        }
        catch (HttpListenerException)
        {
            _listener.Close();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{_port}/");
            _listener.Start();
            MainFile.Logger.Log("Could not bind to all interfaces, falling back to localhost only");
        }
        MainFile.Logger.Log($"HTTP server listening on port {_port}");
        Task.Run(() => ListenLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _wsHandler.CloseAll();
        _listener.Stop();
        _listener.Close();
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
                MainFile.Logger.Log($"HTTP error: {ex.Message}");
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
