# Fishnet Multiplayer Implementation Plan

## Overview

Transform the Compersion Unity 2D platformer into a multiplayer game using Fishnet networking with Edgegap server hosting, targeting WebGL deployment on Reddit's Devvit platform with support for 1000+ concurrent players.

## Architecture Decisions

### Networking Stack
- **Editor/Standalone Client**: Tugboat transport (UDP, for local testing)
- **WebGL Client**: Bayou transport (WebSocket — browsers cannot use UDP)
- **Server (Production)**: Multipass transport — listens on BOTH Tugboat (UDP port 7770) AND Bayou (WS port 7771) simultaneously
- **Framework**: Fishnet v4.6.22 (Asset Store, installed at Assets/FishNet)
- **Bayou**: Installed at Assets/FishNet/Plugins/Bayou (imported from GitHub releases)
- **Hosting**: Edgegap cloud deployment with Docker containers

### ⚠️ Transport Setup Notes (READ BEFORE RESUMING WORK)
**Current state (Phases 1–4 testing):**
- NetworkManager uses **Tugboat only** as transport
- Bayou component is NOT on the NetworkManager (remove it to avoid auto-init conflicts)
- This lets editor/MPPM testing work with UDP

**Phase 5 — switching to Multipass for production:**
1. Add `Multipass` component to NetworkManager GameObject
2. Add `Tugboat` component (port 7770, UDP) — for standalone/editor clients
3. Add `Bayou` component (port 7771, WebSocket) — for WebGL clients
4. Set TransportManager → Transport = Multipass
5. In Multipass inspector: add both Tugboat and Bayou as its transports
6. WebGL build: NetworkBootstrapper connects to port 7771 (Bayou)
7. Editor/standalone build: NetworkBootstrapper connects to port 7770 (Tugboat)
8. Server Dockerfile: expose both ports 7770/udp and 7771/tcp

**Testing Bayou (WebGL) without full Edgegap deployment:**
1. Run a Unity Editor host (server listening on Bayou port 7771)
2. Build a WebGL client: File → Build Settings → WebGL → Build
3. Serve it locally: `cd Builds/WebGL && python3 -m http.server 8080`
4. Open browser → `http://localhost:8080` → WebGL client connects via WebSocket to ws://localhost:7771
5. Both editor host and browser client should be visible in the scene

### Key Architectural Patterns
1. **Client-Server Model**: Server-authoritative architecture
2. **Player Sync**: Client-side prediction for local player, interpolation for remote players
3. **Cloud Sync**: Server spawns clouds, broadcasts state to all clients via ObserversRpc
4. **Interest Management**: Spatial culling (50 unit radius for players, 25 for clouds)
5. **Quest System**: Local to each player (not synchronized across network)

## Critical Files to Modify

### Existing Files
- **Assets/Scripts/Player/PlayerControllerM.cs** - Keep unchanged (local input controller)
- **Assets/Scripts/Environment/CloudManager.cs** - Add events for spawn/despawn that network layer subscribes to
- **Assets/Scripts/Game/GameManagerM.cs** - Replace local player spawning with network spawning system
- **Assets/Scripts/Game/GameServices.cs** - Only register owned players, server-only CloudManager
- **Assets/Scripts/DevvitBridge.cs** - Send Reddit username to server via RPC after connection

### New Files to Create

**Priority 1 - Core Networking:**
1. `Assets/Scripts/Network/NetworkPlayerController.cs` - NetworkBehaviour wrapper for PlayerControllerM
2. `Assets/Scripts/Network/NetworkPlayerSpawner.cs` - Server spawns players on client connection
3. `Assets/Scripts/Environment/NetworkCloudManager.cs` - Server-authoritative cloud spawning

**Priority 2 - Scalability:**
4. `Assets/Scripts/Network/PlatformerDistanceCondition.cs` - Interest management (AOI culling)
5. `Assets/Scripts/Network/CloudStateSync.cs` - Efficient cloud position updates (10Hz)

**Priority 3 - Integration:**
6. `Assets/Scripts/Network/EdgegapConnector.cs` - Client connection to Edgegap servers
7. `Assets/Scripts/Editor/ServerBuilder.cs` - Automated Linux server build script

**Priority 4 - Deployment:**
8. `Builds/LinuxServer/Dockerfile` - Container image for Edgegap deployment
9. `tools/build_multiplayer.sh` - Unified build script for client + server

