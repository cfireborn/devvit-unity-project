# Unity Editor Computer-Use Runbook

This project includes a reusable agent skill for operating and visually verifying the Unity Editor through macOS Computer Use:

- [`skills/unity-editor-computer-use/SKILL.md`](skills/unity-editor-computer-use/SKILL.md)
- [`skills/unity-editor-computer-use/references/compersion-unity-controls.md`](skills/unity-editor-computer-use/references/compersion-unity-controls.md)

Future agents should use that skill whenever a task requires proof from the actual Editor, Simulator, Inspector, Play Mode, Admin debugger, or Console. Source inspection and command-line compilation remain prerequisites, not substitutes for visual verification.

The skill deliberately names every required project document in one ordered list, including the architecture handoff and this human runbook. Follow the complete list before clicking the Editor; do not stop after only the first linked file.

## What this captures

The skill records the workflow that successfully imported and verified the COMPERSION backdrop kit:

- bootstrap the product-provided Computer Use wrapper through persistent `node_repl`;
- mix accessibility elements with screenshot-derived coordinates;
- refresh assets and wait through Unity domain reloads without duplicate actions;
- distinguish Unity's main toolbar from the Simulator toolbar;
- test whole-device and readable zoom levels;
- pan the Simulator viewport without moving gameplay;
- open the hidden Admin Menu and use deterministic story checkpoints;
- prove overlay priority and story callbacks;
- separate static, Editor, interaction, and multiplayer claims;
- detect and remove Play Mode-generated TMP atlas noise without touching user work.

## Fast human checklist

1. Save work and note `git status --short`.
2. Name the target, trigger/checkpoint, modal behavior, next state, device/orientation, and runtime topology.
3. For review-only work, agree on potentially serializing Refresh/Play Mode tests before running them.
4. Refresh assets in Unity and require zero new Console errors.
5. Enter Play Mode only after compilation settles.
6. Use Fit/20% for the full device and roughly 40% for visual details.
7. Re-derive coordinates from every fresh screenshot.
8. Use five top-right in-game taps for Admin; `Story 4/7` is specific to the COMPERSION title card.
9. Verify the requested interaction and downstream state, not only the screenshot.
10. Exit Play Mode, compare repository status, and report untested paths explicitly.

## Installing as a personal skill

The checked-in copy is canonical for this project. If automatic personal-skill discovery is desired, copy the complete `unity-editor-computer-use` folder into the current Codex skills directory while preserving its `agents/` and `references/` subfolders. Do not move the repository copy; future project-specific corrections belong here first.

## Maintenance

Update the reference when the Unity version, Editor layout, Simulator profile, Canvas resolution, Admin checkpoint array, or hidden gesture changes. Coordinates in the reference are examples only; screenshots and accessibility state remain authoritative.
