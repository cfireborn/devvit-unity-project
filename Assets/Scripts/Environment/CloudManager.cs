using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

/// <summary>
/// Server/offline: activates horizontal lanes by player viewport, runs a fixed-spacing loop per lane,
/// drives pooled clouds via Rigidbody2D.MovePosition in FixedUpdate. Clients receive NetworkObjects from FishNet.
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

    /// <summary>Last Update: player view rects (camera + viewportMargin, clipped). Used for lane activation, viewport cull, and TrySpawnSlot gate.</summary>
    readonly List<PlayerViewRect> _viewportCullRects = new List<PlayerViewRect>();

    Transform _poolParent;
    bool _cloudsFrozen;

    #endregion

    #region Lifecycle

    public void CollectSceneClouds()
    {
        CloudPlatform[] sceneClouds = Object.FindObjectsByType<CloudPlatform>(FindObjectsSortMode.None);
        foreach (CloudPlatform cloud in sceneClouds)
        {
            if (!_nonPooled.Contains(cloud.gameObject))
                _nonPooled.Add(cloud.gameObject);

            if (cloud.wasActiveAtStart && !_active.Contains(cloud.gameObject))
                _active.Add(cloud.gameObject);
        }
    }

    void Start()
    {
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

        foreach (var cloud in _active)
        {
            if (cloud != null)
            {
                ActivateNonPooledCloud(cloud);
                _onCloudActivated?.Invoke(cloud, cloud.transform.localScale.x);
            }
        }

        var sceneZones = Object.FindObjectsByType<CloudNoSpawnZone>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int z = 0; z < sceneZones.Length; z++)
            RegisterNoSpawnZone(sceneZones[z]);
    }

    void Update()
    {
        if (settings == null || cloudPrefabs == null || cloudPrefabs.Length == 0) return;
        if (_lanes == null) return;

        BuildPlayerViewRects(_viewportCullRects);
        UpdateLaneActivation(_viewportCullRects);
        ViewportCullPooledClouds();
    }

    void FixedUpdate()
    {
        if (settings == null || _lanes == null) return;
        GetLaneHorizontalSpan(out float left, out float right);
        float dt = Time.fixedDeltaTime;

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
                float targetX = SlotCenterX(lane, left, i);
                GameObject cloud = lane.clouds[i];
                if (cloud == null)
                {
                    TrySpawnSlot(lane, left, right, i, targetX);
                    continue;
                }

                var platform = cloud.GetComponent<CloudPlatform>();
                var rb = cloud.GetComponent<Rigidbody2D>();
                if (platform == null || rb == null)
                    continue;

                if (platform.IsDespawning || platform.IsBoundaryStopped)
                    continue;

                Vector2 natLane = GetPrefabNativeMainSize(lane.prefab);
                float scaleX = cloud.transform.localScale.x;
                Bounds mainAtTarget = MainBoundsWorld(targetX, platform.pooledWorldY, natLane, scaleX);
                GetBlockEntryOverlapParts(mainAtTarget, out bool overlapStrictEntry, out bool overlapEntryOnly);
                bool crossedIntoEntryOnly = overlapEntryOnly && !platform.pooledPrevOverlapEntryOnly;
                platform.pooledPrevOverlapEntryOnly = overlapEntryOnly;
                if (overlapStrictEntry || crossedIntoEntryOnly)
                {
                    platform.TriggerBlockEntryFromBoundary();
                    continue;
                }

                // Only despawn pooled lane clouds when they reach the travel-direction exit boundary (see ShouldExitDespawnForTarget).
                float cloudHalfW = natLane.x * scaleX * 0.5f;
                if (ShouldExitDespawnForTarget(lane, left, right, targetX, cloudHalfW))
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
        var gameServices = FindFirstObjectByType<GameServices>();
        if (gameServices != null)
        {
            gameServices.onPlayerRegistered -= TryRegisterPlayer;
            gameServices.onPlayerDeregistered -= OnPlayerDeregisteredFromServices;
        }
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
    }

    public void UnregisterPlayer(Transform playerTransform)
    {
        if (playerTransform == null) return;
        _players.Remove(playerTransform);
    }

    /// <summary>Called by GameManager when the scene or player context changes; lane fill is continuous so this is a no-op hook.</summary>
    public void RequestViewportFill()
    {
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

                var platform = cloud.GetComponent<CloudPlatform>();
                if (platform == null || !platform.isPooled || platform.IsDespawning) continue;

                var rb = cloud.GetComponent<Rigidbody2D>();
                if (rb == null) continue;

                Vector2 nat = GetPrefabNativeMainSize(lane.prefab);
                float scaleX = cloud.transform.localScale.x;
                Bounds mainBounds = MainBoundsWorld(rb.position.x, platform.pooledWorldY, nat, scaleX);
                if (!MainBoundsVisibleToAnyPlayer(mainBounds))
                    ReturnCloudToPool(cloud);
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
        lane.halfWidthCached = nat.x * lane.laneScale * 0.5f;
        lane.step = 2f * lane.halfWidthCached + lane.baseSpacing;

        GetLaneHorizontalSpan(out float left, out float right);
        float usable = right - left - 2f * lane.halfWidthCached;
        if (usable < 0f) usable = 0f;

        lane.slotCount = usable <= 0f ? 1 : Mathf.Max(1, Mathf.FloorToInt(usable / lane.step) + 1);
        while (lane.slotCount > 1 && (lane.slotCount - 1) * lane.step > usable + 0.0001f)
            lane.slotCount--;

        lane.loopPhase = Random.Range(0f, 1f);
        lane.clouds.Clear();
        for (int i = 0; i < lane.slotCount; i++)
            lane.clouds.Add(null);

        for (int i = 0; i < lane.slotCount; i++)
            TrySpawnSlot(lane, left, right, i, SlotCenterX(lane, left, i));
    }

    void DeactivateLane(LaneState lane)
    {
        for (int i = 0; i < lane.clouds.Count; i++)
        {
            var cloud = lane.clouds[i];
            if (cloud != null)
                ReturnCloudToPool(cloud);
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
        float hw = nat.x * scale * 0.5f;
        if (!SlotIsSafeForNewSpawn(lane, left, right, targetX, hw)) return;

        float spawnY = SampleSpawnY(lane, true);
        Bounds spawnBounds = MainBoundsWorld(targetX, spawnY, nat, scale);
        if (IntersectsAnyBlockSpawn(spawnBounds))
            return;
        if (!MainBoundsVisibleToAnyPlayer(spawnBounds))
            return;
        // Cap check runs after visibility: visible slots always spawn to keep the viewport filled;
        // only off-screen pre-spawns (within viewportMargin) are capped to bound pool size.
        if (settings.maxDynamicClouds > 0 && DynamicCloudCount >= settings.maxDynamicClouds) return;

        AcquireCloudFromPool(lane, scale, out GameObject cloud, out CloudPlatform platform);
        platform.pooledWorldY = spawnY;
        platform.slotIndex = slotIndex;
        cloud.transform.position = new Vector3(targetX, spawnY, 0f);
        GetBlockEntryOverlapParts(spawnBounds, out _, out bool entryOnlyAtSpawn);
        platform.pooledPrevOverlapEntryOnly = entryOnlyAtSpawn;

        _onCloudActivated?.Invoke(cloud, scale);
        _active.Add(cloud);
        lane.clouds[slotIndex] = cloud;
    }

    int DynamicCloudCount => _active.Count - _nonPooled.Count;

    #endregion

    #region Prefab sizing

    Vector2 GetPrefabNativeMainSize(GameObject prefab)
    {
        if (_prefabNativeMainSize.TryGetValue(prefab, out Vector2 sz)) return sz;
        var temp = Instantiate(prefab, _poolParent);
        temp.transform.position = Vector3.zero;
        temp.transform.localScale = Vector3.one;
        var p = temp.GetComponent<CloudPlatform>();
        if (p == null) p = temp.AddComponent<CloudPlatform>();
        Bounds b = p.GetMainBounds();
        Object.Destroy(temp);
        sz = new Vector2(Mathf.Max(0.0001f, b.size.x), Mathf.Max(0.0001f, b.size.y));
        _prefabNativeMainSize[prefab] = sz;
        return sz;
    }

    void ComputeScaleBoundsForPrefab(GameObject prefab, out float sMin, out float sMax)
    {
        Vector2 native = GetPrefabNativeMainSize(prefab);
        sMin = Mathf.Max(settings.minCloudMainBoundsWidth / native.x, settings.minCloudMainBoundsHeight / native.y);
        sMax = Mathf.Min(settings.maxCloudMainBoundsWidth / native.x, settings.maxCloudMainBoundsHeight / native.y);
    }

    static Bounds MainBoundsWorld(float centerX, float centerY, Vector2 nativeSize, float uniformScale)
    {
        Vector3 size = new Vector3(nativeSize.x * uniformScale, nativeSize.y * uniformScale, 0f);
        return new Bounds(new Vector3(centerX, centerY, 0f), size);
    }

    bool IntersectsAnyBlockSpawn(Bounds cloudMainBounds)
    {
        int n = _noSpawnZones.Count;
        if (n == 0) return false;
        for (int i = 0; i < n; i++)
        {
            CloudNoSpawnZone z = _noSpawnZones[i];
            if (!z.blockSpawn) continue;
            if (!z.TryGetWorldBounds(out Bounds zb)) continue;
            if (cloudMainBounds.Intersects(zb)) return true;
        }
        return false;
    }

    /// <summary>
    /// blockSpawn + blockEntry: overlap stops immediately (cannot spawn inside these zones).
    /// blockEntry only: overlap tracked via <paramref name="overlapEntryOnly"/> for transition detection (spawn inside allowed).
    /// </summary>
    void GetBlockEntryOverlapParts(Bounds cloudMainBounds, out bool overlapStrictEntry, out bool overlapEntryOnly)
    {
        overlapStrictEntry = false;
        overlapEntryOnly = false;
        int n = _noSpawnZones.Count;
        for (int i = 0; i < n; i++)
        {
            CloudNoSpawnZone z = _noSpawnZones[i];
            if (!z.blockEntry) continue;
            if (!z.TryGetWorldBounds(out Bounds zb)) continue;
            if (!cloudMainBounds.Intersects(zb)) continue;
            if (z.blockSpawn) overlapStrictEntry = true;
            else overlapEntryOnly = true;
        }
    }

    #endregion

    #region Pooling & cloud lifecycle

    void AcquireCloudFromPool(LaneState lane, float scale, out GameObject cloud, out CloudPlatform platform)
    {
        GameObject prefab = lane.prefab;
        if (prefab != null && TryDequeueFromPrefabPool(prefab, out cloud))
            cloud.SetActive(true);
        else
            cloud = Instantiate(prefab, _poolParent);

        cloud.transform.localScale = new Vector3(scale, scale, scale);
        platform = cloud.GetComponent<CloudPlatform>();
        if (platform == null) platform = cloud.AddComponent<CloudPlatform>();
        platform.pooledSourcePrefab = prefab;
        platform.SetCloudManager(this);
        platform.SetMovementSpeed(lane.speed);
        platform.laneIndex = lane.index;
        platform.isPooled = true;
        platform.isMoving = false;
        platform.ignoreNoSpawnZones = true;
    }

    bool TryDequeueFromPrefabPool(GameObject prefab, out GameObject cloud)
    {
        cloud = null;
        if (prefab == null || !_poolByPrefab.TryGetValue(prefab, out Queue<GameObject> q) || q.Count == 0)
            return false;
        cloud = q.Dequeue();
        _queuedInPool.Remove(cloud);
        return true;
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
        if (cloud == null) return false;
        if (_queuedInPool.Contains(cloud)) return false;

        if (!_nonPooled.Contains(cloud))
            _nonPooled.Add(cloud);

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

        cloud.SetActive(false);

        if (_nonPooled.Contains(cloud))
        {
            _active.Remove(cloud);
            _onCloudDeactivated?.Invoke(cloud);
            return;
        }

        ReturnCloudToPool(cloud);
    }

    public void ReturnCloudToPool(GameObject cloud)
    {
        if (cloud == null || _nonPooled.Contains(cloud)) return;

        _active.Remove(cloud);

        var platform = cloud.GetComponent<CloudPlatform>();
        if (_lanes != null && platform != null && platform.laneIndex >= 0 && platform.laneIndex < _lanes.Length)
        {
            LaneState lane = _lanes[platform.laneIndex];
            if (platform.slotIndex >= 0 && platform.slotIndex < lane.clouds.Count)
                lane.clouds[platform.slotIndex] = null;
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
            var platform = go.GetComponent<CloudPlatform>();
            if (platform == null) continue;
            if (_cloudsFrozen)
                platform.isMoving = false;
            else if (!platform.IsBoundaryStopped && !platform.IsDespawning)
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
            var platform = go.GetComponent<CloudPlatform>();
            if (platform != null)
                platform.SetMovementSpeed(-platform.moveSpeed);
        }
    }

    public void RegisterNoSpawnZone(CloudNoSpawnZone zone)
    {
        if (zone != null && !_noSpawnZones.Contains(zone))
            _noSpawnZones.Add(zone);
    }

    public void RegisterBlockSpawnZone(CloudNoSpawnZone zone) => RegisterNoSpawnZone(zone);

    public IReadOnlyList<GameObject> GetActiveClouds() => _active;

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
