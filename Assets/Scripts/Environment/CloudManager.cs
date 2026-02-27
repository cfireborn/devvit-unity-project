using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns and despawns cloud platforms around the player. Uses distance threshold for updates
/// so faster player movement triggers more frequent spawn/despawn checks.
/// </summary>
public class CloudManager : MonoBehaviour
{
    [Header("References")]
    public Transform player;
    public CloudLadderController cloudLadderController;
    [Tooltip("Cloud prefabs to spawn from. Each should have CloudPlatform component.")]
    public GameObject[] cloudPrefabs;

    [Header("Spawn/Despawn")]
    [Tooltip("Spawn clouds within this distance of the player.")]
    public float spawnRadius = 20f;
    [Tooltip("Despawn clouds beyond this distance from the player.")]
    public float despawnRadius = 25f;
    [Tooltip("Maximum number of active clouds.")]
    public int maxClouds = 15;
    [Tooltip("Only run spawn/despawn checks when player has moved at least this far.")]
    public float distanceThresholdForUpdate = 2f;
    [Tooltip("Do not spawn a cloud if its position would be within this distance of any existing cloud's bounds.")]
    public float minDistanceFromOtherClouds = 1f;

    [Header("Variation")]
    public Vector2 speedRange = new Vector2(-2f, 2f);
    public Vector2 scaleRange = new Vector2(0.8f, 1.2f);
    [Tooltip("Max retries when spawn position is in blockSpawn zone.")]
    public int maxSpawnRetries = 10;

    // ----- Network Events -----
    /// <summary>Fired on server when a cloud is spawned. Args: (networkId, prefabIndex, position, scale, speed)</summary>
    public event System.Action<int, int, Vector2, float, float> OnCloudSpawned;
    /// <summary>Fired on server when a cloud is returned to pool. Args: (networkId)</summary>
    public event System.Action<int> OnCloudDespawned;

    readonly Queue<GameObject> _pool = new Queue<GameObject>();
    readonly List<CloudNoSpawnZone> _blockSpawnZones = new List<CloudNoSpawnZone>();
    readonly List<GameObject> _active = new List<GameObject>();
    List<GameObject> _nonPooled = new List<GameObject>();
    Vector3 _lastUpdatePosition;
    Transform _poolParent;
    bool _forceUpdate = false;

    // Network ID tracking (server-side only)
    readonly Dictionary<GameObject, int> _cloudNetIds = new Dictionary<GameObject, int>();
    readonly Dictionary<int, GameObject> _idToCloud = new Dictionary<int, GameObject>();
    int _nextNetId = 0;

    void Start()
    {
        TryRegisterPlayer();
        if (player == null)
        {
            var gameServices = FindFirstObjectByType<GameServices>();
            if (gameServices != null)
            {
                gameServices.onPlayerRegistered += TryRegisterPlayer;
            }
        }

        var gameServicesForLadder = FindFirstObjectByType<GameServices>();
        if (gameServicesForLadder != null && cloudLadderController != null)
            gameServicesForLadder.RegisterCloudLadderController(cloudLadderController);

        _poolParent = new GameObject("CloudPool").transform;
        _poolParent.SetParent(transform);

        CloudPlatform[] clouds = Object.FindObjectsByType<CloudPlatform>(FindObjectsSortMode.None);
        foreach (CloudPlatform cloud in clouds)
        {
            if (!_nonPooled.Contains(cloud.gameObject))
            {
                _nonPooled.Add(cloud.gameObject);
            }

            if (!_active.Contains(cloud.gameObject))
            {
                _active.Add(cloud.gameObject);
            }
        }
    }

    void Update()
    {
        if (player == null || cloudPrefabs == null || cloudPrefabs.Length == 0) return;

        float dist = Vector3.Distance(player.position, _lastUpdatePosition);
        if (dist < distanceThresholdForUpdate && !_forceUpdate) return;

        _lastUpdatePosition = player.position;
        _forceUpdate = false;

        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var cloud = _active[i];
            if (cloud == null) { _active.RemoveAt(i); continue; }
            float d = Vector3.Distance(player.position, cloud.transform.position);
            if (d > despawnRadius && !_nonPooled.Contains(cloud))
            {
                ReturnCloudToPool(cloud);
            }
        }