## Implementation Phases

### Phase 1: Foundation (Week 1)
**Goal**: Basic client-server connection working in Unity Editor

**Steps**:
1. Install Fishnet from Asset Store → `Assets/Plugins/FishNet`
2. Download Bayou transport from https://github.com/FirstGearGames/Bayou → `Assets/Plugins/Bayou`
3. Create NetworkManager GameObject in SimpleLevel.unity with components:
   - NetworkManager
   - ServerManager (StartOnHeadless: true, MaxClients: 1024)
   - ClientManager
   - TransportManager (Transport: Bayou, Port: 7770)
   - TimeManager
   - ObserverManager (DefaultCondition: Distance, UpdateHostVisibility: true)
4. Test: Start as Host in editor, verify NetworkManager initializes

**Verification**:
- NetworkManager console logs show "Server started" and "Client connected"

### Phase 2: Player Networking (Week 2)
**Goal**: Multiple players can connect and see each other moving

**Steps**:
1. Create NetworkPlayer prefab:
   - Duplicate existing PlayerControllerM prefab
   - Add NetworkObject component (IsSpawnable: true, OwnerAuthority: true, EnablePrediction: true)
   - Add NetworkTransform (SyncPosition: true, UpdateRate: 20, Interpolation: 10)

2. Create NetworkPlayerController.cs:
   - Extends NetworkBehaviour
   - OnStartClient(): Enable PlayerControllerM only for owned player
   - Remote players: Disable input, render visuals only
   - SyncVars for visual state (isGliding, isOnLadder)

3. Create NetworkPlayerSpawner.cs:
   - Attach to NetworkManager GameObject
   - OnRemoteConnectionState: Spawn NetworkPlayer prefab when client connects
   - ServerManager.Spawn(playerObj, connection) to give ownership

4. Modify GameManagerM.cs:
   - Disable local player spawning if NetworkManager.IsClient
   - Let NetworkPlayerSpawner handle spawning instead

5. Modify GameServices.cs:
   - RegisterPlayer(): Check networkPlayer.IsOwner before registering
   - Prevents registering remote players with camera/input systems

**Testing with ParrelSync**:
- Install ParrelSync from Asset Store
- Create clone project
- Main project: Start as Server+Client (Host)
- Clone project: Start as Client (connect to localhost)
- Verify both players spawn, move independently, camera follows local player only

**Verification**:
- [ ] Player spawns for both clients
- [ ] Player movement syncs correctly
- [ ] Camera follows local player only
- [ ] Remote player sprites update based on movement

### Phase 3: Cloud Networking (Week 3)
**Goal**: Server spawns clouds, all clients render same clouds in sync

**Steps**:
1. Create NetworkCloudManager.cs:
   - Attach to CloudManager GameObject
   - OnStartServer(): Enable CloudManager.enabled = true
   - OnStartClient(): If !IsServer, disable CloudManager.enabled = false
   - Subscribe to CloudManager events (OnCloudSpawned, OnCloudDespawned)
   - [ObserversRpc] RpcSpawnCloud(id, position, speed, scale) - broadcasts to all clients
   - [ObserversRpc] RpcDespawnCloud(id) - broadcasts despawn

2. Modify CloudManager.cs:
   - Add UnityEvent: OnCloudSpawned(int id, Vector3 pos, float speed, float scale)
   - Add UnityEvent: OnCloudDespawned(int id)
   - Invoke events in SpawnCloud() and ReturnCloudToPool()
   - Assign unique network ID to each cloud (static counter)

3. Create CloudStateSync.cs:
   - Attach to CloudManager GameObject
   - Server Update(): Every 100ms, sync cloud positions that moved > 0.01 units
   - [ObserversRpc] RpcSyncCloudStates(CloudState[] states) - delta updates only
   - Clients interpolate cloud positions between updates

4. Ladder Synchronization:
   - Similar pattern: NetworkCloudLadderController wraps CloudLadderController
   - [ObserversRpc] RpcCreateLadder(cloudA_id, cloudB_id)
   - [ObserversRpc] RpcDestroyLadder(ladder_id)

5. Modify GameServices.cs:
   - RegisterCloudManager(): Only if NetworkManager.IsServer

**Testing**:
- Host spawns, clouds appear around them
- Client connects, sees same clouds
- Both players can parkour on synced clouds
- Ladders appear for both players

