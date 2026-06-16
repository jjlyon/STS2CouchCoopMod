# StS2 Game Flow For Modders

This document maps the major Slay the Spire 2 runtime flows visible in the
decompiled reference source under `reference/sts2-decompiled`. It is written for
mod authors who need to decide where to hook, patch, subscribe, or wrap game
behavior.

The most important files are:

- `MegaCrit/sts2/Core/Nodes/NGame.cs`: top-level scene transitions, new run/load run entry points.
- `MegaCrit/sts2/Core/Runs/RunState.cs`: durable run state, players, acts, map coordinates, room stack, hook listener enumeration.
- `MegaCrit/sts2/Core/Runs/RunManager.cs`: run setup, map generation, room transitions, act transitions, save/end/cleanup, multiplayer synchronizer setup.
- `MegaCrit/sts2/Core/Combat/CombatManager.cs`: combat setup, turn loop, end-turn coordination, victory/loss.
- `MegaCrit/sts2/Core/Hooks/Hook.cs`: central dispatcher from gameplay code to model hook methods.
- `MegaCrit/sts2/Core/Models/AbstractModel.cs`: the overridable hook surface implemented by cards, relics, potions, modifiers, powers, etc.
- `MegaCrit/sts2/Core/Modding/ModHelper.cs`: mod content pool insertion and global run/combat hook subscribers.
- `MegaCrit/sts2/Core/Rooms/*.cs`: room-specific enter/exit/resume behavior.
- `MegaCrit/sts2/Core/Multiplayer/Game/*.cs` and `Core/GameActions/Multiplayer/*.cs`: multiplayer voting, action queue sync, player choice sync, rewards/rest/event sync.

## Mental Model

StS2 has one gameplay loop with network synchronization layered around it.
Single player uses `NetSingleplayerGameService`; multiplayer uses host/client
`INetGameService` implementations. Both create a `RunState`, initialize
`RunManager`, enter an act, create a map, then enter rooms. Combat and room
logic mostly call the same hooks in both modes.

The main hook path is:

1. Gameplay code calls a static method in `Hook`.
2. `Hook` asks `RunState.IterateHookListeners` or `CombatState.IterateHookListeners`.
3. Those listeners include active player cards, enchantments, relics, potions,
   modifiers, powers, the `MultiplayerScalingModel`, and any mod subscriber
   models returned by `ModHelper`.
4. Each listener gets the corresponding virtual method on `AbstractModel`.

Global mod models can participate without being physically in a deck/relic list:

- `ModHelper.SubscribeForRunStateHooks(id, runState => models)`
- `ModHelper.SubscribeForCombatStateHooks(id, combatState => models)`

`id` is sorted ordinally, so subscriber ordering is deterministic.

## Run Creation

### Single Player

The normal single-player path starts in character select UI and ends at
`RunManager.EnterAct(0)`:

1. `NCharacterSelectScreen.StartNewSingleplayerRun(seed, acts)`
   plays transition audio/visuals and calls `NGame.StartNewSingleplayerRun`.
2. `NGame.StartNewSingleplayerRun(character, shouldSave, acts, modifiers, seed, gameMode, ascensionLevel, dailyTime)`
   builds an unlock state from progress and creates one player with net id `1`.
3. `RunState.CreateForNewRun(players, acts, modifiers, gameMode, ascensionLevel, seed)`
   creates `RunRngSet`, `RunOddsSet`, mutable acts, card state, and the
   `MultiplayerScalingModel`.
4. `RunManager.SetUpNewSinglePlayer(runState, shouldSave, dailyTime)`
   installs `NetSingleplayerGameService`, initializes synchronizers, applies
   new-run effects, and generates act room pools.
5. `NGame.StartRun(runState)` preloads run/act assets, finalizes starting relics,
   calls `RunManager.Launch()`, creates `NRun`, and calls
   `RunManager.EnterAct(0, doTransition: false)`.

Primary mod points:

- Add new content to pools before game initialization with `ModHelper.AddModelToPool`.
- Patch/observe `NGame.StartNewSingleplayerRun` or `NCharacterSelectScreen` for menu-driven run creation.
- Use `ModifierModel.OnRunCreated` / `OnRunLoaded` for run-level setup.
- Use `RunManager.RunStarted` if you patch into the singleton event.
- Use `Hook.AfterActEntered`, `Hook.AfterMapGenerated`, and `Hook.BeforeRoomEntered/AfterRoomEntered` for early gameplay setup.

### Multiplayer

Multiplayer run creation is parallel to single player, but players and settings
come from `StartRunLobby`.

1. Host/client character/custom/daily screens create a `StartRunLobby`.
2. Lobby players choose characters, seed, ascension, game mode, and modifiers.
3. `NCharacterSelectScreen.StartNewMultiplayerRun` or
   `NGame.StartNewMultiplayerRun(lobby, ...)` creates one `Player` per
   `LobbyPlayer`, preserving each player's `NetId` and unlock state.
