# Codex live development loop

This repo is easiest to test remotely through the mod's local HTTP/WebSocket APIs. Do not rely on the STS2 dev console or keyboard focus for normal validation.

## When code changes need live testing

Use this loop for behavior changes:

1. Close any running Slay the Spire 2 process so the loaded mod DLL is not locked.
2. Install the changed mod:

   ```powershell
   dotnet build
   ```

3. Launch STS2 with BaseLib, STS2MCP, and CouchCoopMod enabled.
4. Wait for both local servers:

   ```powershell
   dotnet run --project tools/CouchCoopHarness -- health --timeout 60
   ```

5. Start an unseeded couch run:

   ```powershell
   dotnet run --project tools/CouchCoopHarness -- start --players 2
   ```

6. Inspect state and send actions as needed:

   ```powershell
   dotnet run --project tools/CouchCoopHarness -- state --slot 0 --pretty
   dotnet run --project tools/CouchCoopHarness -- action --slot 0 --name end_turn
   ```

7. Reset before another scenario:

   ```powershell
   dotnet run --project tools/CouchCoopHarness -- reset
   ```

If `dotnet build` fails, stop and report the compiler error. Do not run a separate compile-only precheck as part of this live test loop.

## Harness purpose

`tools/CouchCoopHarness` provides plumbing only:

- `health`: waits for `http://localhost:8080/` and `http://localhost:15526/api/v1/couch/state?slot=0`.
- `start`: joins the phone WebSocket as host, initializes couch co-op, starts an unseeded run, and waits for actionable state.
- `reset`: sends the existing `reset_couch` WebSocket command.
- `state`: prints the current couch state for a slot.
- `action`: posts one action to `/api/v1/couch/action?slot=N`. Prefer `--name` plus normal flags in PowerShell, for example `--name choose_map_node --index 0`; raw `--json` is still supported when useful.

The harness should not choose gameplay strategy. Codex should read state, make ordinary test decisions, and use `action` for the next legal move.

PowerShell can mangle inline JSON quotes when commands are nested through agent shells. Use structured flags unless a raw JSON argument is truly needed:

```powershell
dotnet run --project tools/CouchCoopHarness -- action --slot 0 --name choose_map_node --index 0
dotnet run --project tools/CouchCoopHarness -- action --slot 0 --name play_card --card-index 0 --target JAW_WORM_0
dotnet run --project tools/CouchCoopHarness -- action --slot 0 --name end_turn
```

If raw JSON is needed, avoid typing it inline in a nested PowerShell command. Put it in a variable and pass the variable:

```powershell
$json = '{"action":"choose_map_node","index":0}'
dotnet run --project tools/CouchCoopHarness -- action --slot 0 --json $json
```

The harness also accepts simple PowerShell-mangled object payloads passed through `--json`, such as `{action:choose_map_node,index:0}`, and normalizes them back to valid JSON. Prefer this only as a fallback; structured flags are clearer.

## Choosing what to test

If the user's request already implies the scenario, proceed without asking. Examples:

- A combat-control change should be tested in combat.
- A lobby or slot change should be tested through the phone lobby flow.
- A reward UI change should be tested by reaching or exercising rewards.

Ask the user only when the test target is materially unclear. Good questions are specific:

- "Should I test one complete combat or push through as much of the act as possible?"
- "For this UI change, should I prioritize phone lobby flow or in-combat controls?"
- "Should I choose rewards normally, or bias toward cards that exercise the changed mechanic?"

## Browser UI checks

Use the `browser:control-in-app-browser` skill for visual testing at:

```text
http://localhost:8080/
```

Use browser checks for lobby rendering, slot claim, visible phone state, action buttons, layout, and frontend quirks. If the browser tool is unavailable, continue with harness/API checks and report that visual verification was skipped.

## Notes

- Tests start standard unseeded runs.
- There is no seed discovery or fixed seed catalog in this loop.
- There are no development-only debug endpoints in this loop.
- `tmp/` scripts are scratch artifacts; prefer `tools/CouchCoopHarness` for repeatable work.
