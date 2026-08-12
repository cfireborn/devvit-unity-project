# Story Through the Second Goal — Implementation and Recovery Runbook

Last reconciled with `Assets/Levels/SimpleLevel.unity` and the supporting scripts on 2026-08-12.

This document is the source of truth for the playable narrative from game start through the end of the Second Story Goal. It is written for designers, programmers, and future agents who need to understand, test, repair, or extend the sequence without accidentally reintroducing the removed tutorial branches.

## Verification Status

- The scene references and six-event sequence described below were installed through the Unity Editor and passed an Editor-side persistent-listener audit.
- The dialogue assets are present and non-empty.
- The delivery-cloud/postbox scene branch and the fixed-ladder tutorial callbacks are absent from the shipping scene.
- The local-player interaction guard is present in `InteractionTrigger.IsAllowed`.
- A complete post-fix host and remote-client playthrough is still required. Do not describe the sequence as runtime-verified until a human has completed the checklist in this document after a scene reload/domain reload.

The distinction between serialized validation and a runtime playthrough is intentional. The first failed implementation looked correct in discussion but had not been saved into `SimpleLevel.unity`, so Play Mode continued to execute the old callbacks.

## Intended Playable Sequence

The streamlined story is linear:

1. Gray greets Hermes and asks Hermes to deliver a letter to Spike.
2. Completing Gray's dialogue creates `Deliver Gray's Letter to Spike`.
3. Reaching Spike completes the first goal.
4. The portraitless `COMPERSION` definition appears before Spike speaks.
5. Completing the title opens Spike's reply dialogue.
6. Completing Spike's dialogue creates `Return Spike's Reply to Gray`.
7. Reaching Gray completes the second goal and opens Gray's return dialogue.
8. Completing Gray's dialogue opens the end-of-demo panel.
9. The panel apologizes for the unfinished game, thanks the player, and links to both the narrative script and the studio mailing list.

