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

    // Server-side position sync timer
    float _syncTimer;
    const float SyncInterval = 0.1f; // 10Hz

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
        // Clean up any locally created cloud objects
        foreach (var go in _clientClouds.Values)
        {
            if (go != null) Destroy(go);
        }
        _clientClouds.Clear();
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

    /// <summary>Tells all clients to spawn a cloud.</summary>
    [ObserversRpc(ExcludeServer = true)]
    void RpcSpawnCloud(int id, int prefabIdx, Vector2 pos, float scale, float speed)
    {
        ClientSpawnCloud(id, prefabIdx, pos, scale, speed);
    }

    /// <summary>Tells all clients to despawn a cloud.</summary>
    [ObserversRpc(ExcludeServer = true)]
    void RpcDespawnCloud(int id)
    {
        ClientDespawnCloud(id);
    }

    /// <summary>Periodic position corrections for all active clouds.</summary>
    [ObserversRpc(ExcludeServer = true)]
    void RpcSyncPositions(int[] ids, Vector2[] positions)
    {
        for (int i = 0; i < ids.Length; i++)
        {
            if (_clientClouds.TryGetValue(ids[i], out GameObject go) && go != null)
            {
                // Snap if too far off, otherwise lerp gently
                Vector2 current = go.transform.position;
                float error = Vector2.Distance(current, positions[i]);
                if (error > 2f)
                    go.transform.position = positions[i];
                else if (error > 0.05f)
                    go.transform.position = Vector2.Lerp(current, positions[i], 0.3f);
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

        var prefabs = _cloudManager.cloudPrefabs;
        if (prefabs == null || prefabIdx < 0 || prefabIdx >= prefabs.Length) return;

        GameObject cloud = Instantiate(prefabs[prefabIdx], pos, Quaternion.identity);
        cloud.transform.localScale = new Vector3(scale, scale, scale);

        var platform = cloud.GetComponent<CloudPlatform>();
        if (platform != null)
        {
            platform.SetMovementSpeed(speed);
            platform.ignoreNoSpawnZones = true; // clients don't self-despawn
            // No CloudManager reference — server controls lifecycle
        }

        _clientClouds[id] = cloud;
    }

    void ClientDespawnCloud(int id)
    {
        if (_clientClouds.TryGetValue(id, out GameObject go))
        {
            if (go != null) Destroy(go);
            _clientClouds.Remove(id);
        }
    }
}
