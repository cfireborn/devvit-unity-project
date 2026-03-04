# Agent Handoff — Compersion Multiplayer (Bayou/Edgegap Phase)

## What This Project Is

Compersion is a Unity 2D platformer deployed on Reddit's Devvit platform as a WebGL game. It uses Fishnet 4.6.22 for multiplayer. The goal is to support 1000+ concurrent Reddit users playing together on cloud-platform parkour with synced ladders.

This document is written for an agent with zero prior context. Read everything before writing a line of code.

---

## Project Layout (relevant paths)

```
Assets/
  FishNet/                          ← Fishnet 4.6.22 (Asset Store)
  FishNet/Plugins/Bayou/            ← Bayou WebSocket transport (.unitypackage, imported manually)
  Scripts/
    Network/
      NetworkBootstrapper.cs        ← Starts server/host/client; 5s offline fallback
      NetworkPlayerSpawner.cs       ← Server spawns NetworkPlayer prefab per client
      NetworkPlayerController.cs    ← Owner enables input; remotes receive visual RPCs
      NetworkCloudManager.cs        ← Server-authoritative cloud sync (ObserversRpc, 10Hz)
      NetworkCloudLadderController.cs ← Server-authoritative ladder sync (NEW, same pattern)
    Environment/
      CloudManager.cs               ← Server-only cloud spawning/pooling; fires network events
      CloudLadderController.cs      ← Server-only ladder logic; fires network events (modified)
      CloudPlatform.cs              ← Has networkPrefabIndex field
    Game/
      GameManagerM.cs               ← Orchestrates spawn, reset, offline fallback
      GameServices.cs               ← Lightweight registry; client-side only
    Player/
      PlayerControllerM.cs          ← Input + physics; exposes MoveInputX, IsGliding; uses TimeManager.OnTick when networked
  Scenes/
    SimpleLevel.unity               ← The game scene
ProjectSettings/                    ← Standard Unity project settings
MULTIPLAYER_IMPLEMENTATION_PLAN.md ← Living plan doc, keep updated
```

---

## Current Network Architecture

### Transport (IMPORTANT — read before touching NetworkManager)

**Right now (Phases 1–4 complete):**
- NetworkManager uses **Tugboat only** (UDP)
- Bayou component must NOT be on the NetworkManager — it auto-inits and causes "Connection refused" spam
- MPPM (Multiplayer Play Mode, `com.unity.multiplayer.playmode` 1.6.3) is used for editor testing
- Main editor window = host (server+client), MPPM virtual players = pure clients

**Your job (Phase 5 — Bayou/Edgegap):**
- Switch to Multipass transport (Tugboat UDP + Bayou WebSocket simultaneously)
- Set up Edgegap cloud hosting
- Wire EdgegapConnector.cs so WebGL clients get a server address from Edgegap API

### Three Modes — ALWAYS support all three

Every component must work in:
1. **Networked host** — `IsServerStarted = true`, server+client on same machine
2. **Networked pure client** — `IsServerStarted = false`, connected to remote server
3. **Offline fallback** — NetworkManager in scene but connection timed out (5s). `GameManagerM.ActivateOfflineMode()` fires. Player spawns locally with grey tint. Clouds and ladders re-enable directly.

**Critical pattern for offline safety:** Never call `IsServerStarted` or `IsClientStarted` on `NetworkBehaviour` in `Update`/`FixedUpdate` — these dereference the NetworkObject's internal manager which is null in offline mode → NullReferenceException. Instead, cache bools:
```csharp
bool _serverRunning;  // set true in OnStartServer, false in OnStopServer
bool _clientRunning;  // set true in OnStartClient, false in OnStopClient
```
See `NetworkCloudManager.cs` for the established pattern.

---

## What Is Built and Working (Phases 1–3 complete)