4. `RunManager.SetUpNewMultiPlayer(runState, lobby, shouldSave, dailyTime)`
   uses lobby `NetService` and `InputSynchronizer`.
5. `RunManager.InitializeShared` creates:
   `ChecksumTracker`, `RunLocationTargetedMessageBuffer`, `FlavorSynchronizer`,
   `ActionQueueSet`, `ActionExecutor`, `ActionQueueSynchronizer`,
   `PlayerChoiceSynchronizer`, `MapSelectionSynchronizer`,
   `ActChangeSynchronizer`, `EventSynchronizer`, `RewardSynchronizer`,
   `RestSiteSynchronizer`, `OneOffSynchronizer`,
   `TreasureRoomRelicSynchronizer`, and `CombatReplayWriter`.
6. `RunManager.InitializeRunLobby` creates `RunLobby` and
   `CombatStateSynchronizer` for true multiplayer.
7. `NGame.StartRun` proceeds exactly like single player.

Primary mod points:

- If adding multiplayer-sensitive content, check `runState.Players.Count > 1`
  and `RunManager.Instance.NetService.Type`.
- Player-driven actions should become `GameAction`s and use
  `ActionQueueSynchronizer.RequestEnqueue`.
- Hooks that may ask for UI choices during combat should use
  `PlayerChoiceContext.SignalPlayerChoiceBegun/Ended`; `HookPlayerChoiceContext`
  turns that pause into a synchronized `GenericHookGameAction`.
- Do not mutate remote-only state directly. The host is authoritative for
  action enqueueing, map vote resolution, shared event option resolution, and
  act transition readiness.

### Saved Runs and Replays

Saved single-player load:

1. `NMainMenu.OnContinueButtonPressedAsync`
2. `RunState.FromSerializable(save)`
3. `RunManager.SetUpSavedSinglePlayer(runState, save)`
4. `NGame.LoadRun(runState, save.PreFinishedRoom)`
5. `RunManager.GenerateMap()`
6. `RunManager.LoadIntoLatestMapCoord(AbstractRoom.FromSerializable(preFinishedRoom, runState))`

Saved multiplayer load:

- `NMultiplayerLoadGameScreen`, custom run load, and daily load screens call
  `RunManager.SetUpSavedMultiPlayer(runState, LoadRunLobby)`.

Replay load:

- `RunManager.SetUpReplay(runState, CombatReplay)` uses `NetReplayGameService`
  and loaded serialized run data.

Important distinction: `InitializeSavedRun` validates rooms, restores saved maps
and map drawings, calls `modifier.OnRunLoaded(State)`, and does not call
`InitializeNewRun`.

## Run State Ownership

`RunState` owns durable gameplay state:

- `Players`
- mutable `Acts`
- current act index and act floor
- current `ActMap`
- visited map coordinates
- map point history
- current room stack
- visited event ids
- all live cards in the run
- `RunRngSet`, `RunOddsSet`, shared relic grab bag
- modifiers and `MultiplayerScalingModel`
- derived `UnlockState` across all players

`RunState.IterateHookListeners(combatState)` is the key listener collector.
Outside combat it includes active players' deck cards/enchantments, active
relics, potions, modifiers, multiplayer scaling, plus mod run subscribers.
Inside combat it also includes combat listeners via `CombatState`.

For hook eligibility, removed cards/potions, melted relics, and inactive players
are filtered out.

## Map Construction

Map generation is triggered by `RunManager.EnterAct` through
`SetActInternal(actIndex)`:

1. Set `State.CurrentActIndex`, clearing visited map coordinates and resetting
   act floor.
2. Reset unknown-map-point odds.
3. `PreloadManager.LoadActAssets(State.Act)`.
4. `RunManager.GenerateMap()`.

`GenerateMap()` does:

1. `MapSelectionSynchronizer.BeforeMapGenerated()` increments generation count
   and cancels stale multiplayer votes.
2. If a saved map exists for this act, load `SavedActMap`.
3. Otherwise call `State.Act.CreateMap(State, replaceTreasureWithElites: false)`,
   which normally returns `StandardActMap.CreateFor(runState, ...)`.
4. Call `Hook.ModifyGeneratedMap(State, map, actIndex)` for newly generated maps.
5. Call `Hook.AfterMapGenerated(State, map, actIndex)`.
6. If the run did not start with Neow, change the first act starting node from
   ancient to monster.
7. Assign `State.Map`, remove stale visited coordinates, and send it to
   `NMapScreen`.

`StandardActMap`:

- uses a seeded `Rng(runState.Rng.Seed, $"act_{actIndex + 1}_map")`.
- uses 7 columns.
- creates a starting map point at row 0, normal paths starting at row 1, a boss
  point after the grid, and optionally a second boss.
- generates 7 paths with crossover prevention.
- assigns fixed rows: first real row monsters, upper treasure/elite row,
  pre-boss rest row, boss, optional second boss.
- fills remaining points from `ActModel.GetMapPointTypes`.
- prunes/repairs paths and post-processes layout.

Map-type hooks:

- `ModifyGeneratedMap`
- `ModifyGeneratedMapLate`
- `AfterMapGenerated`
- `ModifyUnknownMapPointRoomTypes`
- `ModifyOddsIncreaseForUnrolledRoomType`
- `ShouldAllowFreeTravel`

## Map Movement

Single-player map selection usually calls `RunManager.EnterMapCoord(coord)`.

Flow:

1. `RunState.AddVisitedMapCoord(coord)`.
2. `RunManager.EnterMapCoordInternal(coord, preFinishedRoom, saveGame)`.
3. Resolve `MapPoint.PointType`.
4. `EnterMapPointInternal(actFloor, pointType, preFinishedRoom, saveGame)`.
5. Save current run if needed.
6. Roll a `RoomType`:
   - concrete map point types map directly.
   - `Unknown` calls `State.Odds.UnknownMapPoint.Roll(blacklist, State)`.
   - `Ancient` maps to `RoomType.Event`.
   - first-run tutorial can turn `Unassigned` into an event.
7. Create the room with `CreateRoom`.
8. Pause the action executor.
9. Append map point history.
10. Enter the room.

Multiplayer map movement uses `MapSelectionSynchronizer`:

1. Players vote via `VoteForMapCoordAction` / `NetVoteForMapCoordAction`.
2. Votes are stored by player slot and map generation count.
3. Once all players vote, the host randomly chooses one voted coord with
   `_multiplayerMapPointSelection`.
4. Host enqueues `MoveToMapCoordAction`.
5. All peers enter the chosen map coordinate through the same `RunManager` path.

Hook and patch points:

- `NMapScreen` and `NMapPoint` for UI selection, pings, drawing, vote display.
- `MapSelectionSynchronizer.PlayerVoteChanged`, `PlayerVoteCancelled`,
  `PlayerVotesCleared` for multiplayer map UX.
- `MoveToMapCoordAction`, `VoteForMapCoordAction` for action-level interception.
- `Hook.ShouldProceedToNextMapPoint` gates reward-screen progression.

## Room Entry And Exit

`RunManager` owns the room stack. `EnterRoom(room)` exits all current rooms and
then calls `EnterRoomInternal(room)`. `EnterRoomWithoutExitingCurrentRoom` pushes
a child room on top of the current room, used by event-triggered combat.

`EnterRoomInternal`:

1. Pushes the room on `RunState`.
2. Calls `Hook.BeforeRoomEntered(State, room)` unless restoring a room stack or
   entering the map room.
3. Calls `room.Enter(State, isRestoringRoomStackBase)`.
4. Updates music/ambience.
5. Marks room type visited in the act if this is the base room.
6. Updates `RunLocationTargetedMessageBuffer`.
7. Unpauses action executor for non-combat rooms.
8. Raises `RunManager.RoomEntered`.

Most concrete rooms call `Hook.AfterRoomEntered(runState, room)` inside their
`EnterInternal`, after creating/preparing room-specific state and UI.

Room types:

- `MapRoom`: map screen.
- `CombatRoom`: monster/elite/boss combat.
- `EventRoom`: normal event, shared event, ancient, or event layout combat.
- `TreasureRoom`: shared treasure relic selection plus rewards.
- `MerchantRoom`: merchant inventory and purchases.
- `RestSiteRoom`: rest-site options synchronized per player.

Exit is room-specific via `AbstractRoom.Exit`. `RunManager.ExitCurrentRooms`
pops until empty.

## Combat Room And Encounter Setup

`RunManager.CreateRoom(RoomType.Monster/Elite/Boss)` creates:

```csharp
new CombatRoom(State.Act.PullNextEncounter(roomType).ToMutable(), State)
```

`CombatRoom.StartCombat`:

1. Generate encounter monsters if not already generated.
2. Preload combat assets.
3. Create enemy creatures and add them to `CombatState`.
4. Record monster ids in map history.
5. Create `NCombatRoom`.
6. `CombatManager.SetUpCombat(CombatState)`.
7. `Hook.AfterRoomEntered(runState, this)`.
8. `CombatManager.AfterCombatRoomLoaded()`.

`CombatManager.SetUpCombat`:

- stores `_state`.
- calls `MultiplayerScalingModel.OnCombatEntered`.
- resets and populates each player's combat state.
- starts `NetCombatCardDb`.
- subscribes creatures to `CombatStateTracker`.
- raises `CombatSetUp`.

`CombatManager.StartCombatInternal`:

- starts encounter BGM if present.
- calls each creature `AfterAddedToRoom`.
- pauses action executor and marks sync state `NotPlayPhase`.
- sets `IsInProgress = true`.
- calls `Hook.BeforeCombatStart`.
- shows combat start UI/FTUE.
- starts the first turn.

## Combat Turn Loop

`CombatManager.StartTurn` handles both player and enemy sides.

Turn start flow:

1. Determine creatures and players starting the turn. Extra turns can restrict
   this to `_playersTakingExtraTurn`.
