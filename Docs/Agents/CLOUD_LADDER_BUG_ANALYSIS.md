# Cloud and Ladder Incident Analysis

**Updated:** 2026-09-02
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

`SimpleLevel` originally configured FishNet `PhysicsMode.TimeManager` at 60 Hz (the current performance candidate is 40 Hz). FishNet manually calls `Physics2D.Simulate` between `OnTick` and `OnPostTick`, while pooled clouds previously advanced in Unity `FixedUpdate`. During a slow render frame, several `MovePosition` targets could therefore be written before FishNet simulated its catch-up ticks; the last target won, producing a large step followed by empty physics ticks.

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
- evaluates automatic ladder topology at 10 Hz and existing-ladder surface/obstruction validity at 20 Hz. Endpoint activity, despawn state, and pooling generation still fail closed every frame; an intermediate-cloud obstruction closes an old long binding on the next geometry pass (within about 50 ms nominally);
- removes the former 1.25-second ladder evaporation hold, which could leave two collinear one-ended ladders around an already invisible middle cloud;
- prefilters pairs by cached AABB distance/overlap before exact polygon and physics obstruction work;
- caches ladder cap/middle renderers and skips child/collider rebuilds when only the root pose changed, eliminating steady-state `"Middle_" + index` and `Transform.Find` garbage;
- derives the cloud NetworkTransform interval from the scene tick rate. At the current 40 Hz candidate, poses are sent every two ticks (20 Hz) and FishNet render interpolation fills the frames between them;
- prevents pooled clouds from registering redundant per-platform physics callbacks; only `CloudManager` drives them;
- maintains a pure-client active-ladder registry instead of scanning every spawned FishNet object every rendered frame.

The authoritative cloud move hook is `TimeManager.OnPrePhysicsSimulation(float)`, not `OnPreTick`. This uses FishNet's actual scaled physics delta after incoming/reconciliation and immediately before `Physics2D.Simulate`. `DeliveryCloudPlatform` now overrides and calls the base disable cleanup so no subscription survives deactivation.

Network cloud despawn explicitly uses `DespawnType.Destroy` for every cloud variant. This removes the prior one-prefab pooling-policy inconsistency without changing prefab component order or FishNet spawnable identity.

### 11. Remote head carry, catch-up ticks, and respawn state

The first detached-head implementation still had a low-frame-rate correction loop. A remote player's FishNet `NetworkTransform` advances its render root during `TimeManager.OnUpdate`, then FishNet invokes gameplay `OnTick` handlers before `OnPrePhysicsSimulation`. The head driver did not sample the new render target until pre-physics, so a rider jumping during `OnTick` inherited the previous tick's velocity. Its catch-up budget also reset to the current render-frame delta every time the target changed; at a 25 FPS 2/2/3-tick cadence this alternated the reported speed and could kick a rider.

The head driver now lazily samples a remote target from both the rider's velocity query and the pre-physics hook. It divides target displacement by the actual scaled elapsed time between samples, advances the kinematic surface by at most that measured speed per physics tick, reports the upcoming sample to same-tick jump logic, and reports zero immediately when the surface stops. Simulated owners expose their live `Rigidbody2D.linearVelocity`. First-contact head velocity is persisted on both axes so a buffered landing-jump retains momentum rather than losing most horizontal carry on the next airborne tick.

Respawn now clears current and pending platform velocity, manual-carry state, fixed grounded state, and `GroundChecker`'s cached ground collider/platform before clearing ladder state. A boundary teleport therefore cannot reapply motion from the pre-respawn cloud or player head.

`SimpleLevel` now has a candidate 40 Hz FishNet physics rate with `Maximum Frame Ticks = 3` and tick dropping enabled. This lowers the three-tick floor from 20 FPS to about 13.3 FPS. The timing model expects the bounded remote surface to remain within one fractional physics tick above that floor; the 40 Hz tree has only received the current-cadence host smoke so far, not a multi-cadence sweep. Sustained rendering below that floor advances a render-interpolated remote root faster than the permitted physics time, so no head algorithm can simultaneously stay attached, keep true velocity, and retain the three-tick performance cap. The current implementation deliberately avoids accelerating a rider to consume that backlog; sub-floor attachment remains a documented degradation case rather than a claimed fix.

