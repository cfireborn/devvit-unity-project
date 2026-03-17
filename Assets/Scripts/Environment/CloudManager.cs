using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

/// <summary>
/// Spawns and recycles cloud platforms in horizontal world-absolute lanes.
///
/// Lane system:
///   - Lanes sit at fixed Y positions: laneIndex * settings.laneSpacing.
///   - A lane activates when any registered player is within settings.laneActivationDistance
///     vertically of the lane center.
///   - Lane activation is only re-evaluated when a player crosses a lane boundary.
///   - On activation, each lane gets a fresh random prefab, speed (magnitude + direction),
///     and cloud density (spacing). All clouds in the lane share these properties.
///   - On deactivation, all lane clouds are returned to the pool and lane state is reset.
///   - Viewport = union of (player.x ± viewportHalfWidth). Clouds outside viewport are pooled.
///   New clouds spawn at viewport edge ± spawnMargin so they travel in from off-screen.
///
/// Networking: In a networked server context, clouds are spawned as NetworkObjects via
/// ServerManager.Spawn() so FishNet replicates them to all clients automatically.
/// NetworkTransform on each cloud GO syncs position at ~20Hz.
/// In offline mode, clouds use a simple GameObject pool.
///
/// INSPECTOR SETUP REQUIRED:
/// - Add NetworkObject + NetworkTransform + NetworkCloud components to each cloud prefab.
/// - Register each cloud prefab in the NetworkManager's Spawnable Prefabs list.
/// </summary>
public class CloudManager : MonoBehaviour
{
    [Header("References")]
    public CloudLadderController cloudLadderController;
    [Tooltip("When set, lanes and cloud extent are derived from this boundary. When null, defaults to 50 lanes centered at this transform.")]
    public BoundaryManager boundaryManager;
    [Tooltip("Cloud prefabs to spawn from. Each should have a CloudPlatform component.")]
    public GameObject[] cloudPrefabs;
    [Tooltip("All lane and density configuration.")]
    public CloudBehaviorSettings settings;

    const int FallbackLaneCount = 50;

#if UNITY_EDITOR
    [Header("Editor")]
    [Tooltip("Horizontal half-width of lane lines drawn in Scene view (world units).")]
    [SerializeField] float _gizmoLaneHalfWidth = 50f;
    [Tooltip("When enabled, draw min/max cloud radius circles and average spacing markers on lane gizmos.")]
    [SerializeField] bool _gizmoShowCloudSizeAndSpacing;
#endif

    // Injected by NetworkCloudManager before CloudManager is enabled.
    // Server: reparent to root + ServerManager.Spawn + SyncScale.
    // Offline first-creation: strip FishNet components.
    // Offline pool-reuse: no-op (already stripped).
    internal Action<GameObject, float> _onCloudActivated;
    // null = offline pool path. Server: FishNet Despawn or Destroy.
    internal Action<GameObject> _onCloudDeactivated;

    // ── Lane state ───────────────────────────────────────────────────────────