2. `Creature.BeforeTurnStart`.
3. `Hook.BeforeSideTurnStart`.
4. If player side:
   - enable player actions.
   - clear ready-to-end-turn and ready-to-begin-enemy-turn sets.
   - prepare enemy next moves, except during extra turns.
5. If enemy side:
   - show enemy turn banner.
6. `Creature.AfterTurnStart`.
7. `Hook.AfterBlockCleared`.
8. For each starting player, `SetupPlayerTurn`.
9. `Hook.AfterSideTurnStart`.
10. For player side:
    - process orb queue `AfterTurnStart`.
    - checksum.
    - dead or non-starting players auto-ready.
    - `Hook.BeforePlayPhaseStart`.
    - check win.
    - unpause action executor.
    - set action sync state `PlayPhase`.
    - raise `TurnStarted`.
11. For enemy side:
    - raise `TurnStarted`.
    - checksum.
    - execute enemy turns.

`SetupPlayerTurn`:

1. Reset/add energy, gated by `ShouldPlayerResetEnergy`.
2. `Hook.AfterEnergyReset`.
3. `Hook.BeforeHandDraw`.
4. `Hook.ModifyHandDraw`.
5. first turn: move bottom/innate cards and clamp innate draw.
6. `CardPileCmd.Draw(... fromHandDraw: true)`.
7. `Hook.AfterPlayerTurnStart`.

Card play:

- UI or commands enqueue `PlayCardAction` / `NetPlayCardAction`.
- `ActionExecutor` runs ready actions from `ActionQueueSet`.
- Card code eventually calls `CardModel.OnPlay` with a `PlayerChoiceContext`.
- Hook points around card play include `ShouldPlay`, `BeforeCardPlayed`,
  `AfterCardPlayed`, `AfterCardPlayedLate`, card movement hooks, damage/block
  modification hooks, and resource spend hooks.

Player end-turn flow:

1. `PlayerCmd.EndTurn` calls `CombatManager.SetReadyToEndTurn`.
2. In multiplayer, all players must be ready. Single player proceeds immediately.
3. `AfterAllPlayersReadyToEndTurn` sets sync state `EndTurnPhaseOne`.
4. Wait for player-driven action queue to drain or pause.
5. `EndPlayerTurnPhaseOneInternal`:
   - `Hook.BeforeTurnEnd`.
   - per-player orb `BeforeTurnEnd`.
   - exhaust ethereal cards.
   - run turn-end-in-hand cards.
   - `Hook.BeforeFlush`.
   - checksum.
6. Enqueue `ReadyToBeginEnemyTurnAction`.
7. When all players ready to begin enemy turn, `AfterAllPlayersReadyToBeginEnemyTurn`:
   - sync state `NotPlayPhase`.
   - raise `AboutToSwitchToEnemyTurn`.
   - `EndPlayerTurnPhaseTwoInternal`.
   - discard non-retained cards if `ShouldFlush`.
   - retain retained cards and call `AfterCardRetained`.
   - switch sides and start enemy turn.

Enemy turn:

1. For each enemy still in combat:
   - node performs intent animation.
   - `enemy.TakeTurn()`.
   - wait if paused.
   - check win/loss.
2. checksum.
3. `EndEnemyTurn`.
4. `EndEnemyTurnInternal`:
   - `Hook.BeforeTurnEnd`.
   - player combat end-of-turn cleanup.
   - `Hook.AfterTurnEnd`.
5. check win.
6. switch back to player side and `StartTurn()`.

Combat victory:

1. `CombatManager.IsEnding` becomes true when no primary enemy is alive unless
   `Hook.ShouldStopCombatFromEnding` returns true.
2. `CheckWinCondition` calls `EndCombatInternal`.
3. `EndCombatInternal`:
   - clears combat flags.
   - revives players before combat end.
   - `Hook.AfterCombatEnd`.
   - `room.OnCombatEnded()`.
   - writes replay.
   - player `AfterCombatEnd`.
   - `Hook.AfterCombatVictoryEarly` and `AfterCombatVictory`.
   - saves turns taken.
   - if final boss/second boss, records win time.
   - marks combat pre-finished.
   - saves run with prefinished room.
   - enables map travel.
   - updates progress, achievements, music.
   - raises `CombatWon` and `CombatEnded`.

Combat loss:

1. Death logic in `CreatureCmd` detects game over.
2. `CombatManager.LoseCombat()` stores pending loss.
3. `RunManager.OnEnded(isVictory: false)` serializes and uploads/records run
   history, metrics, achievements, and deletes current run saves as applicable.
4. `NRun.ShowGameOverScreen(serializableRun)` pushes `NGameOverScreen`.

## Events

Events are driven by `EventRoom` and `EventSynchronizer`.

`EventRoom.EnterInternal`:

1. Preload event assets.
2. `RunManager.EventSynchronizer.BeginEvent(CanonicalEvent, IsPreFinished, OnStart)`.
3. Subscribe each local mutable event's `StateChanged`.
4. If `LayoutType == Combat`, generate internal combat state.
5. Create `NEventRoom` unless restoring a room stack base.
6. `Hook.AfterRoomEntered`.
7. local event `AfterEventStarted`.

