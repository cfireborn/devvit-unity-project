# Fishnet Multiplayer Implementation Plan

## ⚠️ IMPORTANT: Always Support Offline / Single-Player Mode

**Every feature added to this project must work in all three runtime modes:**

1. **Networked host** — `InstanceFinder.NetworkManager != null`, `IsServerStarted == true`
2. **Networked client** — `InstanceFinder.NetworkManager != null`, `IsServerStarted == false`
3. **Offline fallback** — `NetworkManager` present in scene but connection timed out; `NetworkBootstrapper` calls `GameManagerM.ActivateOfflineMode()` after 5 s

**Common pitfalls:**
- Components disabled in `Awake()` for network safety (e.g. `CloudManager`, `PlayerControllerM`) stay disabled in offline mode unless explicitly re-enabled in `ActivateOfflineMode()`.
- `InstanceFinder.NetworkManager != null` is `true` even offline — use `IsServerStarted` / `IsClientStarted` to distinguish, not just NetworkManager presence.
- Any new server-only logic (RPCs, `OnStartServer` hooks) needs an offline equivalent path via `ActivateOfflineMode()` or `GameServices.onPlayerRegistered`.
- The CloudManager **GameObject** must always be active in the scene — only the **component** is toggled by code. A disabled GO is invisible to `FindFirstObjectByType`.
- Test offline: lower `connectionTimeoutSeconds` on `NetworkBootstrapper` to 2–3 s and run without a server.

---

## Overview

Transform the Compersion Unity 2D platformer into a multiplayer game using Fishnet networking with Edgegap server hosting, targeting WebGL deployment on Reddit's Devvit platform.

---

## Architecture

### Networking Stack
| Context | Transport | Port |
|---------|-----------|------|
| Editor / MPPM testing (Phases 1–4) | **Tugboat** (UDP) | 7770 |
| WebGL client (Phase 5+) | **Bayou** (WebSocket) | 7771 |
| Production server | **Multipass** (Tugboat + Bayou together) | 7770 UDP + 7771 TCP |

- **Framework**: Fishnet v4.6.22 — `Assets/FishNet`
- **Bayou**: `Assets/FishNet/Plugins/Bayou` (imported from GitHub releases .unitypackage — git URL does NOT work)
- **Hosting**: Edgegap cloud deployment with Docker containers

### ⚠️ Transport Setup (READ BEFORE RESUMING WORK)

**Current state (Phases 1–4, editor/MPPM testing):**
- NetworkManager uses **Tugboat only**
- Bayou component must NOT be on the NetworkManager — it self-initializes and causes "Connection refused" errors even when another transport is selected

**Phase 5 — switching to Multipass for production:**
1. Add `Multipass` component to NetworkManager GameObject
2. Add `Tugboat` (port 7770, UDP) and `Bayou` (port 7771, WebSocket) components
3. Set TransportManager → Transport = Multipass; add both as Multipass transports
4. WebGL `NetworkBootstrapper` connects to port 7771; editor/standalone connects to 7770
5. Dockerfile: expose both `7770/udp` and `7771/tcp`

**Testing Bayou (WebGL) before full Edgegap deployment:**
1. Run a Unity Editor host (server listening on Bayou port 7771)
2. Build WebGL: File → Build Settings → WebGL → Build
3. Serve locally: `cd Builds/WebGL && python3 -m http.server 8080`
4. Open `http://localhost:8080` — browser client connects via `ws://localhost:7771`

### Key Architectural Patterns
- **Client-Server**: Server-authoritative architecture
- **Player Sync**: Client-side prediction for local player, NetworkTransform interpolation for remote players; PhysicsMode = TimeManager required on TimeManager component
- **Cloud Sync**: Server-only CloudManager; spawn/despawn/position broadcast to all clients (including host) via ObserversRpc; host borrows CloudManager references instead of creating duplicate GOs
- **Offline Fallback**: 5 s connection timeout → `ActivateOfflineMode()` → local player spawned (grey tint) + CloudManager re-enabled
- **Quest System**: Local to each player — no network sync needed

---

## What's Been Built

