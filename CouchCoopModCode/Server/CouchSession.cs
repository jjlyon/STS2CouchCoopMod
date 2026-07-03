using System.Net.WebSockets;

namespace CouchCoopMod.CouchCoopModCode.Server;

public sealed class CouchSession
{
    public string ConnectionId { get; init; } = "";
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Player";
    public int? PlayerSlot { get; set; }
    public string? CharacterId { get; set; }
    public bool IsHost { get; set; }
    public bool IsSpectator { get; set; }
    public WebSocket Socket { get; init; } = null!;
    public SemaphoreSlim SendLock { get; } = new(1, 1);
}
