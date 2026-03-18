# Cloud Ladder Bug Analysis
**Date:** 2026-03-03
**Commits analyzed:** `c667395` (Cloud lane changes), `730f4a7` (better lane management), `563f13d` (minor code cleanup)
**Author of changes:** Maya (Ram-Head)

---

## The Bug

**Symptom:** Ladder between the two initial scene cloud prefabs (Cloud_1 at ~(-1.34, -0.52) and Cloud_2) in SimpleLevel flickers — appearing and disappearing rapidly — and is uninteractable by the player.

---

## What Maya's New System Does (Summary)

### CloudManager (730f4a7 + c667395)
The old system **recycled** clouds — when a cloud drifted past the active window, it was teleported back to the entry side. The new system **pools** clouds — when a cloud drifts outside the **viewport** (`player.x ± viewportHalfWidth`), it is returned to the pool and despawned. New clouds spawn at the viewport edge + `spawnMargin`, travelling in from off-screen.

Key architectural changes:
- `activeWindowHalfWidth` / `recycleMargin` / `recycleReentryMaxGap` → replaced by `viewportHalfWidth` / `spawnMargin`
- Cloud **scale** is now derived from `minCloudRadius`/`maxCloudRadius` (radius = half-height in world units) so all clouds have predictable physical size regardless of prefab native scale
- Cloud spacing is now `baseSpacing` (edge-to-edge gap) randomized per lane from `minCloudSpacing`/`maxCloudSpacing`
- `_forceFill` flag: on first Update and on player join/respawn, runs 10 fill iterations to populate the viewport immediately
- `IsLaneUnderpopulated` now uses `baseSpacing + spacingVariation` as the max gap threshold (instead of a flat `maxCloudSpacing`)
- Two new guards before despawning: `IsPlayerOnAnyLadderPartner(cloud)` and `ShouldKeepCloudActiveForLadders(cloud, viewportLeft, viewportRight)` — prevents despawning a cloud that's part of an active ladder pair while the partner is still visible

### CloudLadderController (c667395)
- Old: `_usedCloudsScratch` — once a cloud appeared in any pair it was blocked from ALL further pairing that frame
- New: `_hasLadderAboveScratch` / `_hasLadderBelowScratch` — separate tracking. A cloud can now be the lower end of one ladder AND the upper end of another (supports "chain" ladders up a column of clouds)
- `GetEdgeYAtX()` — polygon-aware edge detection. If `mainCollider` is a `PolygonCollider2D`, samples the actual polygon boundary at a given world X to find the true top/bottom surface rather than the AABB edge
- `ladderInsetIntoCloud = 0.2f` — ladder visuals/collider extend 0.2 units **into** each cloud from its surface edge, so there's no floating gap at the connection point
- `ShouldKeepCloudActiveForLadders` and `IsPlayerOnAnyLadderPartner` — new CloudManager helpers to prevent viewport culling from breaking active ladders

---

## Bug 1: The Flicker — Root Cause

### The `ComputeValidPairs` seeding problem

In `CloudLadderController.ComputeValidPairs()`, the very first thing that happens is:

```csharp
// Step 1 — seed from EXISTING ladders
foreach (var kvp in _ladders)
{
    var (lower, upper) = kvp.Key;
    if (lower != null) _hasLadderAboveScratch.Add(lower);
    if (upper != null) _hasLadderBelowScratch.Add(upper);
}

// Step 2 — main pairing loop
for (int i ...) for (int j ...)
{
    ...
    if (_hasLadderAboveScratch.Contains(lower) || _hasLadderBelowScratch.Contains(upper))
        continue;   // ← SKIPS the existing (A,B) pair entirely
    if (ShouldHaveLadder(a, b))
    {
        _validPairsScratch.Add(pair);
        ...
    }
}

// Step 4 — re-validate existing ladders
foreach (var kvp in _ladders)
{
    if (ShouldHaveLadder(kvp.Key.Item1, kvp.Key.Item2))
        _validPairsScratch.Add(kvp.Key);  // re-adds them here
}
```