### Files Created
| File | Purpose |
|------|---------|
| `Assets/Scripts/Network/NetworkBootstrapper.cs` | Starts server/host/client by build target + MPPM player index; 5 s offline fallback |
| `Assets/Scripts/Network/NetworkPlayerSpawner.cs` | Server spawns NetworkPlayer prefab per connecting client |
| `Assets/Scripts/Network/NetworkPlayerController.cs` | Enables input/physics for owner only; syncs visual state (sprite, flip) at 15 Hz via ServerRpc → ObserversRpc |
| `Assets/Scripts/Network/NetworkCloudManager.cs` | Server-authoritative clouds; 10 Hz position sync; late-joiner TargetRpc; host borrows CloudManager refs; offline re-enable |

### Files Modified
| File | Change |
|------|--------|
| `Assets/Scripts/Game/GameManagerM.cs` | Skips local spawn when NetworkManager present; `ActivateOfflineMode()` spawns locally + re-enables clouds; uses `gameServices.onPlayerRegistered` as single player-ready hook |
| `Assets/Scripts/Environment/CloudManager.cs` | Added `OnCloudSpawned` / `OnCloudDespawned` events; network ID tracking (`_cloudNetIds`, `_idToCloud`); `GetNetworkCloudStates()`, `GetCloudPositions()`, `TryGetCloudById()` |
| `Assets/Scripts/Environment/CloudPlatform.cs` | Added `networkPrefabIndex` field |
| `Assets/Scripts/Player/PlayerControllerM.cs` | Added `MoveInputX` / `IsGliding` properties; subscribes to `TimeManager.OnTick` when networked instead of using `FixedUpdate` |

### Inspector Setup Required
- **TimeManager** component on NetworkManager: set `PhysicsMode = TimeManager`
- **NetworkPlayer prefab**: NetworkObject + NetworkTransform components
- **CloudManager GameObject**: must be **active** in scene hierarchy (only the component is toggled by code)
- **MPPM**: `com.unity.multiplayer.playmode` 1.6.3 installed; main editor = host, virtual players = clients

---

## Remaining Work

### Phase 3.5 — Ladder Sync *(next up)*
**Goal**: Ladders that appear on the host also appear on all clients

The `CloudLadderController` is not networked. Ladders currently only appear on the server/host.

**Steps**:
1. Add C# events to `CloudLadderController` (same pattern as `CloudManager`):
   - `OnLadderCreated(int id, int cloudANetId, int cloudBNetId, Vector2 pos)`
   - `OnLadderDestroyed(int id)`
2. Create `NetworkCloudLadderController.cs` — attach alongside `CloudLadderController`, add NetworkObject:
   - `OnStartServer`: subscribe to ladder events, disable LadderController on clients
   - `[ObserversRpc] RpcCreateLadder(int id, int cloudAId, int cloudBId, Vector2 pos)`
   - `[ObserversRpc] RpcDestroyLadder(int id)`
   - `[TargetRpc] TargetSyncAllLadders(conn, LadderState[])` for late joiners
   - Client: instantiate ladder prefab when RPC fires; destroy on despawn
3. `ActivateOfflineMode()` in `GameManagerM` — re-enable LadderController component (same pattern as CloudManager)

---

### Phase 4 — Scalability
**Goal**: Support 1000+ players with interest management

**Steps**:
1. Create `PlatformerDistanceCondition.cs`:
   - Extends `ObserverCondition`
   - `ConditionMet()`: true if target within 50 units (players) or 25 units (clouds)
2. Apply to NetworkPlayer prefab via ObserverCondition component
3. Physics optimization for remote players:
   - `Rigidbody2D.simulated = false` (render only, no collision cost)
4. Optional `NetworkLODManager.cs`:
   - Reduce `NetworkTransform.UpdateRate` in high-density areas (10+ nearby players → 10 Hz)
5. Load testing with `BotSpawner.cs` (100–1000 bots)

**Verification**:
- [ ] 100 players, stable 60+ FPS
- [ ] 500 players with AOI active (client sees ~20 nearby)
- [ ] 1000 player stress test, server CPU < 80%

---

### Phase 5 — WebGL Build & Edgegap
**Goal**: WebGL client connects to Linux dedicated server on Edgegap

**Steps**:
1. Create `Assets/Scripts/Editor/ServerBuilder.cs`:
   - MenuItem: "Build/Build Linux Server"
   - `BuildTarget.LinuxHeadlessSimulation`, `StandaloneBuildSubtarget.Server`
   - Output: `Builds/LinuxServer/GameServer`