    /// <summary>Per-lane runtime data. Created once in Start(), reset on each deactivation.</summary>
    class LaneState
    {
        public readonly int index;
        public readonly float worldY;
        public bool isActive;
        public GameObject prefab;
        public float speed;              // sign = direction, same for all clouds in lane
        public float radius;             // world units; scale derived so Y bounds fit in 2*radius
        public float baseSpacing;       // edge-to-edge gap (world units)
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
            radius = 0f;
            baseSpacing = 0f;
            clouds.Clear();
        }
    }

    LaneState[] _lanes;

    // ── Player tracking ──────────────────────────────────────────────────────

    readonly List<Transform> _players = new List<Transform>();
    // Per-player last lane index used for lane-crossing detection
    readonly List<int> _lastLaneIndex = new List<int>();

    // ── Pools & scene clouds ─────────────────────────────────────────────────

    readonly Queue<GameObject> _pool = new Queue<GameObject>();
    readonly List<CloudNoSpawnZone> _blockSpawnZones = new List<CloudNoSpawnZone>();
    // Pre-placed scene clouds — never returned to pool
    readonly List<GameObject> _nonPooled = new List<GameObject>();
    // Flat list of all active clouds (pooled + non-pooled). Consumed by CloudLadderController.
    readonly List<GameObject> _active = new List<GameObject>();

    Transform _poolParent;
    bool _forceUpdate;
    bool _forceFill;
    readonly List<Bounds> _boundsInWindow = new List<Bounds>();
    readonly Dictionary<GameObject, float> _prefabNativeHeightY = new Dictionary<GameObject, float>();

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    /// <summary>
    /// Registers all CloudPlatform instances already present in the scene as non-pooled clouds.
    /// Must be called before CloudManager is disabled (e.g. by NetworkCloudManager) so that
    /// ReturnCloudToPool's guard is in place before any FishNet lifecycle callbacks run.
    /// Safe to call multiple times — Contains guards prevent duplicates.
    /// </summary>
    public void CollectSceneClouds()
    {
        CloudPlatform[] sceneClouds = Object.FindObjectsByType<CloudPlatform>(FindObjectsSortMode.None);
        foreach (CloudPlatform cloud in sceneClouds)
        {
            if (!_nonPooled.Contains(cloud.gameObject))
                _nonPooled.Add(cloud.gameObject);

            if (cloud.wasActiveAtStart && !_active.Contains(cloud.gameObject))
            {
                _active.Add(cloud.gameObject);
            }
        }
    }

    void Start()
    {
        _poolParent = new GameObject("CloudPool").transform;
        _poolParent.SetParent(transform); // Keep pooled clouds under CloudManager for scene organization

        // Build lane array from boundary or fallback (50 lanes centered at CloudManager)
        if (settings != null)
        {
            float baseY;
            int laneCount;
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
            _lanes = new LaneState[laneCount];
            for (int i = 0; i < laneCount; i++)
                _lanes[i] = new LaneState(i, baseY + i * settings.laneSpacing);
        }

        // Register self and ladder controller with GameServices
        var gameServices = FindFirstObjectByType<GameServices>();
        if (gameServices != null && cloudLadderController != null)
            gameServices.RegisterCloudLadderController(cloudLadderController);

        // Register primary player (may not be available yet in networked startup)
        TryRegisterPlayer();
        if (_players.Count == 0 && gameServices != null)
            gameServices.onPlayerRegistered += TryRegisterPlayer;

        foreach (var cloud in _active)
        {
            if (cloud != null)
            {
                ActivateNonPooledCloud(cloud);
                _onCloudActivated?.Invoke(cloud, cloud.transform.localScale.x);
            }
        }

        _forceUpdate = true;
        _forceFill = true;
    }

    void Update()
    {
        if (settings == null || cloudPrefabs == null || cloudPrefabs.Length == 0) return;
        if (_players.Count == 0 || _lanes == null) return;

        for (int p = 0; p < _players.Count; p++)
        {
            if (_players[p] == null) continue;
            int currentLane = PlayerLaneIndex(_players[p]);
            if (currentLane == _lastLaneIndex[p] && !_forceUpdate) continue;

            int oldLane = _lastLaneIndex[p];
            _lastLaneIndex[p] = currentLane;

            GetLaneRange(oldLane, out int oldMin, out int oldMax);
            GetLaneRange(currentLane, out int newMin, out int newMax);

            for (int li = newMin; li <= newMax; li++)
            {
                if (!_lanes[li].isActive)
                    ActivateLane(_lanes[li]);
            }

            for (int li = oldMin; li <= oldMax; li++)
            {
                if (li >= newMin && li <= newMax) continue;
                if (_lanes[li].isActive && !AnyPlayerActivatesLane(li))
                    DeactivateLane(_lanes[li]);
            }
        }
        _forceUpdate = false;

        // ── 2. Viewport and cloud maintenance ─────────────────────────────────

        float minPlayerX = float.MaxValue, maxPlayerX = float.MinValue;
        foreach (var pt in _players)
        {
            if (pt == null) continue;
            if (pt.position.x < minPlayerX) minPlayerX = pt.position.x;
            if (pt.position.x > maxPlayerX) maxPlayerX = pt.position.x;
        }
        float viewportLeft = minPlayerX - settings.viewportHalfWidth;
        float viewportRight = maxPlayerX + settings.viewportHalfWidth;

        Bounds? extendedBounds = boundaryManager != null ? boundaryManager.GetExtendedBounds() : (Bounds?)null;
        Bounds? innerBounds = boundaryManager != null ? boundaryManager.GetInnerBounds() : (Bounds?)null;
        if (extendedBounds.HasValue)
        {
            viewportLeft = Mathf.Max(viewportLeft, extendedBounds.Value.min.x);
            viewportRight = Mathf.Min(viewportRight, extendedBounds.Value.max.x);
        }

        int fillIterations = _forceFill ? 10 : 1;
        _forceFill = false;

        foreach (var lane in _lanes)
        {
            if (!lane.isActive) continue;

            for (int c = lane.clouds.Count - 1; c >= 0; c--)
            {
                var cloud = lane.clouds[c];
                if (cloud == null) continue;
                var platform = cloud.GetComponent<CloudPlatform>();
                Bounds b = platform != null ? platform.GetMainBounds() : new Bounds(cloud.transform.position, Vector3.zero);

                if (innerBounds.HasValue && !innerBounds.Value.Contains(new Vector3(cloud.transform.position.x, cloud.transform.position.y, 0f)))
                {
                    if (platform != null) platform.TriggerBlockEntryFromBoundary();
                    continue;
                }
                if (b.max.x < viewportLeft || b.min.x > viewportRight)
                {
                    if (platform != null && platform.IsPlayerOnCloud) continue;
                    if (cloudLadderController != null && cloudLadderController.IsPlayerOnAnyLadderPartner(cloud)) continue;
                    if (cloudLadderController != null && cloudLadderController.ShouldKeepCloudActiveForLadders(cloud, viewportLeft, viewportRight)) continue;
                    DeactivateCloud(cloud);
                }
            }

            for (int iter = 0; iter < fillIterations && IsLaneUnderpopulated(lane, viewportLeft, viewportRight); iter++)
                SpawnCloudInLane(lane, viewportLeft, viewportRight);
        }
    }

    void OnDestroy()
    {
        var gameServices = FindFirstObjectByType<GameServices>();
        if (gameServices != null)
            gameServices.onPlayerRegistered -= TryRegisterPlayer;
    }

    // ── Player registration ──────────────────────────────────────────────────

    void TryRegisterPlayer()
    {
        var gameServices = FindFirstObjectByType<GameServices>();
        if (gameServices == null) return;
        var p = gameServices.GetPlayer();
        if (p != null)
            RegisterPlayer(p.transform);
    }

    /// <summary>Register a player Transform for lane-activation tracking.
    /// Called by TryRegisterPlayer (via GameServices) or directly by multiplayer code.</summary>
    public void RegisterPlayer(Transform playerTransform)
    {
        if (playerTransform == null || _players.Contains(playerTransform)) return;
        _players.Add(playerTransform);
        int startLane = _lanes != null ? PlayerLaneIndex(playerTransform) : 0;
        _lastLaneIndex.Add(startLane - 1);
        _forceUpdate = true;
        _forceFill = true;
    }

    /// <summary>Request that the next Update runs extra fill iterations so the viewport is populated (e.g. after game start or respawn).</summary>
    public void RequestViewportFill()
    {
        _forceUpdate = true;
        _forceFill = true;
    }

    // ── Lane helpers ─────────────────────────────────────────────────────────

    int PlayerLaneIndex(Transform player)
    {
        if (settings == null || settings.laneSpacing <= 0f || _lanes == null || _lanes.Length == 0) return 0;
        float firstLaneY = _lanes[0].worldY;
        int idx = Mathf.RoundToInt((player.position.y - firstLaneY) / settings.laneSpacing);
        return Mathf.Clamp(idx, 0, _lanes.Length - 1);
    }

    void GetLaneRange(int laneIndex, out int min, out int max)
    {
        int radius = Mathf.CeilToInt(settings.laneActivationDistance / settings.laneSpacing);
        min = Mathf.Max(0, laneIndex - radius);
        max = Mathf.Min(_lanes.Length - 1, laneIndex + radius);
    }

    bool AnyPlayerActivatesLane(int laneIndex)
    {
        GetLaneRange(laneIndex, out int min, out int max);
        foreach (var pt in _players)
        {
            if (pt == null) continue;
            int pLane = PlayerLaneIndex(pt);
            if (pLane >= min && pLane <= max) return true;
        }
        return false;
    }

    void ActivateLane(LaneState lane)
    {
        lane.isActive = true;
        lane.prefab = cloudPrefabs[Random.Range(0, cloudPrefabs.Length)];
        float magnitude = Random.Range(settings.speedRange.x, settings.speedRange.y);
        lane.speed = Random.value < 0.5f ? magnitude : -magnitude;
        lane.radius = Random.Range(settings.minCloudRadius, settings.maxCloudRadius);
        lane.baseSpacing = Random.Range(settings.minCloudSpacing, settings.maxCloudSpacing);
    }

    void DeactivateLane(LaneState lane)
    {
        // Return all lane clouds to pool
        for (int i = lane.clouds.Count - 1; i >= 0; i--)
        {
            var cloud = lane.clouds[i];
            if (cloud != null)
                ReturnCloudToPool(cloud);
        }
        lane.Reset();
    }

    // ── Density check ────────────────────────────────────────────────────────

    bool IsLaneUnderpopulated(LaneState lane, float viewportLeft, float viewportRight)
    {
        _boundsInWindow.Clear();
        foreach (var cloud in lane.clouds)
        {
            if (cloud == null) continue;
            var platform = cloud.GetComponent<CloudPlatform>();
            if (platform == null) continue;
            Bounds b = platform.GetMainBounds();
            if (b.max.x >= viewportLeft && b.min.x <= viewportRight)
                _boundsInWindow.Add(b);
        }

        if (_boundsInWindow.Count == 0) return true;

        _boundsInWindow.Sort((a, b) => a.min.x.CompareTo(b.min.x));
        // baseSpacing = edge-to-edge gap; underpopulated if any gap exceeds base + variation
        float maxGap = lane.baseSpacing + settings.spacingVariation;

        if (_boundsInWindow[0].min.x - viewportLeft > maxGap) return true;
        for (int i = 0; i < _boundsInWindow.Count - 1; i++)
        {
            if (_boundsInWindow[i + 1].min.x - _boundsInWindow[i].max.x > maxGap) return true;
        }
        if (viewportRight - _boundsInWindow[_boundsInWindow.Count - 1].max.x > maxGap) return true;

        return false;
    }

    // ── Spawn & recycle ──────────────────────────────────────────────────────

    /// <summary>Number of dynamically spawned clouds currently active (excludes manually placed scene clouds).</summary>
    int DynamicCloudCount => _active.Count - _nonPooled.Count;

    float GetPrefabNativeHeightY(GameObject prefab)
    {
        if (_prefabNativeHeightY.TryGetValue(prefab, out float h)) return h;
        var temp = Instantiate(prefab, _poolParent);
        temp.transform.position = Vector3.zero;
        temp.transform.localScale = Vector3.one;
        var p = temp.GetComponent<CloudPlatform>();
        if (p == null) p = temp.AddComponent<CloudPlatform>();
        Bounds b = p.GetMainBounds(); // use main collider for consistent Y extent
        Object.Destroy(temp);
        h = b.size.y;
        _prefabNativeHeightY[prefab] = h;
        return h;
    }

    void SpawnCloudInLane(LaneState lane, float viewportLeft, float viewportRight)
    {
        if (lane.prefab == null) return;
        if (settings.maxDynamicClouds > 0 && DynamicCloudCount >= settings.maxDynamicClouds) return;

        float nativeH = GetPrefabNativeHeightY(lane.prefab);
        if (nativeH <= 0f) return;
        float desiredRadius = settings.radiusVariation > 0
            ? Random.Range(settings.minCloudRadius, settings.maxCloudRadius)
            : lane.radius;
        float scale = (2f * desiredRadius) / nativeH;

        GameObject cloud;
        if (_pool.Count > 0)
        {
            cloud = _pool.Dequeue();
            cloud.SetActive(true);
        }
        else
        {
            cloud = Instantiate(lane.prefab, _poolParent);
        }

        cloud.transform.localScale = new Vector3(scale, scale, scale);
        var platform = cloud.GetComponent<CloudPlatform>();
        if (platform == null) platform = cloud.AddComponent<CloudPlatform>();
        platform.SetCloudManager(this);
        platform.SetMovementSpeed(lane.speed);
        platform.laneIndex = lane.index;
        platform.isPooled = true;

        float spawnY = lane.worldY + (settings.laneHeightVariation == 0 ? 0 : Random.Range(-settings.laneHeightVariation, settings.laneHeightVariation));
        cloud.transform.position = new Vector3(0f, spawnY, 0f);
        Bounds bounds = platform.GetMainBounds();
        float halfWidth = bounds.extents.x;

        float entryEdge = lane.speed >= 0f ? viewportLeft - settings.spawnMargin : viewportRight + settings.spawnMargin;
        float centerX = lane.speed >= 0f ? entryEdge + halfWidth : entryEdge - halfWidth;

        // Edge-to-edge gap from nearest cloud on entry side = baseSpacing ± variation
        _boundsInWindow.Clear();
        foreach (var c in lane.clouds)
        {
            if (c == null) continue;
            var pl = c.GetComponent<CloudPlatform>();
            if (pl == null) continue;
            Bounds cb = pl.GetMainBounds();
            if (lane.speed >= 0f && cb.max.x < viewportRight) _boundsInWindow.Add(cb);
            if (lane.speed < 0f && cb.min.x > viewportLeft) _boundsInWindow.Add(cb);
        }
        if (_boundsInWindow.Count > 0)
        {
            _boundsInWindow.Sort((a, b) => (lane.speed >= 0f ? a.min.x : -a.max.x).CompareTo(lane.speed >= 0f ? b.min.x : -b.max.x));
            float refX = lane.speed >= 0f ? _boundsInWindow[0].min.x : _boundsInWindow[0].max.x;
            float edgeGap = Mathf.Max(0f, lane.baseSpacing + Random.Range(-settings.spacingVariation, settings.spacingVariation));
            centerX = lane.speed >= 0f ? refX - halfWidth - edgeGap : refX + halfWidth + edgeGap;
        }

        var spawnPos = new Vector2(centerX, spawnY);
        int retries = 0;
        while (IsPositionInBlockSpawnZone(spawnPos) && retries < settings.maxSpawnRetries)
        {
            float nudge = Random.Range(0.5f, 2f) * (lane.speed >= 0f ? -1f : 1f);
            spawnPos.x += nudge;
            spawnPos.y = lane.worldY + (settings.laneHeightVariation == 0 ? 0 : Random.Range(-settings.laneHeightVariation, settings.laneHeightVariation));
            retries++;
        }
        if (retries >= settings.maxSpawnRetries)
        {
            ReturnCloudToPool(cloud);
            return;
        }

        cloud.transform.position = new Vector3(spawnPos.x, spawnPos.y, 0f);

        _onCloudActivated?.Invoke(cloud, scale);
        _active.Add(cloud);
        lane.clouds.Add(cloud);
    }

    public bool ActivateNonPooledCloud(GameObject cloud)
    {
        if (cloud == null) return false;
        if (_pool.Contains(cloud)) return false; // already pooled

        if (!_nonPooled.Contains(cloud))
        {
            _nonPooled.Add(cloud);
        }

        cloud.SetActive(true);
        if (!_active.Contains(cloud))
        {
            _active.Add(cloud);
            _onCloudActivated?.Invoke(cloud, cloud.transform.localScale.x); // assuming uniform scale
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

        if (!_active.Contains(cloud) && !_pool.Contains(cloud))
            Debug.LogWarning("Attempted to deactivate a cloud not managed by CloudManager: " + cloud.name);

        ReturnCloudToPool(cloud);
    }

    // ── Pool management ──────────────────────────────────────────────────────

    public void ReturnCloudToPool(GameObject cloud)
    {
        // Never pool or despawn manually placed scene clouds.
        if (_nonPooled.Contains(cloud)) return;

        _active.Remove(cloud);

        // Remove from its lane's cloud list
        if (_lanes != null)
        {
            var platform = cloud.GetComponent<CloudPlatform>();
            if (platform != null && platform.laneIndex >= 0 && platform.laneIndex < _lanes.Length)
                _lanes[platform.laneIndex].clouds.Remove(cloud);
        }

        if (_onCloudDeactivated != null)
        {
            _onCloudDeactivated(cloud);
            return;
        }

        // Offline: return to pool under CloudManager for scene organization
        cloud.SetActive(false);
        cloud.transform.SetParent(_poolParent);
        _pool.Enqueue(cloud);
    }

    // ── Spawn zone helpers ───────────────────────────────────────────────────

    public void RegisterBlockSpawnZone(CloudNoSpawnZone zone)
    {
        if (zone != null && zone.blockSpawn && !_blockSpawnZones.Contains(zone))
            _blockSpawnZones.Add(zone);
    }

    bool IsPositionInBlockSpawnZone(Vector2 pos)
    {
        if (boundaryManager != null)
        {
            Bounds extended = boundaryManager.GetExtendedBounds();
            if (pos.x < extended.min.x || pos.x > extended.max.x || pos.y < extended.min.y || pos.y > extended.max.y)
                return true;
        }
        foreach (var zone in _blockSpawnZones)
        {
            if (zone == null) continue;
            var col = zone.GetComponent<Collider2D>();
            if (col != null && col.OverlapPoint(pos))
                return true;
        }
        return false;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Get all currently active clouds (pooled + non-pooled). Used by CloudLadderController.</summary>
    public IReadOnlyList<GameObject> GetActiveClouds() => _active;

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (settings == null) return;
        int laneCount;
        float baseY;
        float leftX, rightX;
        if (boundaryManager != null)
        {
            Bounds extended = boundaryManager.GetExtendedBounds();
            laneCount = Mathf.Max(1, Mathf.CeilToInt(extended.size.y / settings.laneSpacing));
            baseY = extended.min.y + settings.laneYOffset;
            leftX = extended.min.x;
            rightX = extended.max.x;
        }
        else
        {
            laneCount = FallbackLaneCount;
            float centerY = transform.position.y;
            baseY = centerY - (laneCount - 1) * 0.5f * settings.laneSpacing + settings.laneYOffset;
            float halfWidth = _gizmoLaneHalfWidth;
            leftX = -halfWidth;
            rightX = halfWidth;
        }
        Gizmos.color = new Color(0f, 0.8f, 1f, 0.6f);
        for (int i = 0; i < laneCount; i++)
        {
            float worldY = baseY + i * settings.laneSpacing;
            Vector3 from = new Vector3(leftX, worldY, 0f);
            Vector3 to = new Vector3(rightX, worldY, 0f);
            Gizmos.DrawLine(from, to);
        }

        if (_gizmoShowCloudSizeAndSpacing)
        {
            for (int i = 0; i < laneCount; i++)
            {
                float worldY = baseY + i * settings.laneSpacing;
                Vector3 center = new Vector3((leftX + rightX) * 0.5f, worldY, 0f);
                Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
                DrawGizmoCircle(center, settings.minCloudRadius);
                Gizmos.color = new Color(1f, 0.8f, 0f, 0.5f);
                DrawGizmoCircle(center, settings.maxCloudRadius);
                Gizmos.color = new Color(0f, 0.8f, 1f, 0.6f);
                for (float x = leftX; x <= rightX; x += settings.minCloudSpacing)
                    DrawGizmoCircle(new Vector3(x, worldY, 0f), 0.12f);
                Gizmos.color = new Color(1f, 0.4f, 0.8f, 0.6f);
                for (float x = leftX; x <= rightX; x += settings.maxCloudSpacing)
                    DrawGizmoCircle(new Vector3(x, worldY, 0f), 0.18f);
            }
        }
    }

    static void DrawGizmoCircle(Vector3 center, float radius, int segments = 24)
    {
        float step = 360f / segments * Mathf.Deg2Rad;
        for (int i = 0; i < segments; i++)
        {
            float a = i * step, b = (i + 1) * step;
            Gizmos.DrawLine(
                center + new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f),
                center + new Vector3(Mathf.Cos(b) * radius, Mathf.Sin(b) * radius, 0f));
        }
    }
#endif
}
