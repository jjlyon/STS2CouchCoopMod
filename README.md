# CouchCoopMod

Play Slay the Spire 2 from your phone on a shared screen — Jackbox style. One game runs on the TV, players join by scanning a QR code.

## How It Works

CouchCoopMod runs an HTTP/WebSocket server inside StS2. Players scan a QR code (or enter a URL) on their phones to get a mobile web UI that shows game state and lets them play cards, navigate the map, choose events, and more.

Game state reading and action injection are handled by [STS2MCP](https://github.com/Gennadiyev/STS2MCP), which CouchCoopMod proxies through.

## Requirements

- Slay the Spire 2 with mod support enabled
- [STS2MCP](https://github.com/Gennadiyev/STS2MCP) mod installed (provides the game state API)
- Players' phones must be on the same local network as the game PC

## Installation

1. Install STS2MCP following its instructions.
2. Download the latest CouchCoopMod release (or build from source — see below).
3. Copy `CouchCoopMod.dll` and `CouchCoopMod.json` to your StS2 mods folder:
   - **Windows:** `<Steam>/steamapps/common/Slay the Spire 2/mods/CouchCoopMod/`
   - **Linux:** `~/.local/share/Steam/steamapps/common/Slay the Spire 2/mods/CouchCoopMod/`
   - **macOS:** `~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/mods/CouchCoopMod/`
4. Launch the game and enable CouchCoopMod in Settings > Mod Settings.

## Usage

1. Start a run in Slay the Spire 2.
2. Press **F9** to show the QR code overlay.
3. Scan the QR code with your phone (or open the displayed URL in a browser).
4. Play the game from your phone — the web UI updates in real time.
5. Press **F9** again to dismiss the QR code.

### Windows Firewall

If phones can't connect, you may need to allow the port through Windows Firewall:

```
netsh advfirewall firewall add rule name="CouchCoopMod" dir=in action=allow protocol=tcp localport=8080
```

## Building from Source

Requires .NET 9 SDK and a local Slay the Spire 2 installation (the build references `sts2.dll` from your game directory).

```
dotnet build
```

The build automatically copies the DLL and manifest to your mods folder.

To configure the StS2 install path manually, create a `Directory.Build.props` file (gitignored) with:

```xml
<Project>
    <PropertyGroup>
        <GodotPath>C:/megadot/MegaDot_v4.5.1-stable_mono_win64.exe</GodotPath>
    </PropertyGroup>
</Project>
```

## Supported Game Screens

- Combat (hand, enemies, card play with targeting, end turn)
- Map navigation
- Events and dialogue
- Rest sites
- Rewards
- Card selection / card rewards
- Shop
- Generic fallback for unhandled screens

## License

MIT