**Verification**:
- [ ] Clouds spawn on server only
- [ ] Clouds appear on all clients
- [ ] Cloud positions sync (no duplicates)
- [ ] Ladders sync correctly
- [ ] Players can climb synced ladders

### Phase 4: Scalability (Week 4)
**Goal**: Support 1000+ players with interest management

**Steps**:
1. Create PlatformerDistanceCondition.cs:
   - Extends ObserverCondition
   - ConditionMet(): Returns true if target within 50 units (players) or 25 units (clouds)
   - Prevents sending updates about distant players/clouds

2. Apply to NetworkPlayer prefab:
   - Add ObserverCondition component
   - Set to PlatformerDistanceCondition

3. Create NetworkLODManager.cs (optional):
   - Server Update(): Adjust NetworkTransform.UpdateRate based on player density
   - High density areas (10+ nearby players): Reduce to 10Hz
   - Normal areas: Full 20Hz updates

4. Physics Optimization:
   - Remote players: Set Rigidbody2D.simulated = false (render only, no physics)
   - Remote players: Use simple CircleCollider2D (not complex polygon)

5. Load Testing:
   - Create BotSpawner.cs: Spawn 100-1000 bot players for stress testing
   - Monitor: Server CPU < 80%, RAM ~1GB for 1000 players, Client FPS > 60

**Verification**:
- [ ] 100 players with stable FPS (60+)
- [ ] 500 players with interest management (only see ~20 nearby)
- [ ] 1000 players stress test (server CPU < 80%)

### Phase 5: WebGL Build & Edgegap Integration (Week 5)
**Goal**: WebGL client connects to Linux dedicated server on Edgegap

**Steps**:
1. Create ServerBuilder.cs (Editor script):
   - MenuItem: "Build/Build Linux Server"
   - BuildTarget: LinuxHeadlessSimulation
   - StandaloneBuildSubtarget.Server
   - Output: Builds/LinuxServer/GameServer

2. Build WebGL client:
   - Platform: WebGL
   - Compression: GZip
   - Publishing: Decompression Fallback = enabled
   - Output: Use existing export_devvit.sh script

3. Create Dockerfile in Builds/LinuxServer/:
   ```dockerfile
   FROM ubuntu:22.04
   RUN apt-get update && apt-get install -y libglu1-mesa xvfb
   WORKDIR /app
   COPY GameServer.x86_64 GameServer_Data/ ./
   RUN chmod +x GameServer.x86_64
   EXPOSE 7770/tcp 7770/udp
   CMD ["./GameServer.x86_64", "-batchmode", "-nographics"]
   ```

4. Install Edgegap Unity Plugin from Asset Store:
   - Window → Edgegap → Settings → Enter API key

5. Create EdgegapConnector.cs:
   - On WebGL client load: Request server from Edgegap API (POST /v1/deploy)
   - Receive server IP:Port
   - ClientManager.StartConnection($"ws://{ip}:{port}")

6. Modify DevvitBridge.cs:
   - After FetchInitData(), wait for player spawn
   - Send Reddit username to server: networkPlayer.CmdSetUsername(username)

7. Edgegap Deployment:
   - Build server → Containerize → Push to registry.edgegap.com
   - Deploy instance with edgegap.json config (port 7770, 512MB RAM, 250 CPU)

**Testing**:
- Build WebGL client, host locally or upload to Devvit
- Deploy server to Edgegap (or run Docker locally for testing)
- WebGL client connects via WebSocket
- Verify Reddit usernames display above players

**Verification**:
- [ ] WebGL client connects to server
- [ ] Server runs in Docker container
- [ ] Edgegap deployment successful
- [ ] Reddit usernames sync to server

### Phase 6: Integration & Polish (Week 6)
**Goal**: Full integration with Devvit, production-ready

**Steps**:
1. Camera System:
   - Verify CameraManager.cs only follows owned player
   - Add networkPlayer.IsOwner check in OnPlayerRegistered()

2. Quest System:
   - Confirm goals remain local (no network changes needed)
   - Each player has independent delivery quests
   - DialogueTrigger suspends input locally only

3. Visual Polish:
   - Add Reddit username TextMesh above each player
   - Color-code players (random hue per player)
   - Add spawn animation for new players

4. Build Pipeline:
   - Create build_multiplayer.sh: Build WebGL + Linux server + Docker push
   - Integrate with existing export_devvit.sh workflow

