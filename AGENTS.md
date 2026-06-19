# Repository Guidelines

## Project Structure & Module Organization

This repository contains a Slay the Spire 2 Godot/.NET mod. Main C# source lives in `CouchCoopModCode/`:

- `CouchCoopModCode/Server`: embedded HTTP/WebSocket server and sessions.
- `CouchCoopModCode/Couch`: couch co-op run bootstrap, reset, and slot logic.
- `CouchCoopModCode/Web`: embedded phone UI assets served by the mod.
- `CouchCoopModCode/QRCode`: QR overlay helpers.

`CouchCoopMod/` contains mod metadata/assets such as `mod_image.png`; `CouchCoopMod.json` is the mod manifest. `STS2MCP/` is a vendored/patched bridge and may need coordinated edits. `reference/sts2-decompiled/` is generated reference source for research only. `tmp/` and `.config/` are local artifacts.

## Build, Test, and Development Commands

```powershell
dotnet build
```

Builds the mod and installs `CouchCoopMod.dll`, manifest, and dependencies into the detected STS2 mods folder.

```powershell
dotnet build /p:SkipModInstall=true
```

Compile-only check. Prefer this while STS2 is running because the loaded mod DLL may be locked.

```powershell
dotnet build /p:SkipMcpInstall=true
```

Builds this mod without rebuilding/installing `STS2MCP`.

If STS2 or Godot paths are not detected, create a gitignored `Directory.Build.props` as shown in `README.md`.

## Coding Style & Naming Conventions

Use C# with nullable reference types enabled. Follow existing style: 4-space indentation, PascalCase for types/methods/properties, camelCase for locals/parameters, and `_camelCase` for private fields. Keep gameplay authority on the server/session side; phone clients must not choose player identity directly.

## Testing Guidelines

There is no formal test suite in this repo yet. For live behavior testing, follow `docs/codex-dev-loop.md`: close STS2, run `dotnet build` to install the changed mod, launch STS2, then use `tools/CouchCoopHarness` for health/start/state/action/reset. Manually or browser-test the phone UI at `http://<game-pc-ip>:8080/`, covering lobby, slot claim, start, combat, map, rewards, and reset/abandon if touched.

## Commit & Pull Request Guidelines

Git history uses short imperative summaries, for example `Add QR code overlay with auto IP detection and F9 toggle`. Keep commits focused. PRs should include a brief behavior summary, build command/results, manual test notes, linked issue/context, and screenshots or phone UI captures for visible UI changes.

## Agent-Specific Notes

Do not edit generated reference files under `reference/sts2-decompiled/` unless explicitly asked. Treat `STS2MCP/` as editable but coordinated vendor code. Avoid committing local path files, generated `bin/obj/out`, `tmp/`, or `.config/`.

When implementing mod functionality not already built into this mod, first consult `docs/sts2-game-flow-for-modders.md` for relevant run, map, combat, event, menu, and multiplayer hook points.

When testing through Codex, prefer the maintained harness in `tools/CouchCoopHarness` over scratch scripts in `tmp/`. The harness provides plumbing only; use current state and the user's requested scenario to decide which legal actions to send.
