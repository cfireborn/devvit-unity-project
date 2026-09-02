using System.Collections.Generic;
using FishNet;
using FishNet.Managing.Timing;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

/// <summary>
/// Server/offline: activates horizontal lanes by player viewport, runs a fixed-spacing loop per lane,
/// drives pooled clouds via Rigidbody2D.MovePosition immediately before the active physics clock.
/// Clients receive NetworkObjects from FishNet.
/// </summary>
public class CloudManager : MonoBehaviour
{
    #region Serialized & constants

    [Header("References")]
    public CloudLadderController cloudLadderController;
    [Tooltip("When set, lanes and cloud extent are derived from this boundary. When null, defaults to 50 lanes centered at this transform.")]
    public BoundaryManager boundaryManager;
    [Tooltip("Cloud prefabs to spawn from. Each should have a CloudPlatform component.")]
    public GameObject[] cloudPrefabs;
    [Tooltip("All lane and density configuration.")]
    public CloudBehaviorSettings settings;

    const int FallbackLaneCount = 50;
    const float ExitBoundaryEpsilon = 0.05f;
    const float LifecycleUpdateInterval = 0.1f;

#if UNITY_EDITOR
    [Header("Editor")]
    [Tooltip("Horizontal half-width of lane lines drawn in Scene view (world units).")]
    [SerializeField] float _gizmoLaneHalfWidth = 50f;
    [Tooltip("Odd-index lanes: min main-bounds size, min spacing. Even-index lanes: max size, max spacing. Each marker draws primary + secondary wire box (min vs max bounds).")]
    [SerializeField] bool _gizmoShowCloudSizeAndSpacing;
#endif

    #endregion

    #region Callbacks & nested types

    internal System.Action<GameObject, float> _onCloudActivated;
    internal System.Action<GameObject> _onCloudDeactivated;
    internal System.Func<GameObject, Transform, GameObject> _acquireCloudInstance;

    struct PlayerViewRect
    {
        public float minX, maxX, minY, maxY;
    }

    class LaneState
    {
        public readonly int index;
        public readonly float worldY;
        public bool isActive;
        public GameObject prefab;
        public float speed;
        public float laneFixedYOffset;
        public float baseSpacing;
        public float laneScale;
        /// <summary>Normalized position along the lane loop in [0,1). 0 = loop start, advances by speed via CloudManager.</summary>
        public float loopPhase;
        public int slotCount;
        public float halfWidthCached;
        public float step;
        /// <summary>One entry per slot; null = empty. Index matches loop slot index.</summary>
        public readonly List<GameObject> clouds = new List<GameObject>();
        /// <summary>Stable randomized Y for an empty slot while it waits for a safe spawn opportunity.</summary>
        public readonly List<float> slotSpawnY = new List<float>();

        public LaneState(int index, float worldY)
        {
            this.index = index;
            this.worldY = worldY;
        }

        public void Reset()
        {
            isActive = false;
            prefab = null;
            speed = 0f;
            laneFixedYOffset = 0f;
            baseSpacing = 0f;
            laneScale = 0f;
            loopPhase = 0f;
            slotCount = 0;
            halfWidthCached = 0f;
            step = 0f;
            clouds.Clear();
            slotSpawnY.Clear();
        }
    }

    #endregion

    #region Fields

    LaneState[] _lanes;

    readonly List<Transform> _players = new List<Transform>();

    readonly Dictionary<GameObject, Queue<GameObject>> _poolByPrefab = new Dictionary<GameObject, Queue<GameObject>>();
    readonly HashSet<GameObject> _queuedInPool = new HashSet<GameObject>();
    readonly List<CloudNoSpawnZone> _noSpawnZones = new List<CloudNoSpawnZone>();
    readonly List<GameObject> _nonPooled = new List<GameObject>();
    readonly List<GameObject> _active = new List<GameObject>();

    readonly Dictionary<GameObject, Vector2> _prefabNativeMainSize = new Dictionary<GameObject, Vector2>();
    readonly Dictionary<GameObject, Vector2> _prefabNativeMainCenterOffset = new Dictionary<GameObject, Vector2>();
    readonly Dictionary<GameObject, Vector2> _prefabNativeVisualSize = new Dictionary<GameObject, Vector2>();

    /// <summary>Last Update: player view rects (camera + viewportMargin, clipped). Used for lane activation, viewport cull, and TrySpawnSlot gate.</summary>
    readonly List<PlayerViewRect> _viewportCullRects = new List<PlayerViewRect>();
    readonly List<(float left, float right)> _mergedViewportIntervals = new List<(float left, float right)>();

    Transform _poolParent;
    bool _cloudsFrozen;
    int _localPooledCloudCount;
    int _dynamicCloudCount;
    int _nextLocalPoolWarning = 50;
    TimeManager _subscribedTimeManager;
    float _nextLifecycleUpdateTime;
    bool _lifecycleRefreshRequested = true;
    bool _spawnPassRequested = true;

    #endregion

    #region Lifecycle

    void OnEnable()
    {
        _lifecycleRefreshRequested = true;
        _spawnPassRequested = true;
        SubscribeToNetworkPhysicsClock();
    }

    void OnDisable()
    {
        UnsubscribeFromNetworkPhysicsClock();
    }

    public void CollectSceneClouds()
    {
        CloudPlatform[] sceneClouds = Object.FindObjectsByType<CloudPlatform>(FindObjectsSortMode.None);
        foreach (CloudPlatform cloud in sceneClouds)
        {
            // A live runtime instance may still exist when network startup falls back to
            // offline mode. Keep anything already owned by a prefab pool out of the
            // scene-cloud collection so it can still return to that pool normally.
            if (cloud.pooledSourcePrefab != null) continue;

            if (!_nonPooled.Contains(cloud.gameObject))
                _nonPooled.Add(cloud.gameObject);

            if (cloud.wasActiveAtStart && !_active.Contains(cloud.gameObject))
                _active.Add(cloud.gameObject);
        }
    }

