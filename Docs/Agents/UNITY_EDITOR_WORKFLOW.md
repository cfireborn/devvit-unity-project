# Unity Editor Workflow for Agents and Human Collaborators

Last updated for Unity 6.2 (`6000.2.8f1`) on 2026-08-18.

This guide explains how to operate the Compersion Unity project safely and how to leave scene, prefab, ScriptableObject, and multiplayer changes in a state another person can understand. It supplements `AGENTS.md`; feature-specific runbooks remain authoritative for their systems.

## Start Here

Before touching the Editor:

1. Read `Docs/Agents/AGENTS.md` and `Docs/Agents/AGENTS-MOSTRECENT.md`.
2. Read the relevant system runbook. For the current story, use `Docs/Agents/STORY_THROUGH_SECOND_GOAL.md`.
3. Run `git status --short` and note existing changes. They may belong to the user or another task.
4. Confirm the shipping scene path is `Assets/Levels/SimpleLevel.unity`.
5. Use the Unity version recorded in the README and project settings. Do not casually upgrade the project or packages.

## One Project, One Unity Editor

Do not launch a second Unity process or `-batchmode` import against this project while the desktop Editor has it open. Unity's asset database and `Library` are not safe for two writers.

Typical collision symptoms include:

- `attempt to write a readonly database`
- a project-already-open warning
- an import that hangs without producing a useful compile result
- scene or asset changes apparently disappearing after the other process saves

When the Editor is already open, use that Editor for compilation, imports, menu tools, and scene saves. If batch mode is truly necessary, close the desktop Editor first and confirm no MPPM clone still owns the project.

## Know Which Window Has Focus

Unity tools can open separate floating windows, including:

- Multiplayer Play Mode
- Edgegap Server Hosting
- Build Profiles
- Package Manager

Automation and keyboard shortcuts act on the focused Unity window. If the Hierarchy and Inspector seem missing, a utility window is probably focused. Close the utility window with Command-W or raise the main window titled `SimpleLevel - devvit-unity-project`.

Do not close the entire Unity application when only a utility window needs to be dismissed.

## Never Author Scene State in Play Mode

The Play button is blue while Play Mode is active. The Hierarchy may contain runtime objects such as:

- `NetworkPlayer(Clone)`
- `Cloud_*(Clone)`
- `Ladder(Clone)`

Changes made to ordinary scene instances in Play Mode are generally reverted when Play Mode stops. This was the practical cause of a previous story handoff failing to reach disk.

Before editing:

1. Stop Play Mode with the toolbar Play control or Command-P.
2. Wait for runtime clones to disappear.
3. Wait for script compilation and asset import to finish.
4. Confirm the Hierarchy contains the authored scene rather than runtime clones.

## Scene Editing Workflow

1. Open `Assets/Levels/SimpleLevel.unity` from the Project window.
2. Select the target in the Hierarchy and verify its full parent chain.
3. Inspect whether it is a prefab instance, an added child, or an ordinary scene object.
4. Make the smallest change in the Inspector.
5. Save with Command-S.
6. Double-click `SimpleLevel` again to reload it from disk.
7. Reopen the target and confirm the reference or event survived reload.
8. Inspect `git diff -- Assets/Levels/SimpleLevel.unity` before testing.

Prefer Editor-authored serialization for object references and UnityEvents. Hand-editing scene YAML should be a last resort because file IDs, nested prefab overrides, and array sizes are easy to corrupt.

If an external tool changes the scene while it is open, do not save a stale in-memory copy over the file. Reopen the scene from the Project window and choose the disk version when prompted.

## Prefab Instances and Overrides

The character instances are nested prefab variants with scene overrides. A change can land in three places:

- the base character prefab shared by every character
- the Koi/Puffer variant prefab
- only the `SimpleLevel` scene instance

Before pressing **Apply**, decide whether every prefab instance should receive the change. Story progression wiring is scene-specific and normally remains a scene override. Do not apply it to the shared base character prefab.

## Admin Story Checkpoint UI

