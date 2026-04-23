using System.Text;

namespace CouchCoopMod.CouchCoopModCode.Server;

public class GameStateProxy
{
    private static readonly HttpClient Client = new()
    {
        BaseAddress = new Uri("http://localhost:15526"),
        Timeout = TimeSpan.FromSeconds(5)
    };

    private string? _lastStateHash;

    public async Task<string?> GetStateAsync()
    {
        try
        {
            var response = await Client.GetAsync("/api/v1/singleplayer?format=json");
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsStringAsync();
        }
        catch
        {
            return null;
        }
    }

    public async Task<(bool success, string response)> ExecuteActionAsync(string actionJson)
    {
        try
        {
            var content = new StringContent(actionJson, Encoding.UTF8, "application/json");
            var response = await Client.PostAsync("/api/v1/singleplayer", content);
            var body = await response.Content.ReadAsStringAsync();
            return (response.IsSuccessStatusCode, body);
        }
        catch (Exception ex)
        {
            return (false, $"{{\"error\":\"{ex.Message}\"}}");
        }
    }

    public async Task<(string? state, bool changed)> PollAsync()
    {
        var state = await GetStateAsync();
        if (state == null) return (null, false);
        var hash = state.GetHashCode().ToString();
        var changed = hash != _lastStateHash;
        _lastStateHash = hash;
        return (state, changed);
    }
}