`EventSynchronizer.BeginEvent` creates one mutable `EventModel` per player and
calls `eventModel.BeginEvent(player, isPrefinished)`.

`EventModel.BeginEvent`:

1. assigns owner.
2. seeds event RNG from run seed, owner net id for non-shared events, and event id.
3. `BeforeEventStarted`.
4. `CalculateVars`.
5. initial event state via `GenerateInitialOptions`.

Choosing options:

- `NEventRoom` calls `EventSynchronizer.ChooseLocalOption(index)`.
- Non-shared events choose immediately for the local player and send
  `OptionIndexChosenMessage`.
- Shared events store votes from all players. Host randomly chooses among voted
  option indices and sends `SharedEventOptionChosenMessage`.
- `EventOption.Chosen` invokes `BeforeChosen` and then the option callback.
- Choices are saved into map point history unless
  `ThatWontSaveToChoiceHistory()` was used.

Event-triggered combat:

- Shared events can call `EventModel.EnterCombatWithoutExitingEvent`.
- This creates a `CombatRoom` over the current event room with
  `ParentEventId` and `ShouldResumeParentEventAfterCombat`.
- `RunManager.EnterRoomWithoutExitingCurrentRoom` pushes the combat room.
- After rewards, `RunManager.ProceedFromTerminalRewardsScreen` resumes or exits.

Useful event extension points:

- subclass `EventModel` and implement `GenerateInitialOptions`.
- `IsAllowed(IRunState)` controls event eligibility.
- `CalculateVars`, `BeforeEventStarted`, `AfterEventStarted`,
  `OnEventFinished`, `Resume`.
- `EventOption.BeforeChosen`.
- `Hook.ModifyNextEvent`.
- `RunManager.EventSynchronizer` for shared/non-shared option synchronization.

## Ancients

`AncientEventModel` is a specialized `EventModel`:

- `LocTable` is `ancients`.
- layout is `EventLayoutType.Ancient`.
- exposes map icon paths for ancient map nodes.
- defines an `AncientDialogueSet`.
- has `AllPossibleOptions`.
- `BeforeEventStarted` heals the player to full, modified by ascension
  `WearyTraveler`; Neow sets current HP to 0 first for the intro lerp.
- `GenerateInitialOptionsWrapper` checks `Hook.ShouldAllowAncient`.
- blocked ancients present only a proceed option.
- if no options or loading prefinished, `StartPreFinished` marks done.
- `Done()` saves ancient choices to history and finishes.

Ancient map placement:

- `ActModel.GenerateRooms` chooses `_rooms.Ancient` from unlocked act ancients
  plus shared ancient subsets.
- `RunManager.GenerateRooms` distributes shared ancients among later acts.
- `StandardActMap` sets `StartingMapPoint.PointType = Ancient`.
- If the run did not start with Neow and act index is 0, `RunManager.GenerateMap`
  changes the starting map point to `Monster`.

## Rewards, Treasure, Merchant, Rest Sites

Reward flow is spread through reward classes and `RewardsCmd`, but key hooks are:

- `BeforeRewardsOffered`
- `ModifyRewards` / `ModifyRewardsLate`
- `AfterModifyingRewards`
- `AfterRewardTaken`
- card reward creation and option hooks:
  `ModifyCardRewardCreationOptions`, `ModifyCardRewardOptions`,
  `ModifyCardRewardAlternatives`, `ModifyCardRewardUpgradeOdds`,
  `ShouldAllowSelectingMoreCardRewards`

Treasure:

- `TreasureRoom.EnterInternal` creates `NTreasureRoom`, calls
  `Hook.AfterRoomEntered`, and starts
  `TreasureRoomRelicSynchronizer.BeginRelicPicking`.
- `TreasureRoomRelicSynchronizer` creates shared relic options, collects votes,
  resolves result, and enqueues `PickRelicAction`.
- `TreasureRoom.DoNormalRewards` uses `OneOffSynchronizer`.
- `TreasureRoom.DoExtraRewardsIfNeeded` calls `RewardsCmd.OfferForRoomEnd`.
- `Hook.ShouldGenerateTreasure` gates treasure behavior.

Merchant:

- `MerchantRoom.EnterInternal` creates `MerchantInventory` for the local player,
  preloads assets, creates `NMerchantRoom`, then `Hook.AfterRoomEntered`.
- Purchases call reward synchronizer methods like
  `SyncLocalGoldLost`, `SyncLocalObtainedCard`, `SyncLocalObtainedRelic`,
  `SyncLocalObtainedPotion`.
- `MerchantRoom.Exit` writes unpicked shop choices to history.
- Hooks include `ModifyMerchantCardPool`, `ModifyMerchantCardRarity`,
  `ModifyMerchantCardCreationResults`, `ModifyMerchantPrice`,
  `ShouldAllowMerchantCardRemoval`, `ShouldRefillMerchantEntry`,
  `AfterItemPurchased`.

