# Feasibility: Replacing FishNet/Edgegap with Devvit Realtime

> Historical analysis only. Devvit runtime integration is not part of the current build; do not treat the proposed bridge or backend work below as an active dependency.

## Short Answer: Not feasible as a full replacement. Feasible as a complement.

---

## Why Devvit Realtime Can't Replace FishNet for Physics Sync

### 1. Latency is fundamentally wrong
FishNet syncs player positions at 15Hz (66ms per tick). Each update needs to complete well under that budget.

Devvit Realtime path per update:
```
Unity → JS bridge → HTTP POST to Devvit function → Redis write → realtime.send() → subscriber onMessage → Unity
```
That chain is **200–600ms+**, not 66ms. Movement would be unplayably laggy.

### 2. No persistent game loop on Devvit
FishNet's server runs a continuous authoritative physics loop — it owns all Rigidbodies, spawns/despawns objects, runs OnTick at 15Hz. Devvit's "server" is serverless functions that handle a request and exit. There is no persistent process to run a physics tick. Redis is the only shared state.

### 3. JSON-only, no binary protocol
FishNet sends tightly packed binary frames. Devvit realtime is JSON pub/sub — significantly larger and slower for position/rotation data across 10+ players.

### 4. No connection/spawn/ownership model
FishNet provides: NetworkTransform (auto interpolation), ServerRpc/ObserversRpc, NetworkObject spawning, client ownership, BufferLast for late joiners. All of this would need to be rebuilt from scratch.

### 5. Unknown rate limits
Devvit realtime docs don't publish rate limits. At 15Hz × N players you'd likely hit throttling quickly.

---

## What IS Feasible: Devvit Realtime as a Complement

A Devvit realtime complement would require a separate Reddit-hosted API layer alongside FishNet:

| Feature | Notes |
|---------|-------|
| Live player count on the post embed | `realtime.send` on connect/disconnect events |
| Leaderboard push updates | `realtime.send` when any player posts a score |
| Spectator presence | Low-freq 1Hz position broadcast for non-players |
| Post-level reactions visible to Reddit viewers | Event-driven, no timing constraints |

Files that would change:
- A new JavaScript bridge or external integration layer — only if Devvit support is explicitly reintroduced
- New `.jslib` or extension of the existing Bayou jslib pattern
- Devvit TypeScript backend (outside this repo) — add `realtime.send()` on score post

---

## Alternative: Full Devvit-native Architecture (not recommended)

Would require redesigning the game as turn-based or tile-based (no real-time physics), 1–2Hz update cap, no authoritative collision. Fundamentally a different game from a 2D platformer.

---

## Recommendation

**Keep FishNet + Edgegap** for multiplayer physics — it's working and already deployed.

Do not add Devvit realtime as part of the current build. Revisit this only if the publishing/runtime target explicitly returns to Reddit-hosted Devvit features.
