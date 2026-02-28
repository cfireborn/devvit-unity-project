using System.Collections.Generic;
using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

/// <summary>
/// Struct used for initial cloud sync when a new client connects.
/// FishNet auto-serializes structs with primitive/Vector2 fields.
/// </summary>
public struct NetworkCloudState
{
    public int id;
    public int prefabIndex;
    public Vector2 position;
    public float scale;
    public float speed;
}

/// <summary>
/// Attach to the CloudManager GameObject (alongside CloudManager component).
/// Also add a NetworkObject component to that GameObject.
///
/// Responsibilities:
/// - Server: runs CloudManager, listens for spawn/despawn events, broadcasts to clients.
/// - Clients: disables CloudManager, receives RPCs and manages a local cloud dictionary.
/// - Late joiners: sends current cloud state when a new client connects.
/// - Periodic position sync at 10Hz to keep clients in sync with moving clouds.
/// </summary>
public class NetworkCloudManager : NetworkBehaviour
{
    CloudManager _cloudManager;

    // Client-side: maps network cloud ID → locally instantiated cloud GameObject
    readonly Dictionary<int, GameObject> _clientClouds = new Dictionary<int, GameObject>();

    // Host-only: IDs whose GameObjects are owned by CloudManager (don't Destroy them on despawn)
    readonly HashSet<int> _serverManagedClouds = new HashSet<int>();

    // Server-side position sync timer
    float _syncTimer;
    const float SyncInterval = 0.1f; // 10Hz

    // Client-side: maps cloud ID → stored move speed (CloudPlatform disabled on client)
    readonly Dictionary<int, float> _clientCloudSpeeds = new Dictionary<int, float>();

    void Awake()
    {
        _cloudManager = GetComponent<CloudManager>();

        // Disable CloudManager immediately if we're in a network context.
        // CloudManager.Start() would run before OnStartServer/OnStartClient fire,
        // causing both host and client to spawn their own independent clouds.
        // OnStartServer() will re-enable it for the server only.
        if (_cloudManager != null && InstanceFinder.NetworkManager != null)
            _cloudManager.enabled = false;
    }

    // ── Server lifecycle ──────────────────────────────────────────────────────

    public override void OnStartServer()
    {
        base.OnStartServer();
        _cloudManager.enabled = true;
        _cloudManager.OnCloudSpawned += ServerOnCloudSpawned;
        _cloudManager.OnCloudDespawned += ServerOnCloudDespawned;

        // Listen for new clients connecting so we can send them current cloud state
        InstanceFinder.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        _cloudManager.OnCloudSpawned -= ServerOnCloudSpawned;
        _cloudManager.OnCloudDespawned -= ServerOnCloudDespawned;

        if (InstanceFinder.NetworkManager != null)
            InstanceFinder.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
    }