Rest site:

- `RestSiteRoom.EnterInternal` calls `RestSiteSynchronizer.BeginRestSite`,
  preloads assets with current options, creates `NRestSiteRoom`, then
  `Hook.AfterRoomEntered`.
- Hooks include `ModifyRestSiteOptions`, `ModifyRestSiteHealAmount`,
  `ModifyRestSiteHealRewards`, `AfterRestSiteHeal`, `AfterRestSiteSmith`,
  `ShouldDisableRemainingRestSiteOptions`.
- Some options, such as `MendRestSiteOption`, use `PlayerChoiceSynchronizer` to
  sync target choices.

## Multiplayer Synchronization

Multiplayer uses several layers:

### Action Queue

`ActionQueueSynchronizer` converts local `GameAction`s into network messages.

- Client sends `RequestEnqueueActionMessage` to host.
- Host enqueues and broadcasts `ActionEnqueuedMessage`.
- Single player and host enqueue directly.
- `GameAction.ToNetAction()` / `INetAction.ToGameAction(player)` bridge local
  actions and network payloads.
- Combat action state:
  - `NotInCombat`: cancel combat/deferred actions.
  - `NotPlayPhase`: pause player queues.
  - `PlayPhase`: unpause and request deferred play-phase actions.
  - `EndTurnPhaseOne`: cancel all player-driven combat actions.

Hook actions:

- `HookPlayerChoiceContext` creates `GenericHookGameAction` when a model hook
  requests a player choice.
- That hook action is synchronized with `RequestEnqueueHookActionMessage` /
  `HookActionEnqueuedMessage`.
- The original hook task pauses until the synchronized choice resumes.

### Player Choices

`PlayerChoiceSynchronizer` assigns per-player choice ids:

- `ReserveChoiceId(player)`
- `SyncLocalChoice(player, choiceId, result)`
- `WaitForRemoteChoice(player, choiceId)`

Use this for non-action choices where a remote player's selected card/target/etc.
must be known deterministically.

### Map, Act, Event, Reward, Rest, Treasure

- `MapSelectionSynchronizer`: all-player map voting, host chooses destination.
- `ActChangeSynchronizer`: all players ready before next act.
- `EventSynchronizer`: one event model per player, non-shared option messages,
  shared event voting and host choice.
- `RewardSynchronizer`: sync obtained/skipped cards, relics, potions, gold.
- `RestSiteSynchronizer`: per-player rest-site options.
- `TreasureRoomRelicSynchronizer`: shared treasure relic vote/pick resolution.
- `OneOffSynchronizer`: one-off deterministic local rewards.
- `PeerInputSynchronizer` and `HoveredModelTracker`: remote cursor/focus/screen
  and hover state for UI awareness.
- `CombatStateSynchronizer` and `ChecksumTracker`: load/sync windows and
  divergence detection.

## Menus And UI Flow

There is no general `Hook.cs` equivalent for menus. Menu integration is by
patching or extending Godot nodes/screens, or by intercepting methods/signals.

Main menu:

- `NGame.LoadMainMenu` creates `NMainMenu`.
- `NMainMenu` owns top-level submenu stack and buttons.
- Continue flow:
  `NMainMenu.OnContinueButtonPressedAsync` -> `RunState.FromSerializable` ->
  `RunManager.SetUpSavedSinglePlayer` -> `NGame.LoadRun`.
- Main-menu abandon uses `NAbandonRunConfirmPopup` and deletes saved run data
  rather than killing an active run.

Character select:

- `NCharacterSelectScreen` implements `IStartRunLobbyListener` and
  `ICharacterSelectButtonDelegate`.
- Single-player initialize uses a `StartRunLobby` with `NetSingleplayerGameService`.
- Host/client initialize use real multiplayer services.
- Start buttons call `StartNewSingleplayerRun` or `StartNewMultiplayerRun`.

Custom/daily:

- `NCustomRunScreen` and `NDailyRunScreen` create `StartRunLobby` with
  `GameMode.Custom` or `GameMode.Daily`.
- Custom run has modifier and seed UI.
- Daily run has time-server/daily payload setup and score upload via
  `DailyRunUtility`.
- Load screens mirror saved multiplayer load.

Pause/menu in-run:

- `NPauseMenu` opens settings, compendium, give-up, disconnect, save-and-quit.
- Give up creates `NAbandonRunConfirmPopup`, which calls `RunManager.Abandon`
  for active runs.
- Save-and-quit calls `NGame.ReturnToMainMenu`, which saves/cleans up.
- Disconnect confirms if net service is connected; otherwise returns to menu.

Overlays and modals:

- `NOverlayStack` handles overlays such as reward and game-over screens.
- `NModalContainer` handles modal popups and updates `ActiveScreenContext`.
- `NCapstoneContainer` handles capstone/submenu-style screens.
- `ActiveScreenContext` and `IScreenContext` are important for controller focus
  and currently active UI behavior.

Map UI:

- `NMapScreen` displays map, map drawings, votes, travel state.
- `NMapPoint` subclasses represent normal/boss/ancient map points.
- Multiplayer map drawings and votes are UI-level sync surfaces.

Game over:

- `CreatureCmd` calls `RunManager.OnEnded(false)` and `NRun.ShowGameOverScreen`.
- Victory calls `RunManager.OnEnded(true)` and then kills all players to enter
  the same end-of-run presentation pipeline.
- `NGameOverScreen` shows score, badges, discoveries, run summary, leaderboard,
  and return-to-menu behavior.

## Ways Runs Start

- New standard single player: `NCharacterSelectScreen` ->
  `NGame.StartNewSingleplayerRun`.
- New standard multiplayer: `StartRunLobby` ->
  `NGame.StartNewMultiplayerRun`.
- New custom single/multiplayer: `NCustomRunScreen` with `GameMode.Custom`.
- New daily single/multiplayer: `NDailyRunScreen` with `GameMode.Daily`.
- Continue saved single player: `NMainMenu.OnContinueButtonPressedAsync`.
- Continue saved multiplayer/custom/daily: respective load screens with
  `LoadRunLobby`.
- Debug bootstrap: `NSceneBootstrapper`, `NMultiplayerTest`, and
  `RunManager.EnterRoomDebug` paths.
- Replay: `RunManager.SetUpReplay`.

## Ways Runs End Or Leave Gameplay

- Combat loss: all players dead -> `CreatureCmd` ->
  `RunManager.OnEnded(false)` -> `NRun.ShowGameOverScreen`.
- Victory: final act victory room `TheArchitect` ->
  `RunManager.WinRun` -> `OnEnded(true)` -> kill all players for end screen.
- Active run abandon/give up: `RunManager.Abandon` -> multiplayer lobby abandon
  or local `AbandonInternal` -> kill all players.
- Main-menu saved-run abandon: `NMainMenu.AbandonRun` deletes save/progress
  bookkeeping for the saved run.
- Save and quit: `NPauseMenu.CloseToMenu` -> `NGame.ReturnToMainMenu` ->
  `SaveManager.SaveRun` path and `RunManager.CleanUp`.
- Disconnect/error/divergence: net error flows call
  `ReturnToMainMenuWithError` and/or `RunManager.CleanUp`.
- Normal cleanup: `RunManager.CleanUp` disposes synchronizers, resets combat,
  clears UI containers, disconnects net service, clears local context and state.

`RunManager.OnEnded` is the central durable end-of-run method. It updates map
history player stats, serializes the run, updates progress and achievements,
creates run history, uploads metrics/daily score, and deletes current run saves
for single-player or host multiplayer.

## Hook Surface By Gameplay Area

This is not every method on `AbstractModel`, but it covers the major families a
modder normally needs.

Run/map/room:

- `AfterActEntered`
- `AfterMapGenerated`
- `ModifyGeneratedMap`, `ModifyGeneratedMapLate`
- `ModifyUnknownMapPointRoomTypes`
- `ModifyOddsIncreaseForUnrolledRoomType`
- `BeforeRoomEntered`, `AfterRoomEntered`
- `ShouldProceedToNextMapPoint`
- `ShouldAllowFreeTravel`

Combat lifecycle:

- `BeforeCombatStart`, `BeforeCombatStartLate`
- `AfterCombatEnd`
- `AfterCombatVictoryEarly`, `AfterCombatVictory`
- `ShouldStopCombatFromEnding`
- `AfterCreatureAddedToCombat`
- `BeforeDeath`, `AfterDeath`
- `ShouldDie`, `ShouldDieLate`
- `ShouldCreatureBeRemovedFromCombatAfterDeath`
- `AfterPreventingDeath`

Turns:

- `BeforeSideTurnStart`
- `AfterSideTurnStart`, `AfterSideTurnStartLate`
- `AfterPlayerTurnStartEarly`, `AfterPlayerTurnStart`,
  `AfterPlayerTurnStartLate`
- `BeforePlayPhaseStart`, `BeforePlayPhaseStartLate`
- `BeforeTurnEndVeryEarly`, `BeforeTurnEndEarly`, `BeforeTurnEnd`
- `AfterTurnEnd`, `AfterTurnEndLate`
- `ShouldTakeExtraTurn`

Cards and piles:

- `BeforeCardPlayed`
- `AfterCardPlayed`, `AfterCardPlayedLate`
- `BeforeCardAutoPlayed`, `AfterCardGeneratedForCombat`
- `AfterCardEnteredCombat`
- `AfterCardChangedPiles`, `AfterCardChangedPilesLate`
- `AfterCardDrawnEarly`, `AfterCardDrawn`
- `AfterCardDiscarded`
- `AfterCardExhausted`
- `AfterCardRetained`
- `BeforeCardRemoved`
- `ShouldPlay`, `ShouldDraw`, `ShouldFlush`, `ShouldEtherealTrigger`
- `ModifyCardPlayCount`
- `ModifyCardPlayResultPileTypeAndPosition`
- `ModifyShuffleOrder`
- `AfterShuffle`

