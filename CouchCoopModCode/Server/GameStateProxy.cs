using System.Text;
using System.Text.Json;

namespace CouchCoopMod.CouchCoopModCode.Server;

public class GameStateProxy
{
    private static readonly HttpClient Client = new()
    {
        BaseAddress = new Uri("http://localhost:15526"),
        Timeout = TimeSpan.FromSeconds(5)
    };

    private string? _lastStateHash;

    public async Task<string?> GetStateAsync(int? slot = null)
    {
        try
        {
            var path = slot.HasValue
                ? $"/api/v1/couch/state?slot={slot.Value}"
                : "/api/v1/couch/state?slot=0";
            var response = await Client.GetAsync(path);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                response = await Client.GetAsync("/api/v1/singleplayer?format=json");
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsStringAsync();
        }
        catch
        {
            return null;
        }
    }

    public async Task<(bool success, string response)> ExecuteActionAsync(string actionJson, int? slot = null)
    {
        try
        {
            var requestBody = AddSlotToAction(actionJson, slot);
            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
            var response = await Client.PostAsync($"/api/v1/couch/action?slot={slot.GetValueOrDefault(0)}", content);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                content = new StringContent(actionJson, Encoding.UTF8, "application/json");
                response = await Client.PostAsync("/api/v1/singleplayer", content);
            }
            var responseBody = await response.Content.ReadAsStringAsync();
            return (response.IsSuccessStatusCode, responseBody);
        }
        catch (Exception ex)
        {
            return (false, $"{{\"error\":\"{ex.Message}\"}}");
        }
    }

    public async Task<(string? state, bool changed)> PollAsync()
    {
        var state = await GetStateAsync(0);
        if (state == null) return (null, false);
        var hash = state.GetHashCode().ToString();
        var changed = hash != _lastStateHash;
        _lastStateHash = hash;
        return (state, changed);
    }

    private static string AddSlotToAction(string actionJson, int? slot)
    {
        if (!slot.HasValue) return actionJson;

        try
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, object?>>(actionJson);
            if (data == null) return actionJson;
            data["slot"] = slot.Value;
            return JsonSerializer.Serialize(data);
        }
        catch
        {
            return actionJson;
        }
    }
}
