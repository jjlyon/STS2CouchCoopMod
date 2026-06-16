# CouchCoopMod

CouchCoopMod is an experimental local couch co-op mod for Slay the Spire 2. One STS2 process runs on the shared screen, while players connect from phones to claim local multiplayer slots and control their assigned character privately.

The target experience is Jackbox-style local multiplayer:

- The TV keeps the native STS2 game view.
- Phones show each player's private hand, choices, rewards, potions, and actions.
- Other players are treated as multiplayer allies.
- The run is created as an in-process local multiplayer run, without Steam lobbies or helper game clients.

## Current Status

This is active development code. The local lobby, slot assignment, couch run bootstrap, private per-slot state, and most phone actions are implemented. Some multiplayer edge cases and card-selection flows still need hardening.

Known rough areas:

- Some Neow relic/card-selection flows still need more coverage.
- Resetting/abandoning active couch runs is implemented as a cheap developer command, but still needs more live-game validation.
- The shared TV hand-hiding/native spectator presentation is not final.
- The web UI is functional, not polished.

## How It Works

CouchCoopMod runs an HTTP/WebSocket server inside STS2 on port `8080`.

Players open the local web UI from a phone, join the couch lobby, claim a slot, and receive state/actions for that slot. The server proxies per-slot state and actions through the vendored `STS2MCP` couch bridge.

Core pieces:

- `CouchCoopModCode/Server`: local HTTP/WebSocket server and session handling.
- `CouchCoopModCode/Couch`: local multiplayer run bootstrap/reset logic.
- `CouchCoopModCode/Web`: phone UI.
- `STS2MCP`: vendored/patched game-state and action bridge.
- `reference/sts2-decompiled`: local decompiled STS2 reference source for internals research.

## Features

- Local couch lobby from the normal phone menu.
- Host session with locked slot 0.
- Player slot claiming for 2 to 4 players.
- Session reconnect support through browser local storage.
- Optional seeded couch run start.
- Local couch run bootstrap using STS2 multiplayer internals.
- Per-slot private state through `/api/v1/couch/state?slot=N`.
- Per-slot action routing through `/api/v1/couch/action?slot=N`.
- Shared lobby broadcasts and per-player state broadcasts over WebSocket.
- QR overlay toggle with `F9`.
- Basic developer reset command over WebSocket.

## Supported Phone Screens

- Main menu and local co-op entry.
- Couch lobby and slot selection.
- Combat: hand, enemies, targeting, card play, end turn, potions, piles, relics.
- Map navigation and multiplayer-style map voting.
- Events and dialogue.
- Rest sites.
- Rewards and card rewards.
- Shops and fake merchant.
- Treasure rooms.
- Card selection screens, including several grid/choice variants.
- Relic, bundle, Crystal Sphere, and other specialized selection screens.
- Game-over return-to-menu flow.

## WebSocket Commands

Client to server:

```json
{"type":"join","name":"Player"}
{"type":"rejoin","session_id":"..."}
{"type":"claim_slot","slot":1}
{"type":"spectate"}
{"type":"init_couch","player_count":2}
{"type":"start_couch","player_count":2,"seed":"OPTIONAL"}
{"type":"reset_couch"}
{"type":"action","action":"play_card","card_index":0}
```

Server to client:

```json
{"type":"session","session_id":"...","player_slot":0,"is_host":true,"role":"player"}
{"type":"lobby","couch_initialized":true,"player_count":2,"players":[],"slots":[]}
{"type":"state","state_type":"combat","player_slot":0,"state":{}}
{"type":"notice","message":"..."}
{"type":"error","message":"..."}
```

The phone never decides authority by supplying a player id. The server-owned WebSocket session determines the slot used for state and actions.

## Requirements

- Slay the Spire 2 with mod support enabled.
- BaseLib.
- The vendored/installed `STS2MCP` mod.
- .NET 9 SDK for building from source.
- Phones on the same local network as the game PC.

## Installation

Build from source:

```powershell
dotnet build
```

The normal build copies `CouchCoopMod.dll` and `CouchCoopMod.json` to the detected STS2 mods folder.

For compile-only builds without installing into the game:

```powershell
dotnet build /p:SkipModInstall=true
```

If STS2 is not auto-detected, create a gitignored `Directory.Build.props` with local paths:

```xml
<Project>
  <PropertyGroup>
    <Sts2Path>C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2</Sts2Path>
    <GodotPath>C:/path/to/MegaDot_v4.5.1-stable_mono_win64.exe</GodotPath>
  </PropertyGroup>
</Project>
```

## Usage

1. Launch STS2 with BaseLib, STS2MCP, and CouchCoopMod enabled.
2. Open the phone UI at `http://<game-pc-ip>:8080/`.
3. From the phone menu, choose local co-op, player count, and optional seed.
4. The first player initializes the local session and becomes host in slot 0.
5. Other phones join and claim remaining slots.
6. Host starts the local couch run.

Press `F9` in game to show or hide the QR code overlay.

## Firewall

If phones cannot connect, allow TCP port `8080` through Windows Firewall:

```powershell
netsh advfirewall firewall add rule name="CouchCoopMod" dir=in action=allow protocol=tcp localport=8080
```

## Development Notes

- `reference/sts2-decompiled` is a generated reference copy of the current local STS2 assembly.
- `tmp/` contains local investigation/test artifacts and is not part of the mod runtime.
- Avoid treating `STS2MCP` as a fixed external API; this repo patches it as part of the couch bridge.
- Use `dotnet build /p:SkipModInstall=true` for quick compile checks when the game is running, because the installed DLL is locked while loaded.

## License

MIT