5. Production Config:
   - TransportManager: Use secure WebSocket (wss://) for production
   - Edgegap: Set max_duration, auto-scaling rules
   - Error handling: Reconnection logic, timeout handling

**Final Testing**:
- End-to-end: Reddit user loads Devvit post → WebGL loads → Connects to Edgegap → Spawns → Sees other players
- Multiple users in parallel
- Cloud parkour with multiple players
- Delivery quests complete independently
- Server uptime > 1 hour with players joining/leaving

**Verification**:
- [ ] Full Devvit integration working
- [ ] Multiple Reddit users can play together
- [ ] Clouds sync for parkour gameplay
- [ ] Delivery quests work independently
- [ ] Server stable with player churn

## Network Traffic Budget (1000 Players)

### Per Player Bandwidth:
- Player position: 20 updates/sec × 12 bytes = 240 B/s
- Player state: On change only × 4 bytes = ~10 B/s
- Cloud positions: 10 updates/sec × 15 clouds × 16 bytes = 2.4 KB/s
- **With Interest Management (20 nearby players)**: ~5 MB/s per player

### Server Bandwidth:
- 1000 players × 5 MB/s = **5 GB/s** (manageable with AOI + LOD)

## Critical Design Decisions

### Why Fishnet?
- Excellent WebGL support (Bayou transport)
- Built-in client-side prediction
- Efficient Observer System (interest management)
- Active development and community

### Why Server-Authoritative Clouds?
- Prevents duplicate clouds from multiple clients
- Single source of truth for world state
- Clients are lightweight renderers only
- Easier to maintain synchronized parkour platforms

### Why Local Quests?
- Each player has independent progression
- Prevents quest conflicts in shared world
- Simpler networking (less state to sync)
- Reddit API integration per-player (POST /api/level-completed)

### Why Bayou for Both Client & Server?
- Simplicity: No transport mismatch
- WebGL requirement: MUST use WebSocket (Bayou)
- Server choice: Could use Tugboat (UDP) but Bayou simpler for initial deployment
- Performance: WebSocket overhead negligible for 2D platformer (< 100ms latency acceptable)

## Risk Mitigation

| Risk | Impact | Mitigation |
|------|--------|------------|
| WebSocket latency in WebGL | High | Client-side prediction, 100ms input buffer |
| Server CPU overload (1000+ players) | Critical | Interest management, physics optimization, LOD |
| Cloud sync bandwidth | High | 10Hz updates, delta compression, only moving clouds |
| Desync between clients | Medium | Server reconciliation, teleport threshold (5 units) |
| Docker container size | Low | Minimal Ubuntu image, strip client assets from server |

## Testing Milestones

- [ ] Phase 2: 2 players see each other moving
- [ ] Phase 3: 10 players parkour on synced clouds
- [ ] Phase 4: 100 players with stable 60 FPS
- [ ] Phase 4: 500 players with AOI (see ~20 nearby)
- [ ] Phase 4: 1000 player stress test (server CPU < 80%)
- [ ] Phase 5: WebGL connects to Edgegap server
- [ ] Phase 6: Full Reddit integration with usernames

## Verification Steps

After implementation, test end-to-end:

1. **Local Testing**: Use ParrelSync to test with 2-4 clients in Unity Editor
2. **WebGL Testing**: Build WebGL client, test in browser with local server
3. **Edgegap Testing**: Deploy server to Edgegap, connect WebGL client
4. **Load Testing**: Use BotSpawner to simulate 1000 players
5. **Devvit Integration**: Upload WebGL build to Devvit, test with Reddit users
6. **Parkour Testing**: Verify multiple players can parkour on same clouds
7. **Quest Testing**: Verify delivery quests work independently per player
8. **Reconnection Testing**: Test client disconnect/reconnect scenarios

## Resources

- **Fishnet Docs**: https://fish-networking.gitbook.io/docs/
- **Bayou Transport**: https://github.com/FirstGearGames/Bayou
- **Edgegap Unity Integration**: https://docs.edgegap.com/unity
- **Fishnet + Edgegap Sample**: https://docs.edgegap.com/docs/sample-projects/unity-netcodes/fishnet-on-edgegap-webgl
- **Fishnet Observer System**: https://fish-networking.gitbook.io/docs/guides/features/observers
