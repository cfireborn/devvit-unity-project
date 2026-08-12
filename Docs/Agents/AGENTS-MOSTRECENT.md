# Agent Handoff — Compersion Multiplayer and Story (August 11, 2026)

## Snapshot
- **Game**: Compersion — Unity 6 (6000.2.8f1) 2D platformer published as WebGL on GitHub Pages.
- **Publishing**: Devvit exporting is retired; releases use Unity's normal WebGL build and publish `Builds/WebGL` through GitHub Pages.
- **Networking**: FishNet 4.6.22 with Multipass transport (Tugboat UDP for editor/standalone, Bayou WebSocket for WebGL).
- **Hosting**: Linux headless build packaged through the Edgegap Unity plugin, fronted by a persistent Cloudflare Tunnel (`compersion.charliefeuerborn.com`) for Bayou while Tugboat uses the per-deploy Edgegap hostname.
- **Offline mode**: Automatic fallback after `NetworkBootstrapper` waits `connectionTimeoutSeconds` (5 s default). Gameplay components expose `ActivateOfflineMode()` so the single-player loop keeps running without a server.

Use this document plus `Docs/Agents/AGENTS.md` to orient yourself before modifying code, build pipelines, or deployments. Read `Docs/Agents/UNITY_EDITOR_WORKFLOW.md` before operating the Unity Editor, and read `Docs/Agents/STORY_THROUGH_SECOND_GOAL.md` before modifying dialogue, goals, or their scene wiring.

For human operational steps and failure recovery, use `Docs/EDGEGAP_SERVER_OPERATIONS.md`. For implementation invariants and agent audit checks, use `Docs/Agents/EDGEGAP_CLOUDFLARE_OPERATIONS.md`. This handoff is architectural context, not a substitute for either runbook.

---

## Repository Map (paths relative to project root)
```
Assets/
  FishNet/                      ← FishNet runtime + Bayou plugin
  Levels/SimpleLevel.unity      ← Only enabled shipping scene
  Scripts/
    Game/                       ← GameManagerM, GameServices, quest glue
    Player/                     ← PlayerControllerM + ScriptableObjects
    Environment/                ← Cloud system, ladders, level objects
    Network/                    ← Wrappers: NetworkBootstrapper, cloud+ladder sync, player sync, admin UI
Builds/
  EdgegapServer/                ← Edgegap plugin output (ServerBuild binary et al.)
  WebGL/                        ← Latest WebGL export (published to GitHub Pages)
Docs/Agents/                    ← You are here (AGENTS, AGENTS-MOSTRECENT, plans)
  STORY_THROUGH_SECOND_GOAL.md  ← Current narrative wiring, cuts, recovery, and test checklist
  UNITY_EDITOR_WORKFLOW.md      ← Safe Unity operation and persistence checklist
Server/                         ← Dockerfile and runtime start script (no tunnel credential/config)
Docs/EDGEGAP_SERVER_OPERATIONS.md ← Human operations runbook
Docs/Agents/EDGEGAP_CLOUDFLARE_OPERATIONS.md ← State-machine invariants and maintenance audit
update-edgegap-dockerfile.sh    ← Copies our Dockerfile into the Edgegap plugin cache
```

Other reference docs (mobile guides, historical analyses) live alongside these files inside `Docs/Agents/`.

---

## Runtime Modes & Network Flow
1. **Editor host (server + local client)** — The main Unity Editor hosts only when `useLocal`, `editorStartAsHost`, and `CurrentPlayer.IsMainEditor` are all true. It starts Tugboat server transport and then dials its local target.
2. **Clients and standalone startup** — MPPM virtual players and WebGL are clients. Non-WebGL standalone builds currently call both `TryStartServer` and `TryConnectClient`; the connection target still depends on `useLocal`, so do not describe every standalone as either a pure client or a local host without checking its build target and overrides.
3. **Offline single-player** — Triggered when no server responds or validation fails. `GameManagerM.ActivateOfflineMode()` spawns the player, re-enables `CloudManager` + `CloudLadderController`, and applies a grey tint. Wrappers that remain attached offline need a safe `ActivateOfflineMode()` path. The player path instead uses `NetworkPlayerSpawner.ActivateOfflineMode()` to strip FishNet components, including `NetworkPlayerController`, before explicitly re-enabling `PlayerControllerM`.

