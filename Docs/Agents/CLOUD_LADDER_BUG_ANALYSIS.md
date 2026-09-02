# Cloud and Ladder Incident Analysis

**Updated:** 2026-09-01
**Scope:** `SimpleLevel`, cloud pooling/generation, FishNet cloud prefabs, ladders, and riding/ground detection

## Reported symptoms

- Dynamic clouds stopped spawning entirely.
- Large cloud sprites could overlap even when their platform colliders did not.
- Ladders could bind to a farther cloud, pass through an intermediate cloud, flicker, or remain disconnected after pooling.
- Cloud movement and the local squirrel riding a cloud could appear stuttery.
- A collaborator observed that changing a cloud prefab could break FishNet spawning.

The missing-image cursor shown by the browser/Codex UI was explicitly excluded from this investigation.

## Root causes and fixes

### 1. No clouds: physical collider size was used as visual size

The larger decorative colliders on the cloud prefabs had been disabled, leaving a thin platform collider about `2.337 x 0.06` world units. `CloudManager` used that physical collider height to derive the allowed scale. With the configured visual radius constraints this produced an impossible interval on affected prefabs (approximately `minScale 9.17 > maxScale 2.28`), so every spawn attempt was rejected.

`CloudManager` now caches the complete rendered bounds from all child `SpriteRenderer`s and derives scale limits from the rendered width and height. Physical colliders remain the source of collision surfaces, not art dimensions. The spawn path also assigns both `Transform.position` and `Rigidbody2D.position` before callbacks/network spawning so the initial authoritative physics pose is coherent.

### 2. Sprite overlap: lane spacing ignored the largest rendered sprite

The configured lane spacing was `1.25`, while the largest rendered cloud height is about `2.73` at its allowed scale. Separate lanes could therefore be physically valid but visibly overlap. `CloudManagerSettings_Basic` now uses `2.8` lane spacing and zero random lane-height offset. Horizontal edge spacing continues to use rendered extents.

### 3. Wrong ladder target and skipped intermediate clouds

The former ladder scan was sensitive to iteration order and pair-level bookkeeping. It could accept a distant high/low pair before considering the nearer cloud between them.

`CloudLadderController` now:

- builds globally ranked candidates from actual cloud surface gaps and actual ladder X;
- prefers valid existing pairs only through bounded hysteresis rather than unconditional retention;
- reserves a cloud's independent above/below slots only after the best candidate is selected;
- rejects a candidate when a `Physics2D.OverlapBox` through the open ladder span intersects an enabled, non-trigger collider owned by another active managed `CloudPlatform`;
- revalidates forced pairs with the same obstruction and lifecycle rules.

This means a ladder from a very high cloud to a very low cloud is not allowed to tunnel through an eligible middle cloud.

### 4. Floating ladders after cloud pooling

An active or retiring ladder could outlive an endpoint that had been returned to the cloud pool, and the same pooled object could be reactivated while an old retirement callback still referred to it.

Each cloud activation now increments a generation number. Active and retiring ladder entries capture both endpoints' generations and are discarded if an endpoint is inactive, missing, or has since been reused. Invalid ladders are removed before replacements are created. Pool-retirement callbacks therefore cannot mutate a ladder belonging to a newer activation.

### 5. Riding jitter and incorrect ground binding

Ground selection used an allocation-limited overlap query and mostly collider-center distance. On composite cloud art this could select a visually nearby but physically wrong collider, and the fixed 32-entry buffer could silently omit the best contact.

`GroundChecker` now grows its reusable overlap buffer up to 256 entries when full and ranks candidates by the collider's `ClosestPoint` to the character. Together with the authoritative `Rigidbody2D.position` initialization and server-authenticated position-only `NetworkTransform`, this reduces ground-parent switching and spawn-frame corrections while a squirrel rides a moving cloud.

### 6. Ambiguous boundary dependency

`CloudManager` first binds a `BoundaryManager` on its own GameObject. It only falls back to a scene search when exactly one candidate exists; multiple candidates fail with an explicit error instead of binding nondeterministically. `SimpleLevel` carries the intended serialized override.

### 7. Floating client ladders: authoring sprite and fail-open endpoint resolution

The ladder prefab carried an enabled root `SpriteRenderer` used only as an editor placeholder. The server/offline pool path removed that component at runtime, but a pure FishNet client never runs that path, so it could briefly show an unpositioned ladder as soon as the prefab spawned. Later, if either cloud ID or endpoint was temporarily unavailable, the client update loop skipped the ladder without hiding the last valid geometry or trigger.