[Read the full narrative script](https://docs.google.com/document/d/106QIZJeDZGRbEJ3huw_ZdnunQI2Nq3jT-q7ZVcbd3fE/edit?tab=t.0#heading=h.emwin8ig3aqr).

## Deliberate Cuts and Adaptations

These are design decisions, not missing implementation:

- **No fixed ladder tutorial.** Players enter the world with the normal multiplayer ladder system already operating. Gray's opening dialogue must not enable a `LadderTrigger` or call `BuildLadder`.
- **No ordinary-mail/postbox side goal.** Spike does not offer a randomly placed delivery-cloud or postbox goal. The story moves directly back to Gray.
- **No goal-choice screen in this sequence.** There is only one active story delivery at a time.
- **No flower choice.** The current opening dialogue treats the pressed flower as part of Gray's letter. Adding a choice would require a dialogue-choice feature and is outside the no-new-feature adaptation.
- **The title card uses the existing dialogue UI.** `CompersionTitleDialogue` has no portrait, so `DialogueUI` hides the portrait and displays the definition. A separate cinematic title-card system was intentionally not added.
- **The demo ends after Gray's response.** The end panel links to the complete narrative rather than pretending later beats are implemented.

The old delivery-cloud classes and prefabs may remain in the repository for future experiments, but they are disabled by `OptionalGameplayFeatures.DeliveryAndGoalSystemEnabled == false` and are not referenced by this story scene.

## Files That Own the Sequence

| Responsibility | File or asset |
|---|---|
| Shipping scene and UnityEvents | `Assets/Levels/SimpleLevel.unity` |
| Opening dialogue | `Assets/UI/Assets/Dialogue/TutorialLevel/KoiTutorialDialogue.asset` |
| Spike reply dialogue | `Assets/UI/Assets/Dialogue/TutorialLevel/SpikeTutorialDialogue_1.asset` |
| COMPERSION definition | `Assets/UI/Assets/Dialogue/TutorialLevel/CompersionTitleDialogue.asset` |
| Gray's second-goal response | `Assets/UI/Assets/Dialogue/TutorialLevel/GrayReturnDialogue.asset` |
| Dialogue step data | `Assets/Scripts/Game/Dialogue/DialogueInstance.cs` |
| Dialogue display and completion event | `Assets/Scripts/UI/DialogueUI.cs` |
| Dialogue trigger-to-dialogue bridge | `Assets/Scripts/Game/GameLogic/DialogueTrigger.cs` |
| Goal creation and assignment | `Assets/Scripts/Game/GameLogic/GoalAssignmentTrigger.cs` |
| Goal validation and completion | `Assets/Scripts/Game/GameLogic/GoalCompletionTrigger.cs` |
| Local-player trigger filtering | `Assets/Scripts/Game/GameLogic/InteractionTrigger.cs` |
| Local goal list and primary goal | `Assets/Scripts/Player/PlayerControllerM.cs` |
| Goal HUD | `Assets/UI/UI.prefab` |
| End panel and external URLs | `Assets/Scripts/UI/GameUIManager.cs` |

`SpikeTutorialDialogue_2.asset` is an empty retired asset. It is not referenced by `SimpleLevel`. Do not assign it to a live `DialogueTrigger`: `DialogueUI` closes an empty instance without raising the completion event, which stalls any chain waiting for that dialogue to finish.

## Scene Component Layout

Use the component type, assigned asset, and parent character to identify components. Unity file IDs are included only as last-resort diagnostics because reserialization can change them.

### Gray / `Koi_CharacterPrefab`

On the added child named `FirstTimeDeliveryInteraction`:

- Enabled `DialogueTrigger`
  - Asset: `KoiTutorialDialogue`
  - Delay: `0`
  - Single Use: enabled
  - Completion: exactly `FirstLetterAssignment.EnableGoal()`
- Enabled `GoalAssignmentTrigger`
  - Generated display name: `Deliver Gray's Letter to Spike`
  - Completion: Spike's child `GoalCompletionTrigger`
  - Letter item: assigned
  - Make Primary Goal On Receive: enabled
  - Single Use: enabled
  - Auto Disable After Use: enabled
  - Wait For Enable Animation: enabled
  - Enable animation delay: `1.5` seconds

On Gray's character root/base components:

- Enabled `GoalCompletionTrigger`
  - This is the destination for Spike's reply.
  - Single Use: enabled.
  - Completion: exactly `GrayReturnDialogue.TriggerNow()`.
- Disabled `DialogueTrigger`
  - Asset: `GrayReturnDialogue`
  - Delay: `0`
  - Single Use: enabled
  - Completion: exactly `GameUIManager.ShowEndOfDemo()`
- Disabled duplicate `GoalAssignmentTrigger`
  - This old first-letter source is not used.
  - Its goal events are empty.

### Spike / `Puffer_CharacterPrefab`

On the added child named `FirstTimeDeliveryInteraction`:

- Enabled `GoalCompletionTrigger`
  - This completes Gray's first letter.
  - Single Use: enabled.
  - Completion: exactly `CompersionTitle.TriggerNow()`.
- Disabled `DialogueTrigger` used as `CompersionTitle`
  - Asset: `CompersionTitleDialogue`
  - Delay: `0`
  - Single Use: enabled
  - Completion: exactly `SpikeReply.TriggerNow()`.
- Disabled `DialogueTrigger` used as `SpikeReply`
  - Asset: `SpikeTutorialDialogue_1`
  - Delay: `0`
  - Single Use: enabled
  - Completion: exactly `ReturnAssignment.EnableGoal()`.
- No `RandomCloudDeliveryGoalTrigger`.

On Spike's character root/base components:

- Enabled `GoalAssignmentTrigger`
  - Generated display name: `Return Spike's Reply to Gray`
  - Completion: Gray's root `GoalCompletionTrigger`
  - Letter item and give-item animation references: inherited from the base character prefab
  - Make Primary Goal On Receive: enabled
  - Single Use: enabled
  - Auto Disable After Use: enabled
  - Enable animation delay: `0`
  - `waitForEnableAnimation` remains inherited and true, so the goal is added on the next frame after Spike's dialogue closes. This one-frame yield is expected, not the removed five-second wait.
- Disabled root `GoalCompletionTrigger`
  - This duplicate completion route is not used.
  - Its success and failure events are empty.

### Current Diagnostic File IDs

These IDs match the scene as of the last reconciliation. Prefer semantic identification above.

| Component | Current file ID |
|---|---:|
| Gray opening dialogue | `1234040960` |
| Gray first-letter assignment | `1234040958` |
| Spike first-letter completion | `284141364` |
| COMPERSION dialogue | `284141368` |
| Spike reply dialogue | `284141369` |
| Spike return assignment | `2147255703` |
| Gray return completion | `1995255492` |
| Gray return dialogue | `1808536126` |
| Game UI manager | `1983460075` |

## The Six-Event Spine

Every narrative transition has exactly one effective persistent listener:

| Source event | Target call | State change after the call |
|---|---|---|
| Gray opening `onDialogueComplete` | First assignment `EnableGoal()` | First goal is generated/wired immediately, then added and made primary after the 1.5-second give-item animation wait. |
| Spike first completion `onCompletionSucceeded` | COMPERSION `TriggerNow()` | First goal is already removed before the title opens. |
| COMPERSION `onDialogueComplete` | Spike reply `TriggerNow()` | Shared dialogue panel closes and reopens for Spike. |
| Spike reply `onDialogueComplete` | Return assignment `EnableGoal()` | Return goal is generated/wired immediately, then added and made primary after the expected one-frame yield. |
| Gray return completion `onCompletionSucceeded` | Gray return dialogue `TriggerNow()` | Second goal is removed before Gray responds. |
| Gray return dialogue `onDialogueComplete` | `GameUIManager.ShowEndOfDemo()` | The terminal local overlay opens and gameplay input is suspended. |

All six calls are runtime-only, zero-argument persistent calls. `TriggerNow` is serialized against the base `InteractionTrigger` type even when the target component is a `DialogueTrigger`; that inherited-method target is valid.

`GoalCompletionTrigger` removes the active goal before invoking its success event. `GoalAssignmentTrigger` generates and wires its goal before adding it to the player, but the configured animation wait defers the actual add/primary operation. The old arrow therefore clears before the next dialogue; the first new arrow appears after 1.5 seconds, and the return arrow appears on the frame after Spike finishes speaking.

## Why Story Progress Is Local in Multiplayer

Story state is intentionally not network-synchronized:

1. `PlayerControllerM` owns each process's goal list and primary goal.
2. `GoalAssignmentTrigger.EnableGoal()` resolves `GameServices.GetPlayer()`, which is the locally registered playable character for that process.
3. `InteractionTrigger.IsAllowed()` finds `PlayerControllerM` on the entering collider and requires that controller to be enabled.
4. `NetworkPlayerController` enables `PlayerControllerM` only for the owning client. Remote player proxies have the controller disabled and cannot consume local story triggers.
5. The trigger `_used` flags and generated `Goal` objects are ordinary scene/runtime state in each process. They are not FishNet state and do not need shared quest ownership.
6. Dedicated-server player controllers remain disabled, so server-side physics overlaps do not advance a server copy of the story.

Expected multiplayer behavior:

- Two players may be on different story beats at the same NPC.
- One player completing a delivery does not complete or assign the other player's goal.
- NPCs, clouds, ladders, and player transforms may be shared/networked while dialogue and goals remain local.
- A newly launched client starts from the beginning.

There is no save system or reconnect resume. Reloading the scene/process restarts the story. Reconnecting without a scene reload is not a supported persistence path and must not be documented as one.

## Non-Negotiable Setup Rules

### Call scripted dialogue with `TriggerNow`

COMPERSION, Spike's reply, and Gray's return dialogue components stay disabled. Their GameObjects stay active. The preceding event calls `TriggerNow()` directly.

Do not wire both `set_enabled(true)` and `TriggerNow()`. A disabled `InteractionTrigger` may retain an overlapping collider. Its `OnEnable()` path deliberately reprocesses that overlap, so enabling it while Hermes is still standing at the NPC can auto-open the dialogue; the explicit call then replaces that first dialogue session and produces duplicate visible behavior even though per-session callbacks no longer leak.

### Keep dialogue delays at zero

All story `DialogueTrigger.activationDelaySeconds` values are `0`. A delayed trigger may fire after a different dialogue has opened and replace the dialogue the player is currently reading. `DialogueUI` now discards the replaced session's completion callback safely, but the player-facing interruption still breaks the intended sequence.

### Every live dialogue asset needs at least one step

`DialogueTrigger.ShowDialogue` gives `DialogueUI` a completion callback scoped to that dialogue session. An empty `DialogueInstance` closes immediately without invoking the callback, so the trigger never advances the story. Unlike the former shared-event subscription, it does not leave a callback behind to react to another dialogue.

### Require registered local UI and player services

The active scene must contain `GameServices` and an active `DialogueUI`. `DialogueUI.Start()` registers itself with `GameServices`; this must finish before a story dialogue is triggered. If `GameServices.GetDialogueUI()` is null, `DialogueTrigger` shows nothing, but `InteractionTrigger` can still consume its single use.

Goal assignment also requires `GameServices.GetPlayer()` to return the local enabled `PlayerControllerM`. `EnableGoal()` returns without adding anything when the local player is not registered. Confirm both registrations before diagnosing the event wiring.

### Goal assignments are enabled before they are called

The first-letter and return `GoalAssignmentTrigger` components are enabled at scene baseline. They do not react to colliders by themselves. Keeping them enabled avoids relying on coroutine behavior from a disabled component when the item-give animation is used.

### Do not reactivate removed routes

- Do not add `RandomCloudDeliveryGoalTrigger` back to Spike's story child.
- Do not put `DeliveryCloud_Base` back in the shipping scene for this quest.
- Do not assign `SpikeTutorialDialogue_2` to a live trigger.
- Do not restore Gray's `LadderTrigger.set_enabled` or `LadderTrigger.BuildLadder` callbacks.
- Do not disable the independent ladder trigger/system itself; working ladders are available from spawn.
- Do not make the duplicate root assignment/completion components part of the event chain.

### Goal HUD baseline

The goal indicator root in `Assets/UI/UI.prefab` is active. It hides or updates itself based on the local primary goal. The goal-selection overlay starts inactive and is not presented automatically by this sequence. The active direction-indicator button can still open it manually, even when only one goal exists.

## End-of-Demo Panel

`GameUIManager.ShowEndOfDemo()` is the terminal call.

Current behavior:

- It is idempotent; repeated calls do not stack input suspension.
- If no panel is assigned in the Inspector, it constructs `EndOfDemoPanel` under the active Canvas at runtime.
- The message gives a light developer apology and thanks the player.
- The first button is `Read the narrative script`; it calls `GameUIManager.OpenNarrativeScript()` with the Google Doc URL, then turns gray while remaining clickable.
- The second button is `Join the mailing list`; it calls `GameUIManager.OpenMailingList()` with `https://forms.gle/hxLfkX4au94oon1B8`, then turns gray while remaining clickable.
- Gameplay input is suspended only while the end panel is visible.
- `Keep exploring` is hidden until both link buttons have each been pressed at least once. It then calls `GameUIManager.CloseEndOfDemo()`, hides the panel, and releases exactly the panel's own gameplay suspension so the player can continue moving around after the story ends.
- `Assets/UI/UI.prefab` keeps the optional panel null and serializes both external URLs. The null panel therefore selects the working runtime fallback. If an authored panel is assigned later, preserve both exact URLs and keep that panel inactive at baseline.

If a designer replaces the runtime fallback with authored UI, assign the panel to `GameUIManager.endOfDemoPanel` and start it inactive. That authored panel must also reproduce the visited-color and close-visibility bindings currently populated by `BuildDefaultEndOfDemoPanel`; wiring only the three public methods is not enough to show the gate correctly. Preserve the guarded `ShowEndOfDemo` and `CloseEndOfDemo` state transitions so each visible interval owns exactly one gameplay suspension.

## Admin Story Checkpoints

`AdminMenu.storyCheckpoints` is the ordered, Inspector-editable navigation list. Its defaults are:

| Index | Label | Exact local story snapshot | Teleport anchor |
|---:|---|---|---|
| 0 | Spawn - Before Gray | No goals, count 0; every story milestone unconsumed | Spawn |
| 1 | Gray - Letter for Spike | First goal active/primary, count 0; Gray opening and assignment consumed | Gray |
| 2 | Spike - Before delivery | Same state as index 1 | Spike |
| 3 | Spike - Reply for Gray | Return goal active/primary, count 1; Gray and Spike milestones consumed | Spike |
| 4 | Gray - Before return | Same state as index 3 | Gray |
| 5 | Ending - Thank-you UI | No goals, count 2; every milestone consumed; ending overlay shown with both links unpressed | Ending/Gray |

Previous and Next immediately apply the adjacent entry; Apply reapplies the selected entry. Application is an exact transaction rather than event replay:

1. Resolve `GameServices.GetPlayer()` and require its local enabled `PlayerControllerM`. Never search for an arbitrary player because it could be a remote proxy.
2. Resolve all eight semantic story components by dialogue asset or generated-goal display name, then prepare the cached goal required by the target stage.
3. Close inventory, goal selection, and active dialogue through their owner APIs. Reset only the ending panel's own suspension.
4. Cancel activation delays and goal-animation coroutines; replace every trigger's consumed/enabled state with the stage snapshot.
5. Atomically replace the player's one active narrative goal, primary goal, and completed count without firing assignment/completion events.
6. Teleport through `PlayerControllerM.ResetForRespawn`, which clears movement and calls the owner-authoritative FishNet `NetworkTransform.Teleport()`.
7. Show the ending panel only for the terminal checkpoint, then raise the Admin Panel above it using a nested high-sorting Canvas.

This changes only the current process's story state. The owner teleport replicates normally so other players see the move, while no quest state or ownership is synchronized. On a dedicated server, `GameServices` has no enabled local player, so applying a checkpoint fails without changing story state.

Critical failure points:

- Keep approach markers outside auto-enter colliders. The fallback uses story trigger transforms plus offsets; authored platform-relative marker children are more reliable if level art or collider sizes change.
- Do not call `EnableGoal`, `CompleteGoal`, or a story UnityEvent from a checkpoint. Those paths replay dialogue/animation side effects and cannot safely travel backward.
- `DialogueUI` owns a per-session completion callback. Cancelling or replacing dialogue discards that callback; do not restore shared anonymous completion listeners, which could fire a stale transition later.
- `GoalAssignmentTrigger.ApplyCheckpointActivationState` stops its animation coroutine and resets the animator trigger. Removing that reset lets a delayed goal reappear after a backward jump.
- The array derives trigger state from `StoryStage`. Adding a genuinely new narrative milestone requires extending stage-to-snapshot logic, not merely inserting a differently named entry.
- The optional authored UI requires label plus Previous, Apply, and Next buttons. Partially assigned controls intentionally fall back to runtime UI.

Recommended regression matrix: apply each of the six entries from every other entry (36 transitions), apply each entry twice, apply during Gray's 1.5-second item animation, apply during dialogue, and apply Ending while Admin is open. Verify exact goal/count state, no late goal assignment, no stale dialogue transition, correct teleport, and Admin clickable over Ending.

## Unity Editor Repair Procedure

Read `Docs/Agents/UNITY_EDITOR_WORKFLOW.md` before editing the scene.

1. Exit Play Mode. Wait for MPPM clones and runtime-spawned objects to disappear.
2. Confirm the open scene is `Assets/Levels/SimpleLevel.unity`.
3. Record `git status --short`; preserve unrelated changes.
4. In the Hierarchy, search for `FirstTimeDeliveryInteraction`. There are two; identify them by the parent `Koi_CharacterPrefab` and `Puffer_CharacterPrefab`.
5. Identify duplicate components by their assigned dialogue asset and target completion component, not merely by their order in the Inspector.
6. Rebuild each UnityEvent so its effective list contains the one listener shown in the six-event table.
7. Keep the scripted dialogue component checkboxes off and use `TriggerNow()`.
8. Ensure both goal assignments are enabled and target the correct completion triggers.
9. Clear the unused root components' event lists and leave them disabled.
10. Remove the random delivery component and any `DeliveryCloud_Base` scene instance.
11. Save the scene with `File -> Save` or Command-S.
12. Double-click `SimpleLevel` in the Project window to force a disk reload before Play Mode.
13. Inspect the Git diff. Confirm only intended scene references changed.

### Prefab override array warning

Unity may retain serialized `Array.data[n]` prefab override records after an event's `Array.size` is reduced. Runtime deserialization treats `Array.size` as authoritative, so out-of-range records are ignored. However, increasing the event size later can make an old record visible again.

When editing a previously looped root event:

- Inspect the effective listener list in the Inspector, not just raw YAML matches.
- If increasing the list size, remove every visible old listener before adding the intended call.
- Do not infer active callbacks from an out-of-range `Array.data[n]` line alone.
- After saving, reopen the scene and recheck the effective list.

## Full Runtime Test Checklist

Clear the Console immediately before each pass. Test from a freshly reloaded scene.

### Offline or host pass

- [ ] Gray's opening appears once.
- [ ] Completing it queues `Deliver Gray's Letter to Spike`; the goal appears after the 1.5-second give-item animation wait.
- [ ] The goal indicator points to Spike.
- [ ] No fixed ladder tutorial dialogue or forced ladder build occurs.
- [ ] Reaching Spike clears the first goal.
- [ ] `COMPERSION` appears before any Spike line.
- [ ] Advancing the title opens Spike's reply exactly once.
- [ ] Finishing Spike's reply creates `Return Spike's Reply to Gray`.
- [ ] The return goal appears on the next frame; no five-second or delivery-cloud delay occurs.
- [ ] The goal indicator points to Gray.
- [ ] Returning to Gray clears the second goal.
- [ ] `GrayReturnDialogue` plays exactly once.
- [ ] Finishing it opens the developer ending panel.
- [ ] Movement remains suspended and a second `ShowEndOfDemo()` call does not duplicate the panel or suspend count.
- [ ] `Keep exploring` is initially hidden.
- [ ] The narrative button opens the expected Google Doc, turns gray, and remains clickable.
- [ ] The mailing-list button opens `https://forms.gle/hxLfkX4au94oon1B8`, turns gray, and remains clickable.
- [ ] Pressing only one link leaves `Keep exploring` hidden; pressing both reveals it.
- [ ] `Keep exploring` hides the panel and restores movement; repeated close attempts never underflow or release another modal's gameplay suspension.
- [ ] If `ShowEndOfDemo()` is called again after closing, the panel takes one new suspension, both links remain gray/clickable, and `Keep exploring` is immediately available.
- [ ] No Console exception, missing-reference warning, or duplicate-listener symptom appears.
- [ ] Open Admin with backtick, then traverse all six story checkpoints forward and backward.
- [ ] Previous/Next teleports to the intended local Gray/Spike/spawn region and restores exact active goal/count state.
- [ ] Apply during Gray's item-give wait; no delayed first goal appears after jumping backward.
- [ ] Apply during dialogue; completing a later dialogue does not fire the cancelled dialogue's transition.
- [ ] At Ending, Admin remains above and clickable over the end overlay.
- [ ] Jumping backward from Ending hides/resets the overlay and restores movement without releasing another modal's suspension.

### Host plus one remote client

- [ ] Main Editor host and one MPPM pure client see each other's movement.
- [ ] Each player can leave Gray's opening at a different time.
- [ ] One player may finish at Spike while the other still has the first goal.
- [ ] The first player's title/dialogue does not appear on the second player's UI.
- [ ] Each player independently receives the return goal.
- [ ] One player finishing Gray does not finish the other's goal or show the other's end panel.
- [ ] Remote proxy colliders do not trigger local NPC interactions.
- [ ] Applying a story checkpoint on one client changes only that client's goal/dialogue/end state.
- [ ] The checkpoint teleport is visible to the other client but does not teleport the other player's character.

### Separate dedicated-server pass

This requires a dedicated server process; it is not part of the host-plus-MPPM topology.

- [ ] Connect a client and move its observed proxy through the NPC areas.
- [ ] The server emits no `AddGoal:` or `GoalCompletionTrigger: completing goal:` messages for that proxy.
- [ ] If a test harness inspects state, no server-side story goal is created and no end panel is activated.

### Late client

- [ ] Start a fresh client process after the host is already beyond Spike.
- [ ] The fresh client begins at Gray's opening.
- [ ] Shared clouds/ladders are synchronized, while story state remains fresh and local.

## Symptom-to-Cause Guide

| Symptom | Most likely cause | First repair check |
|---|---|---|
| Spike speaks before COMPERSION | Spike completion still enables/calls the reply directly | Its only success call must be COMPERSION `TriggerNow()`. |
| COMPERSION never appears | Wrong/empty title asset, disabled GameObject, missing listener, or unregistered `DialogueUI` | Component may be disabled, but its GameObject and asset must be active/non-empty; verify `GameServices.GetDialogueUI()`. |
| Dialogue opens twice | `set_enabled(true)` plus `TriggerNow`, a nonzero delay, or overlapping triggers | Remove enable calls; use one immediate `TriggerNow`. |
| Return goal is missing | Spike reply completion is empty, return assignment is disabled, or it targets the wrong Gray completion | Verify reply -> root Spike assignment `EnableGoal`; assignment -> Gray root completion. |
| A goal never appears after dialogue | The local player was not registered when `EnableGoal()` ran | Verify `GameServices.GetPlayer()` returns the local enabled `PlayerControllerM` before the dialogue completes. |
| Return goal points to Spike | Return assignment references Spike's completion or old prefab override | Reassign it to Gray's root `GoalCompletionTrigger`. |
| Gray starts the first quest again | Gray root completion still contains the old reset/enable/assignment loop | Effective success list must contain only Gray-return `TriggerNow`. |
| Gray receives the goal but says nothing | Gray return trigger is missing/empty or completion targets the wrong duplicate dialogue | Use the disabled Gray root dialogue with `GrayReturnDialogue`. |
| End panel does not appear | Gray dialogue completion does not target the scene `GameUIManager`, or the dialogue never completes | Verify `ShowEndOfDemo` persistent target and non-empty Gray asset. |
| An end-panel button does nothing | A custom-authored button is missing its matching URL method, or popup blocking was caused by a non-user call | Wire direct clicks to `OpenNarrativeScript` and `OpenMailingList`, respectively. |
| `Keep exploring` never appears | One link has not been pressed, or a custom-authored panel is not reflecting the two visited flags | Press both links once; runtime fallback reveals the close button only after both calls. |
| Goal arrow is absent | Goal HUD root inactive or the assigned goal was not made primary | Keep GoalIndicator active and `makePrimaryGoalOnReceive` enabled. |
| Postbox/cloud mail returns | Random delivery component or delivery-cloud scene instance was restored | Remove it and keep delivery feature flag false. |
| A remote player advances my story | Local-player guard regressed or remote `PlayerControllerM` is enabled | Audit `InteractionTrigger.IsAllowed` and `NetworkPlayerController`. |
| It worked before editing but not after reopening | Scene/prefab changes were not saved, were made in Play Mode, or stale in-memory scene overwrote disk | Exit Play, save, reopen `SimpleLevel`, then inspect Git diff. |
| UnityEvent YAML shows old calls despite one Inspector listener | Out-of-range prefab overrides remain beyond `Array.size` | Treat size as authoritative; never grow the list without clearing old entries. |

## Change Protocol for Future Story Work

1. Agree on narrative cuts versus new-feature requirements before editing.
2. Edit `DialogueInstance` assets for text-only changes.
3. Use existing `DialogueTrigger`, `GoalAssignmentTrigger`, and `GoalCompletionTrigger` components for linear handoffs.
4. Avoid adding RPCs for per-player story state unless the design changes to shared quest ownership.
5. Make scene reference changes in the Unity Editor and save the scene.
6. Reopen the scene, inspect the effective UnityEvents, and inspect the Git diff.
7. Run offline/host, pure-client, independent-progress, and late-client checks.
8. Record what was actually runtime-verified in this document. Never upgrade an audit result into a playtest claim.
9. If the story spine changes, update this runbook, `AGENTS-MOSTRECENT.md`, and the human-facing README link in the same change.