**Address resolution** happens once in `NetworkBootstrapper.Start()`:
- `useLocal` defaults to true for Editor, macOS standalone, and dedicated-server builds. It defaults to false for WebGL and Windows/Linux non-server standalone builds. `AdminMenuPrefs.UseLocalOverride` can override that resolved default.
- `_bayouAddress` defaults to `edgegapAddress` (Cloudflare tunnel). `_tugboatAddress` falls back to the same domain unless a deployment-specific `edgegapTugboatAddress` is filled in.
- Inspector fields exist for local + Edgegap addresses/ports so designers can swap targets without recompiling.

**Key principle**: never read `IsServerStarted`/`IsClientStarted` inside `Update` or `FixedUpdate` on a `NetworkBehaviour`. Cache `_serverRunning/_clientRunning` booleans in lifecycle callbacks and consult those everywhere else. See `NetworkCloudManager` and `NetworkCloudLadderController` for the canonical cached-flag pattern. `NetworkPlayerController` is a different ownership-driven wrapper and uses `IsSpawned` in `Update`.

---

## Core Systems At a Glance
| Area | File(s) | Notes |
|------|---------|-------|
| Bootstrap & offline | `Assets/Scripts/Network/NetworkBootstrapper.cs`, `Assets/Scripts/Game/GameManagerM.cs` | Starts transports, enforces timeout, toggles offline mode, wires `VirtualJoystick` tint, calls ActivateOfflineMode on subsystems. |
| Player spawning & sync | `NetworkPlayerSpawner.cs`, `NetworkPlayerController.cs`, `PlayerControllerM.cs` | Server spawns a NetworkPlayer prefab per connection. Owner keeps physics active, remotes disable their `Rigidbody2D` sim and mirror visual state via 15 Hz RPCs. `PlayerControllerM` listens to `TimeManager.OnTick` when networked. |
| Clouds | `CloudManager.cs`, `NetworkCloudManager.cs`, `CloudPlatform.cs` | Server-only simulation. Offline clouds reuse the local pool; network clouds use FishNet spawn/despawn and are not pooled. Clients receive replicated cloud objects and transforms. Offline path re-enables the original component. |
| Ladders | `CloudLadderController.cs`, `NetworkCloudLadderController.cs` | Server builds ladders, raises events. Clients rebuild ladder geometry every `LateUpdate` from synced cloud bounds—no continuous ladder RPC stream needed. |
| Local story progression | `InteractionTrigger.cs`, `DialogueTrigger.cs`, `GoalAssignmentTrigger.cs`, `GoalCompletionTrigger.cs`, `PlayerControllerM.cs` | Goals and dialogue progress independently in each client process. Remote player proxies cannot consume local triggers because their `PlayerControllerM` is disabled. |
| Story scene wiring | `Assets/Levels/SimpleLevel.unity`, `Docs/Agents/STORY_THROUGH_SECOND_GOAL.md` | Gray → Spike → Gray. COMPERSION appears before Spike; the second goal ends at Gray and opens the developer ending panel. Removed ladder-tutorial and delivery-cloud routes must stay removed. |
| Admin overrides | `Assets/Scripts/UI/AdminMenu.cs`, `AdminMenuPrefs.cs` | Inspector fields show which address/port is active and allow runtime overrides saved to EditorPrefs. `AttemptConnection` gates WebGL only; Editor offline tests require an invalid/unreachable active endpoint. |
| Editor workflow | `Docs/Agents/UNITY_EDITOR_WORKFLOW.md` | One writer per Unity project; edit outside Play Mode; save, reload, inspect the diff, then test offline/host/pure-client as appropriate. |
| Documentation | `Docs/Agents/*.md` | Keep current runbooks synchronized with implementation. `MULTIPLAYER_IMPLEMENTATION_PLAN.md` is historical and not current architecture guidance. |