**The intended safety net is Step 4.** Existing ladders are re-added to `_validPairsScratch` if `ShouldHaveLadder` still returns true. If `ShouldHaveLadder` consistently returns true for static scene clouds, this should be stable.

**However**, `ShouldHaveLadder` calls `GetMainBounds()` on both clouds. `GetMainBounds()` returns `mainCollider.bounds` — this is the physics-computed AABB, which is finalized at the end of each physics step (FixedUpdate). The Rigidbody2D on CloudPlatform uses `RigidbodyInterpolation2D.Interpolate`, meaning the **visual/transform position** can differ from the **physics position** between FixedUpdate steps.

For scene clouds with `moveSpeed = 0`, the physics position never changes — this should be stable. But see **Bug 2** for an edge case.

### The real trigger: `ShouldHaveLadder` geometric check with `minVerticalGap`

The gap check in `ShouldHaveLadder`:
```csharp
float gap = bu.min.y - bl.max.y;
if (gap < minVerticalGap) return false;  // minVerticalGap = 0.5f
```

The default `ladderInsetIntoCloud = 0.2f` introduced in this commit means `UpdateLadderPosition` now computes the ladder so it **extends 0.2 units into each cloud**:
```csharp
yMin = lowerTopY - 0.2f;   // below the cloud's top surface
yMax = upperBottomY + 0.2f; // above the cloud's bottom surface
```

But `ShouldHaveLadder` still uses the AABB gap (`bu.min.y - bl.max.y`). If the scene clouds were placed with a gap of exactly ~0.5 (right at `minVerticalGap`), floating-point jitter in `mainCollider.bounds` (which is recalculated each physics step from the RB position) can cause this check to flip between `true` and `false` on alternating frames → **ladder created on frame N, removed on frame N+1, created on N+2...** = visible flicker.

**Compounding factor:** `GetEdgeYAtX` is called with `x = (bl.center.x + bu.center.x) / 2`. Cloud_1 is at (-1.34, -0.52). If Cloud_2 has a significantly different X position, the midpoint X may lie **outside** the polygon bounds of one or both clouds. `GetEdgeYAtX` then falls back to AABB anyway, making `ladderInsetIntoCloud` compute `yMin = bl.max.y - 0.2` and `yMax = bu.min.y + 0.2`. The resulting ladder height = `gap - 0.4f` if inset pushes INTO a negative-gap scenario, or if `gap < 0.4f` (overlap), `Mathf.Max(0.1f, ...)` clamps to 0.1, creating a nearly-invisible zero-height collider. The BoxCollider2D is effectively invisible/zero-size = **uninteractable**.

---

## Bug 2: Uninteractable Ladder — Root Cause

Even if the ladder is stable (not flickering), `ladderInsetIntoCloud = 0.2f` causes the ladder's `BoxCollider2D` (which is a **trigger**) to be embedded 0.2 units inside the solid cloud colliders. The cloud colliders are on a physics layer that the player collides with. The player's collider hits the cloud's solid surface before ever reaching the ladder trigger's center. Depending on layer collision matrix settings, the player may never actually enter the ladder trigger at all.

**Separately:** `ShouldHaveLadder` uses AABB but `UpdateLadderPosition` uses the polygon edge. The two clouds' polygon tops/bottoms might be at different Y positions than the AABB edges. It's possible for `ShouldHaveLadder` to say the gap is fine but `UpdateLadderPosition` computes a negative or near-zero ladder height if the polygon edge is lower (for lower cloud top) or higher (for upper cloud bottom) than the AABB edge at the sampled X.

---

## Networking Gotcha: Server Owns CloudManager — Will Maya's System Work?

**Short answer: Mostly yes, but with one real gotcha.**