`AdminMenu` contains an ordered `Story Checkpoints` array and optional authored-UI fields. The game remains usable without Editor setup: when the four required checkpoint UI references are incomplete, it creates a compact Previous/Apply/Next row under `AdminPanel` at runtime. When `Close Admin Panel Button` is unassigned, it also creates a top-right close button.

To replace the fallback with authored controls:

1. Exit Play Mode and open `Assets/UI/UI.prefab` in Prefab Mode.
2. Under `AdminPanel`, create a root panel, one TMP label, and three Buttons named for Previous, Apply, and Next.
3. On `AdminMenu`, assign `Story Checkpoint Controls Root`, `Story Checkpoint Label`, and all three Button fields.
4. Leave each Button's persistent `OnClick` list empty. `AdminMenu.Awake()` attaches and later removes the runtime listeners.
5. Optional: create and style a separate close Button, assign it to `Close Admin Panel Button`, and leave its persistent `OnClick` list empty. It is bound to `AdminMenu.ClosePanel()` at runtime.
6. Save the prefab, exit Prefab Mode, reopen `SimpleLevel`, and enter Play Mode.

All four label/button references must be assigned to select the authored UI. A partially assigned authored root is hidden and the fallback row is created, preventing duplicate controls. The root field itself is optional when the label and all three buttons are assigned.

The four optional marker fields are separate. If left empty, Spawn resolves from `NetworkPlayerSpawner.SpawnPoint`, Gray resolves from the opening trigger, Spike resolves from the first completion trigger, and Ending resolves from Gray. For precise landing positions, add platform-relative empty transforms outside the NPC trigger colliders and assign them as Gray, Spike, and Ending markers. Each array entry's `Teleport Offset` is an additional world-space adjustment.

Use **Revert** only for the exact property you intend to restore. Reverting an entire prefab instance can erase unrelated scene positioning, artwork, or event overrides.

## Working with Duplicate Components

Gray and Spike contain multiple components of the same type. Component order alone is not a stable identifier.

They also each contain a child named `FirstTimeDeliveryInteraction`. A hierarchy search or name-only installer therefore has two matches. Resolve by parent NPC plus component type and assigned asset; an Editor tool must stop without changing anything when it finds zero or multiple semantic matches.

Identify a component using:

- its parent character and GameObject
- its assigned `DialogueInstance`
- its completion target
- its enabled state and purpose

When assigning a UnityEvent target and two components have the same type, drag the specific component header into the event's object field. Verify the selected target afterward instead of trusting a visually identical dropdown label.

## UnityEvent Rules

For every changed event:

1. Count the effective listeners in the Inspector.
2. Verify target component, method, argument mode, and call state.
3. Remove obsolete listeners instead of merely disabling their targets.
4. Save and reopen the scene.
5. Verify the listener list again after reload.

For story dialogue, call disabled components with `TriggerNow()`; do not enable them and then call them while the player overlaps the NPC.

Nested prefab instances may retain raw `Array.data[n]` overrides beyond the serialized `Array.size`. Runtime uses the size, but increasing the list later can reveal old data. Clear visible old listeners before growing an event list.

## Editing Dialogue ScriptableObjects

Dialogue assets live under `Assets/UI/Assets/Dialogue/` and contain a `steps` array.

For each step:

- assign the intended portrait sprite, or leave it null for a portraitless card
- keep text in one step only when it fits the existing dialogue panel
- preview punctuation, apostrophes, em dashes, and line breaks in Play Mode
- never leave a live chained dialogue asset with zero steps

Text-only edits should normally change the ScriptableObject, not the dialogue scripts. A choice, branching condition, cinematic layout, or speaker-name system is a feature request and should be agreed on before implementation.

## COMPERSION Title Card Assets and Tuning

The title card is deliberately programmatic; there is no scene object or authored prefab to keep synchronized. `GameUIManager` creates `CompersionTitleCardUI` under the local UI Canvas when `CompersionTitleDialogue.presentation` is `CompersionTitleCard`. The existing Spike scene events remain unchanged.