2. Build WebGL client via existing `export_devvit.sh` (GZip compression, Decompression Fallback enabled)
3. Create `Builds/LinuxServer/Dockerfile`:
   ```dockerfile
   FROM ubuntu:22.04
   RUN apt-get update && apt-get install -y libglu1-mesa xvfb
   WORKDIR /app
   COPY GameServer.x86_64 GameServer_Data/ ./
   RUN chmod +x GameServer.x86_64
   EXPOSE 7770/udp 7771/tcp
   CMD ["./GameServer.x86_64", "-batchmode", "-nographics"]
   ```
4. Install Edgegap Unity Plugin (Window → Edgegap → Settings → API key)
5. Create `EdgegapConnector.cs`:
   - WebGL client requests server from Edgegap API (POST `/v1/deploy`)
   - Receives IP:Port → `ClientManager.StartConnection($"ws://{ip}:{port}")`
6. Switch NetworkManager transport to **Multipass** (see Transport Setup above)

**Verification**:
- [ ] WebGL client connects to server
- [ ] Server runs in Docker container
- [ ] Edgegap deployment successful

---

### Phase 6 — Polish & Devvit Integration
**Goal**: Production-ready with Reddit username display

**Steps**:
1. Reddit username sync:
   - Modify `DevvitBridge.cs`: after `FetchInitData()`, send username to server via `[ServerRpc] CmdSetUsername(string name)`
   - Server broadcasts via `[ObserversRpc]`
2. Username display: TextMesh Pro label above each NetworkPlayer, updated on username RPC
3. Player color variation: random hue per player, synced via SyncVar
4. Build pipeline: `build_multiplayer.sh` — WebGL build + Linux server + Docker push, integrates with `export_devvit.sh`
5. Production config: secure WebSocket (`wss://`), Edgegap auto-scaling rules, reconnection logic

**Verification**:
- [ ] Reddit usernames visible above players
- [ ] Multiple Reddit users can play together
- [ ] Clouds sync for parkour gameplay
- [ ] Delivery quests work independently per player
- [ ] Server stable with player churn over 1+ hour

---

## Reference

### Network Traffic Budget (1000 Players)
- Player position: 20 Hz × 12 bytes = 240 B/s
- Player state (on-change): ~10 B/s
- Cloud positions: 10 Hz × 15 clouds × 16 bytes = 2.4 KB/s
- **With AOI (20 nearby players visible)**: ~5 MB/s per player
- **Server total**: 1000 × 5 MB/s = 5 GB/s (requires AOI + LOD to be feasible)

### Design Decisions

**Why Fishnet?** WebGL support via Bayou, built-in client prediction, efficient Observer/AOI system, active development.

**Why Tugboat for testing, Bayou for WebGL?** Browsers cannot open UDP sockets — WebGL requires WebSocket (Bayou). UDP (Tugboat) is faster and simpler for editor testing. Multipass lets the server accept both simultaneously.

**Why server-authoritative clouds?** Prevents duplicate clouds from independent clients. Single source of truth makes synchronized parkour reliable. Clients are lightweight renderers.

**Why local quests?** Each player has independent progression. Avoids quest-state conflicts in the shared world. Reddit API calls (POST `/api/level-completed`) are per-player anyway.

### Risk Mitigation
| Risk | Impact | Mitigation |
|------|--------|------------|
| WebSocket latency in WebGL | High | Client-side prediction, 100 ms input buffer |
| Server CPU overload (1000+ players) | Critical | AOI culling, physics opt, LOD rate reduction |
| Cloud sync bandwidth | High | 10 Hz, correction threshold 1.5 units |
| Client/server desync | Medium | Server reconciliation, teleport threshold 5 units |
| Docker image size | Low | Minimal Ubuntu base, strip client-only assets |

### Resources
- **Fishnet Docs**: https://fish-networking.gitbook.io/docs/
- **Bayou Transport**: https://github.com/FirstGearGames/Bayou
- **Edgegap Unity Integration**: https://docs.edgegap.com/unity
- **Fishnet + Edgegap Sample**: https://docs.edgegap.com/docs/sample-projects/unity-netcodes/fishnet-on-edgegap-webgl
- **Fishnet Observer System**: https://fish-networking.gitbook.io/docs/guides/features/observers
