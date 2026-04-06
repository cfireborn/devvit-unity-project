# Agent Handoff — Compersion Multiplayer (March 24, 2026)

## Snapshot
- **Game**: Compersion — Unity 6 (6000.2.8f1) 2D platformer embedded via Devvit WebGL iframe.
- **Networking**: FishNet 4.6.22 with Multipass transport (Tugboat UDP for editor/standalone, Bayou WebSocket for WebGL).
- **Hosting**: Linux headless build packaged through the Edgegap Unity plugin, fronted by a persistent Cloudflare Tunnel (`compersion.charliefeuerborn.com`) for Bayou while Tugboat uses the per-deploy Edgegap hostname.
- **Offline mode**: Automatic fallback after `NetworkBootstrapper` waits `connectionTimeoutSeconds` (5 s default). Gameplay components expose `ActivateOfflineMode()` so the single-player loop keeps running without a server.

Use this document plus `Docs/Agents/AGENTS.md` to orient yourself before modifying code, build pipelines, or deployments.

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
  WebGL/                        ← Latest WebGL export (served to Devvit)
Docs/Agents/                    ← You are here (AGENTS, AGENTS-MOSTRECENT, plans)
Server/                         ← Dockerfile, Cloudflare tunnel config, start script
Tools/export_devvit.sh          ← Packs WebGL build for Devvit upload
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
| Clouds | `CloudManager.cs`, `NetworkCloudManager.cs`, `CloudPlatform.cs` | Server-only spawn/despawn/pooling. Clients receive ObserversRpc events, reuse host GOs when running as host, and lerp/teleport when drift > 1.5 units. Offline path re-enables the original component. |
| Ladders | `CloudLadderController.cs`, `NetworkCloudLadderController.cs` | Server builds ladders, raises events. Clients rebuild ladder geometry every `LateUpdate` from synced cloud bounds—no continuous ladder RPC stream needed. |
| Admin overrides | `Assets/Scripts/UI/AdminMenu.cs`, `AdminMenuPrefs.cs` | Inspector fields show which address/port is active, allow overriding at runtime (saved to EditorPrefs). Includes toggles for forcing local/offline tests. |
| Documentation | `Docs/Agents/*.md`, `Docs/Agents/MULTIPLAYER_IMPLEMENTATION_PLAN.md` | Keep these files current whenever you change transports, hosting steps, or architecture. |

---

## Edgegap Hosting & Linux Build Pipeline

### 1. Edgegap Plugin Workflow
1. **Prerequisites**: Docker Desktop running, Edgegap Unity plugin authenticated (API key), and `Server/cloudflare-credentials.json` checked into your machine (gitignored).
2. **Build** (`Tools → Edgegap Server Hosting → Build`): Produces `Builds/EdgegapServer/ServerBuild` (Unity headless binary, supporting files, `ServerBuild_Data`, etc.).
3. **Containerize**: The plugin copies whatever Dockerfile it finds in its package cache and wraps the build into a Docker image. Our override injects Cloudflare, start script, and tunnel config.
4. **Upload**: Pushes the freshly built image to Edgegap's registry (you can also configure Docker Hub if desired).
5. **Deploy**: Spins up a fleet instance. Note the deployment hostname (`*.pr.edgegap.net`) and external UDP port mapped to internal 7777. Update the inspector/Admin Menu with these values for Tugboat clients.

### 2. Cloudflare Tunnel + Docker Runtime
- `Server/Dockerfile` (source of truth) installs CA certs, downloads `cloudflared`, copies `Server/cloudflare-credentials.json` to `/etc/cloudflared/credentials.json`, and copies `Server/cloudflare-tunnel.yml` for the `compersion` tunnel. It exposes **only** `7777/udp` because Bayou/WebGL traffic flows through Cloudflare outbound.
- `Server/start.sh` is the container entrypoint. It launches `cloudflared tunnel --config /etc/cloudflared/config.yml run &`, then execs `/root/build/ServerBuild -batchmode -nographics $UNITY_COMMANDLINE_ARGS` so the Unity process receives stop signals.
- Legacy `Server/nginx.conf` plus `Server/stunnel.conf` are historical WSS approaches. Keep them for reference but they are not part of the current startup flow.
- `Server/Dockerfile.edgegap-original` is the plugin's stock template for comparison; do not edit it.

### 3. Dockerfile Override Script
- The Edgegap plugin lives under `Library/PackageCache/com.edgegap.unity-servers-plugin@35356e28ab54/`. Building after a clean checkout or plugin update reverts its bundled Dockerfile.
- Run `./update-edgegap-dockerfile.sh` whenever `Library/` is rebuilt. It copies `Server/Dockerfile` into the plugin's `Editor/Dockerfile` so container builds keep installing Cloudflare and invoking `start.sh`.
- If Edgegap releases a new plugin commit, change the `PLUGIN_DIR` constant in the script to match the new hash or the copy step will error out.

### 4. Address Management After Deployments
- WebGL/Bayou clients always hit `edgegapAddress` (default `compersion.charliefeuerborn.com`) on port `edgegapBayouPort` (443). The Cloudflare tunnel routes them into the running container regardless of deployment.
- Tugboat clients (editor, standalone, macOS) must be pointed at the deploy-specific hostname/port pair that Edgegap displays after each launch. Update either the `NetworkBootstrapper` inspector or the Admin Menu overrides before testing.
- Local testing flips `useLocal` on and uses `localAddress`, `localTugboatPort` (7777), and `localBayouPort` (7771).

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
- **Verifying Cloudflare**: After containerizing, inspect the plugin log for the line that downloads `cloudflared-linux-amd64`. If missing, re-run `update-edgegap-dockerfile.sh` before rebuilding.
- **Offline fallback**: Force it by blanking the Edgegap address or setting `AdminMenuPrefs.AttemptConnection = false`. Confirm `CloudManager` + `CloudLadderController` re-enable and the player tint flips grey.

---

## Open Items / Watchlist
1. **Automated Edgegap session discovery**: There is no `EdgegapConnector` yet. WebGL clients rely on the static Cloudflare tunnel, so scaling to multiple simultaneous deployments will require an API-driven session lookup and dynamic Tugboat host selection.
2. **ServerBuilder tooling**: Builds currently flow through the Edgegap plugin UI. If you need CI, scriptable builds for Linux headless, or reproducible Docker contexts, add a custom `ServerBuilder` editor script or CLI pipeline.
3. **Docs hygiene**: When you touch transports, build steps, or hosting credentials, update both `Docs/Agents/AGENTS.md` and this file immediately. They are considered the single source of truth for other agents.
4. **Edgegap plugin hash drift**: Reconfirm the path inside `update-edgegap-dockerfile.sh` whenever you upgrade the plugin. Broken copies silently revert to the stock Dockerfile (no Cloudflare, no start script) and Bayou connections will fail over WSS.

Keep these notes synchronized with the codebase. A future agent should be able to recreate the entire stack—editor testing, server build, container push, and deployment—using only this document plus the README.
