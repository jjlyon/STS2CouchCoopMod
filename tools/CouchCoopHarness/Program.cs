using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var exitCode = await ProgramMain.RunAsync(args);
return exitCode;

internal static class ProgramMain
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        WriteIndented = true
    };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        var command = args[0].ToLowerInvariant();
        var options = Args.Parse(args.Skip(1));
        try
        {
            return command switch
            {
                "health" => await HealthAsync(options),
                "start" => await StartAsync(options),
                "reset" => await ResetAsync(options),
                "state" => await StateAsync(options),
                "action" => await ActionAsync(options),
                _ => UnknownCommand(command)
            };
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"ERROR {ex.Message}");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR {ex.Message}");
            return 1;
        }
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"ERROR unknown command '{command}'");
        PrintUsage();
        return 2;
    }

    private static async Task<int> HealthAsync(Args options)
    {
        var timeout = options.GetTimeout("timeout", TimeSpan.FromSeconds(60));
        var result = await WaitForHealthAsync(timeout, logProgress: true);
        if (!result.IsHealthy)
        {
            Console.Error.WriteLine($"RESULT unhealthy: {result.Message}");
            return 1;
        }

        Console.WriteLine($"RESULT healthy: {result.Message}");
        return 0;
    }

    private static async Task<int> StartAsync(Args options)
    {
        var timeout = options.GetTimeout("timeout", TimeSpan.FromSeconds(90));
        var players = options.GetInt("players", 2);
        if (players is < 2 or > 4)
            throw new ArgumentException("--players must be between 2 and 4.");

        var health = await WaitForHealthAsync(timeout, logProgress: true);
        if (!health.IsHealthy)
        {
            Console.Error.WriteLine($"RESULT unhealthy: {health.Message}");
            return 1;
        }

        await using var client = await HarnessWebSocket.ConnectAsync(timeout);
        await client.SendAsync(new { type = "join", name = "Codex Test Host" });
        await client.WaitUntilAsync(MessageHasType("session"), timeout, "host session");

        await client.SendAsync(new { type = "init_couch", player_count = players });
        await client.WaitUntilAsync(
            message => ContainsText(message, "Local co-op session ready") || MessageHasType("session")(message),
            timeout,
            "couch init");

        await client.SendAsync(new { type = "start_couch", player_count = players });
        var startMessage = await client.WaitUntilAsync(
            message => ContainsText(message, "Started couch run") || MessageHasType("error")(message),
            timeout,
            "couch start");

        if (MessageHasType("error")(startMessage))
        {
            Console.Error.WriteLine($"RESULT start-error: {Summarize(startMessage)}");
            return 1;
        }

        var actionable = await WaitForActionableStateAsync(timeout);
        if (actionable == null)
        {
            Console.Error.WriteLine("RESULT timeout: started run but did not observe actionable state");
            return 1;
        }

        Console.WriteLine($"RESULT started: {SummarizeState(actionable)}");
        return 0;
    }

    private static async Task<int> ResetAsync(Args options)
    {
        var timeout = options.GetTimeout("timeout", TimeSpan.FromSeconds(60));
        var health = await WaitForHealthAsync(timeout, logProgress: true);
        if (!health.CouchServerReady)
        {
            Console.Error.WriteLine($"RESULT unhealthy: {health.Message}");
            return 1;
        }

        await using var client = await HarnessWebSocket.ConnectAsync(timeout);
        await client.SendAsync(new { type = "join", name = "Codex Reset Host" });
        await client.WaitUntilAsync(MessageHasType("session"), timeout, "host session");

        await client.SendAsync(new { type = "init_couch", player_count = 2 });
        await client.WaitUntilAsync(
            message => ContainsText(message, "Local co-op session ready") || MessageHasType("session")(message),
            timeout,
            "couch init");

        await client.SendAsync(new { type = "reset_couch" });
        var resetMessage = await client.WaitUntilAsync(
            message => ContainsText(message, "Reset couch run")
                || ContainsText(message, "no run was active")
                || MessageHasType("error")(message),
            timeout,
            "couch reset");

        if (MessageHasType("error")(resetMessage))
        {
            Console.Error.WriteLine($"RESULT reset-error: {Summarize(resetMessage)}");
            return 1;
        }

        Console.WriteLine($"RESULT reset: {Summarize(resetMessage)}");
        return 0;
    }

    private static async Task<int> StateAsync(Args options)
    {
        var slot = options.GetInt("slot", 0);
        var pretty = options.Has("pretty");
        using var http = NewHttpClient();
        var response = await http.GetAsync($"http://localhost:15526/api/v1/couch/state?slot={slot}");
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"ERROR HTTP {(int)response.StatusCode}: {body}");
            return 1;
        }

        Console.WriteLine(pretty ? PrettyJson(body) : body);
        return 0;
    }

    private static async Task<int> ActionAsync(Args options)
    {
        if (options.Has("help"))
        {
            PrintActionUsage();
            return 0;
        }

        var slot = options.GetInt("slot", 0);
        var json = NormalizeActionJson(options.TryGet("json")) ?? BuildActionJson(options);

        using var _ = JsonDocument.Parse(json);
        if (options.Has("dry-run"))
        {
            Console.WriteLine(PrettyJson(json));
            return 0;
        }

        using var http = NewHttpClient();
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await http.PostAsync($"http://localhost:15526/api/v1/couch/action?slot={slot}", content);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"ERROR HTTP {(int)response.StatusCode}: {body}");
            return 1;
        }

        Console.WriteLine(PrettyJson(body));
        return 0;
    }

    private static string? NormalizeActionJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var _ = JsonDocument.Parse(json);
            return json;
        }
        catch (JsonException)
        {
            var recovered = TryRecoverPowerShellMangledJson(json);
            if (recovered != null)
                return recovered;
            throw;
        }
    }

    private static string? TryRecoverPowerShellMangledJson(string raw)
    {
        raw = raw.Trim();
        if (!raw.StartsWith('{') || !raw.EndsWith('}'))
            return null;

        var payload = new JsonObject();
        var inner = raw[1..^1].Trim();
        if (inner.Length == 0)
            return "{}";

        foreach (var pair in inner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = pair.IndexOf(':');
            if (separator <= 0)
                return null;

            var key = pair[..separator].Trim().Trim('"', '\'');
            var value = pair[(separator + 1)..].Trim().Trim('"', '\'');
            if (string.IsNullOrWhiteSpace(key))
                return null;

            payload[key] = ParseJsonValue(value);
        }

        return payload.ToJsonString(JsonOptions);
    }

    private static string BuildActionJson(Args options)
    {
        var action = options.TryGet("name") ?? options.TryGet("action");
        if (string.IsNullOrWhiteSpace(action))
            throw new ArgumentException("Missing --json or --name. Use 'action --help' for examples.");

        var payload = new JsonObject
        {
            ["action"] = action
        };

        foreach (var (name, rawValue) in options.Values)
        {
            if (name is "slot" or "json" or "name" or "action" or "help")
                continue;
            if (name is "dry-run")
                continue;

            var fieldName = name.Replace('-', '_');
            payload[fieldName] = ParseJsonValue(rawValue);
        }

        return payload.ToJsonString(JsonOptions);
    }

    private static JsonNode? ParseJsonValue(string? rawValue)
    {
        if (rawValue == null)
            return JsonValue.Create(true);

        if (string.Equals(rawValue, "null", StringComparison.OrdinalIgnoreCase))
            return null;
        if (bool.TryParse(rawValue, out var boolValue))
            return JsonValue.Create(boolValue);
        if (long.TryParse(rawValue, out var longValue))
            return JsonValue.Create(longValue);
        if (double.TryParse(rawValue, out var doubleValue))
            return JsonValue.Create(doubleValue);

        return JsonValue.Create(rawValue);
    }

    private static async Task<HealthResult> WaitForHealthAsync(TimeSpan timeout, bool logProgress)
    {
        using var http = NewHttpClient();
        var deadline = DateTimeOffset.UtcNow + timeout;
        var couchReady = false;
        var mcpReady = false;
        string lastMessage = "not checked";

        while (DateTimeOffset.UtcNow < deadline)
        {
            couchReady = await IsReadyAsync(http, "http://localhost:8080/");
            mcpReady = await IsReadyAsync(http, "http://localhost:15526/api/v1/couch/state?slot=0");

            if (couchReady && mcpReady)
                return new HealthResult(true, true, true, "CouchCoopMod and STS2MCP endpoints responded.");

            lastMessage = $"couch8080={couchReady} sts2mcp15526={mcpReady}";
            if (logProgress)
                Console.WriteLine($"WAIT health {lastMessage}");

            await Task.Delay(1000);
        }

        return new HealthResult(false, couchReady, mcpReady, lastMessage);
    }

    private static async Task<bool> IsReadyAsync(HttpClient http, string url)
    {
        try
        {
            using var response = await http.GetAsync(url);
            return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<JsonObject?> WaitForActionableStateAsync(TimeSpan timeout)
    {
        using var http = NewHttpClient();
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var response = await http.GetAsync("http://localhost:15526/api/v1/couch/state?slot=0");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadFromJsonAsync<JsonObject>();
                    var stateType = json?["state_type"]?.GetValue<string?>();
                    if (!string.IsNullOrWhiteSpace(stateType) && stateType != "menu")
                        return json;
                }
            }
            catch
            {
                // Keep waiting while the game changes scenes.
            }

            await Task.Delay(500);
        }

        return null;
    }

    private static HttpClient NewHttpClient()
    {
        return new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    private static Func<string, bool> MessageHasType(string type)
    {
        return message =>
        {
            try
            {
                using var doc = JsonDocument.Parse(message);
                return doc.RootElement.TryGetProperty("type", out var elem)
                    && elem.GetString() == type;
            }
            catch
            {
                return false;
            }
        };
    }

    private static bool ContainsText(string message, string text)
    {
        return message.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private static string Summarize(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string? Get(string name) => root.TryGetProperty(name, out var value) ? value.ToString() : null;
            return $"type={Get("type")} state={Get("state_type")} message={Get("message")} slot={Get("player_slot")} host={Get("is_host")}";
        }
        catch
        {
            return json[..Math.Min(220, json.Length)];
        }
    }

    private static string SummarizeState(JsonObject state)
    {
        var stateType = state["state_type"]?.GetValue<string?>() ?? "unknown";
        var slot = state["player_slot"]?.ToString() ?? "?";
        var floor = state["run"]?["floor"]?.ToString() ?? "?";
        return $"state_type={stateType} player_slot={slot} floor={floor}";
    }

    private static string PrettyJson(string json)
    {
        var node = JsonNode.Parse(json);
        return node?.ToJsonString(PrettyJsonOptions) ?? json;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
        CouchCoopHarness

        Usage:
          dotnet run --project tools/CouchCoopHarness -- health --timeout 60
          dotnet run --project tools/CouchCoopHarness -- start --players 2
          dotnet run --project tools/CouchCoopHarness -- reset
          dotnet run --project tools/CouchCoopHarness -- state --slot 0 --pretty
          dotnet run --project tools/CouchCoopHarness -- action --slot 0 --name choose_map_node --index 0
          dotnet run --project tools/CouchCoopHarness -- action --slot 0 --json '{"action":"end_turn"}'
        """);
    }

    private static void PrintActionUsage()
    {
        Console.WriteLine("""
        CouchCoopHarness action

        Usage:
          dotnet run --project tools/CouchCoopHarness -- action --slot 0 --name choose_map_node --index 0
          dotnet run --project tools/CouchCoopHarness -- action --slot 0 --name play_card --card-index 0 --target JAW_WORM_0
          dotnet run --project tools/CouchCoopHarness -- action --slot 0 --name end_turn
          dotnet run --project tools/CouchCoopHarness -- action --dry-run --name choose_map_node --index 0
          dotnet run --project tools/CouchCoopHarness -- action --slot 0 --json '{"action":"end_turn"}'

        Notes:
          Prefer --name plus ordinary flags in PowerShell. Option names like --card-index become card_index.
          Use --dry-run to print the payload without sending it to the game.
          --json is still available when the shell can pass the JSON string intact.
        """);
    }

    private sealed record HealthResult(bool IsHealthy, bool CouchServerReady, bool McpReady, string Message);

    private sealed class HarnessWebSocket : IAsyncDisposable
    {
        private readonly ClientWebSocket _socket;
        private readonly ConcurrentQueue<string> _messages = new();
        private readonly SemaphoreSlim _signal = new(0);
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _receiveTask;

        private HarnessWebSocket(ClientWebSocket socket)
        {
            _socket = socket;
            _receiveTask = Task.Run(ReceiveLoopAsync);
        }

        public static async Task<HarnessWebSocket> ConnectAsync(TimeSpan timeout)
        {
            var socket = new ClientWebSocket();
            using var cts = new CancellationTokenSource(timeout);
            await socket.ConnectAsync(new Uri("ws://localhost:8080/ws"), cts.Token);
            Console.WriteLine("WS connected");
            return new HarnessWebSocket(socket);
        }

        public async Task SendAsync(object payload)
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            Console.WriteLine($"SENT {json}");
        }

        public async Task<string> WaitUntilAsync(Func<string, bool> predicate, TimeSpan timeout, string description)
        {
            var deadline = DateTimeOffset.UtcNow + timeout;
            while (DateTimeOffset.UtcNow < deadline)
            {
                while (_messages.TryDequeue(out var message))
                {
                    Console.WriteLine($"RECV {Summarize(message)}");
                    if (predicate(message))
                        return message;
                }

                var remaining = deadline - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    break;
                await _signal.WaitAsync(remaining > TimeSpan.FromMilliseconds(250)
                    ? TimeSpan.FromMilliseconds(250)
                    : remaining);
            }

            throw new TimeoutException($"Timed out waiting for {description}.");
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[64 * 1024];
            try
            {
                while (_socket.State == WebSocketState.Open && !_cts.IsCancellationRequested)
                {
                    using var stream = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _socket.ReceiveAsync(buffer, _cts.Token);
                        if (result.MessageType == WebSocketMessageType.Close)
                            return;
                        stream.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    var text = Encoding.UTF8.GetString(stream.ToArray());
                    _messages.Enqueue(text);
                    _signal.Release();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (WebSocketException ex)
            {
                _messages.Enqueue(JsonSerializer.Serialize(new { type = "error", message = ex.Message }, JsonOptions));
                _signal.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try
            {
                if (_socket.State == WebSocketState.Open)
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Harness done", CancellationToken.None);
            }
            catch
            {
                _socket.Abort();
            }

            try { await _receiveTask; }
            catch { /* best effort shutdown */ }
            _socket.Dispose();
            _cts.Dispose();
            _signal.Dispose();
        }
    }

    private sealed class Args
    {
        private readonly Dictionary<string, string?> _values;

        private Args(Dictionary<string, string?> values)
        {
            _values = values;
        }

        public static Args Parse(IEnumerable<string> rawArgs)
        {
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var args = rawArgs.ToArray();
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (!arg.StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"Unexpected argument '{arg}'. Options must use --name value.");

                var name = arg[2..];
                if (string.IsNullOrWhiteSpace(name))
                    throw new ArgumentException("Empty option name.");

                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    values[name] = args[++i];
                }
                else
                {
                    values[name] = null;
                }
            }

            return new Args(values);
        }

        public bool Has(string name)
        {
            return _values.ContainsKey(name);
        }

        public IReadOnlyDictionary<string, string?> Values => _values;

        public string? TryGet(string name)
        {
            return _values.TryGetValue(name, out var value) ? value : null;
        }

        public string GetRequired(string name)
        {
            if (!_values.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"Missing required --{name} value.");
            return value;
        }

        public int GetInt(string name, int defaultValue)
        {
            if (!_values.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
                return defaultValue;
            if (!int.TryParse(value, out var result))
                throw new ArgumentException($"--{name} must be an integer.");
            return result;
        }

        public TimeSpan GetTimeout(string name, TimeSpan defaultValue)
        {
            if (!_values.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
                return defaultValue;
            if (!double.TryParse(value, out var seconds) || seconds <= 0)
                throw new ArgumentException($"--{name} must be a positive number of seconds.");
            return TimeSpan.FromSeconds(seconds);
        }
    }
}
