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
///   - Active clouds that drift past activeWindowHalfWidth + recycleMargin from every player
///     are teleported back to the offscreen entry side of their lane (not despawned), so they
///     recirculate and maintain density without needing new spawns.
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
    [Tooltip("Cloud prefabs to spawn from. Each should have a CloudPlatform component.")]
    public GameObject[] cloudPrefabs;
    [Tooltip("All lane and density configuration.")]
    public CloudBehaviorSettings settings;

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
        public GameObject prefab;        // randomised on every activation
        public float speed;              // randomised on every activation (sign = direction)
        public float scale;              // randomised on every activation (uniform for all clouds in lane)
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
            scale = 1f;
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
        _poolParent.SetParent(transform);

        // Build lane array
        if (settings != null)
        {
            _lanes = new LaneState[settings.laneCount];
            for (int i = 0; i < settings.laneCount; i++)
                _lanes[i] = new LaneState(i, i * settings.laneSpacing);
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

        _forceUpdate = true; // ensure lane activation runs on first Update, even if players start in same lane
    }

    void Update()
    {
        if (settings == null || cloudPrefabs == null || cloudPrefabs.Length == 0) return;
        if (_players.Count == 0 || _lanes == null) return;

        bool anyLaneCrossed = false;
        for (int p = 0; p < _players.Count; p++)
        {
            if (_players[p] == null) continue;
            int currentLane = PlayerLaneIndex(_players[p]);
            if (currentLane == _lastLaneIndex[p] && !_forceUpdate) continue;

            anyLaneCrossed = true;
            int oldLane = _lastLaneIndex[p];
            _lastLaneIndex[p] = currentLane;

            // Compute activation radius in lane indices
            int radius = Mathf.CeilToInt(settings.laneActivationDistance / settings.laneSpacing);

            // Lanes that were in range of the old position
            int oldMin = Mathf.Max(0, oldLane - radius);
            int oldMax = Mathf.Min(_lanes.Length - 1, oldLane + radius);

            // Lanes that are now in range
            int newMin = Mathf.Max(0, currentLane - radius);
            int newMax = Mathf.Min(_lanes.Length - 1, currentLane + radius);

            // Activate lanes now in range that weren't before (for this player)
            for (int li = newMin; li <= newMax; li++)
            {
                if (!_lanes[li].isActive)
                    ActivateLane(_lanes[li]);
            }

            // Deactivate lanes that fell out of range for this player,
            // but only if no other player still activates them
            for (int li = oldMin; li <= oldMax; li++)
            {
                if (li >= newMin && li <= newMax) continue; // still in range for this player
                if (_lanes[li].isActive && !AnyPlayerActivatesLane(li))
                    DeactivateLane(_lanes[li]);
            }
        }
        _forceUpdate = false;

        // ── 2. Per-frame cloud maintenance ────────────────────────────────────

        // Compute the union active X window across all players
        float minPlayerX = float.MaxValue;
        float maxPlayerX = float.MinValue;
        foreach (var pt in _players)
        {
            if (pt == null) continue;
            if (pt.position.x < minPlayerX) minPlayerX = pt.position.x;
            if (pt.position.x > maxPlayerX) maxPlayerX = pt.position.x;
        }

        float windowLeft  = minPlayerX - settings.activeWindowHalfWidth;
        float windowRight = maxPlayerX + settings.activeWindowHalfWidth;
        float recycleLeft  = windowLeft  - settings.recycleMargin;
        float recycleRight = windowRight + settings.recycleMargin;

        foreach (var lane in _lanes)
        {
            if (!lane.isActive) continue;

            // Recycle clouds that have drifted beyond the recycle boundary
            foreach (var cloud in lane.clouds)
            {
                if (cloud == null) continue;
                float cx = cloud.transform.position.x;
                bool beyondExit = lane.speed >= 0f ? cx > recycleRight : cx < recycleLeft;
                if (beyondExit)
                    RecycleCloudToEntry(lane, cloud, windowLeft, windowRight);
            }

            // Maintain target density — spawn one cloud per frame if underpopulated
            if (IsLaneUnderpopulated(lane, windowLeft, windowRight))
                SpawnCloudInLane(lane, windowLeft, windowRight);
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
        _lastLaneIndex.Add(startLane - 1); // force evaluation on first Update
        _forceUpdate = true;
    }

    // ── Lane helpers ─────────────────────────────────────────────────────────

    int PlayerLaneIndex(Transform player)
    {
        if (settings == null || settings.laneSpacing <= 0f) return 0;
        return Mathf.RoundToInt(player.position.y / settings.laneSpacing);
    }

    bool AnyPlayerActivatesLane(int laneIndex)
    {
        int radius = Mathf.CeilToInt(settings.laneActivationDistance / settings.laneSpacing);
        foreach (var pt in _players)
        {
            if (pt == null) continue;
            int pLane = PlayerLaneIndex(pt);
            if (Mathf.Abs(pLane - laneIndex) <= radius) return true;
        }
        return false;
    }

    void ActivateLane(LaneState lane)
    {
        lane.isActive = true;

        // Assign fresh random settings for this activation
        lane.prefab = cloudPrefabs[Random.Range(0, cloudPrefabs.Length)];
        float magnitude = Random.Range(settings.speedRange.x, settings.speedRange.y);
        lane.speed = Random.value < 0.5f ? magnitude : -magnitude;
        lane.scale = Random.Range(settings.scaleRange.x, settings.scaleRange.y);
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

    bool IsLaneUnderpopulated(LaneState lane, float windowLeft, float windowRight)
    {
        // Collect the bounds of every cloud whose collision bounds overlap or sit inside the window.
        var boundsInWindow = new List<Bounds>();
        foreach (var cloud in lane.clouds)
        {
            if (cloud == null) continue;
            var platform = cloud.GetComponent<CloudPlatform>();
            if (platform == null) continue;
            Bounds b = platform.GetBounds();
            // Include if any part of the cloud is within the window
            if (b.max.x >= windowLeft && b.min.x <= windowRight)
                boundsInWindow.Add(b);
        }

        if (boundsInWindow.Count == 0) return true;

        // Sort by min.x (leading edge from left)
        boundsInWindow.Sort((a, b) => a.min.x.CompareTo(b.min.x));

        // Check gap between the window left edge and the first cloud's left edge
        float gapBeforeFirst = boundsInWindow[0].min.x - windowLeft;
        if (gapBeforeFirst > settings.maxCloudSpacing) return true;

        // Check gaps between consecutive cloud edges (trailing edge to next leading edge)
        for (int i = 0; i < boundsInWindow.Count - 1; i++)
        {
            float gap = boundsInWindow[i + 1].min.x - boundsInWindow[i].max.x;
            if (gap > settings.maxCloudSpacing) return true;
        }

        // Check gap between last cloud's trailing edge and the window right edge
        float gapAfterLast = windowRight - boundsInWindow[boundsInWindow.Count - 1].max.x;
        if (gapAfterLast > settings.maxCloudSpacing) return true;

        return false;
    }

    // ── Spawn & recycle ──────────────────────────────────────────────────────

    /// <summary>Number of dynamically spawned clouds currently active (excludes manually placed scene clouds).</summary>
    int DynamicCloudCount => _active.Count - _nonPooled.Count;

    void SpawnCloudInLane(LaneState lane, float windowLeft, float windowRight)
    {
        if (lane.prefab == null) return;

        // Respect the global dynamic cloud cap (0 = unlimited)
        if (settings.maxDynamicClouds > 0 && DynamicCloudCount >= settings.maxDynamicClouds) return;

        float spawnY = lane.worldY + Random.Range(-settings.laneHeightVariation, settings.laneHeightVariation);
        var spawnPos = new Vector2(0f, spawnY); // X corrected after instantiation using actual bounds

        // Respect block-spawn zones (check will be refined after bounds correction below)
        int retries = 0;

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

        cloud.transform.localScale = new Vector3(lane.scale, lane.scale, lane.scale);

        var platform = cloud.GetComponent<CloudPlatform>();
        if (platform == null) platform = cloud.AddComponent<CloudPlatform>();
        platform.SetCloudManager(this);
        platform.SetMovementSpeed(lane.speed);
        platform.laneIndex = lane.index;
        platform.isPooled = true;

        // Position the cloud so its leading edge (the face entering the window) sits at the
        // entry boundary. We need the actual bounds half-width, so place at origin first.
        cloud.transform.position = new Vector3(0f, spawnY, 0f);
        Bounds bounds = platform.GetBounds();
        float halfWidth = bounds.extents.x;

        // Entry X: leading edge at the entry boundary, plus a random stagger gap behind it.
        // Moving right (+speed): leading edge is the left (min.x) side → center = entryEdge + halfWidth
        // Moving left  (-speed): leading edge is the right (max.x) side → center = entryEdge - halfWidth
        float gap = Random.Range(0f, settings.recycleReentryMaxGap);
        float centerX = lane.speed >= 0f
            ? (windowLeft  - gap) + halfWidth
            : (windowRight + gap) - halfWidth;

        spawnPos = new Vector2(centerX, spawnY);

        while (IsPositionInBlockSpawnZone(spawnPos) && retries < settings.maxSpawnRetries)
        {
            float nudge = Random.Range(0.5f, 2f) * (lane.speed >= 0f ? -1f : 1f);
            spawnPos.x += nudge;
            spawnPos.y = lane.worldY + Random.Range(-settings.laneHeightVariation, settings.laneHeightVariation);
            retries++;
        }
        if (retries >= settings.maxSpawnRetries)
        {
            ReturnCloudToPool(cloud);
            return;
        }

        cloud.transform.position = new Vector3(spawnPos.x, spawnPos.y, 0f);

        _onCloudActivated?.Invoke(cloud, lane.scale);
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

        if (!_pool.Contains(cloud) && !_nonPooled.Contains(cloud))
        {
            Debug.LogWarning("Attempted to deactivate a cloud this not managed by the CloudManager, might wanna check that out: " + cloud.name);
        }

        if (_pool.Contains(cloud)) {
            ReturnCloudToPool(cloud); 
            return;
        }

        if (_active.Contains(cloud))
        {
            _active.Remove(cloud);
            _onCloudDeactivated?.Invoke(cloud);
        }
    }

    /// <summary>Teleport a cloud back to the offscreen entry side of its lane (recirculate).</summary>
    void RecycleCloudToEntry(LaneState lane, GameObject cloud, float windowLeft, float windowRight)
    {
        var platform = cloud.GetComponent<CloudPlatform>();
        float halfWidth = platform != null ? platform.GetBounds().extents.x : 0f;

        float gap = Random.Range(0f, settings.recycleReentryMaxGap);
        float centerX = lane.speed >= 0f
            ? (windowLeft  - gap) + halfWidth
            : (windowRight + gap) - halfWidth;

        float newY = lane.worldY + Random.Range(-settings.laneHeightVariation, settings.laneHeightVariation);
        cloud.transform.position = new Vector3(centerX, newY, cloud.transform.position.z);
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

        // Offline / non-networked: return to pool
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
}