The authoring renderer is now disabled in the prefab and defensively in code. On a pure client, all derived ladder sprites and the root trigger fail closed until both endpoint IDs resolve to live `CloudPlatform`s. Losing either endpoint immediately hides both visuals and collision. Runtime geometry is rebuilt only from the lower/upper cloud surfaces; the root placeholder is never used as game art. Pool reuse now disables that placeholder rather than destroying a component, keeping pooled instances structurally stable.

### 8. Cloud stutter: FishNet and Unity were advancing different physics clocks

`SimpleLevel` configures FishNet `PhysicsMode.TimeManager` at 60 Hz. FishNet manually calls `Physics2D.Simulate` between `OnTick` and `OnPostTick`, while pooled clouds previously advanced in Unity `FixedUpdate`. During a slow render frame, several `MovePosition` targets could therefore be written before FishNet simulated its catch-up ticks; the last target won, producing a large step followed by empty physics ticks.

Authoritative pooled clouds and non-pooled moving scene clouds now advance once per FishNet pre-physics tick when TimeManager physics is active, with `FixedUpdate` retained only for genuine Unity/offline physics. `OnEnable` plus a `Start` retry handles scene execution-order differences. NetworkTransform continues to sample the authoritative result after physics.

On a pure client, FishNet `NetworkTransform` is the sole rendered root interpolator; `NetworkCloud` disables Rigidbody2D interpolation so two systems cannot alternately write the same transform. On the host/server, the reverse is intentional: the authoritative Rigidbody2D interpolates its physics poses, while NetworkTransform samples and sends them rather than moving that root.

### 9. Rider stutter: visual rotation and platform carry had competing writers

`NetworkPlayer.prefab` assigned `PlayerControllerM.spriteTransform` to the Rigidbody2D/NetworkTransform root. `UpdateSprite` rotated that root every rendered `Update`, while physics froze it and FishNet synchronized it, creating a visible correction loop. The reference now targets the dedicated `Sprite` child, root rotation synchronization is disabled, and the owner Rigidbody2D uses interpolation.

For replicated clouds on a pure client, platform motion must be applied manually because the local `CloudPlatform` simulation is disabled. That manual delta and the measured platform velocity were both being applied to the squirrel. The controller now uses exactly one grounded carry path: physics-driven kinematic platforms rely on physics, while non-simulated replicated platforms apply the measured positional delta and suppress the duplicate grounded velocity term. The measured velocity remains available as detach momentum.

Client-side prediction was deliberately not added. The owner squirrel is already locally authoritative, and predicting the authoritative cloud root would separate its collision, ladder geometry, and sprite. If live frame-displacement measurements still show residual render jitter after these writer/timing fixes, the safe next experiment is a dedicated visual-child smoother, not root prediction.

The player-head one-way platform is also isolated from competing writers. Its collider is detached onto an independent simulated kinematic body so a rider cannot transfer impulses into the lower squirrel. The driver subscribes to FishNet's pre/post physics callbacks, samples the authoritative Rigidbody2D pose for simulated owners, and closes each tick at the exact owner offset after simulation. FishNet intentionally disables non-owner player rigidbodies and interpolates their Transforms; that path therefore reads the Transform and uses `MovePosition` so the solver sees real kinematic travel and carries a local rider instead of receiving a pre-simulation teleport. Teleports/respawns above the configured threshold still snap. Offline physics mirrors the same pre/post behavior with `FixedUpdate`/`LateUpdate`.

### 10. Build-4 WebGL performance regression

The `d209f0f` Pages release included a cloud shader merge in addition to denser horizontal spacing. The merged fragment shader evaluated the full two-layer cellular dissolve fourteen times per fragment: thirteen blurred shadow taps plus the main sprite. Since every cloud has two renderers, this dominated WebGL frame time. The cloud shader and material are restored to the known-playable `99c2db2` single-sample implementation.

Density also exposed CPU and network scaling costs. The repair now:

- retains SimpleLevel's smaller positive horizontal gaps and `2.8` non-overlapping lane spacing without a first-fit global cap that could starve later lanes or a separated player's viewport;
- evaluates automatic ladder topology at 10 Hz or immediately when active-cloud count changes, while each existing ladder's endpoint generation, exact surface geometry, and intermediate-cloud obstruction still fail closed every frame;
- removes the former 1.25-second ladder evaporation hold, which could leave two collinear one-ended ladders around an already invisible middle cloud;
- prefilters pairs by cached AABB distance/overlap before exact polygon and physics obstruction work;
- caches ladder cap/middle renderers and skips child/collider rebuilds when only the root pose changed, eliminating steady-state `"Middle_" + index` and `Transform.Find` garbage;
- sends cloud NetworkTransform poses every three 60 Hz ticks (20 Hz) and relies on FishNet render interpolation between them;
- prevents pooled clouds from registering redundant per-platform physics callbacks; only `CloudManager` drives them;
- maintains a pure-client active-ladder registry instead of scanning every spawned FishNet object every rendered frame.