### Player Sync ✅ (most reliable)
- `NetworkPlayerController` disables `PlayerControllerM` in `Awake`, re-enables for owner in `OnStartClient`
- Owner sends visual state (moveDir, isGliding) via `[ServerRpc] → [ObserversRpc]` at 15Hz
- Remote players run their walk cycle locally using the synced moveDir
- Position synced by **NetworkTransform** component on the NetworkPlayer prefab
- Uses `IsSpawned` guard in Update (safe — only true when actually network-spawned)
- **TimeManager.OnTick** drives physics for networked players (set PhysicsMode = TimeManager in the TimeManager inspector component on NetworkManager)

### Cloud Sync ✅ (working, more complex)
- `NetworkCloudManager` disables `CloudManager` in `Awake`, re-enables server-only in `OnStartServer`
- Server fires `OnCloudSpawned`/`OnCloudDespawned` C# events → `[ObserversRpc]` broadcasts
- Host borrows CloudManager's existing GO references into `_clientClouds` (no duplicate GOs)
- Pure clients: CloudPlatform disabled, Rigidbody2D set to Kinematic, `MovePosition` in FixedUpdate
- Position corrections sent at 10Hz via `RpcSyncPositions`; only applied if drift > 1.5 units
- Late joiners: `TargetSyncAllClouds` sends full state on connect
- `_serverRunning`/`_clientRunning` bool flags instead of `IsServerStarted`/`IsClientStarted`

### Ladder Sync ✅ (NEW — just implemented, same session)
- `NetworkCloudLadderController` disables `CloudLadderController` in `Awake`, re-enables server-only
- Server fires `OnLadderCreated(id, cloudANetId, cloudBNetId)`/`OnLadderDestroyed(id)` events → `[ObserversRpc]`
- **No periodic position sync needed** — clients re-derive ladder geometry from already-synced cloud positions every LateUpdate by calling `_ladderController.UpdateLadderPosition(lower, upper, ladderGo)` directly (the method was made public)
- Client gets CloudPlatform refs via `NetworkCloudManager.TryGetClientCloud(netId, out go)` → `GetComponent<CloudPlatform>()`
- CloudPlatform is disabled on client clouds but its bounds methods still work (component disabled ≠ null)
- Late joiners: `TargetSyncAllLadders` sends full state
- Offline mode: `GameManagerM.ActivateOfflineMode()` calls `FindFirstObjectByType<NetworkCloudLadderController>(FindObjectsInactive.Include).ActivateOfflineMode()`

### Offline Fallback ✅
- `NetworkBootstrapper` unsubscribes from `OnClientConnectionState` and calls `ClientManager.StopConnection()` before triggering offline mode (prevents late Tugboat "started" callbacks from breaking offline)
- `GameManagerM.ActivateOfflineMode()`:
  1. `SpawnPlayerLocal()` — player spawned, registered with GameServices
  2. `FindFirstObjectByType<NetworkCloudManager>(FindObjectsInactive.Include).ActivateOfflineMode()` — re-enables CloudManager
  3. `FindFirstObjectByType<NetworkCloudLadderController>(FindObjectsInactive.Include).ActivateOfflineMode()` — re-enables CloudLadderController
  4. Grey tint on player sprite (`new Color(0.55f, 0.55f, 0.55f, 1f)`)
- CloudManager **GameObject** must always be **active** in scene hierarchy — only the **component** gets toggled. An inactive GO is invisible to `FindFirstObjectByType`.

---

## Inspector / Scene Setup Required (for the new ladder system)

The user needs to do this in Unity before the ladder sync will work:

1. **Find the CloudLadderController GameObject** in the scene hierarchy
2. **Add NetworkObject component** to it (Component → FishNet → Object → NetworkObject)
3. **Add NetworkCloudLadderController component** to it (the new script)
4. **Verify the CloudLadderController component** is enabled (active checkbox). NetworkCloudLadderController.Awake() will disable it when NetworkManager is present — that's correct.
5. **Register the ladder prefab** — no prefab registration needed for ladders (they're not spawned via ServerManager.Spawn, just Instantiate on each client)

---

## Phase 5 — Your Task: Bayou + Edgegap

### User Manual Tasks (tell the user to do these)

1. **Create an Edgegap account** at https://edgegap.com (free tier available)
2. **Install Edgegap Unity Plugin** from the Unity Asset Store or Package Manager: `Window → Edgegap → Settings → enter API key`
3. **Create a Docker Hub or Edgegap registry account** for pushing server images

### Transport Switch: Tugboat → Multipass

Do this in Unity Editor (requires code + inspector changes):

**Inspector steps:**
1. On the NetworkManager GameObject:
   - Remove any existing Bayou component (if present) — it auto-inits
   - Keep the existing **Tugboat** component (port 7770, UDP)
   - Add **Bayou** component (port 7771, WebSocket)
   - Add **Multipass** component
   - In TransportManager: set Transport = Multipass
   - In Multipass inspector: add Tugboat and Bayou to its transport list
2. Expose port 7770/udp and 7771/tcp in Dockerfile

**Code: NetworkBootstrapper.cs — update port logic**

The current `NetworkBootstrapper` connects to a single `serverAddress`. For Bayou (WebGL), it needs to connect to port 7771. For Tugboat (editor/standalone), port 7770. Add a `serverPort` field and use different ports per build target. The relevant section:

```csharp
// Current (Tugboat-only):
nm.ClientManager.StartConnection(serverAddress);

// Needed (Multipass):
// WebGL → Bayou port 7771
// Editor/standalone → Tugboat port 7770
// Check build target to select port
```

In FishNet with Multipass, `ClientManager.StartConnection(address, port)` lets you specify which transport by port. The Multipass transport will route to whichever sub-transport owns that port.

### EdgegapConnector.cs — Create This File

Create `Assets/Scripts/Network/EdgegapConnector.cs`:

```
Purpose: On WebGL client load, call Edgegap REST API to get/create a server session,
         then connect NetworkBootstrapper to the returned IP:port.

Flow:
1. POST https://api.edgegap.com/v1/sessions (with API key header)
   Body: { "app_name": "compersion", "version_name": "latest" }
2. Poll GET /v1/sessions/{session_id} until status = "Ready"
3. Extract session.ports["game"].external and session.ip
4. Pass to NetworkBootstrapper: nm.ClientManager.StartConnection(ip, port)

Use UnityWebRequest for HTTP calls (coroutine-based).
Store API key in a ScriptableObject or PlayerPrefs — NOT hardcoded.
```

Refer to: https://docs.edgegap.com/docs/sample-projects/unity-netcodes/fishnet-on-edgegap-webgl

### ServerBuilder.cs (Editor script)

Create `Assets/Scripts/Editor/ServerBuilder.cs`:

```csharp
// MenuItem: "Build/Build Linux Server"
// BuildTarget: StandaloneLinux64
// StandaloneBuildSubtarget: Server (Unity 6 / 2022+: use UNITY_SERVER define)
// Output: Builds/LinuxServer/GameServer
```

### Dockerfile

Create `Builds/LinuxServer/Dockerfile`:

```dockerfile
FROM ubuntu:22.04
RUN apt-get update && apt-get install -y libglu1-mesa
WORKDIR /app
COPY GameServer.x86_64 GameServer_Data/ ./
RUN chmod +x GameServer.x86_64
EXPOSE 7770/udp
EXPOSE 7771/tcp
CMD ["./GameServer.x86_64", "-batchmode", "-nographics"]
```

### Testing Bayou Before Full Edgegap

1. In editor, start as host with Bayou on port 7771
2. Build WebGL: File → Build Settings → WebGL → Build → `Builds/WebGL/`
3. Serve: `cd Builds/WebGL && python3 -m http.server 8080`
4. Open `http://localhost:8080` — browser connects via WebSocket to port 7771
5. Both editor host and browser tab should see each other

---

## Key Files — Current Complete Content Summary

### NetworkBootstrapper.cs
- Starts server/host/client by build target + `CurrentPlayer.IsMainEditor` (MPPM)
- Main editor = host (server+client), virtual players = pure clients, UNITY_WEBGL = pure client
- Timeout coroutine: unsubscribes callback + `ClientManager.StopConnection()` + `GameManagerM.ActivateOfflineMode()`
- `_connectionEstablished` bool gates the timeout (set on `LocalConnectionState.Started`)

### NetworkPlayerSpawner.cs
- Plain MonoBehaviour on NetworkManager GameObject
- Listens to `ServerManager.OnRemoteConnectionState` → spawns NetworkPlayer prefab per connection
- `InstanceFinder.ServerManager.Spawn(obj, conn)` gives ownership to connecting client

### NetworkPlayerController.cs
- `Awake`: disables `PlayerControllerM`
- `OnStartClient`: enables controller + rb.simulated for owner; disables for remote
- Owner: 15Hz visual sync via `[ServerRpc] CmdSendVisuals → [ObserversRpc] RpcReceiveVisuals`
- Remote `Update`: runs walk cycle locally from synced moveDir/isGliding
- Position: handled by **NetworkTransform** component (not this script)
- Guard: `if (!IsSpawned) return;` in Update — `IsSpawned` is safe unlike `IsServerStarted`

### NetworkCloudManager.cs
- `Awake`: `_cloudManager.enabled = false` when NetworkManager present
- `OnStartServer`: re-enables CloudManager, subscribes to C# events
- `OnStartClient` (non-server): keeps CloudManager disabled
- `_serverRunning`/`_clientRunning` cached flags (critical — see offline safety note above)
- `FixedUpdate`: drives kinematic cloud movement for pure clients (`!_serverRunning && _clientRunning`)
- `Update`: 10Hz position sync broadcast from server
- Host path: `ClientSpawnCloud` borrows CloudManager refs via `TryGetCloudById`, no new GO
- `TryGetClientCloud(int netId, out GameObject go)` — public, used by ladder controller
- `ActivateOfflineMode()`: sets `_offlineMode`, re-enables CloudManager (handles inactive GO case)

### NetworkCloudLadderController.cs
- Same pattern as NetworkCloudManager but simpler (no position sync loop)
- `Awake`: `_ladderController.enabled = false` when NetworkManager present
- `OnStartServer`: re-enables CloudLadderController, subscribes to ladder events
- `LateUpdate` (client only): for each tracked ladder, gets cloud GOs from NetworkCloudManager, calls `_ladderController.UpdateLadderPosition(lower, upper, ladderGo)` to rebuild geometry
- No position corrections or kinematic movement — position is always derived from cloud positions
- Host: `ClientCreateLadder` returns immediately (no duplicate GO)
- `ActivateOfflineMode()`: re-enables CloudLadderController (handles inactive GO case)

### CloudManager.cs (modified)
- Added: `OnCloudSpawned` (Action<int,int,Vector2,float,float>), `OnCloudDespawned` (Action<int>)
- Added: `_cloudNetIds`, `_idToCloud`, `_nextNetId` for network tracking
- Added: `GetNetworkCloudStates()`, `GetCloudPositions()`, `TryGetCloudById()`, `TryGetNetIdForCloud()`

### CloudLadderController.cs (modified)
- Added: `OnLadderCreated` (Action<int,int,int>), `OnLadderDestroyed` (Action<int>)
- Added: `_ladderNetIds`, `_ladderCloudIds`, `_nextLadderId` for network tracking
- Added: `DespawnLadder()` helper (fires event + returns to pool)
- Added: `GetNetworkLadderStates()` for late-joiner sync
- `UpdateLadderPosition` made **public** (clients call it directly)
- `CreateLadder` fires `OnLadderCreated` with cloud net IDs from `cloudManager.TryGetNetIdForCloud`

### GameManagerM.cs (modified)
- `SpawnPlayer()` returns immediately if `InstanceFinder.NetworkManager != null`
- `OnPlayerRegistered()` subscribes goal trigger, sets playerInstance — fires via GameServices event
- `ActivateOfflineMode()`: SpawnPlayerLocal → NetworkCloudManager.ActivateOfflineMode → NetworkCloudLadderController.ActivateOfflineMode → grey tint
- `ResetGame()` uses `playerInstance ?? gameServices.GetPlayer()` — never FindFirstObjectByType

### PlayerControllerM.cs (modified)
- Added: `public float MoveInputX => moveInput;` and `public bool IsGliding => isGliding;`
- `OnEnable`: subscribes `InstanceFinder.TimeManager.OnTick += OnTick` when NetworkManager present
- `FixedUpdate`: only calls `ApplyMovement()` when NO NetworkManager (offline path)
- `OnTick`: calls `ApplyMovement()` — the networked physics path

---

## Known Limitations / Next Issues

- **Ladders with non-pooled clouds**: Pre-placed scene clouds don't go through `SpawnCloud()` and have no net ID (`-1`). Ladders between such pairs won't sync to clients. These are static clouds that appear on all clients anyway — workaround: ensure `cloudANetId != -1` before syncing.
- **Cloud position sync threshold**: Corrections only applied at drift > 1.5 units. May still see slight cloud position mismatch between clients at close range. Reduce threshold if needed.
- **No reconnection logic**: If a client drops and reconnects, full state is re-sent but no graceful handling of mid-game drops.

---

## Gotchas — Read Before Touching Anything

1. **Never call `IsServerStarted`/`IsClientStarted` in `Update`/`FixedUpdate`** on NetworkBehaviours in this project. Use cached `_serverRunning`/`_clientRunning` bools. The property crashes in offline mode.

2. **Bayou must not be a component on NetworkManager during Tugboat testing** — it auto-initializes and floods the console with connection errors. Remove it; add back only when switching to Multipass.

3. **CloudManager/CloudLadderController GameObjects must be active** in the scene hierarchy. Our code disables the *component*, not the *GameObject*. An inactive GO is invisible to `FindFirstObjectByType` by default (need `FindObjectsInactive.Include`).

4. **`IsSpawned` is the safe FishNet guard for Update** — `IsServerStarted`/`IsClientStarted` are not. See NetworkPlayerController for the correct pattern.

5. **`NetworkBehaviour.OnStartClient()` can fire after a timeout** — Tugboat reports "started" as soon as its UDP socket is ready, before any server response. The `NetworkBootstrapper` now stops the connection before calling offline mode, but `_offlineMode` flag in NetworkCloudManager + NetworkCloudLadderController provides a second defense.

6. **PhysicsMode must be set to TimeManager** in the TimeManager component on the NetworkManager GameObject in the inspector. PlayerControllerM subscribes to `TimeManager.OnTick` for physics in networked mode; FixedUpdate is the offline fallback. Without this setting, FishNet logs a warning.

7. **`CloudPlatform` is disabled on client cloud GOs** but its `GetMainBounds()` and `GetBounds()` methods are still callable — Unity allows calling methods on disabled components. Ladder sync relies on this.

8. **Host cloud GOs are borrowed, not created** — `NetworkCloudManager._serverManagedClouds` tracks which cloud IDs belong to CloudManager. Never Destroy() these. Same principle in NetworkCloudLadderController (host just returns early from ClientCreateLadder).

---

## Resources

- Fishnet docs: https://fish-networking.gitbook.io/docs/
- Bayou: https://github.com/FirstGearGames/Bayou (releases page for .unitypackage)
- Edgegap Unity integration: https://docs.edgegap.com/unity
- Fishnet + Edgegap sample: https://docs.edgegap.com/docs/sample-projects/unity-netcodes/fishnet-on-edgegap-webgl
- Fishnet Observer System (Phase 4): https://fish-networking.gitbook.io/docs/guides/features/observers
