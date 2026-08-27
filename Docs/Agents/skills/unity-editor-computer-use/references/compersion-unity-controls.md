# Compersion Unity Computer-Use Reference

## Contents

- Verification contract and authorization
- Editor landmarks
- Asset refresh and compilation
- Simulator navigation
- Hidden Admin and story checkpoints
- COMPERSION title-card test
- Evidence and cleanup
- Common failures

## Verification contract and authorization

Before interacting with Unity, name the test target and its expected behavior. For a UI surface, record its authored trigger or Admin checkpoint, whether it should block gameplay or pass through special input, the state/callback that must follow dismissal, the device and orientation, and the runtime topology being exercised. The COMPERSION-specific values below are not defaults for unrelated overlays.

Editor Refresh, Play Mode, and font rendering can write project data. An implementation request normally authorizes the verification needed for that implementation. A review-only or diagnostic request does not automatically authorize a test known to serialize assets. In that case, either remain observational or ask before running it. If Unity unexpectedly writes a tracked file during read-only work, preserve the diff and report it; do not make a second mutation to hide it.

## Editor landmarks

Unity accessibility normally exposes the application menus but not most central toolbar, Game view, or Simulator content. Use menu element indices only after a fresh `get_app_state`. Use screenshot coordinates for the toolbar and simulated device.

The reference Unity layout used successfully on a `924x768` Editor window had:

- main toolbar near `y=36`;
- Game/Simulator toolbar near `y=74`;
- Play triangle near `x=477, y=36`;
- Simulator scale control near `x=425..470, y=74`.

These are diagnostic examples, not stable coordinates. Re-derive them whenever the window size, panel layout, DPI, device profile, or Simulator scale changes. A click near `x=455, y=42` opened the Play Mode Scenarios popup because it hit the main toolbar instead of the Simulator toolbar.

If an accidental popup opens, click a clearly empty area outside it, then fetch a fresh full state.

## Asset refresh and compilation

Preferred sequence:

1. Inspect changed `.meta`, prefab, scene, and ScriptableObject YAML from the shell.
2. Open the current Unity state.
3. Click the `Assets` menu using its current accessibility element.
4. Fetch the menu state; locate `Refresh` dynamically.
5. Click `Refresh` once.
6. Wait through import/domain reload.
7. Inspect a new full screenshot and Console state.

Do not assume a stale `Refresh` element index such as `213`; it was correct for one menu tree only.

The Console is successful when it shows no new errors. Preserve the distinction between:

- known Unity analyzer/source-generator messages from external command-line compilation;
- the pre-existing `GroundChecker.NoFilter()` obsolete warning;
- actual new import, missing-script, serialization, or runtime errors.

## Simulator navigation

The project Canvas reference resolution is `540x960`. The Apple iPhone 12 Simulator profile is a useful portrait check.

At a whole-device scale such as 20%, inspect overall hierarchy, safe-area placement, and overlay priority. At roughly 40%, inspect typography, image borders, dividers, and prompt spacing.

At 40%, the device is taller than the central viewport. Drag the Simulator's right vertical scrollbar to inspect the lower card. Drag it back to the top before attempting the hidden Admin gesture. Do not confuse Simulator scrolling with in-game scrolling.

For aspect-fill artwork, inspect both top and bottom crops. Treat cropping of the decorative backdrop as expected only when readable content lives on independent safe-area panels.

## Hidden Admin and story checkpoints

The Admin Menu opens after five taps in the simulated game's top-right corner. Derive the tap point from the visible device content bounds:

- place it just inside the right edge;
- place it below the device bezel/notch and inside game content;
- repeat five distinct clicks.

Examples that worked in one `924x768` layout:

- 20% device scale: approximately `(548, 116)`;
- 40% device scale with the viewport at the top: approximately `(630, 138)`.

If nothing opens, check:

1. Is the viewport scrolled away from the device top?
2. Is another overlay intercepting the gesture?
3. Did the click land in the device bezel or notch?
4. Did Simulator scale or window geometry change?

The Admin story controls are `Previous`, `Apply`, and `Next`. `Previous` and `Next` both select and apply immediately. Starting from `Story 1/7: Spawn - Before Gray`, three `Next` presses reach `Story 4/7: Spike - COMPERSION title`.

Always read the visible checkpoint label before continuing. Do not rely on a remembered button coordinate after the panel changes.

## COMPERSION title-card test

Use this sequence for the authored backdrop kit:

1. Enter Play Mode and wait for the local player.
2. Open Admin with five top-right taps.
3. Reach `Story 4/7: Spike - COMPERSION title`.
4. Confirm the card appears behind Admin and Admin's `X` is still clickable.
5. Close Admin.
6. At whole-device scale, inspect the overall composition.
7. At roughly 40%, inspect the title, flag, pronunciation/noun panel, and lead panel.
8. Scroll the Simulator viewport down and inspect the body panel, continue ornament, and prompt.
9. After the reveal is complete, press Space once.
10. Confirm Spike's reply begins with `Oh. It's you. Gray's mail squirrel, right?`.

The checkpoint is excellent for completed-card dismissal, Admin priority, and layout. It is less reliable for measuring first-input fast-finish through Computer Use because the reveal continues while Admin is open and UI calls add latency. Use natural gameplay or a purpose-built test harness before claiming that timing path was exercised.

## Evidence and cleanup

Capture evidence at the moment it supports a claim:

- Admin visibly above the target overlay;
- title-card top at readable scale;
- title-card bottom at readable scale;
- Spike dialogue after dismissal;
- Console with zero new errors.

After leaving Play Mode, compare repository status with the pre-test snapshot. Unity/TMP may write dynamic glyph data into:

`Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset`

In the observed test, rendering `→` and `♥` populated the glyph tables and expanded the atlas from `1x1` to `512x512`. During implementation work, confirm the file was clean before the test, inspect its exact diff, then restore only that proven generated change. During review-only work, preserve the unexpected diff and ask before restoring it. Never use broad checkout/reset operations or discard user changes.

## Common failures

| Symptom | Likely cause | Recovery |
|---|---|---|
| Assets menu call appears hung | Unity is producing the popup or importing | Wait on the yielded tool cell; do not click again |
| `fs` or `url` is undefined | Screenshot helper bindings were scoped or never initialized | Declare reusable `var` imports explicitly in the active `node_repl` session |
| Scale change opens another popup | Main and Simulator toolbar rows were confused | Dismiss outside the popup, fetch state, click the Simulator row |
| Device becomes huge and clipped | Scale slider jumped too high | Use the latest screenshot to choose a lower point, then inspect with scrollbars |
| Five corner taps do nothing | Wrong viewport position or click is in bezel/notch | Scroll to device top and derive the point from current content bounds |
| Script change is absent in Play Mode | Code was edited while the old domain was running | Exit Play Mode, refresh/compile, then start a new session |
| Card disappears but story does not continue | Input only fast-finished reveal, or callback failed | Distinguish first versus later advance; inspect the next state and Console |
| Large unrelated TMP asset diff appears | Dynamic fallback atlas serialized during QA | Verify it was clean before Play Mode; during implementation restore only that generated file, but during review preserve it and ask first |
