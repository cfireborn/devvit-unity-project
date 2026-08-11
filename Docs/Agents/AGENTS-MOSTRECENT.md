# Agent Handoff — Compersion Multiplayer (March 24, 2026)

## Snapshot
- **Game**: Compersion — Unity 6 (6000.2.8f1) 2D platformer published as WebGL on GitHub Pages.
- **Publishing**: Devvit exporting is retired; releases use Unity's normal WebGL build and publish `Builds/WebGL` through GitHub Pages.
- **Networking**: FishNet 4.6.22 with Multipass transport (Tugboat UDP for editor/standalone, Bayou WebSocket for WebGL).
- **Hosting**: Linux headless build packaged through the Edgegap Unity plugin, fronted by a persistent Cloudflare Tunnel (`compersion.charliefeuerborn.com`) for Bayou while Tugboat uses the per-deploy Edgegap hostname.
- **Offline mode**: Automatic fallback after `NetworkBootstrapper` waits `connectionTimeoutSeconds` (5 s default). Gameplay components expose `ActivateOfflineMode()` so the single-player loop keeps running without a server.

Use this document plus `Docs/Agents/AGENTS.md` to orient yourself before modifying code, build pipelines, or deployments.

For human operational steps and failure recovery, use `Docs/EDGEGAP_SERVER_OPERATIONS.md`. For implementation invariants and agent audit checks, use `Docs/Agents/EDGEGAP_CLOUDFLARE_OPERATIONS.md`. This handoff is architectural context, not a substitute for either runbook.

---

## Repository Map (paths relative to project root)
```
Assets/
  FishNet/                      ← FishNet runtime + Bayou plugin
  Scenes/SimpleLevel.unity      ← Only shipping scene
  Scripts/
    Game/                       ← GameManagerM, GameServices, quest glue
    Player/                     ← PlayerControllerM + ScriptableObjects
    Environment/                ← Cloud system, ladders, level objects
    Network/                    ← Wrappers: NetworkBootstrapper, cloud+ladder sync, player sync, admin UI
Builds/
  EdgegapServer/                ← Edgegap plugin output (ServerBuild binary et al.)
  WebGL/                        ← Latest WebGL export (published to GitHub Pages)
Docs/Agents/                    ← You are here (AGENTS, AGENTS-MOSTRECENT, plans)
Server/                         ← Dockerfile and runtime start script (no tunnel credential/config)
Docs/EDGEGAP_SERVER_OPERATIONS.md ← Human operations runbook
Docs/Agents/EDGEGAP_CLOUDFLARE_OPERATIONS.md ← State-machine invariants and maintenance audit
update-edgegap-dockerfile.sh    ← Copies our Dockerfile into the Edgegap plugin cache
```

Other reference docs (mobile guides, historical analyses) live alongside these files inside `Docs/Agents/`.

---

## Runtime Modes & Network Flow
1. **Host (server + local client)** — Default for the main Unity editor window (when `editorStartAsHost` is checked). `NetworkBootstrapper` starts the server, then dials itself via Tugboat using `_tugboatAddress`/`_tugboatPort`.
2. **Remote client** — Multiplayer Play Mode (MPPM) virtual players, standalone builds, and WebGL. `SetClientTransport<T>()` chooses Tugboat or Bayou based on build target or Admin Menu overrides, then `TryConnectClient` handles validation + timeout coroutine.
3. **Offline single-player** — Triggered when no server responds or validation fails. `GameManagerM.ActivateOfflineMode()` spawns the player, re-enables `CloudManager` + `CloudLadderController`, and applies a grey tint. Every NetworkBehaviour that disables gameplay code must expose an `ActivateOfflineMode()` hook so this path is complete.

**Address resolution** happens once in `NetworkBootstrapper.Start()`:
- `useLocal` defaults to true for Editor / Standalone / Server, false for WebGL. Admin Menu toggles can override it at runtime via `AdminMenuPrefs.UseLocalOverride`.
- `_bayouAddress` defaults to `edgegapAddress` (Cloudflare tunnel). `_tugboatAddress` falls back to the same domain unless a deployment-specific `edgegapTugboatAddress` is filled in.
- Inspector fields exist for local + Edgegap addresses/ports so designers can swap targets without recompiling.

**Key principle**: never read `IsServerStarted`/`IsClientStarted` inside `Update` or `FixedUpdate` on a `NetworkBehaviour`. Cache `_serverRunning/_clientRunning` booleans in lifecycle callbacks and consult those everywhere else. See `NetworkCloudManager`, `NetworkCloudLadderController`, and `NetworkPlayerController` for the canonical pattern.

---

