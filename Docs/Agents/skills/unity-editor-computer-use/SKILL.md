---
name: unity-editor-computer-use
description: Operate and visually verify this Compersion Unity project through the macOS Unity Editor using Computer Use and node_repl. Use for Unity asset refresh/import checks, Inspector or prefab setup, entering and exiting Play Mode, Game/Simulator mobile QA, Admin story-checkpoint testing, dialogue and input verification, Console inspection, screenshots, or any request that requires proving a Unity change in the actual Editor instead of relying only on source inspection.
---

# Unity Editor Computer Use

Use the real Unity Editor as a verification surface after completing safe file-level checks. Keep repository edits in the normal coding workflow; use Computer Use for Editor state, import, Play Mode, Inspector, Simulator, and visual evidence.

Before operating this project, read this complete chain in order. These are prerequisites, not optional background links:

- `../../AGENTS.md`
- `../../AGENTS-MOSTRECENT.md`
- `../../UNITY_EDITOR_WORKFLOW.md`
- `../../UNITY_COMPUTER_USE.md`
- `references/compersion-unity-controls.md`
- `../../STORY_THROUGH_SECOND_GOAL.md` when testing narrative progression

Then identify the verification contract from the request and code: target UI or system, trigger or deterministic checkpoint, expected modal/input behavior, expected downstream callback/state, device/orientation, and runtime topology (offline, host, pure client, WebGL, or dedicated server). Infer these from the project runbooks when they are explicit. Ask only when a missing item would materially change the test.

## Prepare

1. Read the applicable `computer-use` skill completely and bootstrap its plugin-owned wrapper through `node_repl`. Do not import `@oai/sky` directly or hardcode a cached plugin version.
2. Inspect `git status --short` before Play Mode. Record pre-existing changes so Unity-generated noise can be distinguished from user work.
3. Run the smallest relevant static checks first: compilation, GUID/reference inspection, and `git diff --check`.
4. Never start a second Unity process or batch-mode Unity while the desktop Editor owns this project.

Refresh, Play Mode, and rendering can serialize project data even when the intended test is observational. For an implementation request, these are normal verification steps, but snapshot the exact tracked files first. For a read-only review or diagnosis, do not start a potentially serializing test unless the user authorized Editor execution; if an unexpected diff appears, preserve and report it rather than silently restoring it.

Use reusable `var` bindings in `node_repl`; the session is persistent. Initialize screenshot helpers explicitly:

```js
if (!globalThis.sky) {
  var { setupComputerUseRuntime } = await import("<computer-use-plugin-root>/scripts/computer-use-client.mjs");
  await setupComputerUseRuntime({ globals: globalThis });
}
var fs = await import("node:fs/promises");
var url = await import("node:url");
var state = await sky.get_app_state({ app: "Unity", disableDiff: true });
nodeRepl.write(state.text);
if (state.screenshot) {
  await nodeRepl.emitImage({
    bytes: await fs.readFile(url.fileURLToPath(state.screenshot.url)),
    mimeType: "image/png"
  });
}
```

## Operate by fresh state

1. Call `get_app_state` before every new interaction phase.
2. Prefer accessibility `element_index` for menus, buttons, fields, and dialogs.
3. Unity exposes little Game/Simulator content through accessibility. When semantic elements are absent, use the latest screenshot and coordinate clicks or drags.
4. Fetch a fresh state after every action that changes menus, Play Mode, focus, viewport scale, scene state, or UI visibility. Never reuse menu indices from an older tree.
5. If a tool call yields a running cell, wait on that cell. Do not repeat the click while Unity is importing or compiling.

## Import and compile

1. Make file/meta/prefab changes before entering Play Mode.
2. In Unity, choose `Assets -> Refresh` using the current accessibility tree.
3. Wait for import and domain reload to finish.
4. Inspect the Console. Require zero new errors; identify warnings as new or pre-existing.
5. Recheck the serialized files from the shell. A clean Inspector view is not persistence evidence.

Do not edit scripts during Play Mode and assume the running view updated. Exit Play Mode, refresh/recompile, and restart the test.

## Play Mode and Simulator QA

1. Enter Play Mode only after compilation is stable.
2. Confirm Play Mode from multiple cues: the toolbar state, runtime clone objects in Hierarchy, and live game logs.
3. Start with `Fit to Screen` or the smallest Simulator scale that shows the whole device.
4. Increase to roughly 40% for typography and border inspection. The device will exceed the viewport; use the Simulator's own scrollbars to inspect top and bottom separately.
5. Derive coordinates from the current screenshot. Main-toolbar and Simulator-toolbar controls occupy different rows; clicking the wrong row can open Play Mode Scenario menus instead of changing Simulator scale.
6. Test the requested interaction, not only appearance. For story UI, verify the next authored callback or state transition.
7. Exit Play Mode when finished.

## Project story QA

Use the hidden Admin gesture and deterministic checkpoints instead of replaying the whole story when the target has a checkpoint. Confirm that Admin remains above overlays and its close button remains operable.

For the COMPERSION title card, verify at minimum:

- Admin checkpoint `4/7` opens the card at Spike.
- Admin stays above the card.
- Portrait top and bottom composition are readable.
- Completed-card Space dismisses into Spike's reply.
- The card blocks gameplay/mobile input while active.
- The test does not claim first-input fast-finish, physical touch, landscape, or multiplayer unless those paths were actually exercised.

## Clean up and report

1. Exit Play Mode.
2. Compare `git status --short` with the pre-test snapshot.
3. For an implementation task, remove only Unity-generated noise proven clean in the pre-test snapshot and changed solely by this test. Dynamic TMP fallback atlases can serialize glyph and texture data after rendering characters such as `♥`. Inspect the exact diff and restore only the affected file; never use a broad reset/checkout or discard an unknown pre-existing change. For a read-only task, leave unexpected generated changes in place and request permission before restoring them.
4. Run the final compile and `git diff --check`.
5. Report separately:
   - static checks;
   - Editor import/Console results;
   - visual profiles and viewport scales inspected;
   - interactions actually performed;
   - paths still pending.

Do not convert static reasoning into a Play Mode claim, and do not convert one local-host Editor pass into a remote-client or WebGL claim.
