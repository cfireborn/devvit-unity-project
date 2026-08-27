# Agent Instructions — Compersion

## Core Principle: Minimum Effective Change

Before writing any code, ask: **what is the smallest change that correctly solves this problem?**

A one-word fix beats a one-line fix. A one-line fix beats a five-line fix. A five-line fix beats a new method. A new method beats a new class. Only escalate when the smaller option genuinely cannot work.

If you find yourself rewriting a function to fix one branch of it, stop. Fix the branch.

---

## Before You Change Anything

1. **Read the relevant files first.** Never propose changes to code you haven't read. Understand what's already there — the answer is often already present or a one-line addition away.
2. **Trace the call chain.** A bug that appears in file C was probably introduced in file A. Fix A, not C.
3. **Check for existing patterns.** This codebase has established conventions (FishNet lifecycle flags, offline mode guards, SyncVar → BufferLast RPC pattern). Match them exactly rather than inventing a new approach.

---

## Change Size Rules

**Do not add** comments, logging, error handling, or null checks that aren't directly required by the task.

**Do not refactor** surrounding code while fixing a bug. The scope of a change is the bug, not the file.

**Do not extract** a helper method unless the same logic appears in three or more places and you are asked to.

**Do not introduce** a new abstraction (interface, base class, manager, event system) unless the task explicitly requires it.

**Ask before rewriting.** If the right fix seems to require rewriting more than ~20 lines, pause and confirm with the user that the scope is correct.

---

## Architecture: Think Before You Add

Every new component, event, dictionary, and RPC has a carrying cost: it must be initialized, cleaned up, kept in sync, and understood by the next agent. Before adding one, ask:

- Does an existing FishNet feature already do this? (NetworkTransform, ObserversRpc BufferLast, Objects.Spawned, etc.)
- Can the data be derived from something already synced rather than synced independently?
- Will this still make sense in 6 months when the codebase has grown?

Prefer **deriving** state from already-synced data over **replicating** it separately. Ladder positions are derived from cloud positions — not independently synced — because clouds are already synced. That pattern was chosen deliberately.

---

## FishNet-Specific Rules

- **Never use `IsServerStarted` or `IsClientStarted` in `Update`/`FixedUpdate`** on a NetworkBehaviour that may exist in offline mode. Use cached `_serverRunning`/`_clientRunning` bool flags set in `OnStartServer`/`OnStartClient`.
- **`[SyncVar]` attribute is gone in FishNet v4.** Use `[ObserversRpc(RunLocally = true, BufferLast = true)]` for spawn-time value sync.
- **Late-joiner sync is free** when using NetworkObject + BufferLast RPCs. Do not write manual `TargetRpc` late-joiner passes unless FishNet genuinely cannot cover the case.
- **`InstanceFinder.IsServerStarted`** is safe to call statically anywhere. `NetworkBehaviour.IsServerStarted` is not safe in offline mode.

---

## What Not To Do

- Do not add a new script when a two-line addition to an existing one will work.
- Do not split a component into two components to "separate concerns" unless asked.
- Do not add `Debug.Log` calls unless actively debugging a specific reported issue, and remove them when done.
- Do not rewrite working code to match your preferred style.
- Do not leave TODO comments. Either do the thing or don't.

---

## Project Context

See `AGENTS-MOSTRECENT.md` in this same folder for the in-depth architecture brief, deployment runbooks, and current priorities. Read it end-to-end before touching multiplayer code or build tooling.

Before editing scenes, prefabs, ScriptableObjects, or UnityEvents, read `UNITY_EDITOR_WORKFLOW.md`. It records the project's safe Editor workflow, persistence checks, prefab-override hazards, multiplayer test modes, and recovery steps.

Before controlling the Unity Editor through Computer Use, use `skills/unity-editor-computer-use/SKILL.md` and read `UNITY_COMPUTER_USE.md`. They record the proven macOS `node_repl` workflow for asset refresh, Play Mode, Simulator zoom/panning, hidden Admin checkpoints, visual evidence, and cleanup of Unity-generated test noise.

Before changing dialogue or goal progression, read `STORY_THROUGH_SECOND_GOAL.md`. Its **Verification Status** is authoritative: do not turn a serialized wiring audit into a runtime-playtest claim.

## Publishing Target

- The current publishing target is WebGL through GitHub Pages.
- Devvit exporting is retired. Do not recreate or look for a Devvit-specific export script, Unity editor menu, or double-build artifact-copy workflow; the retired tooling has been removed.
- Devvit runtime bridge/API integration is also retired; the current build has no Devvit-specific runtime dependency.

---

## Runtime Modes & Architecture Quick Reference