An additional proposal to increase cloud `NetworkTransform` interpolation from 3 to 6 was rejected before release. In the bundled FishNet version interpolation is maintained against received goal snapshots; when the proposal was evaluated, clouds sent every three 60 Hz ticks. Treating the value as raw simulation ticks could therefore add roughly 300 ms rather than the assumed 100 ms. The current 40 Hz candidate preserves a 20 Hz cloud stream by sending every two ticks and does not change the interpolation setting. Any future buffer change must be selected from an instrumented pure-client comparison (presentation latency, cloud frame-step variance, squirrel-to-cloud relative offset, ladder binding/churn, GC, and late join), not from send-interval arithmetic alone.

### 12. Dedicated-server CPU saturation on the 0.25-vCPU profile

The first production smoke on Edgegap showed approximately 59-64% of the allocated 0.25 vCPU with no clients and 98-101% with two WebGL clients. Memory remained near 26%, so CPU—not memory—was the constrained resource. Although the short two-client cloud/head/jump test remained visually smooth, a saturated server has no durable headroom for tick bursts, garbage collection, or another player.

`SimpleLevel` had both FishNet frame-rate fields set to their maximum value of 500. In the first dedicated-server repair, the server-only setting was made explicit at 60 while the 60 Hz network/physics clock was retained. The client setting remains 500; an Editor host therefore uses the client cap and cannot measure the dedicated-server frame cap.

Production measurement of the matching `d5ae2cb` client/server release showed that the frame cap was helpful but insufficient: idle CPU improved from about 59-64% to 52-55%, while two connected WebGL clients still sustained roughly 95-110%. The first apparent one-client result near 52-58% was invalid because the remaining client no longer had dynamic clouds after its peer disconnected. Later exact one-client runs with populated clouds remained roughly 77-100%, with memory near 25%. The load therefore follows an active cloud viewport/world workload; it has not been proven to scale primarily per observer and remains far above the requested 20% CPU target.

The horizontal density increase in `d209f0f` halved SimpleLevel's positive rendered-edge gaps from `0.97-5.02` to `0.485-2.51`. Every additional visible cloud is another spawned FishNet `NetworkObject` whose transform and lifecycle must be observed by every client; it also expands the ladder candidate set quadratically. SimpleLevel therefore restores the previously playable `0.97-5.02` gaps while retaining `2.8` lane spacing, the non-overlap guarantees, viewport coverage, authoritative movement, and all ladder correctness fixes. This is intentionally an isolated rollback before structural optimizations or an arbitrary global cloud cap. A first-come global cap could starve a separated player's viewport and currently causes capped empty slots to retry at physics frequency.

The next candidate reduces FishNet physics/network ticks from 60 to 40 Hz and caps only the dedicated-server render loop at the same 40 FPS. Forty hertz cuts simulation work by one third while keeping cloud snapshots at 20 Hz (`ceil(40 / 20) = 2`) and player input/physics opportunities at 25 ms. FishNet accepts a server cap equal to the tick rate; values below it are coerced to `TickRate + 15`. Matching the configured cap to the clock removes the 15-FPS headroom and up to 15 extra `Update`/`LateUpdate` passes per second when the process keeps pace. FishNet still accumulates elapsed time and may execute zero, one, or multiple due ticks in a frame, so Linux oversleep and catch-up behavior remain production acceptance measurements rather than a one-frame-per-tick guarantee. This is a measured experiment, not a completed performance fix: Editor-host testing uses the unchanged 500 client cap, and acceptance requires a matching Linux build plus sustained populated-world CPU measurements. A 30 Hz candidate was rejected because it would reduce clouds to 15 snapshots per second, raise physics/input intervals to about 33 ms, and compound platformer feel changes.

