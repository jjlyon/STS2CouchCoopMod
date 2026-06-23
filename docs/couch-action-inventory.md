# Couch Action Inventory

This inventory keeps couch co-op aligned with native STS2 multiplayer behavior.
When adding or fixing a phone action, classify it here first, then put gameplay
mutation in the matching adapter layer.

| Action | Slot-aware player source | Native primitive used | Couch shim used | Reflection/UI dependency | Test scenario |
| --- | --- | --- | --- | --- | --- |
| `menu_select` | none | existing menu action handler | none | scene-tree menu/popup UI | Dismiss FTUE or start menu prompt before run |
| `play_card` | `RunState.Players[slot]` | `PlayCardAction` via shared MCP action path | scoped `LocalContext` | combat hand/card UI guards | Slot 0 and slot 1 each play a legal combat card |
| `use_potion` | `RunState.Players[slot]` | shared MCP potion action path | scoped `LocalContext` | potion targeting UI/state | Slot 0 and slot 1 use or reject a potion action correctly |
| `discard_potion` | `RunState.Players[slot]` | shared MCP potion discard path | scoped `LocalContext` | potion inventory UI/state | Slot 0 and slot 1 discard a potion when available |
| `end_turn` | `RunState.Players[slot]` | `CombatManager.SetReadyToEndTurn` in MP | singleplayer fallback only outside couch MP | `NCombatRoom.Ui.Hand` guard | Both slots ready; enemy turn begins after all ready |
| `undo_end_turn` | `RunState.Players[slot]` | `CombatManager.UndoReadyToEndTurn` | none | none | Slot marks ready, undoes, then can play/end again |
| `choose_map_node` | `RunState.Players[slot]` | `VoteForMapCoordAction` through `ActionQueueSynchronizer` | none | `NMapScreen`/`NMapPoint` to map phone index to coord | Both slots vote; final vote queues travel |
| `claim_treasure_relic` | `RunState.Players[slot]` | `PickRelicAction` through `ActionQueueSynchronizer` | none | `TreasureRoomRelicSynchronizer.CurrentRelics` | Both slots vote for treasure relic |
| `proceed` | `RunState.Players[slot]` | boss `VoteToMoveToNextActAction`; shared proceed path | event/shop completion markers | overlay check; event map return facade | Rewards proceed, boss act transition, event/shop waiting |
| `choose_event_option` | `RunState.Players[slot]` | `EventSynchronizer` event model | event completion marker | `PlayerVotedForSharedOptionIndex`; `ChooseOptionForEvent` | Shared event vote and non-shared event choice |
| `advance_dialogue` | `RunState.Players[slot]` | shared MCP dialogue path | scoped `LocalContext` | event dialogue UI | Ancient/event dialogue advances for selected slot |
| `choose_rest_option` | `RunState.Players[slot]` | `RestSiteSynchronizer` options | scoped `LocalContext` | internal `ChooseOption` facade | Slot 0 and slot 1 select rest options |
| `shop_purchase` | `RunState.Players[slot]` | normal merchant purchase for slot 0 | per-slot `MerchantInventory` for remote slots | merchant entry purchase wrapper | Slot 1 buys from its couch inventory |
| `claim_reward` | `RunState.Players[slot]` | shared reward selection path | pending remote reward set | reward overlay for local slot; pending phone reward for remote | Remote slot claims gold/card/relic reward |
| `select_card_reward` | `RunState.Players[slot]` | shared card reward path | pending remote reward set as needed | card reward overlay | Remote slot selects card reward |
| `skip_card_reward` | `RunState.Players[slot]` | shared skip reward path | pending remote reward set as needed | card reward overlay | Remote slot skips card reward |
| `select_card` / `confirm_selection` / `cancel_selection` | `RunState.Players[slot]` | `PlayerChoiceSynchronizer` choice id reservation | pending card choice service | native card selection screens for local slot | Slot 1 handles deck/hand/card choice prompt |
| `select_bundle` / `confirm_bundle_selection` / `cancel_bundle_selection` | `RunState.Players[slot]` | choice id reservation | pending bundle choice service | native bundle UI for local slot | Slot 1 handles bundle choice prompt |
| `combat_select_card` / `combat_confirm_selection` | `RunState.Players[slot]` | choice context signals | pending combat hand choice service | hand selection UI for local slot | Slot 1 handles discard/hand select prompt |
| `select_relic` / `skip_relic_selection` | `RunState.Players[slot]` | choice id reservation | pending relic choice service | native relic selection UI for local slot | Slot 1 handles relic choice prompt |
| `crystal_sphere_set_tool` / `crystal_sphere_click_cell` / `crystal_sphere_proceed` | `RunState.Players[slot]` | shared Crystal Sphere action path | scoped `LocalContext` | Crystal Sphere screen UI | Slot-specific Crystal Sphere interaction |

## Adapter Rules

- Native-backed actions should prefer `GameAction`s or synchronizers and should
  not make gameplay decisions in the phone server.
- Couch UI shims are allowed only where a real remote client would normally own
  a local overlay, hand, merchant inventory, reward screen, or choice prompt.
- Compatibility fallbacks must be wrapped in a named facade in
  `McpMod.CouchAdapters.cs` or `CouchRunBootstrapper.cs`, with the decompiled
  target method named in a comment.
- All local player identity changes must go through `UseCouchLocalContext`.