The implemented visual target is the Figma frame **MOBILE REDESIGN + EXPORTABLE BACKDROP KIT** in [Compersion — Title Card - In Game Text Assets](https://www.figma.com/design/g5NDCTmSeMNUDbCqd1Dyfm/Compersion-%E2%80%94-Title-Card---In-Game-Text-Assets?node-id=0-1). `Assets/UI/UI.prefab` now uses the five transparent `@2x.png` derivatives from `Assets/UI/compersion-title-card/backdrop-kit/` at runtime. The neighboring SVGs remain editable design sources and `MOCKUP—Dramatic-Fade-Sequence.png` remains visual reference only. The code-native dark-teal/umber layout is retained only as a missing-skin fallback.

The five artwork files live in `Assets/UI/compersion-title-card/` and must remain imported as:

- Texture Type: **Sprite (2D and UI)**
- Sprite Mode: **Single**
- Alpha Is Transparency: enabled
- Generate Mip Maps: disabled
- Filter Mode: Bilinear
- Wrap Mode: Clamp

`Assets/UI/UI.prefab` serializes those Sprites on `GameUIManager`. If artwork is renamed or replaced, reopen the prefab and verify all five `COMPERSION Title Card` references. Missing references intentionally fall back to the readable `CompersionTitleDialogue` text instead of stalling the quest.

`GameUIManager.Compersion Title Skin` is the centralized assignment point for the Figma backdrop kit. The five runtime fields are populated on `Assets/UI/UI.prefab`; every field remains independently optional, and a null field uses its code-native fallback without triggering the plain-dialogue fail-safe:

| Figma export | Skin field | Runtime use |
|---|---|---|
| `EXPORT-01—Full-Screen-Vector-Backdrop` | `Full Screen Backdrop` | Entire atmospheric background |
| `EXPORT-02—Title-Backdrop` | `Title Backdrop` | Title/flag surface |
| `EXPORT-03—Content-Panel-Wide` | `Wide Panel` | Lead and definition surfaces |
| `EXPORT-04—Compact-Panel` | `Compact Panel` | Combined pronunciation/noun surface |
| `EXPORT-05—Continue-Ribbon` | `Continue Ribbon` | Prompt surface |

`Polyamory Flag` is an optional separate Sprite override. When it is null, the title builds the cyan/magenta/violet flag with its white chevron and gold heart from non-raycasting UI elements. If `Title Backdrop` already contains the flag, enable `Title Backdrop Includes Flag` to suppress that separate layer. `Balloon And House Silhouette` and `Cloud Silhouette` are fallback-only and are ignored when a full-screen backdrop is assigned.

This project does not currently include Unity Vector Graphics. Preserve downloaded SVGs as design sources if needed, but export/rasterize matching 2× PNG runtime copies and import those as Sprite (2D and UI). Do not add a package solely for the title card without agreeing to that feature/dependency change.

The three framed runtime PNGs rely on explicit 9-slice metadata. Preserve these settings when replacing or reimporting them:

| Runtime PNG | Pixels Per Unit | Sprite border (L/B/R/T) |
|---|---:|---:|
| `EXPORT-02—Title-Backdrop@2x.png` | `400` | `120 / 120 / 120 / 120` |
| `EXPORT-03—Content-Panel-Wide@2x.png` | `400` | `100 / 100 / 100 / 100` |
| `EXPORT-04—Compact-Panel@2x.png` | `400` | `100 / 100 / 100 / 100` |

`EXPORT-01` and `EXPORT-05` are unsliced. `BuildSurface()` automatically selects `Image.Type.Sliced` only when the assigned Sprite has a nonzero border. This preserves the framed panel strokes while keeping the thin continue ornament aspect-correct.

All layout and fallback tuning is centralized in `CompersionTitleCardUI.Initialize()` and its builders:

- the palette constants mirror the kit: `#020507` ink, `#071013` midnight teal, `#08282C` panel teal, `#2A1715` umber, `#D9A94F` primary gold, `#F6D58C` content gold, and `#E9DDBF` warm cream
- `BuildBackdrop()` owns the authored backdrop swap and fallback silhouettes
- `BuildSurface()` owns every authored-surface swap and smoked/gold fallback
- the five anchor rectangles in `Initialize()` own the mobile layout
- `BuildPolyamoryFlag()` owns the isolated flag fallback
- `RevealSequence()` owns one absolute unscaled timeline: title `0.00`, pronunciation+noun `0.36`, lead `0.72`, body `1.08`, prompt `1.62`; every beat fades for `0.42s` while settling downward from `+18px`

The transparent type PNG margins are intentional. Keep `Image.preserveAspect` enabled and do not call `SetNativeSize`, crop the files, or stretch the type artwork. The title Sprite's flat interior face is baked at the corrected Figma color `#F8DEA1`; its brown outline, shadow, antialiasing, original `2120×380` dimensions, and alpha were left intact. If the title is re-exported later, verify that exact face color instead of applying a Unity `Image` tint, which would also alter the outline and shadow. Validate portrait `540×960` first, then landscape, before changing anchors. The full-screen backdrop intentionally uses aspect-fill (`AspectRatioFitter.EnvelopeParent`), so non-reference ratios crop its outer edge; keep all readable content in the independent safe-area panels. The full-screen root stays the only raycast target so HUD/joystick input is blocked, while top-right taps are relayed to the existing hidden Admin gesture. Do not add an override-sorting Canvas: Admin's sorting order `32000` must remain above the card.

## Saving and Proving Persistence

A scene looking correct in the Inspector is not enough.

Use this persistence check:

1. Save the scene.
2. Allow any domain reload to finish.
3. Reopen the scene from the Project window.
4. Reinspect the changed components.
5. Confirm the scene appears in `git status --short`.
6. Confirm the expected asset GUID, method name, or display name appears in the serialized diff.
7. Only then enter Play Mode.

Do not claim a change is implemented if only Play Mode state or an unsaved Inspector is correct.

## Console Discipline

Before a focused test:

1. Open the Console.
2. Clear old entries.
3. Disable Collapse when event ordering matters.
4. Enable Error Pause only when stopping on the first exception is useful.
5. Run one scenario.
6. Read the first error and its full stack before reacting to later cascades.

Package-cache warnings and operational plugin messages are not automatically gameplay failures. Separate pre-existing warnings from messages created during the test.

Do not leave temporary debug logging or Editor installer scripts in the repository after diagnosis.

## Testing Modes

### Offline fallback

Use the Admin Menu connection settings or an unreachable endpoint to trigger the five-second fallback. Verify:

- one local player spawns
- the player has the offline tint
- clouds and ladders run locally
- dialogue, goals, and UI work without FishNet services running

### Main Editor host (local mode)

With the default local configuration, the MPPM main Editor starts as host when `editorStartAsHost` is enabled. In Edgegap mode it is a client, not a host. Test local-host gameplay and watch for server-only physics accidentally consuming local triggers.

### Multiplayer Play Mode pure client

Open `Window -> Multiplayer Play Mode`, enable a virtual player, and enter Play Mode from the main Editor.

- Main Editor: host when testing with the default local configuration.
- Virtual player: pure client.
- Keep the host window responsive; it owns server simulation.
- Test different quest progress on each window rather than moving both players together.

MPPM windows are separate Unity contexts. A correct multiplayer test proves both replication and isolation; merely seeing two players is not enough.

### Fresh late client

Start a new virtual player/client after the host is already running. Check buffered network state for clouds/ladders and fresh local state for unsaved story progression.

## Story Test Pattern

For the current narrative, use `STORY_THROUGH_SECOND_GOAL.md` as the detailed checklist. At minimum, prove:

- COMPERSION precedes Spike
- Spike creates the return-to-Gray goal
- Gray's completion opens Gray's final dialogue
- the end panel and narrative link appear locally
- a second player can remain on a different beat without interference

## Input and Simulator Notes

The Simulator tab may emulate a mobile device. Dialogue advances through the configured input action or the existing keyboard/touch fallbacks. Keep these distinctions in mind:

- A mouse click in the Simulator may be treated as mobile touch input.
- `DialogueUI` currently advances on any primary touch/click while mobile controls are active. It does not consult `VirtualJoystick.IsScreenPositionOverJoystick`, so a joystick-zone touch may also advance an open dialogue. Treat that as a known input issue, not evidence that the story event chain fired out of order.
- If keyboard input appears dead, click the Game/Simulator view once to give it focus.
- Do not diagnose network failure from an unfocused host window that has stopped processing expected input.

## Safe Agent UI Automation

When an agent operates Unity through computer-use tooling:

- inspect a fresh screenshot/state after every window or menu change
- prefer menu items and accessible controls over guessed coordinates
- stop Play Mode before mutations
- expect Unity's accessibility tree to expose menus more reliably than the Hierarchy or Inspector
- close only auxiliary windows when focus is wrong
- avoid working while the human is simultaneously clicking in Unity
- inspect the scene file after every save
- remove temporary Editor automation scripts after they have run
- never accept licenses, change production deployment state, reveal credentials, or apply broad prefab overrides without the required authorization

If reliable Inspector targeting is unavailable, create a narrowly scoped temporary Editor menu tool, make it idempotent and fail closed on ambiguous targets, run it once in the already-open Editor, audit its results, save the scene, and delete the tool. Do not run it twice unless duplicate components/listeners were explicitly checked, and do not leave general-purpose mutation tooling behind.

## Common Failure Recovery

| Symptom | Likely cause | Recovery |
|---|---|---|
| Inspector edits vanished | They were made in Play Mode or the scene was not saved | Stop Play, repeat, save, reopen, inspect Git diff. |
| Scene change is on the wrong character | Duplicate component selected by order/name | Identify by parent and assigned asset; rewire the specific component. |
| Prefab changes spread everywhere | Scene override was applied to a shared prefab | Stop and inspect the exact prefab override. Revert only the unintended property if safe. |
| Unity reports a readonly asset database | Another Unity/batch process opened the project | Stop the second process; use the already-open Editor. |
| Menu tool does not appear | Script compilation is incomplete or failed | Read the first Console compile error and wait for domain reload. |
| Scene on disk differs from Inspector | External edit occurred while scene was open | Reopen the scene from disk before saving anything. |
| Event invokes twice | Listener duplicated, trigger enabled while overlapping, or delayed callback survived | Reduce to one immediate call and reload the scene. |
| Event appears clean but old call returns after adding a row | Out-of-range prefab override data resurfaced | Clear the entire visible list and rebuild it deliberately. |
| MPPM player affects host story | Remote ownership filter regressed | Audit controller enabled states and `InteractionTrigger.IsAllowed`. |

## Before Handing Work Back

- [ ] Scene and assets are saved outside Play Mode.
- [ ] Temporary Editor scripts and debug logs are removed.
- [ ] `git status --short` contains only understood changes.
- [ ] Unrelated user changes are preserved.
- [ ] Scene/prefab diffs contain the intended references.
- [ ] Unity has no new compile errors.
- [ ] The relevant runtime test was actually performed.
- [ ] Host and pure-client behavior were distinguished.
- [ ] Documentation says what was audited versus what was playtested.
- [ ] Critical setup changes are linked from `AGENTS-MOSTRECENT.md`.

## Related Runbooks

- `Docs/Agents/STORY_THROUGH_SECOND_GOAL.md` — current story wiring and recovery
- `Docs/Agents/MOBILE_CONTROLS_SETUP_GUIDE.md` — mobile controls and dialogue input
- `Docs/Agents/EDGEGAP_CLOUDFLARE_OPERATIONS.md` — network hosting invariants
- `Docs/EDGEGAP_SERVER_OPERATIONS.md` — human release operations
- `Docs/Agents/CLOUD_LADDER_BUG_ANALYSIS.md` — ladder-specific historical analysis