    // ── Client lifecycle ─────────────────────────────────────────────────────

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (!IsServerStarted)
        {
            // Pure client: disable CloudManager — server drives all clouds
            _cloudManager.enabled = false;
        }
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        foreach (var kvp in _clientClouds)
        {
            // Only destroy clouds we created — server-managed ones belong to CloudManager
            if (!_serverManagedClouds.Contains(kvp.Key) && kvp.Value != null)
                Destroy(kvp.Value);
        }
        _clientClouds.Clear();
        _clientCloudSpeeds.Clear();
        _serverManagedClouds.Clear();
    }

    // ── Update (server-side position sync) ───────────────────────────────────

    void Update()
    {
        if (!IsServerStarted) return;

        _syncTimer += Time.deltaTime;
        if (_syncTimer >= SyncInterval)
        {
            _syncTimer = 0f;
            _cloudManager.GetCloudPositions(out int[] ids, out Vector2[] positions);
            if (ids.Length > 0)
                RpcSyncPositions(ids, positions);
        }
    }

    void FixedUpdate()
    {
        // Only pure clients drive kinematic clouds — server has CloudPlatform running natively
        if (IsServerStarted) return;

        foreach (var kvp in _clientClouds)
        {
            var go = kvp.Value;
            if (go == null) continue;
            if (!_clientCloudSpeeds.TryGetValue(kvp.Key, out float speed)) continue;
            if (speed == 0f) continue;

            var rb = go.GetComponent<Rigidbody2D>();
            if (rb != null)
                rb.MovePosition(rb.position + new Vector2(speed * Time.fixedDeltaTime, 0f));
        }
    }

    // ── Late joiner sync ──────────────────────────────────────────────────────

    void OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
    {
        if (args.ConnectionState != RemoteConnectionState.Started) return;

        var states = _cloudManager.GetNetworkCloudStates();
        if (states.Count == 0) return;

        TargetSyncAllClouds(conn, states.ToArray());
        Debug.Log($"NetworkCloudManager: Sent {states.Count} clouds to new client {conn.ClientId}.");
    }

    // ── Server event handlers ─────────────────────────────────────────────────

    void ServerOnCloudSpawned(int id, int prefabIdx, Vector2 pos, float scale, float speed)
    {
        RpcSpawnCloud(id, prefabIdx, pos, scale, speed);
    }

    void ServerOnCloudDespawned(int id)
    {
        RpcDespawnCloud(id);
    }

    // ── RPCs (server → clients) ───────────────────────────────────────────────

    /// <summary>Tells all observers (including host) to register a cloud.</summary>
    [ObserversRpc]
    void RpcSpawnCloud(int id, int prefabIdx, Vector2 pos, float scale, float speed)
    {
        ClientSpawnCloud(id, prefabIdx, pos, scale, speed);
    }

    /// <summary>Tells all observers (including host) to unregister a cloud.</summary>
    [ObserversRpc]
    void RpcDespawnCloud(int id)
    {
        ClientDespawnCloud(id);
    }

    /// <summary>Periodic position corrections for all active clouds.</summary>
    [ObserversRpc]
    void RpcSyncPositions(int[] ids, Vector2[] positions)
    {
        for (int i = 0; i < ids.Length; i++)
        {
            // Host's clouds are authoritative — don't correct them
            if (_serverManagedClouds.Contains(ids[i])) continue;
            if (!_clientClouds.TryGetValue(ids[i], out GameObject go) || go == null) continue;

            var rb = go.GetComponent<Rigidbody2D>();
            Vector2 current = rb != null ? rb.position : (Vector2)go.transform.position;
            float error = Vector2.Distance(current, positions[i]);

            // Only correct if meaningfully drifted — avoids fighting with MovePosition
            if (error > 1.5f)
            {
                if (rb != null) rb.MovePosition(positions[i]);
                else go.transform.position = positions[i];
            }
        }
    }

    /// <summary>Sends the full current cloud state to a single newly connected client.</summary>
    [TargetRpc]
    void TargetSyncAllClouds(NetworkConnection conn, NetworkCloudState[] states)
    {
        foreach (var s in states)
            ClientSpawnCloud(s.id, s.prefabIndex, s.position, s.scale, s.speed);
    }

    // ── Client helpers ────────────────────────────────────────────────────────

    void ClientSpawnCloud(int id, int prefabIdx, Vector2 pos, float scale, float speed)
    {
        if (_clientClouds.ContainsKey(id)) return; // already exists

        // Host: the cloud already exists in CloudManager — just borrow the reference.
        if (IsServerStarted)
        {
            if (_cloudManager.TryGetCloudById(id, out GameObject existing))
            {
                _clientClouds[id] = existing;
                _serverManagedClouds.Add(id);
                // No speed entry needed — CloudPlatform drives it natively on server
            }
            return;
        }

        var prefabs = _cloudManager.cloudPrefabs;
        if (prefabs == null || prefabIdx < 0 || prefabIdx >= prefabs.Length) return;

        GameObject cloud = Instantiate(prefabs[prefabIdx], pos, Quaternion.identity);
        cloud.transform.localScale = new Vector3(scale, scale, scale);

        // Disable CloudPlatform — it would fight Rigidbody2D with linearVelocity.
        // We drive movement ourselves via MovePosition in FixedUpdate.
        var platform = cloud.GetComponent<CloudPlatform>();
        if (platform != null)
            platform.enabled = false;

        // Kinematic: not simulated by physics engine, moved via MovePosition.
        // This prevents the cloud from pushing/dragging the player unpredictably.
        var rb = cloud.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        _clientClouds[id] = cloud;
        _clientCloudSpeeds[id] = speed;
    }

    void ClientDespawnCloud(int id)
    {
        if (_clientClouds.TryGetValue(id, out GameObject go))
        {
            // Server-managed clouds belong to CloudManager — don't Destroy them here
            if (!_serverManagedClouds.Contains(id) && go != null)
                Destroy(go);
            _clientClouds.Remove(id);
        }
        _serverManagedClouds.Remove(id);
        _clientCloudSpeeds.Remove(id);
    }
}