Damage/block/hp:

- `BeforeAttack`, `AfterAttack`
- `ModifyAttackHitCount`
- `ModifyDamageAdditive`, `ModifyDamageMultiplicative`, `ModifyDamageCap`
- `AfterModifyingDamageAmount`
- `BeforeDamageReceived`, `AfterDamageReceived`, `AfterDamageReceivedLate`
- `AfterDamageGiven`
- `BeforeBlockGained`, `AfterBlockGained`, `AfterBlockBroken`,
  `AfterBlockCleared`
- `ModifyBlockAdditive`, `ModifyBlockMultiplicative`
- `ShouldClearBlock`
- `ModifyHpLostBeforeOsty`, `ModifyHpLostBeforeOstyLate`
- `ModifyHpLostAfterOsty`, `ModifyHpLostAfterOstyLate`
- `AfterCurrentHpChanged`

Resources:

- `ShouldPlayerResetEnergy`
- `AfterEnergyReset`, `AfterEnergyResetLate`
- `ModifyMaxEnergy`
- `ModifyEnergyGain`
- `AfterEnergySpent`
- `ShouldGainStars`, `AfterStarsGained`, `AfterStarsSpent`
- `ShouldPayExcessEnergyCostWithStars`
- `ModifyXValue`

Powers/orbs/summons:

- `BeforePowerAmountChanged`, `AfterPowerAmountChanged`
- `ModifyPowerAmountGiven`, `ModifyPowerAmountReceived`
- `AfterModifyingPowerAmountGiven`, `AfterModifyingPowerAmountReceived`
- `ShouldPowerBeRemovedOnDeath`
- `AfterOrbChanneled`, `AfterOrbEvoked`
- `ModifyOrbPassiveTriggerCounts`, `AfterModifyingOrbPassiveTriggerCount`
- `ModifyOrbValue`
- `ModifySummonAmount`, `AfterSummon`

Rewards/deck/shop/rest/potions:

- `BeforeRewardsOffered`
- `ModifyRewards`, `ModifyRewardsLate`, `AfterModifyingRewards`
- `AfterRewardTaken`
- `ModifyCardRewardCreationOptions`, `ModifyCardRewardCreationOptionsLate`
- `TryModifyCardRewardOptions`
- `ModifyCardRewardAlternatives`
- `ModifyCardRewardUpgradeOdds`
- `ModifyCardBeingAddedToDeck`
- `ShouldAddToDeck`, `AfterAddToDeckPrevented`
- `ShouldAllowSelectingMoreCardRewards`
- `AfterGoldGained`, `ShouldGainGold`
- `ModifyMerchantCardPool`, `ModifyMerchantCardRarity`,
  `ModifyMerchantCardCreationResults`, `ModifyMerchantPrice`
- `AfterItemPurchased`
- `ShouldAllowMerchantCardRemoval`, `ShouldRefillMerchantEntry`
- `ModifyRestSiteOptions`, `ModifyRestSiteHealAmount`,
  `ModifyRestSiteHealRewards`
- `AfterRestSiteHeal`, `AfterRestSiteSmith`
- `ShouldDisableRemainingRestSiteOptions`
- `BeforePotionUsed`, `AfterPotionUsed`
- `AfterPotionDiscarded`, `AfterPotionProcured`
- `ShouldProcurePotion`, `ShouldForcePotionReward`

Events/ancients:

- `EventModel.IsAllowed`
- `EventModel.CalculateVars`
- `EventModel.BeforeEventStarted`
- `EventModel.AfterEventStarted`
- `EventModel.Resume`
- `EventModel.OnEventFinished`
- `Hook.ModifyNextEvent`
- `Hook.ShouldAllowAncient`

## Practical Hooking Guidance

- Prefer existing model hooks over UI patches when changing gameplay.
- Use `ModHelper.SubscribeForRunStateHooks` for run-wide effects that are not
  attached to a card/relic/potion/modifier.
- Use `ModHelper.SubscribeForCombatStateHooks` for combat-only global effects.
- In multiplayer, any player-driven deterministic mutation should go through a
  synchronized `GameAction`.
- If a hook can open a target/card/select UI during combat, use the provided
  `PlayerChoiceContext`; do not block only the local peer.
- For map changes, use `ModifyGeneratedMap`/`ModifyGeneratedMapLate` and keep
  `RunState.RemoveStaleVisitedMapCoords` implications in mind.
- For menu changes, target screen nodes (`NMainMenu`, `NCharacterSelectScreen`,
  `NCustomRunScreen`, `NDailyRunScreen`, `NPauseMenu`, `NOverlayStack`,
  `NModalContainer`) because menu code does not use the gameplay hook dispatcher.
- For new events/ancients/encounters/cards/relics, add to the appropriate model
  pool before the pool is frozen by `ModHelper.ConcatModelsFromMods`.
- Avoid changing state in visual-only nodes unless the source flow already does
  so. Most durable state belongs in `RunState`, models, commands, or actions.