## Core Systems At a Glance
| Area | File(s) | Notes |
|------|---------|-------|
| Bootstrap & offline | `Assets/Scripts/Network/NetworkBootstrapper.cs`, `Assets/Scripts/Game/GameManagerM.cs` | Starts transports, enforces timeout, toggles offline mode, wires `VirtualJoystick` tint, calls ActivateOfflineMode on subsystems. |
| Player spawning & sync | `NetworkPlayerSpawner.cs`, `NetworkPlayerController.cs`, `PlayerControllerM.cs` | Server spawns a NetworkPlayer prefab per connection. Owner keeps physics active, remotes disable their `Rigidbody2D` sim and mirror visual state via 15 Hz RPCs. `PlayerControllerM` listens to `TimeManager.OnTick` when networked. |
| Clouds | `CloudManager.cs`, `NetworkCloudManager.cs`, `CloudPlatform.cs` | Server-only simulation. Offline clouds reuse the local pool; network clouds use FishNet spawn/despawn and are not pooled. Clients receive replicated cloud objects and transforms. Offline path re-enables the original component. |
| Ladders | `CloudLadderController.cs`, `NetworkCloudLadderController.cs` | Server builds ladders, raises events. Clients rebuild ladder geometry every `LateUpdate` from synced cloud bounds—no continuous ladder RPC stream needed. |
| Admin overrides | `Assets/Scripts/UI/AdminMenu.cs`, `AdminMenuPrefs.cs` | Inspector fields show which address/port is active, allow overriding at runtime (saved to EditorPrefs). Includes toggles for forcing local/offline tests. |
| Documentation | `Docs/Agents/*.md`, `Docs/Agents/MULTIPLAYER_IMPLEMENTATION_PLAN.md` | Keep these files current whenever you change transports, hosting steps, or architecture. |

---

## Edgegap Hosting & Linux Build Pipeline

### 1. Edgegap Plugin Workflow
1. **Prerequisites**: Docker Desktop running, Edgegap Unity plugin authenticated locally, and `./update-edgegap-dockerfile.sh` run after package imports.
2. **Build** (`Tools → Edgegap Server Hosting → Build`): Produces `Builds/EdgegapServer/ServerBuild` (Unity headless binary, supporting files, `ServerBuild_Data`, etc.).
3. **Containerize**: The plugin uses the Dockerfile in its package cache and wraps the build into a Docker image. Our updater replaces that ephemeral file with the secured source Dockerfile, which adds a pinned Cloudflare binary and the start script without adding tunnel credentials or config.
4. **Upload**: Pushes the freshly built image to Edgegap's registry (you can also configure Docker Hub if desired).
5. **Select tag and Save**: After Upload, Chrome opens the existing stable Edgegap version. Select the uploaded tag and save it. Do not create a new version or paste the tunnel secret. The homepage-triggered watchdog starts the singleton deployment when a visitor needs it.

### 2. Cloudflare Tunnel + Docker Runtime
- `Server/Dockerfile` pins cloudflared and separates the tunnel and game into unprivileged accounts. No credential is copied into the image.
- `Server/start.sh` reads hidden `CF_TUNNEL_TOKEN` at runtime via a mode-0600 token file and does not expose it to Unity or process arguments.
- Legacy `Server/nginx.conf` plus `Server/stunnel.conf` are historical WSS approaches. Keep them for reference but they are not part of the current startup flow.
- `Server/Dockerfile.edgegap-original` is the plugin's stock template for comparison; do not edit it.

### 3. Dockerfile Override Script
- The Edgegap plugin lives under `Library/PackageCache/com.edgegap.unity-servers-plugin@*/`. Building after a clean checkout or plugin update reverts its bundled Dockerfile.
- Run `./update-edgegap-dockerfile.sh` whenever `Library/` is rebuilt. It discovers the plugin hash, installs the secured Dockerfile, and makes Upload open the stable secret-bearing Edgegap version. Select the new tag and Save; do not create a fresh version.

### 4. Address Management After Deployments
- WebGL/Bayou clients always hit `edgegapAddress` (default `compersion.charliefeuerborn.com`) on port `edgegapBayouPort` (443). The Cloudflare tunnel routes them into the running container regardless of deployment.
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
- **Verifying Cloudflare**: The secured Dockerfile copies `cloudflared` from a pinned official container image; it does not download a mutable release during the build. After Upload, verify the stable Edgegap version points to the new tag, then use the homepage and the checks in `Docs/EDGEGAP_SERVER_OPERATIONS.md`.
- **Offline fallback**: Force it by blanking the Edgegap address or setting `AdminMenuPrefs.AttemptConnection = false`. Confirm `CloudManager` + `CloudLadderController` re-enable and the player tint flips grey.

---

## Open Items / Watchlist
1. **Automated Edgegap session discovery**: There is no `EdgegapConnector` yet. WebGL clients rely on the static Cloudflare tunnel, so scaling to multiple simultaneous deployments will require an API-driven session lookup and dynamic Tugboat host selection.
2. **ServerBuilder tooling**: Builds currently flow through the Edgegap plugin UI. If you need CI, scriptable builds for Linux headless, or reproducible Docker contexts, add a custom `ServerBuilder` editor script or CLI pipeline.
3. **Docs hygiene**: When you touch transports, build steps, hosting credentials, or watchdog behavior, update the two operations runbooks plus this handoff as appropriate. Do not leave conflicting procedures in older summaries.
4. **Edgegap plugin source drift**: The updater discovers the package hash dynamically and fails closed if it cannot find exactly one plugin or patch the expected Upload flow. Do not bypass that failure; review the updated plugin source first.

Keep these notes synchronized with the codebase. A future agent should be able to recreate the entire stack—editor testing, server build, container push, and deployment—using only this document plus the README.