The structural patch keeps occupied-cloud `Rigidbody2D.MovePosition` calls on FishNet's pre-physics clock while removing administrative work from the hot path. In the current candidate that clock is 40 Hz:

- player viewport rebuild, lane activation, pooled-cloud culling, and empty-slot visibility/no-spawn retries run at 10 Hz, with immediate dirty requests after player, viewport, no-spawn-zone, and return-to-pool lifecycle changes;
- automatic ladder creation/re-ranking runs strictly at 10 Hz. Existing exact geometry/obstruction invalidation uses a separate 20 Hz pass instead of repeating `Physics2D.OverlapBox` for every ladder every rendered frame. Endpoint activity, despawn state, and pooling generation still invalidate immediately, and forced creation remains immediate;
- ladder candidates are sorted by cached lower bound, allowing a mathematically safe vertical break once every later candidate is beyond `maxVerticalGap`;
- non-spawn physics ticks skip wrapped-position math for empty lane slots and reuse each lane's cached rendered half-width for occupied-cloud exit checks;
- server pool-warning diagnostics inspect FishNet caches once per second instead of every frame;
- viewport RPCs reject non-finite values, clamp camera half-height/aspect to supported ranges, and rate-limit dirty refresh requests to the same 10 Hz cadence, preventing a modified client from activating the full level or forcing the lifecycle pass every frame;
- Linux dedicated servers disable steady-state cloud Animator evaluation, disable non-rendered Rigidbody2D interpolation, and build only authoritative ladder root poses/trigger colliders. They re-enable a cloud Animator synchronously before the existing server-owned despawn coroutine triggers it, preserving the client fade and pool-return interval. Hosts, pure clients, offline play, and Editor Server-subtarget tests retain full presentation/interpolation.

This patch intentionally does not claim that ladder selection is no longer quadratic in dense adjacent bands, and it does not yet cache every active slot's component references. Those are follow-ups if the attributable 40 Hz production measurement remains saturated. Acceptance still requires the matching Linux build to prove repeated cloud despawn/pool/reuse and a real WebGL client to prove cloud population, ladder binding, fade, ride/jump behavior, and sustained CPU headroom.

### 13. Pure-client ladder phase skew after NetworkTransform interpolation

`Physics2D.autoSyncTransforms` is disabled. On a pure client, FishNet's `NetworkTransform` updates a cloud's Transform during a render update, but Unity's cached `Collider2D.bounds` is not refreshed until the next physics synchronization. A diagnostic on the matching host/pure-client build measured client collider centers lagging the visible cloud Transforms by approximately `0.005-0.014` world units; a manual `Physics2D.SyncTransforms()` immediately moved those bounds, while the host's before/after values were identical. Ladders could therefore be laid out against the previous physics pose even though their endpoint sprites were already at the next interpolated pose, producing visible cap separation and jitter.

For managed `BoxCollider2D` cloud surfaces, `CloudPlatform.GetCurrentColliderBounds` now derives the current world AABB directly from the box offset, size, edge radius, scale, and rotation using four `TransformPoint` corners. Ladder surface and overlap calculations use that result without a global physics synchronization. Non-box colliders retain native bounds as a compatibility fallback; the optional Delivery/PostBox scene clouds therefore retain the pure-client phase-skew limitation. Linux dedicated servers also retain native `Collider2D.bounds` because they do not render NetworkTransform interpolation and ladder CPU is the constrained resource. The regression harness moves a cloud Transform without synchronizing physics and verifies that its reported main bounds move by the same amount.

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

