# Issues

## Active Blockers

- Entering the first monster can still halt in `state_type: monster` with `Combat ended. Waiting for rewards...`, but with no battle, rewards, event, card-selection, card-reward, or map state available. This has reproduced after all four players completed Neow prompts, all four slots were verified on `map`, and all four successfully voted into combat. Run 009 reproduced it after the final vote response said `travel queued to Monster`; Run 011 reproduced it again with all four slots alive at full HP on floor 2; Run 012 reproduced it again after a successful Precise Scissors removal and all four map votes; Run 013 reproduced it again after a recovered Neow sequence and all four votes into `(3,1)`; Run 014 reproduced it after a clean Neow sequence with Arcane Scroll, Stone Humidifier, Golden Pearl, and New Leaf.

- A slot can receive a Neow reward and later choose a second Neow reward if its per-slot completion state remains or returns to the initial Neow options. In Run 009, Silent received Scroll Boxes, completed bundle selection, then later still showed initial Neow options and successfully chose Arcane Scroll too.

- State reads can fail during Neow reward/card prompt cleanup with errors like `Failed to read couch state: Canonical model of type MegaCrit.Sts2.Core.Models.Cards.SwordBoomerang used in incorrect place.` In Run 013 this happened after New Leaf/Pomander/Small Capsule prompt handling; the run recovered after follow-up actions, but slots 1 and 3 were temporarily unreadable.

- Small Capsule can surface a relic reward that does not appear to be retained after claiming/proceeding. In Run 013, Defect saw Red Mask as a claimable reward, `claim_reward` returned a blank response, and after `proceed` the visible relic list still only showed Cracked Core and Small Capsule.

## Active Quirks / Improvements

- Neow/event proceed state is still hard to reason about from the client. Some slots can be done and report `waiting_for_all_players: true` while other slots are still at initial Neow choices. The `proceed` response says `Waiting for the other local players to finish the event`, but does not identify which slots are unfinished.

- Event states can show `can_proceed: true` with no proceed option in top-level `event.options`; generic `proceed` is still needed in those cases.

- Starting a run can briefly emit `state=unknown` updates before the map state is ready.

- State payloads can temporarily include another slot's card-selection prompt. In Run 013, Defect's `rewards` state and Necrobinder's Neow-done `event` state both included Ironclad's New Leaf transform `card_select` payload.

## Not Currently Reproducing

- Game over now reports `state_type: game_over` in recent runs instead of generic `overlay` / `NGameOverScreen`.

- The old map-vote early-finalization wording has not reappeared in recent runs; recent final votes completed on the fourth slot.

- Run 010 did not reproduce the multiple-Neow-reward bug after Silent chose and completed Scroll Boxes.

- Run 010 did not reproduce the first-combat `Combat ended. Waiting for rewards...` blocker; the first Nibbit combat was fully actionable and ended in `game_over`.

- Run 011 also did not reproduce the multiple-Neow-reward bug after four varied Neow choices, including two New Leaf transforms.

- Run 012 did not reproduce the multiple-Neow-reward bug after Precise Scissors, Fishing Rod, Hefty Tablet, and Nutritious Oyster. The Scissors card-removal prompt completed and did not lead to another reward offer.

- Run 013 did not reproduce the multiple-Neow-reward bug after New Leaf, Pomander, Small Capsule, and Arcane Scroll.

- Run 014 did not reproduce the multiple-Neow-reward bug after Arcane Scroll, Stone Humidifier, Golden Pearl, and New Leaf. It also did not reproduce Run 013's cross-slot prompt leak or canonical model state-read error.
