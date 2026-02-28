# Compersion

A 2D Unity platformer embedded in Reddit posts via the [Devvit](https://developers.reddit.com/docs/devvit) platform. Players parkour across procedurally generated moving cloud platforms, climb ladders between them, and complete delivery quests — all live, with other Reddit users in the same session.

**Engine:** Unity 6 (6000.2.8f1)
**Networking:** FishNet 4.6.22 (Tugboat UDP for editor testing, Bayou WebSocket for WebGL production)
**Deployment:** WebGL → Devvit iframe + Linux dedicated server → Edgegap cloud hosting (in progress)

---

## Getting Started

### Prerequisites

- Unity 6 (6000.2.8f1)
- FishNet 4.6.22 — already in `Assets/FishNet/`
- Multiplayer Play Mode (MPPM) package — `com.unity.multiplayer.playmode` 1.6.3 — for multi-client editor testing

### Opening the project

1. Open in Unity Hub, select the project root
2. Open `Assets/Scenes/SimpleLevel.unity`
3. Press Play in the main editor window — it starts as **host** (server + client)
4. Open `Window → Multiplayer Play Mode` and activate a virtual player — it starts as a **pure client**

Both should connect and see each other moving.

### Running without a server

If no server is reachable within 5 seconds, the game falls back to **offline single-player** automatically. The player spawns with a grey tint to signal offline mode. Clouds and ladders still spawn normally.

---

## Project Structure

```
Assets/Scripts/
  Game/
    GameManagerM.cs             ← Scene orchestrator; player spawn, reset, offline fallback
    GameServices.cs             ← Lightweight runtime registry (player, cloud ladder controller)
    GameState.cs                ← Shared level state (positions, completion flag)
  Player/
    PlayerControllerM.cs        ← All player input and physics (local only)
    PlayerSettingsM.cs          ← Jump, speed, glide tuning — ScriptableObject
  Network/
    NetworkBootstrapper.cs      ← Starts server/host/client by build target; 5s offline timeout
    NetworkPlayerSpawner.cs     ← Server spawns one NetworkPlayer prefab per connecting client
    NetworkPlayerController.cs  ← Enables input for owner; syncs visual state to remotes
    NetworkCloudManager.cs      ← Disables CloudManager on clients; server re-enables it
    NetworkCloud.cs             ← Per-cloud NetworkBehaviour; syncs scale, disables physics on clients
    NetworkCloudLadderController.cs ← Disables CloudLadderController on clients; rebuilds geometry each frame
    NetworkLadder.cs            ← Per-ladder NetworkBehaviour; stores which clouds it bridges
  Environment/
    CloudManager.cs             ← Spawns/despawns clouds around the player
    CloudPlatform.cs            ← Per-cloud movement, despawn animation, bounds helpers
    CloudLadderController.cs    ← Creates/destroys ladders between qualifying cloud pairs
    CloudNoSpawnZone.cs         ← Blocks cloud spawning or entry in a region
    LadderTrigger.cs            ← Player interaction zone on ladder colliders
Scenes/
  SimpleLevel.unity             ← The only shipped scene
MULTIPLAYER_IMPLEMENTATION_PLAN.md   ← Living design doc; read before starting network work
AGENTS.md                            ← Coding guidelines for AI assistants
AGENTS-MOSTRECENT.md                 ← Full technical handoff for the next agent
```

---

## The Cloud System

This is the most complex part of the codebase and the area most likely to be edited for new features. Read this entire section before touching any cloud-related file.

### How it works (offline / single-player)

`CloudManager` runs a spawn/despawn loop in `Update()`. When the player moves more than `distanceThresholdForUpdate` units since the last check:

1. Clouds beyond `despawnRadius` are returned to a `Queue<GameObject>` pool
2. New clouds are pulled from the pool (or instantiated) until `maxClouds` is reached
3. Each cloud gets a random position within `spawnRadius`, a random scale (`scaleRange`), and a random horizontal drift speed (`speedRange`)
4. `CloudPlatform` drives horizontal movement via `Rigidbody2D.linearVelocity` in `FixedUpdate`
5. Clouds despawn themselves (shrink animation → `ReturnCloudToPool`) when they enter a `CloudNoSpawnZone` with `blockEntry = true` and no player is standing on them

`CloudLadderController` runs in `LateUpdate` after clouds have moved. It checks every pair of active clouds and creates a ladder between pairs that are:
- Horizontally overlapping (their X extents share some overlap)
- Within `maxDistance` of each other (center to center)
- Separated vertically by between `minVerticalGap` and `maxVerticalGap`

Ladders are built as a root GameObject (BoxCollider2D trigger) with sprite children (bottom cap, tiled middle segments, top cap) constructed at runtime. Positions update every frame as clouds drift.

### How it works (multiplayer)

In a networked session, the cloud system is **server-authoritative**. Only the server runs `CloudManager` and `CloudLadderController`. Clients receive clouds and ladders as FishNet `NetworkObject` spawns and never run the local spawn logic.

This split is enforced by two NetworkBehaviour "wrapper" components that disable their respective controllers in `Awake()` and re-enable them on the server in `OnStartServer()`:

- **`NetworkCloudManager`** wraps `CloudManager`
- **`NetworkCloudLadderController`** wraps `CloudLadderController`

**Cloud position sync** uses FishNet's built-in `NetworkObject + NetworkTransform`. The server calls `ServerManager.Spawn(nob)` for each cloud; FishNet instantiates the prefab on every client automatically and syncs its position via `NetworkTransform`. `NetworkCloud` (on the prefab) disables `CloudPlatform` and sets the Rigidbody to Kinematic on non-server clients so physics doesn't fight incoming transform updates.

**Ladder position sync** works differently — ladders have no independent movement, so there's no `NetworkTransform`. Instead, `NetworkCloudLadderController.LateUpdate()` runs on clients every frame: it iterates every spawned `NetworkLadder`, looks up its two cloud GameObjects from FishNet's `Objects.Spawned` dictionary, and calls `CloudLadderController.UpdateLadderPosition()` to rebuild the geometry from those already-synced cloud bounds. Always correct, zero extra bandwidth.

**Scale sync** — cloud scale is random per spawn. `NetworkCloud.SyncScale()` is a `[ObserversRpc(BufferLast = true)]` called immediately after `ServerManager.Spawn()`. `BufferLast` means late-joining clients receive the last value automatically. `NetworkLadder.SyncCloudIds()` uses the same pattern to tell clients which two clouds a ladder connects.

### Files and what they own

| File | Owns | Never does |
|------|------|------------|
| `CloudManager.cs` | Spawn/despawn lifecycle, pool, active list | Physics, movement, network RPCs |
| `CloudPlatform.cs` | Per-cloud movement, bounds, despawn animation | Spawning other clouds, knowing about other clouds |
| `CloudLadderController.cs` | Ladder create/destroy, visual geometry, collider sizing | Cloud spawning, network sync, player interaction |
| `NetworkCloudManager.cs` | Enabling/disabling `CloudManager` by network role | Cloud data, movement, game logic |
| `NetworkCloud.cs` | Scale sync, client-side physics mode | Cloud movement, anything ladder-related |
| `NetworkCloudLadderController.cs` | Enabling/disabling `CloudLadderController`, client geometry rebuild | Ladder spawning, cloud data |
| `NetworkLadder.cs` | Storing which cloud ObjectIds this ladder bridges | Any ladder logic or geometry |

### Implementing a layered cloud generation system

The right place is `CloudManager.SpawnCloud()`. That method picks a random position and instantiates a cloud. To add layers (altitude bands, different prefab sets, speeds, densities), change how `spawnPos` is chosen and which prefab is selected.

**What you can freely change in `CloudManager`:**
- How spawn positions are calculated (altitude bands, noise, structured grids)
- Which prefab is selected for a given spawn (e.g. pick from a layer-specific prefab subset)
- Speed and scale ranges per layer
- Any new serialized fields for layer configuration

**What you must not break:**

`_active.Add(cloud)` must be called for every spawned cloud. `CloudLadderController` reads `GetActiveClouds()` to find ladder candidates — clouds not in this list won't get ladders.

`ReturnCloudToPool()` must remain callable from outside (it's called by `CloudPlatform.Update()` when the despawn animation completes).

The `InstanceFinder.IsServerStarted` branch in `SpawnCloud()` and `ReturnCloudToPool()` must stay intact. In the server branch, clouds are spawned via `ServerManager.Spawn()` and destroyed via `ServerManager.Despawn()` — never pooled. Removing or bypassing this branch silently breaks multiplayer.

Do not add `OnCloudSpawned` / `OnCloudDespawned` events back — they were removed when the sync system was rewritten to use `NetworkObject`. The network layer no longer needs them.

**Adding new cloud prefabs:**

Every cloud prefab used in multiplayer must have three components added in the Inspector:
1. `NetworkObject` (Component → FishNet → Object → NetworkObject)
2. `NetworkTransform` (Component → FishNet → Object → NetworkTransform)
3. `NetworkCloud` (the script in `Assets/Scripts/Network/`)

And the prefab must be registered in the `NetworkManager` GameObject's **Spawnable Prefabs** list. Without this, FishNet can't instantiate it on clients and will log errors.

**What you can add without touching the network layer:**
- New serialized fields and layer config on `CloudManager`
- New properties on `CloudPlatform` (e.g. cloud type or layer tag)
- `CloudNoSpawnZone` volumes to constrain where layers appear
- Multiple spawn calls with different parameters from within `SpawnCloud()`

**What requires care:**
- `CloudPlatform.canBuildLadder` — set this false on purely decorative clouds that shouldn't anchor ladders
- If you add a second spawning path that bypasses `_active`, ladders and despawn logic will silently stop working for those clouds

### The offline fallback

When `NetworkBootstrapper` gives up waiting for a server (default 5 seconds), it calls `GameManagerM.ActivateOfflineMode()`, which:
1. Stops the transport to prevent late callbacks from re-triggering network state
2. Spawns the local player
3. Calls `NetworkCloudManager.ActivateOfflineMode()` — sets an `_offlineMode` guard flag and re-enables `CloudManager`
4. Calls `NetworkCloudLadderController.ActivateOfflineMode()` — same for `CloudLadderController`
5. Tints the player grey

The `_offlineMode` flag exists because `OnStartClient` can fire after the timeout (the transport reports its socket as ready before any server responds). Without the flag, `OnStartClient` would re-disable `CloudManager` immediately after we re-enabled it.

**If you add a new cloud-related NetworkBehaviour**, give it an `ActivateOfflineMode()` method and call it from `GameManagerM.ActivateOfflineMode()`.

---

## Editing the Player

`PlayerControllerM.cs` handles all input and physics locally. It is never run by remote clients. `NetworkPlayerController.cs` enables/disables it based on ownership and syncs visual state (movement direction, gliding) to other players at 15Hz via ServerRpc → ObserversRpc.

In multiplayer, physics runs on `TimeManager.OnTick` rather than `FixedUpdate`. Both paths call the same internal `ApplyMovement()` method — don't move physics code out of it.

Player tuning (jump force, speed, glide drag) lives in a `PlayerSettingsM` ScriptableObject assigned in the Inspector. Change values there, not in code.

---

## Editing the Scene

The only scene is `SimpleLevel.unity`. Key GameObjects:

| GameObject | Notes |
|---|---|
| `NetworkManager` | FishNet root — do not reorder or remove its components |
| `CloudManager` | Has `CloudManager` + `NetworkCloudManager` + `NetworkObject`. **Must stay active in the hierarchy.** |
| `CloudLadderController` | Has `CloudLadderController` + `NetworkCloudLadderController` + `NetworkObject`. **Must stay active.** |
| `GameManager` | `GameManagerM` — wires everything together on Start |
| `StartPoint` | Transform used as the player's spawn position |

The CloudManager and CloudLadderController GameObjects **must remain active** in the scene hierarchy at all times. The network components disable the *MonoBehaviour component* at runtime — not the GameObject itself. An inactive GameObject is invisible to `FindFirstObjectByType`, which the offline fallback relies on.

---

## Networking Quick Reference

The full rules are in `AGENTS.md`. The ones most relevant to cloud work:

**Never call `IsServerStarted` or `IsClientStarted` in `Update` or `FixedUpdate`** on a NetworkBehaviour — they crash in offline mode because the NetworkObject's internal manager is null. Use cached bool flags instead:
```csharp
bool _serverRunning; // set true in OnStartServer, false in OnStopServer
bool _clientRunning; // set true in OnStartClient, false in OnStopClient
```

**`InstanceFinder.IsServerStarted`** (static accessor) is safe to call anywhere.

**`[SyncVar]` attribute does not exist in FishNet v4.** For spawn-time value sync, use:
```csharp
[ObserversRpc(RunLocally = true, BufferLast = true)]
public void SyncMyValue(float value) { ... }
```

Every new spawnable prefab must be registered in **NetworkManager → Spawnable Prefabs**.

---

## Building

### WebGL (for Devvit)

`File → Build Settings → WebGL → Build`. Use the existing `export_devvit.sh` script for packaging.

### Linux Server (for Edgegap)

`Build → Build Linux Server` menu item (editor script, in progress). Output: `Builds/LinuxServer/`.

### Editor multi-client testing

`Window → Multiplayer Play Mode`. Main editor window = host. Each virtual player = a pure client connecting to localhost.