- `LadderManagerTest`: **17 passed, 0 failed** on the current combined tree. The harness uses three isolated kinematic clouds and checks exact pairs, including cached presentation/stale-trigger cleanup, movement/removal, truthful forced creation, both adjacent bindings around an obstructing middle cloud, rapid endpoint-generation reuse, and current-Transform bounds before physics synchronization. Range and intermediate-obstruction checks wait through one throttled 10 Hz topology scan; endpoint inactivity, despawn, and generation reuse still fail closed in the first `LateUpdate`.
- `CloudManagerTest`: **17 passed, 0 failed**. All seven dynamic prefabs passed rendered-scale, physical-collider, FishNet behaviour-order, and unique server/client spawn-table round-trip invariants. The added independent rendered-bounds check unions each active dynamic platform's enabled sprite renderers and rejects greater-than-`0.001`-world-unit overlap on both axes without reusing `CloudManager`'s geometry cache. The final Linux Server-target Editor run sampled 13 dynamic clouds across five consecutive end-of-frame samples. This is an active-layout regression, not proof of a full pool/reuse cycle. The test scene deliberately starts its isolated host because the runner requires server lifecycle coverage. `CloudManagerTest` is not in `EditorBuildSettings`, so this fixture change does not alter the production scene list.
- `SimpleLevel` host run on the 40 Hz candidate: the server logged the first authoritative cloud spawn, pooled clouds and ladders appeared, movement and a jump were accepted, and the Console remained at **0 warnings / 0 errors**. This exercises gameplay under the candidate tick rate, but an Editor host uses the client frame cap and is not evidence for the dedicated 55 FPS path.
- Unity Multiplayer Play Mode on the current combined tree: a matching pure client joined the host and replicated clouds/ladders without prefab-ID or behaviour-index faults. Before the bounds repair, the client diagnostic measured the `0.005-0.014` world-unit bounds lag described above; after the repair, before/after-`Physics2D.SyncTransforms` ladder geometry was identical. A separate final local host run did not retain a second connected clone, so remote-head standing/jump/drop remains a required exact WebGL multiplayer smoke rather than a claimed final pass.

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
- The adversarial timing review found no remaining release blocker for the prior 60 Hz, 20-FPS-and-above path. The current 40 Hz candidate lowers that three-tick threshold to about 13.3 FPS, but still requires the exact WebGL multiplayer checks listed above.
- The exact `942e0e5` 40 Hz / 55 FPS release (`2026.09.02.38-942e0e57` WebGL, `2026.09.02.39-942e0e57` Linux server) populated clouds and ladders immediately. One WebGL client sustained about 58-67% CPU and two sustained about 65-70% on the 0.25-vCPU profile, with memory near 25%; both remained responsive during cloud riding, movement, jumping, and a two-client head-platform smoke. This is materially better than the prior 77-100% one-client and 98-110% two-client measurements, but it does not meet the requested 20% CPU target.
- The 40 FPS server-cap follow-up removes the configured 15-FPS headroom without changing the verified 40 Hz physics/network clock. It still requires a matching Linux build and fresh sustained one-client and two-client measurements; it must not be described as meeting the requested target until that evidence exists.

### September 2 live release verification

The final matched release inspected in production was:

- source commit `3372348d1e857a237da872deab6b10230794c37f` (`Stabilize isolated WebGL publishing [publish]`);
- GitHub Pages build `2026.09.02_build6_compersion2d`, reporting client version `2026.09.02.42-3372348d`;
- Edgegap Linux server version `2026.09.02.43-3372348d`, immutable image tag `26.09.03-00.34.32-UTC`, deployment request `b7a382e0e678` in Fremont;
- the public WSS endpoint completed an HTTP 101 upgrade, and the exact server version appeared in deployment logs.

Firefox was used because the Chrome-control extension was unavailable in this session. The following are bounded smoke results, not exhaustive acceptance claims:

