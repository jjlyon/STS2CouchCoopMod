# Couch Co-op Mod for Slay the Spire 2 (Jackbox-Style)

## Context

StS2 supports 2-4 player multiplayer, but each player needs their own game instance via Steam networking. The goal is a mod where one game runs on a shared screen (TV) and players join from their phones by scanning a QR code — like Jackbox Games.

## Architecture

```
Phone Browser ◄─── WebSocket ───► CouchCoopMod ◄─── HTTP ───► STS2MCP ◄──► Game State
                                  (port 8080)                (port 15526)
```

CouchCoopMod handles phone connectivity (HTTP server, WebSocket, QR code overlay, web UI). Game state reading and action injection are delegated to [STS2MCP](https://github.com/Gennadiyev/STS2MCP), which exposes a comprehensive HTTP API.

**Not** building on StS2's existing multiplayer — Steam networking requires Steam auth and peer-to-peer sockets that can't be faked from browsers.

## Technology Stack

| Component | Technology |
|---|---|
| HTTP Server | `System.Net.HttpListener` |
| WebSocket | `System.Net.WebSockets` |
| QR Code | QRCoder (NuGet) |
| Game Bridge | STS2MCP mod (HTTP proxy) |
| Web Frontend | Vanilla HTML/CSS/JS (embedded resources) |
| Mod Framework | Godot.NET.Sdk, HarmonyLib |

## Project Structure

```
CouchCoopMod/
├── CouchCoopMod.csproj / .json
├── CouchCoopModCode/
│   ├── MainFile.cs              # Mod entry, server lifecycle, F9 toggle
│   ├── NetworkHelper.cs         # LAN IP detection
│   ├── Server/
│   │   ├── HttpServer.cs        # HttpListener, routes, static files, STS2MCP polling
│   │   ├── WebSocketHandler.cs  # Connection mgmt, broadcast, action forwarding
│   │   └── GameStateProxy.cs    # HTTP client to STS2MCP
│   ├── QRCode/
│   │   └── QRCodeOverlay.cs     # Godot CanvasLayer QR display
│   └── Web/                     # Embedded in .dll as resources
│       ├── index.html
│       ├── app.js
│       └── styles.css
└── CouchCoopMod/                # Godot assets (mod_image, localization)
```

## Progress

### Done

- [x] **Scaffold** — mod template instantiated, renamed, git initialized
- [x] **HTTP/WebSocket server** — serves embedded web UI, upgrades /ws connections
- [x] **QR code overlay** — auto-detects LAN IP, generates QR, F9 toggle
- [x] **STS2MCP proxy** — polls state every 500ms, forwards actions, broadcasts updates
- [x] **Combat UI** — hand cards, enemies with HP/intents, card targeting flow, end turn
- [x] **Map UI** — path selection with node type icons
- [x] **Event UI** — dialogue, option buttons, advance dialogue
- [x] **Other screens** — rest site, rewards, card rewards, shop, hand select, generic fallback
- [x] **README** — installation, usage, build instructions

### Phase 2: Full Single-Player Coverage

- [ ] Potion management (use/discard from web UI)
- [ ] Relic display (current relics with descriptions)
- [ ] Card piles (view draw/discard/exhaust counts, tap to see contents)
- [ ] Reconnection handling (browser refresh resumes session)
- [ ] Treasure rooms
- [ ] Card upgrade/transform/remove selection
- [ ] Crystal Sphere minigame
- [ ] Relic selection (boss relics)
- [ ] Bundle selection

### Phase 3: Multi-Player Support

Multiple phones control different characters in a multiplayer run.

STS2MCP has separate multiplayer endpoints (`/api/v1/multiplayer`) with support for:
- Per-player state views
- Map vote synchronization
- Event vote synchronization
- Vote-based end turn (`EndPlayerTurnAction`)
- Treasure bid tracking

Implementation:
- [ ] Detect singleplayer vs multiplayer (STS2MCP returns 409 on wrong endpoint)
- [ ] Session management — assign phones to player slots
- [ ] Switch proxy to multiplayer endpoints
- [ ] Per-player state filtering (only show your hand, your potions)
- [ ] Lobby screen — show connected players, ready state
- [ ] Map voting UI
- [ ] Shared event voting UI

### Phase 4: Polish

- [ ] Web UI animations matching game pacing
- [ ] Spectator mode (view-only connections)
- [ ] Card art thumbnails (serve from game assets or STS2MCP)
- [ ] Delta state updates (send diffs instead of full state)
- [ ] Error recovery and connection status indicators
- [ ] Configurable port (mod settings UI)
- [ ] Sound/haptic feedback on phone

## Key Risks

1. **STS2MCP compatibility** — CouchCoopMod depends on STS2MCP's HTTP API. If STS2MCP changes its API or breaks on a game update, CouchCoopMod breaks too.

2. **Game updates** — StS2 is in Early Access with frequent breaking changes. STS2MCP absorbs most of this risk for us.

3. **HttpListener permissions on Windows** — May require firewall rule for non-localhost binding. Documented in README.

4. **Polling latency** — 500ms poll interval means up to 500ms delay in state updates. Could reduce interval or switch to SSE/long-poll from STS2MCP if needed.

## WebSocket Protocol

Server → Client: raw STS2MCP JSON state (has `state_type` field to determine screen)
Client → Server: STS2MCP action format (`{ "action": "play_card", "card_index": 0, "target_combat_id": 1 }`)

## Verification Checklist

1. Build mod, verify it loads in game (Settings → Mod Settings)
2. Verify STS2MCP is running (hit `http://localhost:15526/` from browser)
3. Press F9, scan QR code from phone
4. Start a run — phone should show game state
5. Play a card from phone — verify it plays in game
6. Navigate map from phone
7. Choose event option from phone
8. Refresh phone browser — verify reconnection works