- **Three modes always matter**: host (server+client), remote client, and offline fallback triggered by `NetworkBootstrapper` after `connectionTimeoutSeconds`. A network wrapper that remains attached in offline mode must use safe cached lifecycle flags and/or provide an `ActivateOfflineMode()` path. The player is the deliberate exception: `NetworkPlayerSpawner.ActivateOfflineMode()` destroys FishNet components, including `NetworkPlayerController`, then explicitly re-enables `PlayerControllerM`.
- **Cloud/ladder wrapper pattern**: gameplay scripts live in `Assets/Scripts/Environment`; their network wrappers in `Assets/Scripts/Network` disable the local simulation in `Awake()`, re-enable it on the server in `OnStartServer()`, and provide offline delegates. Follow `NetworkCloudManager` / `NetworkCloudLadderController` for those server-authoritative systems. The player path is different: `PlayerControllerM` uses FishNet's `TimeManager.OnTick`, and `NetworkPlayerController` enables it for the owning client, not the server.
- **FishNet transports**: Multipass hosts Tugboat (UDP, editor/standalone) and Bayou (WebGL via WebSocket). `NetworkBootstrapper` selects which sub-transport to use per build target and exposes `edgegap*` inspector fields that the in-game Admin Menu can override at runtime.
- **Testing loop**: In local mode, the main Unity Editor is a host only when `editorStartAsHost` is enabled and it is the MPPM main Editor; MPPM virtual players are clients. `AdminMenuPrefs.AttemptConnection` is checked only by WebGL. To force Editor offline fallback, point the active local/Edgegap configuration at an invalid or unreachable endpoint and let validation/timeout fail.

---

## Edgegap & Hosting Snapshot

The human build, credential, verification, and recovery procedure is `Docs/EDGEGAP_SERVER_OPERATIONS.md`. The deeper implementation invariants and agent audit checklist are in `Docs/Agents/EDGEGAP_CLOUDFLARE_OPERATIONS.md`. Read both before changing the server image, Edgegap version, tunnel, or watchdog.

- **Deployment target**: Linux dedicated server built via the Edgegap Unity plugin (`Tools → Edgegap Server Hosting`). Build → Containerize → **Upload image and Create app version** creates and pushes the image; despite the stock button label, our patch opens the existing stable version, where selecting its tag prepares it for the visitor-triggered watchdog.
- **Networking split**: Tugboat UDP clients connect directly to the hostname Edgegap assigns per deployment (update `edgegapTugboatAddress` + port in `NetworkBootstrapper`). WebGL/Bayou clients stay on the stable Cloudflare Tunnel domain `compersion.charliefeuerborn.com:443`.
- **Cloudflare tunnel**: the remotely managed tunnel injects hidden `CF_TUNNEL_TOKEN` into the stable Edgegap version. `Server/start.sh` keeps it isolated from Unity and forwards WSS traffic to Bayou on localhost:7771.
- **Server folder**: `Server/Dockerfile` copies a pinned `cloudflared` binary and the start script, exposes `7777/udp`, and runs a small supervisor that launches the tunnel and game under separate unprivileged users. No tunnel config or credential is copied into the image. Legacy helpers (`nginx.conf`, `stunnel.conf`) are unused now that Cloudflare handles TLS; keep them only for reference.

---

## Linux Server Build & Deployment Checklist

1. **Prereqs**: the project-recorded Unity version (`6000.2.8f1` at this handoff), FishNet 4.6.22 already imported, Docker Desktop running, Edgegap plugin logged in locally, and `./update-edgegap-dockerfile.sh` completed. Do not upgrade Unity as part of a release operation.
2. **Build**: In Unity, open `Tools → Edgegap Server Hosting` and click **Build** to emit `ServerBuild` under `Builds/EdgegapServer/`.
3. **Containerize**: Click **Containerize**—the plugin builds with the secured Dockerfile that the updater installed in its package cache. Confirm the log shows the pinned Cloudflare image stage.
4. **Upload & select tag**: Click **Upload image and Create app version** to push the image. The updater patches this stock action so Chrome opens the existing stable Edgegap version; select the new tag and click **Save**. Do not create a version or manually deploy as part of a normal image update. A homepage visit triggers the singleton watchdog deployment when needed.
5. **Connect**: Editor/standalone clients use Tugboat + the fresh hostname; WebGL stays pointed at the Cloudflare address. If no server responds within five seconds, offline mode automatically re-enables gameplay components.

---

## Build and Play Versioning

- `Assets/Editor/BuildVersioning.cs` creates and bumps the gitignored `Assets/Resources/BuildVersion.txt` before every player build and every editor play session. The format is `YYYY.MM.DD.run-commitsha`, for example `2026.08.08.2-bf6505a4`; the run counter resets to 1 on a new local calendar day.
- Before WebGL builds, the hook also assigns the same value to `PlayerSettings.bundleVersion`. `AdminMenu` reads the resource into the `VersionText` object on every `Awake`, so editor and built players expose the exact embedded version.
- The `Compersion` WebGL template adds this version to all generated Unity artifact URLs and to the service-worker script URL. Its service worker only reads from the current version's cache and removes older Compersion caches during activation. Do not patch generated `framework.js` or build output by hand; rebuilding applies the policy automatically.
- If Git is unavailable, versioning retains the last recorded SHA (or `00000000` before the first successful lookup) so local play and builds remain fire-and-forget.

---

## Edgegap Plugin Dockerfile Override

- Unity stores packages under `Library/PackageCache/`, so updating Unity or nuking `Library/` causes the Edgegap plugin to revert to its stock Dockerfile (no Cloudflare tunnel, no start script).
- Run `./update-edgegap-dockerfile.sh` after every package reimport. It discovers the active plugin cache, installs the secured Dockerfile, and patches the **Upload image and Create app version** action to open the stable Edgegap version so its hidden tunnel secret is retained.
- Treat the copied Dockerfile as ephemeral—**edit the source in `Server/Dockerfile` only**, then re-run the script to fan out the change.
- Do not bypass a failed updater. It deliberately stops when the plugin cache is ambiguous or the plugin's upload source has drifted.