### What works correctly
- `CloudManager.Update()` runs only on the server. Server physics runs for all objects (including remote players' `NetworkObject`s). `CloudPlatform.OnCollisionEnter2D`/`OnCollisionExit2D` fires correctly on the server, so `IsPlayerOnCloud` is accurate.
- `ShouldKeepCloudActiveForLadders` and `IsPlayerOnAnyLadderPartner` both run on the server where physics is authoritative. Safe.
- `CloudLadderController.LateUpdate()` runs on the server → creates/despawns ladders as FishNet NetworkObjects. Clients receive them via replication.
- `NetworkCloudLadderController.LateUpdate()` on pure clients rebuilds ladder geometry from synced cloud positions. Correct.

### The gotcha: `wasActiveAtStart` set in `NetworkCloud.Awake()` — BEFORE `NetworkCloudManager.Awake()` disables `CloudManager`

```csharp
// NetworkCloud.Awake():
_platformWasEnabledAtStart = _platform != null && _platform.enabled;
_platform.wasActiveAtStart = _platformWasEnabledAtStart;

// NetworkCloudManager.Awake():
_cloudManager.CollectSceneClouds();  // reads wasActiveAtStart
```

`Awake()` order between sibling components on different GameObjects is not guaranteed. If `NetworkCloudManager.Awake()` (and thus `CollectSceneClouds()`) runs **before** `NetworkCloud.Awake()` sets `wasActiveAtStart`, the scene clouds will have `wasActiveAtStart = false` (default) when `CollectSceneClouds()` reads them. Result: scene clouds are added to `_nonPooled` but **not** to `_active`. The ladder controller never sees them. **No ladder ever forms.**

In practice on the editor host path, Unity processes components in the order they appear on GameObjects, and scene cloud GOs are separate from the CloudManager GO, so execution order depends on scene hierarchy order. This is fragile.

**Fix:** `wasActiveAtStart` should be set in `CloudPlatform.Awake()` itself (not in `NetworkCloud.Awake()`), so it's always ready before any manager runs `CollectSceneClouds()`. Or, make `CollectSceneClouds()` use `cloud.gameObject.activeSelf` directly instead of relying on `wasActiveAtStart`.

### The other gotcha: `GetPrefabNativeHeightY` destroys a temp instance immediately

```csharp
var temp = Instantiate(prefab, _poolParent);
...
Bounds b = p.GetMainBounds();
Object.Destroy(temp);         // ← frame-delayed destroy
h = b.size.y;
```

`Object.Destroy` is deferred to end-of-frame. The temp GO exists on-screen for one frame. More critically, if the cloud prefab has a `NetworkObject`, instantiating it without going through `ServerManager.Spawn()` may log FishNet warnings or cause a brief NetworkObject in an unspawned/detached state. If this causes `GetMainBounds()` to return zero bounds (because the collider hasn't had a physics step yet), `h = 0f` → `SpawnCloudInLane` returns early → **no dynamic clouds ever spawn in that lane**. The fix is to use `Physics2D.SyncTransforms()` before measuring, or read the native bounds from the prefab asset directly rather than instantiating it.

---

## Suggested Debug Logs (Add Temporarily)

In `CloudLadderController.RemoveInvalidLadders()`, before the second removal loop:
```csharp
foreach (var kvp in _ladders)
{
    bool inValid = validPairs.Contains(kvp.Key);
    Debug.Log($"[Ladder] ({kvp.Key.Item1?.name}, {kvp.Key.Item2?.name}) inValidPairs={inValid} shouldHave={ShouldHaveLadder(kvp.Key.Item1, kvp.Key.Item2)}");
}
```

In `CloudLadderController.CreateLadder()`:
```csharp
Debug.Log($"[Ladder] CREATE ({lower.name}, {upper.name}) gap={upper.GetMainBounds().min.y - lower.GetMainBounds().max.y:F3}");
```

In `CloudLadderController.DespawnLadder()` (before `if (_onLadderDeactivated != null)`):
```csharp
Debug.Log($"[Ladder] DESPAWN {ladder.name}", ladder);
```

In `CloudManager.DeactivateCloud()` (for scene clouds):
```csharp
if (_nonPooled.Contains(cloud))
    Debug.Log($"[CloudManager] DEACTIVATE nonpooled scene cloud: {cloud.name}");
```

### Testing Steps
1. Hit Play in editor (offline mode — no server required for initial test)
2. Watch Console for `[Ladder] CREATE` / `[Ladder] DESPAWN` alternating each frame → confirms flicker
3. Check the `gap=` value logged on CREATE — if it's near 0.5 (`minVerticalGap`), that's the oscillation trigger
4. Check `inValidPairs=false` on a frame where the ladder is about to be removed — tells you whether Step 4 is failing to re-add it
5. Look for `shouldHave=false` on a frame where the gap is borderline — confirms the floating-point gap oscillation theory
6. In a network HOST play session, also watch for `[Ladder] CREATE` on frame 1 — if it never appears, check that `wasActiveAtStart` is true on both clouds (select them in the Hierarchy → Inspector → CloudPlatform)

---

## Recommended Fixes

### Fix 1: Stabilize `ShouldHaveLadder` gap check (flicker)
Add a small epsilon buffer to the gap check so near-`minVerticalGap` clouds don't oscillate:
```csharp
// In ShouldHaveLadder:
float gap = bu.min.y - bl.max.y;
if (gap < minVerticalGap - 0.05f) return false;  // 5cm hysteresis
```

### Fix 2: Reduce `ladderInsetIntoCloud` to 0 or make it opt-in (uninteractable)
The current default of 0.2 causes the trigger collider to be buried inside solid cloud colliders. Set it to `0` by default in the Inspector, or ensure the clouds' solid colliders are on a layer that does NOT overlap with the player's ladder detection layer.

Alternatively, change `yMin`/`yMax` to only inset relative to the **gap** midpoint, not penetrating the cloud body:
```csharp
// Instead of subtracting inset from surface edge:
float midGap = (lowerTopY + upperBottomY) * 0.5f;
float halfLadder = (upperBottomY - lowerTopY) * 0.5f + ladderInsetIntoCloud;
yMin = midGap - halfLadder;
yMax = midGap + halfLadder;
```

### Fix 3: `wasActiveAtStart` initialization order (networking gotcha)
Move the `wasActiveAtStart` assignment to `CloudPlatform.Awake()` so it's always set before any manager reads it:
```csharp
// In CloudPlatform.Awake():
wasActiveAtStart = gameObject.activeSelf && enabled;
```
And remove it from `NetworkCloud.Awake()`.

### Fix 4: `GetPrefabNativeHeightY` — avoid instantiating networked prefabs
Cache native height in the prefab's `CloudPlatform` component itself, or measure via `PrefabUtility` in editor only, or call `Physics2D.SyncTransforms()` before measuring bounds on the temp instance.

---

## Files Involved
| File | Role |
|------|------|
| `Assets/Scripts/Environment/CloudManager.cs` | Viewport pool/despawn, lane management |
| `Assets/Scripts/Environment/CloudLadderController.cs` | Ladder pair logic, polygon edge sampling |
| `Assets/Scripts/Environment/CloudPlatform.cs` | Per-cloud movement, bounds, despawn anim |
| `Assets/Scripts/Environment/CloudBehaviorSettings.cs` | All tunable parameters |
| `Assets/Scripts/Environment/BoundaryManager.cs` | Extended/inner bounds for lane clamping |
| `Assets/Scripts/Network/NetworkCloudManager.cs` | Server enables CloudManager, client disables |
| `Assets/Scripts/Network/NetworkCloudLadderController.cs` | Client rebuilds ladder geometry each LateUpdate |
| `Assets/Scripts/Network/NetworkCloud.cs` | Sets `wasActiveAtStart`, disables CloudPlatform on pure clients |
| `Assets/Scenes/SimpleLevel.unity` | Cloud_1 at (-1.34, -0.52), Cloud_2 — both `isMoving=0`, `canBuildLadder=1` |
| `Assets/Scene/Clouds/CloudManagerSettings_Basic.asset` | `minVerticalGap` not set here — default 0.5f in CloudLadderController Inspector |