The authoritative cloud move hook is `TimeManager.OnPrePhysicsSimulation(float)`, not `OnPreTick`. This uses FishNet's actual scaled physics delta after incoming/reconciliation and immediately before `Physics2D.Simulate`. `DeliveryCloudPlatform` now overrides and calls the base disable cleanup so no subscription survives deactivation.

Network cloud despawn explicitly uses `DespawnType.Destroy` for every cloud variant. This removes the prior one-prefab pooling-policy inconsistency without changing prefab component order or FishNet spawnable identity.

### 11. Remote head carry, catch-up ticks, and respawn state

The first detached-head implementation still had a low-frame-rate correction loop. A remote player's FishNet `NetworkTransform` advances its render root during `TimeManager.OnUpdate`, then FishNet invokes gameplay `OnTick` handlers before `OnPrePhysicsSimulation`. The head driver did not sample the new render target until pre-physics, so a rider jumping during `OnTick` inherited the previous tick's velocity. Its catch-up budget also reset to the current render-frame delta every time the target changed; at a 25 FPS 2/2/3-tick cadence this alternated the reported speed and could kick a rider.

The head driver now lazily samples a remote target from both the rider's velocity query and the pre-physics hook. It divides target displacement by the actual scaled elapsed time between samples, advances the kinematic surface by at most that measured speed per physics tick, reports the upcoming sample to same-tick jump logic, and reports zero immediately when the surface stops. Simulated owners expose their live `Rigidbody2D.linearVelocity`. First-contact head velocity is persisted on both axes so a buffered landing-jump retains momentum rather than losing most horizontal carry on the next airborne tick.

Respawn now clears current and pending platform velocity, manual-carry state, fixed grounded state, and `GroundChecker`'s cached ground collider/platform before clearing ladder state. A boundary teleport therefore cannot reapply motion from the pre-respawn cloud or player head.

`SimpleLevel` runs 60 Hz FishNet physics with `Maximum Frame Ticks = 3` and tick dropping enabled. This gives a defined 20 FPS physics floor: above that floor the bounded remote surface remains within one fractional physics tick at 25/30/60/90/144 Hz. Sustained rendering below 20 FPS advances a render-interpolated remote root faster than the permitted physics time, so no head algorithm can simultaneously stay attached, keep true velocity, and retain the three-tick performance cap. The current implementation deliberately avoids accelerating a rider to consume that backlog; sub-20 FPS attachment remains a documented degradation case rather than a claimed fix.

An additional proposal to increase cloud `NetworkTransform` interpolation from 3 to 6 was rejected before release. In the bundled FishNet version interpolation is maintained against received goal snapshots, while clouds send only every three ticks; treating the value as raw simulation ticks could add roughly 300 ms rather than the assumed 100 ms. Any cloud-buffer change must be selected from an instrumented pure-client comparison (presentation latency, cloud frame-step variance, squirrel-to-cloud relative offset, ladder binding/churn, GC, and late join), not from send-interval arithmetic alone.

### 12. Dedicated-server CPU saturation on the 0.25-vCPU profile

The first production smoke on Edgegap showed approximately 59-64% of the allocated 0.25 vCPU with no clients and 98-101% with two WebGL clients. Memory remained near 26%, so CPU—not memory—was the constrained resource. Although the short two-client cloud/head/jump test remained visually smooth, a saturated server has no durable headroom for tick bursts, garbage collection, or another player.

`SimpleLevel` had both FishNet frame-rate fields set to their maximum value of 500. In a dedicated-server build, FishNet deliberately converts that sentinel to `TickRate + 15`, so this scene ran 75 Update/LateUpdate frames around a 60 Hz network/physics clock. The server-only setting is now explicitly 60. FishNet accepts a cap equal to `TickRate` without coercion, preserving all 60 simulation/network ticks while removing up to 15 non-tick server frame passes per second. The client setting remains unchanged; an Editor host still chooses the higher client rate.

This is a capacity experiment, not a paper assumption. Release verification must repeat the same two-client Edgegap load and confirm sustained CPU headroom and smooth tick-dependent motion. If the quarter-core deployment remains near saturation or tick pacing becomes bunched, revert the cap and profile on a larger Edgegap allocation rather than reducing the network tick rate during this incident.

## FishNet prefab gotchas