    void Start()
    {
        // OnEnable may run before FishNet's InstanceFinder is populated. Try again
        // after all scene Awake calls so networked clouds never fall back to Unity's
        // unrelated FixedUpdate clock.
        SubscribeToNetworkPhysicsClock();

        // NetworkCloudManager may collect in Awake before scene clouds have run Awake.
        // Repeat here after all Awake calls; collection is idempotent.
        CollectSceneClouds();
        ResolveBoundaryManager();

        _poolParent = new GameObject("CloudPool").transform;
        _poolParent.SetParent(transform);

        if (settings != null)
        {
            GetLaneCountAndBaseY(out int laneCount, out float baseY);
            _lanes = new LaneState[laneCount];
            for (int i = 0; i < laneCount; i++)
                _lanes[i] = new LaneState(i, baseY + i * settings.laneSpacing);
        }

        var gameServices = FindFirstObjectByType<GameServices>();
        if (gameServices != null && cloudLadderController != null)
            gameServices.RegisterCloudLadderController(cloudLadderController);

        TryRegisterPlayer();
        if (gameServices != null)
        {
            gameServices.onPlayerRegistered += TryRegisterPlayer;
            gameServices.onPlayerDeregistered += OnPlayerDeregisteredFromServices;
        }

        for (int i = 0; i < _active.Count; i++)
        {
            var cloud = _active[i];
            if (cloud == null || !ActivateNonPooledCloud(cloud))
            {
                _active.RemoveAt(i);
                i--;
                continue;
            }

            _onCloudActivated?.Invoke(cloud, cloud.transform.localScale.x);
        }

        var sceneZones = Object.FindObjectsByType<CloudNoSpawnZone>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int z = 0; z < sceneZones.Length; z++)
            RegisterNoSpawnZone(sceneZones[z]);
    }

    void ResolveBoundaryManager()
    {
        if (boundaryManager != null) return;

        boundaryManager = GetComponent<BoundaryManager>();
        if (boundaryManager != null) return;

        BoundaryManager[] candidates = Object.FindObjectsByType<BoundaryManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (candidates.Length == 1)
            boundaryManager = candidates[0];
        else if (candidates.Length > 1)
            Debug.LogError("CloudManager: boundaryManager is unassigned and the scene contains multiple BoundaryManagers.");
    }

    void Update()
    {
        if (settings == null || cloudPrefabs == null || cloudPrefabs.Length == 0) return;
        if (_lanes == null) return;
        if (!_lifecycleRefreshRequested && Time.unscaledTime < _nextLifecycleUpdateTime) return;

        _lifecycleRefreshRequested = false;
        _nextLifecycleUpdateTime = Time.unscaledTime + LifecycleUpdateInterval;

        BuildPlayerViewRects(_viewportCullRects);
        BuildMergedHorizontalViewportIntervals(_viewportCullRects, _mergedViewportIntervals);
        UpdateLaneActivation(_viewportCullRects);
        ViewportCullPooledClouds();
        // Empty slots only need visibility/no-spawn checks at lifecycle cadence.
        // Moving occupied clouds still advance on every physics tick below.
        _spawnPassRequested = true;
    }

    void FixedUpdate()
    {
        if (UsesNetworkPhysicsClock) return;
        AdvanceCloudPhysics(Time.fixedDeltaTime);
    }

    void OnNetworkPrePhysicsSimulation(float deltaTime)
    {
        if (!isActiveAndEnabled || _subscribedTimeManager == null) return;
        AdvanceCloudPhysics(deltaTime);
    }

    void AdvanceCloudPhysics(float dt)
    {
        if (settings == null || _lanes == null) return;
        GetLaneHorizontalSpan(out float left, out float right);
        bool attemptEmptySlotSpawns = _spawnPassRequested;
        _spawnPassRequested = false;

        foreach (var lane in _lanes)
        {
            if (!lane.isActive || lane.prefab == null || !LaneSlotLayoutValid(lane)) continue;

            float loopLen = lane.slotCount * lane.step;
            if (loopLen > 0f && !_cloudsFrozen)
            {
                float delta = lane.speed * dt / loopLen;
                lane.loopPhase = Mathf.Repeat(lane.loopPhase + delta, 1f);
            }

            for (int i = 0; i < lane.clouds.Count; i++)
            {
                GameObject cloud = lane.clouds[i];
                if (cloud == null)
                {
                    if (attemptEmptySlotSpawns)
                    {
                        float emptySlotTargetX = SlotCenterX(lane, left, i);
                        TrySpawnSlot(lane, left, right, i, emptySlotTargetX);
                    }
                    continue;
                }

                float targetX = SlotCenterX(lane, left, i);

                var platform = GetCloudPlatform(cloud);
                var rb = platform != null ? platform.GetComponent<Rigidbody2D>() : null;
                if (platform == null || rb == null)
                {
                    Debug.LogError($"CloudManager: pooled cloud '{cloud.name}' is missing CloudPlatform or Rigidbody2D; removing its stuck slot.");
                    ReturnCloudToPool(cloud);
                    continue;
                }

                if (platform.IsDespawning || platform.IsBoundaryStopped)
                    continue;

                float scaleX = cloud.transform.localScale.x;
                Bounds mainAtCurrent = PrefabMainBoundsWorld(rb.position.x, platform.pooledWorldY, lane.prefab, scaleX);
                Bounds mainAtTarget = PrefabMainBoundsWorld(targetX, platform.pooledWorldY, lane.prefab, scaleX);
                Bounds sweptMainBounds = mainAtCurrent;
                sweptMainBounds.Encapsulate(mainAtTarget.min);
                sweptMainBounds.Encapsulate(mainAtTarget.max);

                if (ShouldBlockEntryMovement(mainAtCurrent, sweptMainBounds))
                {
                    platform.TriggerBlockEntryFromBoundary();
                    continue;
                }

                // Only despawn pooled lane clouds when they reach the travel-direction exit boundary (see ShouldExitDespawnForTarget).
                // Every slot in a lane uses laneScale, so this is the same value as
                // recomputing the prefab's rendered/main width for every live cloud.
                float cloudHalfW = lane.halfWidthCached;
                bool crossedLoopSeam = lane.speed >= 0f
                    ? targetX < rb.position.x - ExitBoundaryEpsilon
                    : targetX > rb.position.x + ExitBoundaryEpsilon;
                if (crossedLoopSeam || ShouldExitDespawnForTarget(lane, left, right, targetX, cloudHalfW))
                {
                    platform.TriggerBlockEntryFromBoundary();
                    continue;
                }

                rb.MovePosition(new Vector2(targetX, platform.pooledWorldY));
            }
        }
    }

    void OnDestroy()
    {
        UnsubscribeFromNetworkPhysicsClock();
        var gameServices = FindFirstObjectByType<GameServices>();
        if (gameServices != null)
        {
            gameServices.onPlayerRegistered -= TryRegisterPlayer;
            gameServices.onPlayerDeregistered -= OnPlayerDeregisteredFromServices;
        }
    }

    bool UsesNetworkPhysicsClock => _subscribedTimeManager != null;
    internal bool UsesNetworkPhysicsClockForTests => UsesNetworkPhysicsClock;

    void SubscribeToNetworkPhysicsClock()
    {
        if (_subscribedTimeManager != null) return;

        TimeManager timeManager = InstanceFinder.TimeManager;
        if (timeManager == null || timeManager.PhysicsMode != PhysicsMode.TimeManager) return;

        _subscribedTimeManager = timeManager;
        _subscribedTimeManager.OnPrePhysicsSimulation += OnNetworkPrePhysicsSimulation;
    }

    void UnsubscribeFromNetworkPhysicsClock()
    {
        if (_subscribedTimeManager == null) return;
        _subscribedTimeManager.OnPrePhysicsSimulation -= OnNetworkPrePhysicsSimulation;
        _subscribedTimeManager = null;
    }

    #endregion

    #region GameServices & players

    void OnPlayerDeregisteredFromServices(PlayerControllerM player)
    {
        if (player != null)
            UnregisterPlayer(player.transform);
    }

    void TryRegisterPlayer()
    {
        var gameServices = FindFirstObjectByType<GameServices>();
        if (gameServices == null) return;
        var p = gameServices.GetPlayer();
        if (p != null)
            RegisterPlayer(p.transform);
    }

    public void RegisterPlayer(Transform playerTransform)
    {
        if (playerTransform == null || _players.Contains(playerTransform)) return;
        _players.Add(playerTransform);
        _lifecycleRefreshRequested = true;
    }

    public void UnregisterPlayer(Transform playerTransform)
    {
        if (playerTransform == null) return;
        if (_players.Remove(playerTransform))
            _lifecycleRefreshRequested = true;
    }

    public int RegisteredPlayerCount
    {
        get
        {
            PruneDestroyedPlayers();
            return _players.Count;
        }
    }

    public int ActiveLaneCount
    {
        get
        {
            if (_lanes == null) return 0;
            int count = 0;
            for (int i = 0; i < _lanes.Length; i++)
                if (_lanes[i].isActive) count++;
            return count;
        }
    }

    /// <summary>Called by GameManager when the scene or player context changes.</summary>
    public void RequestViewportFill()
    {
        _lifecycleRefreshRequested = true;
        _spawnPassRequested = true;
    }

    /// <summary>Removes destroyed player transforms (e.g. if an object was destroyed without going through UnregisterPlayer).</summary>
    void PruneDestroyedPlayers()
    {
        for (int i = _players.Count - 1; i >= 0; i--)
        {
            if (_players[i] == null)
                _players.RemoveAt(i);
        }
    }

    void BuildPlayerViewRects(List<PlayerViewRect> dst)
    {
        dst.Clear();
        PruneDestroyedPlayers();

        for (int i = 0; i < _players.Count; i++)
        {
            Transform t = _players[i];
            if (t == null) continue;
            GetPlayerViewRect(t, out PlayerViewRect r);
            ClipRectToExtendedBounds(ref r);
            if (r.minX < r.maxX && r.minY < r.maxY)
                dst.Add(r);
        }
    }

    static void BuildMergedHorizontalViewportIntervals(
        List<PlayerViewRect> rects,
        List<(float left, float right)> dst)
    {
        dst.Clear();
        for (int i = 0; i < rects.Count; i++)
            dst.Add((rects[i].minX, rects[i].maxX));
        dst.Sort((a, b) => a.left.CompareTo(b.left));

        for (int i = 1; i < dst.Count;)
        {
            var previous = dst[i - 1];
            var current = dst[i];
            if (current.left <= previous.right)
            {
                dst[i - 1] = (previous.left, Mathf.Max(previous.right, current.right));
                dst.RemoveAt(i);
            }
            else
                i++;
        }
    }

    void GetPlayerViewRect(Transform t, out PlayerViewRect r)
    {
        Vector2 c = t.position;
        GetHalfExtentsForPlayer(t, out float hw, out float hh);
        float m = settings.viewportMargin;
        r.minX = c.x - hw - m;
        r.maxX = c.x + hw + m;
        r.minY = c.y - hh - m;
        r.maxY = c.y + hh + m;
    }

    void GetHalfExtentsForPlayer(Transform t, out float halfWidth, out float halfHeight)
    {
        var npc = t.GetComponent<NetworkPlayerController>();
        if (npc != null)
        {
            npc.GetWorldCameraHalfExtents(out halfWidth, out halfHeight);
            return;
        }
        var cam = Camera.main;
        if (cam != null && cam.orthographic)
        {
            halfHeight = cam.orthographicSize;
            halfWidth = halfHeight * cam.aspect;
            return;
        }
        halfWidth = settings.fallbackViewportHalfWidth;
        halfHeight = settings.fallbackViewportHalfHeight;
    }

    void ClipRectToExtendedBounds(ref PlayerViewRect r)
    {
        if (boundaryManager == null) return;
        Bounds b = boundaryManager.GetExtendedBounds();
        r.minX = Mathf.Max(r.minX, b.min.x);
        r.maxX = Mathf.Min(r.maxX, b.max.x);
        r.minY = Mathf.Max(r.minY, b.min.y);
        r.maxY = Mathf.Min(r.maxY, b.max.y);
    }

    #endregion

    #region Viewport & lane activation

    void UpdateLaneActivation(List<PlayerViewRect> playerRects)
    {
        foreach (var lane in _lanes)
        {
            float ly = LaneYForActivation(lane);
            bool shouldBeActive = false;
            foreach (var pr in playerRects)
            {
                if (ly >= pr.minY && ly <= pr.maxY)
                {
                    shouldBeActive = true;
                    break;
                }
            }
            if (shouldBeActive && !lane.isActive)
                ActivateLane(lane);
            else if (!shouldBeActive && lane.isActive)
                DeactivateLane(lane);
        }
    }

    static bool BoundsIntersectsPlayerRect(Bounds b, PlayerViewRect r)
    {
        return b.max.x >= r.minX && b.min.x <= r.maxX && b.max.y >= r.minY && b.min.y <= r.maxY;
    }

    bool MainBoundsVisibleToAnyPlayer(Bounds mainBounds)
    {
        int n = _viewportCullRects.Count;
        if (n == 0) return false;
        for (int i = 0; i < n; i++)
        {
            if (BoundsIntersectsPlayerRect(mainBounds, _viewportCullRects[i]))
                return true;
        }
        return false;
    }

    void ViewportCullPooledClouds()
    {
        if (_lanes == null) return;
        foreach (var lane in _lanes)
        {
            if (!lane.isActive || lane.prefab == null || !LaneSlotLayoutValid(lane)) continue;

            for (int i = 0; i < lane.clouds.Count; i++)
            {
                GameObject cloud = lane.clouds[i];
                if (cloud == null) continue;
                if (_nonPooled.Contains(cloud)) continue;

                var platform = GetCloudPlatform(cloud);
                if (platform == null || !platform.isPooled || platform.IsDespawning) continue;

                var rb = platform.GetComponent<Rigidbody2D>();
                if (rb == null) continue;

                float scaleX = cloud.transform.localScale.x;
                Bounds mainBounds = PrefabMainBoundsWorld(rb.position.x, platform.pooledWorldY, lane.prefab, scaleX);
                if (!MainBoundsVisibleToAnyPlayer(mainBounds))
                {
                    bool keepForRider = platform.IsPlayerOnCloud ||
                        (cloudLadderController != null && cloudLadderController.IsPlayerOnAnyLadderPartner(cloud));
                    bool keepForVisibleLadder = cloudLadderController != null &&
                        cloudLadderController.ShouldKeepCloudActiveForLadders(cloud, _mergedViewportIntervals);
                    if (!keepForRider && !keepForVisibleLadder)
                        ReturnCloudToPool(cloud);
                }
            }
        }
    }

    static float LaneYForActivation(LaneState lane)
    {
        return lane.isActive ? lane.worldY + lane.laneFixedYOffset : lane.worldY;
    }

    void GetLaneCountAndBaseY(out int laneCount, out float baseY)
    {
        if (boundaryManager != null)
        {
            Bounds extended = boundaryManager.GetExtendedBounds();
            laneCount = Mathf.Max(1, Mathf.CeilToInt(extended.size.y / settings.laneSpacing));
            baseY = extended.min.y + settings.laneYOffset;
        }
        else
        {
            laneCount = FallbackLaneCount;
            float centerY = transform.position.y;
            baseY = centerY - (laneCount - 1) * 0.5f * settings.laneSpacing + settings.laneYOffset;
        }
    }

    #endregion

    #region Boundary & horizontal span

    void GetLaneHorizontalSpan(out float left, out float right)
    {
        if (boundaryManager != null)
        {
            Bounds e = boundaryManager.GetExtendedBounds();
            left = e.min.x;
            right = e.max.x;
        }
        else
        {
            float cx = transform.position.x;
            float half = settings.fallbackViewportHalfWidth;
            left = cx - half;
            right = cx + half;
        }
    }

    #endregion

    #region Lane loop: phase, slots, movement

    static bool LaneSlotLayoutValid(LaneState lane)
    {
        return lane.slotCount > 0 && lane.clouds.Count == lane.slotCount;
    }

    static CloudPlatform GetCloudPlatform(GameObject cloud) =>
        cloud != null ? cloud.GetComponentInChildren<CloudPlatform>(true) : null;

    void ActivateLane(LaneState lane)
    {
        lane.isActive = true;
        lane.prefab = cloudPrefabs[Random.Range(0, cloudPrefabs.Length)];
        float magnitude = Random.Range(settings.speedRange.x, settings.speedRange.y);
        lane.speed = Random.value < 0.5f ? magnitude : -magnitude;
        lane.laneFixedYOffset = settings.laneHeightVariation <= 0f
            ? 0f
            : Random.Range(-settings.laneHeightVariation, settings.laneHeightVariation);
        lane.baseSpacing = Random.Range(settings.minCloudSpacing, settings.maxCloudSpacing);
        ComputeScaleBoundsForPrefab(lane.prefab, out float sMin, out float sMax);
        if (sMin > sMax) lane.laneScale = sMin;
        else lane.laneScale = Random.Range(sMin, sMax);

        Vector2 nat = GetPrefabNativeMainSize(lane.prefab);
        lane.halfWidthCached = Mathf.Max(nat.x, GetPrefabNativeVisualWidth(lane.prefab)) * lane.laneScale * 0.5f;
        lane.step = 2f * lane.halfWidthCached + lane.baseSpacing;

        GetLaneHorizontalSpan(out float left, out float right);
        float usable = right - left - 2f * lane.halfWidthCached;
        if (usable < 0f) usable = 0f;

        lane.slotCount = usable <= 0f ? 1 : Mathf.Max(1, Mathf.FloorToInt(usable / lane.step) + 1);
        while (lane.slotCount > 1 && (lane.slotCount - 1) * lane.step > usable + 0.0001f)
            lane.slotCount--;

        lane.loopPhase = Random.Range(0f, 1f);
        lane.clouds.Clear();
        lane.slotSpawnY.Clear();
        for (int i = 0; i < lane.slotCount; i++)
        {
            lane.clouds.Add(null);
            lane.slotSpawnY.Add(float.NaN);
        }

        for (int i = 0; i < lane.slotCount; i++)
            TrySpawnSlot(lane, left, right, i, SlotCenterX(lane, left, i));
    }

    void DeactivateLane(LaneState lane)
    {
        bool noPlayersConnected = _players.Count == 0;
        for (int i = 0; i < lane.clouds.Count; i++)
        {
            var cloud = lane.clouds[i];
            if (cloud != null)
            {
                var platform = GetCloudPlatform(cloud);
                if (platform != null && !noPlayersConnected)
                    platform.BeginDespawnAnimation();
                else
                    ReturnCloudToPool(cloud);
            }
        }
        lane.Reset();
    }

    float SlotCenterX(LaneState lane, float left, int slotIndex)
    {
        float loopLen = lane.slotCount * lane.step;
        if (loopLen <= 0f)
            return left + lane.halfWidthCached;
        float distanceAlongLoop = lane.loopPhase * loopLen;
        float raw = distanceAlongLoop + slotIndex * lane.step;
        float wrapped = Mathf.Repeat(raw, loopLen);
        return left + lane.halfWidthCached + wrapped;
    }

    bool ShouldExitDespawnForTarget(LaneState lane, float left, float right, float targetCenterX, float halfWidth)
    {
        if (lane.speed >= 0f)
            return targetCenterX + halfWidth >= right - ExitBoundaryEpsilon;
        return targetCenterX - halfWidth <= left + ExitBoundaryEpsilon;
    }

    bool SlotIsSafeForNewSpawn(LaneState lane, float left, float right, float targetCenterX, float hw)
    {
        if (lane.speed >= 0f)
            return targetCenterX + hw < right - ExitBoundaryEpsilon;
        return targetCenterX - hw > left + ExitBoundaryEpsilon;
    }

    float EffectiveLaneSpawnY(LaneState lane)
    {
        return lane.worldY + lane.laneFixedYOffset;
    }

    float SampleSpawnY(LaneState lane, bool applyCloudHeightJitter)
    {
        float y = EffectiveLaneSpawnY(lane);
        if (applyCloudHeightJitter && settings.cloudHeightVariation > 0f)
            y += Random.Range(-settings.cloudHeightVariation, settings.cloudHeightVariation);
        return y;
    }

    float GetOrCreateSlotSpawnY(LaneState lane, int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= lane.slotSpawnY.Count)
            return SampleSpawnY(lane, true);
        if (float.IsNaN(lane.slotSpawnY[slotIndex]))
            lane.slotSpawnY[slotIndex] = SampleSpawnY(lane, true);
        return lane.slotSpawnY[slotIndex];
    }

    bool TryGetSpawnScale(LaneState lane, out float scale)
    {
        ComputeScaleBoundsForPrefab(lane.prefab, out float sMin, out float sMax);
        if (sMin > sMax)
        {
            scale = 0f;
            return false;
        }
        scale = Mathf.Clamp(lane.laneScale, sMin, sMax);
        return true;
    }

    void TrySpawnSlot(LaneState lane, float left, float right, int slotIndex, float targetX)
    {
        if (slotIndex < 0 || slotIndex >= lane.clouds.Count) return;
        if (lane.clouds[slotIndex] != null) return;
        if (!TryGetSpawnScale(lane, out float scale)) return;

        Vector2 nat = GetPrefabNativeMainSize(lane.prefab);
        float hw = Mathf.Max(nat.x, GetPrefabNativeVisualWidth(lane.prefab)) * scale * 0.5f;
        if (!SlotIsSafeForNewSpawn(lane, left, right, targetX, hw)) return;

        float spawnY = GetOrCreateSlotSpawnY(lane, slotIndex);
        Bounds spawnBounds = PrefabMainBoundsWorld(targetX, spawnY, lane.prefab, scale);
        if (IntersectsAnyBlockSpawn(spawnBounds))
            return;
        if (!MainBoundsVisibleToAnyPlayer(spawnBounds))
            return;
        // Only visible candidates reach this point; the global cap still bounds total lane-managed clouds.
        if (settings.maxDynamicClouds > 0 && DynamicCloudCount >= settings.maxDynamicClouds) return;

        AcquireCloudFromPool(lane, scale, out GameObject cloud, out CloudPlatform platform);
        platform.pooledWorldY = spawnY;
        platform.slotIndex = slotIndex;
        Vector2 spawnPosition = new Vector2(targetX, spawnY);
        cloud.transform.position = new Vector3(spawnPosition.x, spawnPosition.y, 0f);
        Rigidbody2D rb = platform.GetComponent<Rigidbody2D>();
        if (rb != null)
            rb.position = spawnPosition;
        _onCloudActivated?.Invoke(cloud, scale);
        _active.Add(cloud);
        _dynamicCloudCount++;
        lane.clouds[slotIndex] = cloud;
    }

    int DynamicCloudCount => _dynamicCloudCount;

    #endregion

    #region Prefab sizing

    Vector2 GetPrefabNativeMainSize(GameObject prefab)
    {
        if (_prefabNativeMainSize.TryGetValue(prefab, out Vector2 sz)) return sz;
        var platform = prefab.GetComponent<CloudPlatform>();
        var mainBox = platform != null ? platform.mainCollider as BoxCollider2D : null;
        BoxCollider2D[] boxes = mainBox != null
            ? new[] { mainBox }
            : prefab.GetComponentsInChildren<BoxCollider2D>(true);

        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < boxes.Length; i++)
        {
            BoxCollider2D box = boxes[i];
            if (box == null || !box.enabled) continue;
            Vector2 center = prefab.transform.InverseTransformPoint(box.transform.TransformPoint(box.offset));
            Vector2 right = prefab.transform.InverseTransformVector(box.transform.TransformVector(Vector2.right * box.size.x * 0.5f));
            Vector2 up = prefab.transform.InverseTransformVector(box.transform.TransformVector(Vector2.up * box.size.y * 0.5f));
            Vector2 extents = new Vector2(Mathf.Abs(right.x) + Mathf.Abs(up.x), Mathf.Abs(right.y) + Mathf.Abs(up.y));
            min = Vector2.Min(min, center - extents);
            max = Vector2.Max(max, center + extents);
        }

        if (float.IsInfinity(min.x))
        {
            Debug.LogError($"CloudManager: cloud prefab '{prefab.name}' needs an enabled BoxCollider2D for lane sizing.");
            min = Vector2.one * -0.5f;
            max = Vector2.one * 0.5f;
        }

        Vector2 centerOffset = (min + max) * 0.5f;
        sz = new Vector2(Mathf.Max(0.0001f, max.x - min.x), Mathf.Max(0.0001f, max.y - min.y));
        _prefabNativeMainSize[prefab] = sz;
        _prefabNativeMainCenterOffset[prefab] = centerOffset;
        return sz;
    }

    Vector2 GetPrefabNativeVisualSize(GameObject prefab)
    {
        if (_prefabNativeVisualSize.TryGetValue(prefab, out Vector2 size)) return size;

        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        SpriteRenderer[] renderers = prefab.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled || renderer.sprite == null) continue;
            Bounds rendererBounds = renderer.localBounds;
            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 local = new Vector3(
                    (corner & 1) == 0 ? rendererBounds.min.x : rendererBounds.max.x,
                    (corner & 2) == 0 ? rendererBounds.min.y : rendererBounds.max.y,
                    0f);
                Vector2 rootPoint = prefab.transform.InverseTransformPoint(renderer.transform.TransformPoint(local));
                min = Vector2.Min(min, rootPoint);
                max = Vector2.Max(max, rootPoint);
            }
        }

        size = float.IsInfinity(min.x)
            ? GetPrefabNativeMainSize(prefab)
            : new Vector2(Mathf.Max(0.0001f, max.x - min.x), Mathf.Max(0.0001f, max.y - min.y));
        _prefabNativeVisualSize[prefab] = size;
        return size;
    }

    float GetPrefabNativeVisualWidth(GameObject prefab) => GetPrefabNativeVisualSize(prefab).x;

    void ComputeScaleBoundsForPrefab(GameObject prefab, out float sMin, out float sMax)
    {
        Vector2 native = GetPrefabNativeVisualSize(prefab);
        sMin = Mathf.Max(settings.minCloudMainBoundsWidth / native.x, settings.minCloudMainBoundsHeight / native.y);
        sMax = Mathf.Min(settings.maxCloudMainBoundsWidth / native.x, settings.maxCloudMainBoundsHeight / native.y);
    }

    Bounds PrefabMainBoundsWorld(float transformX, float transformY, GameObject prefab, float uniformScale)
    {
        Vector2 nativeSize = GetPrefabNativeMainSize(prefab);
        _prefabNativeMainCenterOffset.TryGetValue(prefab, out Vector2 centerOffset);
        Vector3 size = new Vector3(nativeSize.x * uniformScale, nativeSize.y * uniformScale, 0f);
        Vector3 center = new Vector3(
            transformX + centerOffset.x * uniformScale,
            transformY + centerOffset.y * uniformScale,
            0f);
        return new Bounds(center, size);
    }

    bool IntersectsAnyBlockSpawn(Bounds cloudMainBounds)
    {
        int n = _noSpawnZones.Count;
        if (n == 0) return false;
        for (int i = 0; i < n; i++)
        {
            CloudNoSpawnZone z = _noSpawnZones[i];
            if (z == null) continue;
            if (!z.blockSpawn) continue;
            if (!z.TryGetWorldBounds(out Bounds zb)) continue;
            if (BoundsOverlap2D(cloudMainBounds, zb)) return true;
        }
        return false;
    }

    bool ShouldBlockEntryMovement(Bounds currentBounds, Bounds sweptBounds)
    {
        int n = _noSpawnZones.Count;
        for (int i = 0; i < n; i++)
        {
            CloudNoSpawnZone z = _noSpawnZones[i];
            if (z == null || !z.blockEntry) continue;
            if (!z.TryGetWorldBounds(out Bounds zb)) continue;
            if (!BoundsOverlap2D(sweptBounds, zb)) continue;
            if (z.blockSpawn || !BoundsOverlap2D(currentBounds, zb)) return true;
        }
        return false;
    }

    static bool BoundsOverlap2D(Bounds a, Bounds b) =>
        a.min.x <= b.max.x && a.max.x >= b.min.x &&
        a.min.y <= b.max.y && a.max.y >= b.min.y;

    #endregion

    #region Pooling & cloud lifecycle

    void AcquireCloudFromPool(LaneState lane, float scale, out GameObject cloud, out CloudPlatform platform)
    {
        GameObject prefab = lane.prefab;
        cloud = null;

        // NetworkCloudManager supplies FishNet's server-side retrieval path. Offline
        // mode leaves this null and continues using the local per-prefab pool.
        if (prefab != null && _acquireCloudInstance != null)
            cloud = _acquireCloudInstance(prefab, _poolParent);
        else if (prefab != null)
            TryDequeueFromPrefabPool(prefab, out cloud);

        if (cloud == null)
            cloud = Instantiate(prefab, _poolParent);

        cloud.transform.localScale = new Vector3(scale, scale, scale);
        platform = GetCloudPlatform(cloud);
        if (platform == null) platform = cloud.AddComponent<CloudPlatform>();
        platform.pooledSourcePrefab = prefab;
        platform.SetCloudManager(this);
        platform.SetMovementSpeed(lane.speed);
        platform.laneIndex = lane.index;
        platform.isPooled = true;
        platform.isMoving = false;
        platform.ignoreNoSpawnZones = true;
        if (!cloud.activeSelf)
            cloud.SetActive(true);
    }

    bool TryDequeueFromPrefabPool(GameObject prefab, out GameObject cloud)
    {
        cloud = null;
        if (prefab == null || !_poolByPrefab.TryGetValue(prefab, out Queue<GameObject> q) || q.Count == 0)
            return false;
        while (q.Count > 0)
        {
            cloud = q.Dequeue();
            _queuedInPool.Remove(cloud);
            _localPooledCloudCount = Mathf.Max(0, _localPooledCloudCount - 1);
            if (cloud != null)
                return true;
        }
        return false;
    }

    void EnqueueToPrefabPool(GameObject cloud, GameObject prefabKey)
    {
        if (cloud == null || prefabKey == null) return;
        if (!_poolByPrefab.TryGetValue(prefabKey, out Queue<GameObject> q))
        {
            q = new Queue<GameObject>();
            _poolByPrefab[prefabKey] = q;
        }
        q.Enqueue(cloud);
        _queuedInPool.Add(cloud);
        _localPooledCloudCount++;
        while (_localPooledCloudCount >= _nextLocalPoolWarning)
        {
            Debug.LogWarning($"[Info] Cloud object pool reached {_nextLocalPoolWarning} retained clouds.");
            _nextLocalPoolWarning += 50;
        }
    }

    /// <summary>Clears this instance from any lane slot list (fallback when laneIndex/slotIndex are missing).</summary>
    void RemoveCloudFromLaneSlots(GameObject cloud)
    {
        if (_lanes == null || cloud == null) return;
        foreach (var lane in _lanes)
        {
            for (int i = 0; i < lane.clouds.Count; i++)
            {
                if (lane.clouds[i] == cloud)
                {
                    lane.clouds[i] = null;
                    return;
                }
            }
        }
    }

    public bool ActivateNonPooledCloud(GameObject cloud)
    {
        if (cloud == null) { print("CloudManager: cloud is null"); return false; }
        if (!OptionalGameplayFeatures.DeliveryAndGoalSystemEnabled &&
            cloud.GetComponent<DeliveryCloudPlatform>() != null)
        {
            cloud.SetActive(false);
            return false;
        }
        if (_queuedInPool.Contains(cloud)) { print("CloudManager: cloud is in pool"); return false; }

        if (!_nonPooled.Contains(cloud))
            _nonPooled.Add(cloud);

        var platform = GetCloudPlatform(cloud);
        if (platform != null)
            platform.SetCloudManager(this);

        cloud.SetActive(true);
        if (!_active.Contains(cloud))
        {
            _active.Add(cloud);
            _onCloudActivated?.Invoke(cloud, cloud.transform.localScale.x);
        }

        return true;
    }

    public void DeactivateCloud(GameObject cloud)
    {
        if (cloud == null) return;
        var platform = GetCloudPlatform(cloud);
        if (platform != null && platform.isPersistent) return;

        if (_nonPooled.Contains(cloud))
        {
            _active.Remove(cloud);
            if (_onCloudDeactivated != null)
                _onCloudDeactivated(cloud);
            else
                cloud.SetActive(false);
            return;
        }

        ReturnCloudToPool(cloud);
    }

    public void ReturnCloudToPool(GameObject cloud)
    {
        if (cloud == null || _nonPooled.Contains(cloud) || _queuedInPool.Contains(cloud)) return;

        if (_active.Remove(cloud))
            _dynamicCloudCount = Mathf.Max(0, _dynamicCloudCount - 1);

        // Schedule one replacement attempt. The slot will not be retried on every
        // physics tick if its wrapped position is still outside all viewports.
        _spawnPassRequested = true;

        var platform = GetCloudPlatform(cloud);
        if (_lanes != null && platform != null && platform.laneIndex >= 0 && platform.laneIndex < _lanes.Length)
        {
            LaneState lane = _lanes[platform.laneIndex];
            if (platform.slotIndex >= 0 && platform.slotIndex < lane.clouds.Count &&
                lane.clouds[platform.slotIndex] == cloud)
            {
                lane.clouds[platform.slotIndex] = null;
                if (platform.slotIndex < lane.slotSpawnY.Count)
                    lane.slotSpawnY[platform.slotIndex] = float.NaN;
            }
            else
                RemoveCloudFromLaneSlots(cloud);
        }
        else
            RemoveCloudFromLaneSlots(cloud);

        if (_onCloudDeactivated != null)
        {
            _onCloudDeactivated(cloud);
            return;
        }

        if (platform != null)
        {
            platform.slotIndex = -1;
            platform.isMoving = false;
        }

        cloud.SetActive(false);
        cloud.transform.SetParent(_poolParent);
        GameObject prefabKey = platform != null ? platform.pooledSourcePrefab : null;
        if (prefabKey == null)
            Object.Destroy(cloud);
        else
            EnqueueToPrefabPool(cloud, prefabKey);
    }

    #endregion

    #region Public API

    /// <summary>True while cloud movement is paused via ToggleCloudFreeze().</summary>
    public bool CloudsFrozen => _cloudsFrozen;

    /// <summary>Pause or resume all cloud movement. Pooled clouds stop advancing their loop phase;
    /// non-pooled scene clouds have their isMoving flag cleared/restored.</summary>
    public void ToggleCloudFreeze()
    {
        _cloudsFrozen = !_cloudsFrozen;
        foreach (var go in _nonPooled)
        {
            if (go == null) continue;
            var platform = GetCloudPlatform(go);
            if (platform == null) continue;
            if (_cloudsFrozen)
                platform.isMoving = false;
            else if (!platform.isPersistent && !platform.IsBoundaryStopped && !platform.IsDespawning)
                platform.isMoving = true;
        }
    }

    /// <summary>Flip the travel direction of every active lane. Non-pooled scene clouds are reversed too.</summary>
    public void ReverseAllLaneSpeeds()
    {
        if (_lanes != null)
            foreach (var lane in _lanes)
                if (lane.isActive) lane.speed = -lane.speed;

        foreach (var go in _nonPooled)
        {
            if (go == null) continue;
            var platform = GetCloudPlatform(go);
            if (platform != null)
                platform.SetMovementSpeed(-platform.moveSpeed);
        }
    }

    public void RegisterNoSpawnZone(CloudNoSpawnZone zone)
    {
        if (zone != null && !_noSpawnZones.Contains(zone))
        {
            _noSpawnZones.Add(zone);
            _lifecycleRefreshRequested = true;
        }
    }

    public void UnregisterNoSpawnZone(CloudNoSpawnZone zone)
    {
        if (zone != null)
        {
            if (_noSpawnZones.Remove(zone))
                _lifecycleRefreshRequested = true;
        }
    }

    public void RegisterBlockSpawnZone(CloudNoSpawnZone zone) => RegisterNoSpawnZone(zone);

    public IReadOnlyList<GameObject> GetActiveClouds() => _active;

    /// <summary>True for a lane-managed cloud rather than a pre-placed scene cloud.</summary>
    public bool IsDynamicCloud(GameObject cloud) => cloud != null && !_nonPooled.Contains(cloud);

    /// <summary>Lane baselines match internal pooled lane layout (extended boundary or fallback).</summary>
    public bool TryGetLaneLayout(out float baseY, out int laneCount, out float laneSpacing)
    {
        baseY = 0f;
        laneCount = 0;
        laneSpacing = 0f;
        if (settings == null) return false;
        laneSpacing = settings.laneSpacing;
        GetLaneCountAndBaseY(out laneCount, out baseY);
        return true;
    }

    /// <summary>Axis-aligned bounds covering lane baselines and horizontal spawn span.</summary>
    public bool TryGetSpawnBounds(out Bounds bounds)
    {
        bounds = default;
        if (settings == null) return false;
        GetLaneCountAndBaseY(out int laneCount, out float baseY);
        GetLaneHorizontalSpan(out float left, out float right);
        float minY = baseY;
        float maxY = baseY + (laneCount - 1) * settings.laneSpacing;
        float height = Mathf.Max(maxY - minY, 0.01f);
        float width = Mathf.Max(right - left, 0.01f);
        Vector3 center = new Vector3((left + right) * 0.5f, (minY + maxY) * 0.5f, 0f);
        bounds = new Bounds(center, new Vector3(width, height, 0f));
        return true;
    }

    /// <summary>True if cloud main bounds overlap a blockSpawn <see cref="CloudNoSpawnZone"/>.</summary>
    public bool IsMainBoundsBlockedByNoSpawnZones(Bounds cloudMainBounds) =>
        IntersectsAnyBlockSpawn(cloudMainBounds);

    /// <summary>
    /// Samples a world position for a stationary delivery cloud: same horizontal span, lane baseline + height jitter,
    /// and block-spawn zone rules as pooled clouds. Does not apply viewport visibility or lane slot exit checks.
    /// </summary>
    public bool TryPickDeliverySpawnWorldPosition(
        GameObject prefab,
        float lanesBaseY,
        int laneCount,
        float laneSpacing,
        IReadOnlyList<int> restrictToLaneIndicesOrNull,
        Collider2D vicinityOrNull,
        Vector2 playerPosition,
        float minDistanceFromPlayerWhenNoVicinity,
        int maxAttempts,
        out int chosenLaneIndex,
        out Vector3 worldPosition)
    {
        chosenLaneIndex = -1;
        worldPosition = default;
        if (prefab == null || settings == null || laneCount <= 0) return false;

        Vector2 native = GetPrefabNativeMainSize(prefab);
        float scale = Mathf.Abs(prefab.transform.localScale.x);
        float halfW = native.x * scale * 0.5f;

        GetLaneHorizontalSpan(out float left, out float right);

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            int laneIdx;
            if (restrictToLaneIndicesOrNull != null && restrictToLaneIndicesOrNull.Count > 0)
                laneIdx = restrictToLaneIndicesOrNull[Random.Range(0, restrictToLaneIndicesOrNull.Count)];
            else
                laneIdx = Random.Range(0, laneCount);

            float laneBaseline = lanesBaseY + laneIdx * laneSpacing;
            float y = laneBaseline;
            if (settings.cloudHeightVariation > 0f)
                y += Random.Range(-settings.cloudHeightVariation, settings.cloudHeightVariation);

            float xmin = left + halfW;
            float xmax = right - halfW;
            if (vicinityOrNull != null)
            {
                Bounds vb = vicinityOrNull.bounds;
                xmin = Mathf.Max(xmin, vb.min.x + halfW);
                xmax = Mathf.Min(xmax, vb.max.x - halfW);
            }

            if (xmin > xmax) continue;

            float x = Random.Range(xmin, xmax);

            if (vicinityOrNull == null)
            {
                if (Vector2.Distance(new Vector2(x, y), playerPosition) < minDistanceFromPlayerWhenNoVicinity)
                    continue;
            }

            Bounds mainB = PrefabMainBoundsWorld(x, y, prefab, scale);
            if (IntersectsAnyBlockSpawn(mainB))
                continue;

            chosenLaneIndex = laneIdx;
            worldPosition = new Vector3(x, y, 0f);
            return true;
        }

        return false;
    }

    /// <summary>Native main-collider size for a prefab at scale 1 (same cache as pooled clouds).</summary>
    public Vector2 GetPrefabNativeMainSizePublic(GameObject prefab) => GetPrefabNativeMainSize(prefab);

    /// <summary>Native rendered size for a prefab at scale 1.</summary>
    public Vector2 GetPrefabNativeVisualSizePublic(GameObject prefab) => GetPrefabNativeVisualSize(prefab);

    /// <summary>Valid uniform scale interval derived from configured rendered cloud dimensions.</summary>
    public bool TryGetPrefabScaleRange(GameObject prefab, out float minScale, out float maxScale)
    {
        minScale = 0f;
        maxScale = 0f;
        if (prefab == null || settings == null) return false;
        ComputeScaleBoundsForPrefab(prefab, out minScale, out maxScale);
        return minScale > 0f && minScale <= maxScale && !float.IsNaN(minScale) && !float.IsNaN(maxScale);
    }

    /// <summary>Horizontal travel speed for a lane while it is active; false if lane index is invalid or inactive.</summary>
    public bool TryGetActiveLaneSpeed(int laneIndex, out float speed)
    {
        speed = 0f;
        if (_lanes == null || laneIndex < 0 || laneIndex >= _lanes.Length) return false;
        LaneState lane = _lanes[laneIndex];
        if (!lane.isActive) return false;
        speed = lane.speed;
        return true;
    }

    /// <summary>
    /// Moves a non-pooled cloud under lane loop control (same as pooled clouds). Removes it from the non-pooled set.
    /// <paramref name="poolKeyPrefab"/> is used as <see cref="CloudPlatform.pooledSourcePrefab"/> if the cloud later despawns into the pool.
    /// </summary>
    public bool TryAdoptNonPooledCloudIntoLane(GameObject cloud, int laneIndex, GameObject poolKeyPrefab)
    {
        if (cloud == null || poolKeyPrefab == null) return false;
        if (!_nonPooled.Contains(cloud)) return false;
        if (_lanes == null || laneIndex < 0 || laneIndex >= _lanes.Length) return false;

        LaneState lane = _lanes[laneIndex];
        if (!lane.isActive || lane.prefab == null || !LaneSlotLayoutValid(lane)) return false;

        var platform = GetCloudPlatform(cloud);
        if (platform == null) return false;

        var rb = platform.GetComponent<Rigidbody2D>();
        if (rb == null) return false;

        GetLaneHorizontalSpan(out float left, out float right);

        int bestSlot = -1;
        float bestDist = float.MaxValue;
        for (int i = 0; i < lane.clouds.Count; i++)
        {
            if (lane.clouds[i] != null) continue;
            float tx = SlotCenterX(lane, left, i);
            float d = Mathf.Abs(rb.position.x - tx);
            if (d < bestDist)
            {
                bestDist = d;
                bestSlot = i;
            }
        }

        if (bestSlot < 0) return false;

        float targetX = SlotCenterX(lane, left, bestSlot);
        float scaleX = cloud.transform.localScale.x;
        float spawnY = rb.position.y;
        Bounds mainAtTarget = PrefabMainBoundsWorld(targetX, spawnY, poolKeyPrefab, scaleX);
        if (IntersectsAnyBlockSpawn(mainAtTarget))
            return false;

        platform.pooledWorldY = spawnY;
        if (_nonPooled.Remove(cloud) && _active.Contains(cloud))
            _dynamicCloudCount++;
        lane.clouds[bestSlot] = cloud;
        if (bestSlot < lane.slotSpawnY.Count)
            lane.slotSpawnY[bestSlot] = spawnY;
        platform.isPooled = true;
        platform.isMoving = false;
        platform.laneIndex = laneIndex;
        platform.slotIndex = bestSlot;
        platform.pooledSourcePrefab = poolKeyPrefab;
        platform.SetMovementSpeed(lane.speed);
        platform.ignoreNoSpawnZones = true;
        rb.MovePosition(new Vector2(targetX, spawnY));
        return true;
    }

    #endregion