- One WebGL client loaded the populated moving-cloud world and accepted a jump.
- Two WebGL clients joined the same session. A local squirrel jumped away and landed back on the remote squirrel's detached head platform; the resulting stack stayed visually stable over a three-second sample.
- A live middle-cloud routing case showed two adjacent ladders sharing the middle cloud rather than one high-to-low ladder passing through it. Over approximately 20 seconds, the observed ladder endpoints continued following their cloud surfaces without a visible floating/stale ladder.
- A 24-frame capture over approximately 5.2 seconds found zero integer-pixel relative movement in the sampled rider/cloud crop. This is evidence against large pixel-step jitter in that sample; it does not measure subpixel motion, browser frame pacing, or sustained FPS.
- After the clients disconnected, server network traffic returned near zero and CPU returned toward its prior range after about 50 seconds. This argues against a gross disconnect lifecycle leak, but it is not a long-duration soak.

Observed Edgegap utilization on the 0.25-vCPU deployment remained approximately 27-38% CPU with no clients and approximately 45-60% with one or two short-lived clients; memory stayed around 28-29%. This is a large improvement over the earlier saturation, but it does **not** meet the requested 20% CPU target. The client-count samples were roughly 30-45 seconds rather than the two-minute acceptance windows, and the deployment graph does not separate Unity from the `cloudflared` sidecar. No further production timing or density change is justified until server-side timing/counters isolate the remaining cost.

A local Unity pure-client profile against the live server showed steady Editor frames around 5-8 ms (one inspected frame was 5.28 ms) and about 0.05 ms in `CloudLadderController.LateUpdate`; cloud-manager/network-cloud callbacks were near zero in the sampled frame. A 13.66 ms cloud-manager frame was consistent with cold spawn/JIT work, not proven as a steady-state cause. Editor profiling does not establish WebGL CPU, GPU, or frame-pacing behavior.

The public homepage watchdog remained circuit-open and displayed `Startup needs attention` even while the separately tested Edgegap deployment was ready and the WSS handshake succeeded. Treat watchdog recovery as a separate operational blocker; do not reset its admin state without authenticated inspection.

The Edgegap build left the Editor with the Linux Server subtarget active, which caused Editor Play Mode to follow the dedicated-server branch and made the cloud harness fail before spawning a local player. `NetworkBootstrapper` now gives `UNITY_EDITOR` precedence when both `UNITY_EDITOR` and `UNITY_SERVER` are defined. Actual dedicated-server builds still use the server branch because `UNITY_EDITOR` is absent. After restoring the Linux Server target, the Editor logged `UNITY EDITOR PLAY MODE`, spawned the local player and 13 clouds, and completed `CloudManagerTest` with **17 passed, 0 failed**, 0 warnings, and 0 errors.

The first two fresh-checkout WebGL attempts for the 40/40 release failed closed after Unity's initial linker, IL2CPP, and WebAssembly work completed: a timestamp-triggered Bee follow-up pass reran UnityLinker and intermittently lost resolution of the already-present Newtonsoft assembly, so Unity never emitted the final template or `index.html`. The publisher now settles package resolution and asset import in a separate batch invocation before starting the Build Profile invocation. Its existing profile, metadata, complete-artifact, and push gates remain authoritative; a successful Unity process exit alone is still not treated as a publication.

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
| `Assets/Scripts/Network/NetworkBootstrapper.cs` | Keep Editor Play Mode on the host/client path after a Linux Server-target build |
| `Assets/Scripts/Testing/CloudManagerTestRunner.cs` | Seven-prefab render/physics/FishNet invariants and independent active-renderer overlap regression |
| `Assets/Scripts/Testing/LadderManagerTestRunner.cs` | Deterministic exact-pair and intermediate-obstruction tests |
| `Assets/Scene/Clouds/Cloud_Base.prefab` | Server-authenticated position-only transform configuration |
| `Assets/Scene/Clouds/Cloud_2.prefab` | Repaired FishNet behaviour schema |
| `Assets/Scene/Clouds/CloudManagerSettings_Basic.asset` | Non-overlapping visual lane spacing |
| `Assets/Levels/SimpleLevel.unity` | Explicit boundary binding, four-unit ladder reach, and matched 40 Hz FishNet tick / dedicated-frame cap |
| `Assets/Levels/CloudManagerTest.unity` | Start the isolated host required by the dynamic-spawn/network-prefab regression harness |