The collaborator warning is directionally correct: an arbitrary sprite or collider edit does not inherently break networking, but a prefab edit can change the network protocol when it changes the `NetworkObject`, its `NetworkBehaviour` component set/order, or the generated spawnable-prefab table.

FishNet sends a prefab identifier and the receiving peer resolves that identifier through its local spawnable-prefab collection. It also serializes `NetworkBehaviour` identity by component index. Consequently, an old server and a new WebGL client can instantiate the wrong asset or deserialize state into the wrong behaviour if either table or behaviour order differs.

Project-specific findings:

- The August 19 default-prefab table had 19 entries; the current table has 24, with cloud IDs shifted.
- `Cloud_2` previously serialized duplicate `NetworkCloud` components and an order equivalent to `[NetworkCloud, NetworkCloud, NetworkTransform]`. It is repaired to exactly `[NetworkTransform, NetworkCloud]`, matching the other six cloud prefabs.
- `Cloud_2`'s variant-local `NetworkObject` despawn policy and `NetworkTransform` interpolation are aligned with `Cloud_Base`; its stale serialized prefab ID is deliberately not hand-edited because FishNet assigns the current generated-table index during initialization.
- All seven configured cloud prefabs now have exactly one root `NetworkObject`, one `NetworkTransform`, one `NetworkCloud`, a `CloudPlatform`, and at least one enabled non-trigger collider.
- The automated invariant validates unique IDs and round-trips each prefab through both FishNet's server and client spawnable-prefab lookup.
- A same-build Unity Multiplayer Play Mode run was required because a host-only test does not exercise client prefab instantiation.

Official FishNet references:

- [Spawnable Prefabs](https://fish-networking.gitbook.io/docs/fishnet-building-blocks/scriptableobjects/spawnableprefabs)
- [Default Prefab Objects](https://fish-networking.gitbook.io/docs/fishnet-building-blocks/scriptableobjects/spawnableprefabs/defaultprefabobjects)
- [Configuration and Tools](https://fish-networking.gitbook.io/docs/fishnet-building-blocks/configuration-and-tools)
- [Network Transform](https://fish-networking.gitbook.io/docs/fishnet-building-blocks/components/network-transform)
- [Time Manager](https://fish-networking.gitbook.io/docs/fishnet-building-blocks/components/managers/time-manager)
- [Network Tick Smoother](https://fish-networking.gitbook.io/docs/fishnet-building-blocks/components/tick-smoothers/networkticksmoother)

If FishNet reports prefab-ID or behaviour-index errors after a future prefab change, use its supported **Refresh Default Prefabs** / **Reserialize NetworkObjects** tools and retest a pure client. Do not hand-edit IDs. Client and server must be deployed from the same commit; keep the previous server available until the matching replacement is selected and ready.

`Cloud_2` is runtime-valid but remains structurally more fragile than a clean independent prefab because it inherits `NetworkCloud` while owning variant-local `NetworkObject` and `NetworkTransform` components. Its YAML still carries the historical prefab ID 14 while the generated current table resolves it at 18; FishNet overwrites that runtime value and the server/client round-trip test passes. A post-release cleanup should use FishNet's supported reserialization flow and either recreate it as a clean variant or use one networked cloud prefab with a separately selected cosmetic variant. That cleanup must not be mixed into this incident release.

## Why the suggested rollback was not used

The August 19 state was inspected as a reference, not restored wholesale. It predates the current 24-entry FishNet prefab table and would reintroduce incompatible component/table mappings. It also would not address the impossible rendered-scale interval that caused the current no-cloud regression. The safer repair keeps the current table, restores a consistent schema on all seven cloud prefabs, and changes size measurement at its source.

## Verification performed

- `LadderManagerTest`: **16 passed, 0 failed** on the current combined tree. The harness uses three isolated kinematic clouds and checks exact pairs, including cached presentation/stale-trigger cleanup, movement/removal, truthful forced creation, both adjacent bindings around an obstructing middle cloud, and rapid endpoint-generation reuse. The latter two waits explicitly cover one throttled 10 Hz topology scan; invalid existing bindings still fail closed in the first `LateUpdate`.
- `CloudManagerTest`: all seven prefabs passed rendered-scale, physical-collider, FishNet behaviour-order, and unique server/client spawn-table round-trip invariants. Dynamic clouds also spawned in offline fallback. The scene's pre-existing `NetworkBootstrapper` fixture still reports `Server did not start`; this is recorded as a test-fixture limitation, not treated as server validation.
- `SimpleLevel` host run after the head-clock and ladder lifecycle fixes: server log confirmed the first authoritative cloud spawn, pooled clouds and ladders appeared, and the current run showed **20 informational logs / 0 warnings / 0 errors**.
- Unity Multiplayer Play Mode on the current combined tree: one virtual pure client joined the host. The client completed with **18 informational logs / 0 warnings / 0 errors** while the host showed both network players, both detached head surfaces, replicated clouds/ladders, and **22 informational logs / 0 warnings / 0 errors**. No prefab-ID or behaviour-index fault appeared.

Follow-up motion/ladder verification added after the initial release:

- The ladder prefab invariant now rejects an enabled placeholder root sprite.
- A network presentation probe constructs derived ladder geometry, verifies it is visible only while bound, then verifies every renderer and the trigger are disabled while unbound.
- The cloud harness now verifies that TimeManager-mode clouds are subscribed to FishNet's physics clock.
- The network-player invariant verifies the sprite-child reference, owner Rigidbody2D interpolation, and disabled root rotation synchronization.
- Production pair rejection strings are built only when `GetLadderGeometryDiagnostic` is explicitly called by a test/debugger. Topology reevaluation is bounded to 10 Hz and cached presentation updates are allocation-free after warm-up.

Final head-carry verification on the September 1 exact disk state:

- C# compilation against the current Unity Bee response completed with no errors; `git diff --check` passed.
- `SimpleLevel` host spawned 22-23 clouds and active ladders, retained the squirrel on a moving cloud, accepted a jump/traversal input, and settled after boundary respawn without continuing stale motion. The Editor Console remained at 0 warnings / 0 errors.
- A late MPPM pure client connected with 14 informational logs / 0 warnings / 0 errors and no FishNet prefab/RPC faults. Reconnects produced three network players and three detached head surfaces; the three-squirrel vertical stack remained attached while the underlying cloud traversed the viewport, providing an additional distributed stack smoke test.
- The adversarial timing review found no remaining release blocker for the supported 20 FPS-and-above path. Sustained sub-20 FPS remains the explicit TimeManager tick-dropping limitation described above.
- The first Edgegap production load test exposed 98-101% CPU on the 0.25-vCPU profile with two clients. The dedicated-server 60 FPS cap described above therefore requires a fresh two-client production load test before final sign-off.

The publish/deployment record and live WebGL smoke results should be appended to the release handoff after the matching client and Edgegap server are online.

## Release and rollback checklist

1. Run `git diff --check` and compile after the final file refresh.
2. Commit the client/server changes together in one `[publish]` commit.
3. Require the foreground publisher's explicit `Published ... successfully` line; its process exit code alone is not sufficient.
4. Verify the GitHub Pages output corresponds to that commit.
5. Rebuild the Edgegap source from the same commit and select that exact newest tag in stable settings.
6. Save/confirm the replacement before terminating the prior deployment.
7. Wake the homepage, wait for the new server to become ready, then run a real WebGL pure-client smoke test.
8. If clouds, prefab deserialization, or readiness fails, restore the previous stable server tag/deployment and matching client rather than running mixed protocol versions.

## Files changed by this repair

| File | Purpose |
|---|---|
| `Assets/Scripts/Environment/CloudManager.cs` | Rendered-bounds scaling, deterministic dependency binding, coherent initial physics pose |
| `Assets/Scripts/Environment/CloudLadderController.cs` | Global candidate ranking, obstruction detection, forced-pair and pooling-generation validation |
| `Assets/Scripts/Environment/CloudPlatform.cs` | Activation generations and valid physical-bounds selection |
| `Assets/Scripts/Player/GroundChecker.cs` | Robust overlap growth and closest-surface ground ranking |
| `Assets/Scripts/Player/PlayerControllerM.cs` | Single-writer platform carry and physics/manual platform classification |
| `Assets/Scripts/Player/PlayerHeadPlatform.cs` | Detached one-way head surface driven on FishNet pre/post physics clocks |
| `Assets/Scripts/Environment/CloudBehaviorSettings.cs` | Clarified tuning semantics |
| `Assets/Scripts/Testing/CloudManagerTestRunner.cs` | Seven-prefab render/physics/FishNet invariants |
| `Assets/Scripts/Testing/LadderManagerTestRunner.cs` | Deterministic exact-pair and intermediate-obstruction tests |
| `Assets/Scene/Clouds/Cloud_Base.prefab` | Server-authenticated position-only transform configuration |
| `Assets/Scene/Clouds/Cloud_2.prefab` | Repaired FishNet behaviour schema |
| `Assets/Scene/Clouds/CloudManagerSettings_Basic.asset` | Non-overlapping visual lane spacing |
| `Assets/Levels/SimpleLevel.unity` | Explicit boundary binding and four-unit ladder vertical reach |