#if UNITY_EDITOR
    #region Editor gizmos

    void GetGizmoLaneHorizontalSpan(out float leftX, out float rightX)
    {
        if (boundaryManager != null)
        {
            Bounds extended = boundaryManager.GetExtendedBounds();
            leftX = extended.min.x;
            rightX = extended.max.x;
        }
        else
        {
            float h = _gizmoLaneHalfWidth;
            leftX = -h;
            rightX = h;
        }
    }

    void OnDrawGizmos()
    {
        if (settings == null) return;
        GetLaneCountAndBaseY(out int laneCount, out float baseY);
        GetGizmoLaneHorizontalSpan(out float leftX, out float rightX);

        Color inactiveLaneLine = new Color(0f, 0.8f, 1f, 0.6f);
        Color activeLaneLine = new Color(1f, 0.45f, 0.05f, 0.9f);

        for (int i = 0; i < laneCount; i++)
        {
            float worldY = baseY + i * settings.laneSpacing;
            Vector3 from = new Vector3(leftX, worldY, 0f);
            Vector3 to = new Vector3(rightX, worldY, 0f);
            bool active = Application.isPlaying && LaneIsActiveForGizmo(i);
            Gizmos.color = active ? activeLaneLine : inactiveLaneLine;
            Gizmos.DrawLine(from, to);
        }

        if (_gizmoShowCloudSizeAndSpacing)
        {
            Color primaryOdd = new Color(1f, 0.55f, 0.1f, 0.85f);
            Color secondaryOdd = new Color(1f, 0.85f, 0.2f, 0.55f);
            Color primaryEven = new Color(0.35f, 0.75f, 1f, 0.85f);
            Color secondaryEven = new Color(0.65f, 0.45f, 1f, 0.55f);
            Color activePrimary = new Color(1f, 0.35f, 0f, 0.9f);
            Color activeSecondary = new Color(1f, 0.65f, 0.2f, 0.55f);

            for (int i = 0; i < laneCount; i++)
            {
                float worldY = baseY + i * settings.laneSpacing;
                bool oddLane = (i & 1) == 1;
                bool laneActive = Application.isPlaying && LaneIsActiveForGizmo(i);
                float pw, ph, spacing, sw, sh;
                Color cPri, cSec;
                if (oddLane)
                {
                    pw = settings.minCloudMainBoundsWidth;
                    ph = settings.minCloudMainBoundsHeight;
                    spacing = settings.minCloudSpacing;
                    sw = settings.maxCloudMainBoundsWidth;
                    sh = settings.maxCloudMainBoundsHeight;
                    cPri = primaryOdd;
                    cSec = secondaryOdd;
                }
                else
                {
                    pw = settings.maxCloudMainBoundsWidth;
                    ph = settings.maxCloudMainBoundsHeight;
                    spacing = settings.maxCloudSpacing;
                    sw = settings.minCloudMainBoundsWidth;
                    sh = settings.minCloudMainBoundsHeight;
                    cPri = primaryEven;
                    cSec = secondaryEven;
                }

                if (laneActive)
                {
                    cPri = activePrimary;
                    cSec = activeSecondary;
                }

                float step = pw + spacing;
                if (step <= 0.0001f) continue;

                for (float x = leftX + pw * 0.5f; x <= rightX - pw * 0.5f + 0.0001f; x += step)
                {
                    Vector3 center = new Vector3(x, worldY, 0f);
                    DrawGizmoCloudBoundsPair(center, pw, ph, sw, sh, cPri, cSec);
                }
            }
        }
    }

    bool LaneIsActiveForGizmo(int laneIndex)
    {
        return _lanes != null && laneIndex >= 0 && laneIndex < _lanes.Length && _lanes[laneIndex].isActive;
    }

    static void DrawGizmoCloudBoundsPair(Vector3 center, float primaryW, float primaryH, float secondaryW, float secondaryH, Color primaryColor, Color secondaryColor)
    {
        float aPri = primaryW * primaryH;
        float aSec = secondaryW * secondaryH;
        if (aPri >= aSec)
        {
            Gizmos.color = primaryColor;
            Gizmos.DrawWireCube(center, new Vector3(primaryW, primaryH, 0f));
            Gizmos.color = secondaryColor;
            Gizmos.DrawWireCube(center, new Vector3(secondaryW, secondaryH, 0f));
        }
        else
        {
            Gizmos.color = secondaryColor;
            Gizmos.DrawWireCube(center, new Vector3(secondaryW, secondaryH, 0f));
            Gizmos.color = primaryColor;
            Gizmos.DrawWireCube(center, new Vector3(primaryW, primaryH, 0f));
        }
    }

    #endregion
#endif
}