        int maxSpawnAttempts = maxClouds - _nonPooled.Count * 3;
        while ((_active.Count - _nonPooled.Count < maxClouds) && maxSpawnAttempts > 0)
        {
            SpawnCloud();
            maxSpawnAttempts--;
        }
    }

    void OnDestroy()
    {
        var gameServices = FindFirstObjectByType<GameServices>();
        if (gameServices != null)
            gameServices.onPlayerRegistered -= TryRegisterPlayer;
    }

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

    bool IsPositionTooCloseToExistingClouds(Vector2 pos)
    {
        if (minDistanceFromOtherClouds <= 0f) return false;
        Vector3 pos3 = new Vector3(pos.x, pos.y, 0f);
        foreach (var go in _active)
        {
            if (go == null) continue;
            var platform = go.GetComponent<CloudPlatform>();
            if (platform == null) continue;
            Bounds b = platform.GetBounds();
            Vector3 closest = b.ClosestPoint(pos3);
            if (Vector2.Distance(pos, new Vector2(closest.x, closest.y)) < minDistanceFromOtherClouds)
                return true;
        }
        return false;
    }

    void TryRegisterPlayer()
    {
        var gameServices = FindFirstObjectByType<GameServices>();
        if (gameServices != null)
        {
            var p = gameServices.GetPlayer();
            if (p != null) 
            {
                player = p.transform;
                _lastUpdatePosition = player.position;    
                _forceUpdate = true;
            }
        }
    }

    public void ReturnCloudToPool(GameObject cloud)
    {
        // Fire network despawn event before removing from tracking
        if (_cloudNetIds.TryGetValue(cloud, out int netId))
        {
            OnCloudDespawned?.Invoke(netId);
            _cloudNetIds.Remove(cloud);
            _idToCloud.Remove(netId);
        }

        _active.Remove(cloud);
        cloud.SetActive(false);
        cloud.transform.SetParent(_poolParent);
        _pool.Enqueue(cloud);
    }

    /// <summary>Returns current cloud state for initial sync to newly connected clients.</summary>
    public List<NetworkCloudState> GetNetworkCloudStates()
    {
        var states = new List<NetworkCloudState>();
        foreach (var kvp in _cloudNetIds)
        {
            var go = kvp.Key;
            if (go == null || !go.activeSelf) continue;
            var platform = go.GetComponent<CloudPlatform>();
            if (platform == null) continue;
            states.Add(new NetworkCloudState
            {
                id = kvp.Value,
                prefabIndex = platform.networkPrefabIndex,
                position = go.transform.position,
                scale = go.transform.localScale.x,
                speed = platform.moveSpeed
            });
        }
        return states;
    }

    /// <summary>Returns all active cloud IDs and their current positions (for periodic sync).</summary>
    public void GetCloudPositions(out int[] ids, out Vector2[] positions)
    {
        ids = new int[_cloudNetIds.Count];
        positions = new Vector2[_cloudNetIds.Count];
        int i = 0;
        foreach (var kvp in _cloudNetIds)
        {
            ids[i] = kvp.Value;
            positions[i] = kvp.Key != null ? (Vector2)kvp.Key.transform.position : Vector2.zero;
            i++;
        }
    }

    void SpawnCloud()
    {
        var prefab = cloudPrefabs[Random.Range(0, cloudPrefabs.Length)];
        if (prefab == null) return;

        Vector2 spawnPos;
        int retries = 0;
        do
        {
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float r = Random.Range(spawnRadius * 0.3f, spawnRadius);
            Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
            spawnPos = (Vector2)player.position + offset;
            if (!IsPositionInBlockSpawnZone(spawnPos) && !IsPositionTooCloseToExistingClouds(spawnPos))
                break;
            retries++;
        }
        while (retries < maxSpawnRetries);

        if (retries >= maxSpawnRetries)
            return;

        int prefabIdx = System.Array.IndexOf(cloudPrefabs, prefab);

        GameObject cloud;
        if (_pool.Count > 0)
        {
            cloud = _pool.Dequeue();
            cloud.SetActive(true);
        }
        else
        {
            cloud = Instantiate(prefab, _poolParent);
        }

        cloud.transform.position = spawnPos;

        float scale = Random.Range(scaleRange.x, scaleRange.y);
        cloud.transform.localScale = new Vector3(scale, scale, scale);

        var platform = cloud.GetComponent<CloudPlatform>();
        if (platform == null)
            platform = cloud.AddComponent<CloudPlatform>();

        // Track prefab index on first creation (pool reuse preserves this)
        if (platform.networkPrefabIndex == 0 && prefabIdx > 0)
            platform.networkPrefabIndex = prefabIdx;
        else if (_pool.Count == 0) // freshly instantiated
            platform.networkPrefabIndex = prefabIdx;

        platform.SetCloudManager(this);
        float speed = Random.Range(speedRange.x, speedRange.y);
        platform.SetMovementSpeed(speed);
        platform.isPooled = true;
        _active.Add(cloud);

        // Assign network ID and fire event (NetworkCloudManager listens on server)
        int netId = _nextNetId++;
        _cloudNetIds[cloud] = netId;
        _idToCloud[netId] = cloud;
        OnCloudSpawned?.Invoke(netId, platform.networkPrefabIndex, spawnPos, scale, speed);
    }

    /// <summary>Get all currently active clouds. Used by CloudLadderController.</summary>
    public IReadOnlyList<GameObject> GetActiveClouds()
    {
        return _active;
    }
}