---

## Current Story Through the Second Goal

The playable story in `Assets/Levels/SimpleLevel.unity` is a local, linear sequence:

`Gray opening → first letter goal → Spike completion → COMPERSION definition → Spike reply → return-to-Gray goal → Gray response → end panel with narrative and mailing-list links`

- The fixed-ladder tutorial and ordinary-mail/delivery-cloud branch are deliberate cuts. Ladders work normally from spawn.
- `SpikeTutorialDialogue_2.asset` is empty and retired. It is not referenced by the shipping scene; wiring it into a live chain would stall completion.
- The end panel is created by `GameUIManager.ShowEndOfDemo()` when no authored panel is assigned. Each external-link button grays after use but stays clickable; once both have been pressed, `Keep exploring` appears and resumes gameplay.
- Story state is not synchronized. Each local `PlayerControllerM` owns its own goals, while disabled remote proxies are rejected by `InteractionTrigger.IsAllowed()`.
- The six serialized scene transitions passed an Editor-side persistent-listener audit. A complete post-fix host plus pure-client playthrough is still pending and must not be reported as passed until performed.

The exact component layout, diagnostic file IDs, prefab-array warning, repair procedure, and runtime checklist live in `Docs/Agents/STORY_THROUGH_SECOND_GOAL.md`.

---

## Edgegap Hosting & Linux Build Pipeline

### 1. Edgegap Plugin Workflow
1. **Prerequisites**: Docker Desktop running, Edgegap Unity plugin authenticated locally, and `./update-edgegap-dockerfile.sh` run after package imports.
2. **Build** (`Tools → Edgegap Server Hosting → Build`): Produces `Builds/EdgegapServer/ServerBuild` (Unity headless binary, supporting files, `ServerBuild_Data`, etc.).
3. **Containerize**: The plugin uses the Dockerfile in its package cache and wraps the build into a Docker image. Our updater replaces that ephemeral file with the secured source Dockerfile, which adds a pinned Cloudflare binary and the start script without adding tunnel credentials or config.
4. **Upload**: Click the stock **Upload image and Create app version** button to push the freshly built image to Edgegap's registry. The updater patches its post-upload destination, so the label does not describe the final browser action in this project.
5. **Select tag and Save**: Chrome opens the existing stable Edgegap version. Select the uploaded tag and save it. Do not create a new version or paste the tunnel secret. The homepage-triggered watchdog starts the singleton deployment when a visitor needs it.

### 2. Cloudflare Tunnel + Docker Runtime
- `Server/Dockerfile` pins cloudflared and separates the tunnel and game into unprivileged accounts. No credential is copied into the image.
- `Server/start.sh` reads hidden `CF_TUNNEL_TOKEN` at runtime via a mode-0600 token file and does not expose it to Unity or process arguments.
- Legacy `Server/nginx.conf` plus `Server/stunnel.conf` are historical WSS approaches. Keep them for reference but they are not part of the current startup flow.
- `Server/Dockerfile.edgegap-original` is the plugin's stock template for comparison; do not edit it.

### 3. Dockerfile Override Script
- The Edgegap plugin lives under `Library/PackageCache/com.edgegap.unity-servers-plugin@*/`. Building after a clean checkout or plugin update reverts its bundled Dockerfile.
- Run `./update-edgegap-dockerfile.sh` whenever `Library/` is rebuilt. It discovers the plugin hash, installs the secured Dockerfile, and makes **Upload image and Create app version** open the stable secret-bearing Edgegap version. Select the new tag and Save; do not create a fresh version.

### 4. Address Management After Deployments
- WebGL/Bayou clients always hit `edgegapAddress` (default `compersion.charliefeuerborn.com`) on port `edgegapBayouPort` (443). The Cloudflare tunnel routes them into the running container regardless of deployment.
- Homepage and watchdog health intentionally use a direct handshake to that same WSS address, matching website commit `763e6fb`; no separate Unity HTTP readiness endpoint is required.
- WebGL attempts this connection automatically on a fresh session. Set `AdminMenuPrefs.AttemptConnection` false only when deliberately testing offline fallback.
- Tugboat clients (editor, standalone, macOS) must be pointed at the deploy-specific hostname/port pair that Edgegap displays after each launch. Update either the `NetworkBootstrapper` inspector or the Admin Menu overrides before testing.
- Local testing flips `useLocal` on and uses `localAddress`, `localTugboatPort` (7777), and `localBayouPort` (7771).

### 5. Calendar Versioning + WebGL Cache Updates

- `Assets/Editor/BuildVersioning.cs` creates and bumps the gitignored `Assets/Resources/BuildVersion.txt` before every build and editor play session, records `YYYY.MM.DD.run-<8-char Git SHA>`, and sets `PlayerSettings.bundleVersion` for WebGL builds.
- `AdminMenu.Awake()` copies that resource into the prefab's `VersionText`, making the embedded version visible in editor play and every player build.
- `Assets/WebGLTemplates/Compersion/` appends the version to Unity artifact and service-worker URLs. The generated service worker activates immediately, checks only the current version cache, and deletes older caches for this game. No generated WebGL file needs manual editing after a build.

---

## Testing & Operational Tips
- **Multiplayer Play Mode**: Window → Multiplayer Play Mode. Keep the main editor window focused when acting as host; launch 1–3 virtual players for quick regression tests.
- **WebGL smoke test** (serve the browser build locally):
  ```
  cd /Users/cfire/Desktop/devvit-unity-project/Builds/WebGL
  python3 -m http.server 8080
  ```
  Then open `http://localhost:8080` in a browser. Ensure Admin Menu's `AttemptConnection` is true so WebGL actually dials Bayou.
- **Local macOS server** (run a dedicated server from the terminal without Unity):
  ```
  cd /Users/cfire/Desktop/devvit-unity-project/Builds/MacOSServer
  ./SampleGame
  ```
  The server starts headless and listens on the configured Tugboat port (default 7777). Editor/standalone clients can then connect to `localhost`.
- **Verifying Cloudflare**: The secured Dockerfile copies `cloudflared` from a pinned official container image; it does not download a mutable release during the build. After **Upload image and Create app version** completes, verify the stable Edgegap version points to the new tag, then use the homepage and the checks in `Docs/EDGEGAP_SERVER_OPERATIONS.md`.
- **Offline fallback**: In WebGL, set `AdminMenuPrefs.AttemptConnection = false` for immediate offline startup. In Editor, select the intended local/Edgegap mode and use an invalid or unreachable active address/port so validation or the timeout triggers fallback. Confirm `CloudManager` + `CloudLadderController` re-enable and the player tint flips grey.

---

## Open Items / Watchlist
1. **Automated Edgegap session discovery**: There is no `EdgegapConnector` yet. WebGL clients rely on the static Cloudflare tunnel, so scaling to multiple simultaneous deployments will require an API-driven session lookup and dynamic Tugboat host selection.
2. **ServerBuilder tooling**: Builds currently flow through the Edgegap plugin UI. If you need CI, scriptable builds for Linux headless, or reproducible Docker contexts, add a custom `ServerBuilder` editor script or CLI pipeline.
3. **Docs hygiene**: When you touch transports, build steps, hosting credentials, or watchdog behavior, update the two operations runbooks plus this handoff as appropriate. Do not leave conflicting procedures in older summaries.
4. **Edgegap plugin source drift**: The updater discovers the package hash dynamically and fails closed if it cannot find exactly one plugin or patch the expected **Upload image and Create app version** flow. Do not bypass that failure; review the updated plugin source first.

Keep these notes synchronized with the codebase. A future agent should be able to recreate the entire stack—editor testing, server build, container push, and deployment—using only this document plus the README.
