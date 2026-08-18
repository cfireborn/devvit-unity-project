using System;
using System.Collections.Generic;
using FishNet;
using FishNet.Object;
using FishNet.Utility.Performance;
using UnityEngine;

/// <summary>
/// Attach to the CloudManager GameObject (alongside CloudManager component).
/// Also add a NetworkObject component to that GameObject.
///
/// Responsibilities:
/// - Server: enables CloudManager so it can spawn clouds as NetworkObjects.
///           FishNet replicates each cloud's NetworkObject to all clients automatically.
///           NetworkTransform on each cloud GO syncs position at ~20Hz.
/// - Clients: disables CloudManager. FishNet's network spawn system instantiates clouds
///            on clients when the server spawns them. NetworkCloud.OnStartClient()
///            disables CloudPlatform and sets Rigidbody2D to Kinematic on pure clients.
/// - Offline fallback: ActivateOfflineMode() re-enables CloudManager for local pooling.
///
/// Server and offline modes are distinguished by which delegates are set on CloudManager;
/// the component is disabled until OnStartServer or ActivateOfflineMode enables it.
/// </summary>
public class NetworkCloudManager : NetworkBehaviour
{
    CloudManager _cloudManager;

    // Prevents OnStartClient from re-disabling CloudManager after offline fallback
    bool _offlineMode;

    // Cached flags — IsServerStarted/IsClientStarted crash in offline mode when
    // the NetworkObject's internal manager is null
    bool _serverRunning;
    bool _loggedFirstServerCloud;
    int _nextServerPoolWarning = 50;
    readonly HashSet<(ushort collectionId, int prefabId)> _cloudPoolKeys = new();

    void Awake()
    {
        _cloudManager = GetComponent<CloudManager>();

        if (_cloudManager == null)
        {
            Debug.LogError("NetworkCloudManager requires a CloudManager component on the same GameObject.");
            enabled = false;
            return;
        }

        _cloudManager.CollectSceneClouds();
        SetOfflineDelegates();

        // Disable CloudManager immediately in a network context.
        // CloudManager.Start() would run before OnStartServer/OnStartClient and cause
        // both host and client to spawn independent clouds.
        // OnStartServer() re-enables it for the server only.
        if (_cloudManager != null && InstanceFinder.NetworkManager != null)
            _cloudManager.enabled = false;
    }

    // ── Delegate injection ────────────────────────────────────────────────────

    void SetServerDelegates()
    {
        _cloudManager._acquireCloudInstance = (prefab, parent) =>
        {
            var nob = InstanceFinder.NetworkManager.GetPooledInstantiated(prefab, parent, asServer: true);
            return nob != null ? nob.gameObject : null;
        };
        _cloudManager._onCloudActivated = (go, scale) =>
        {
            var nob = go.GetComponent<NetworkObject>();
            if (nob != null)
            {
                if (!nob.IsSceneObject)
                {
                    go.transform.SetParent(null);  // Runtime-spawned NetworkObjects must be root-level.
                    InstanceFinder.ServerManager.Spawn(nob);
                }
                if (nob.IsSpawned)
                {
                    var nc = go.GetComponent<NetworkCloud>();
                    if (nc != null) nc.SyncScale(scale);

                    if (!_loggedFirstServerCloud && _cloudManager.IsDynamicCloud(go))
                    {
                        _loggedFirstServerCloud = true;
                        Debug.Log($"NetworkCloudManager: spawned first server cloud '{go.name}' " +
                            $"(players={_cloudManager.RegisteredPlayerCount}, activeLanes={_cloudManager.ActiveLaneCount}).");
                    }
                }
                else
                    Debug.LogError($"NetworkCloudManager: FishNet did not spawn cloud '{go.name}'.");
            }
            else
                Debug.LogError($"NetworkCloudManager: cloud '{go.name}' has no NetworkObject and cannot replicate.");
        };
        _cloudManager._onCloudDeactivated = go =>
        {
            var nob = go.GetComponent<NetworkObject>();
            if (nob != null && nob.IsSpawned) InstanceFinder.ServerManager.Despawn(nob);
            else Destroy(go);
        };
    }

    void SetOfflineDelegates()
    {
        _cloudManager._acquireCloudInstance = null;
        _cloudManager._onCloudActivated = (go, scale) => NetworkOfflineUtil.StripNetworkComponents(go);
        _cloudManager._onCloudDeactivated = null;  // pool path handles it
    }

    void Update()
    {
        if (!_serverRunning) return;

        var networkManager = InstanceFinder.NetworkManager;
        var defaultPool = networkManager != null ? networkManager.ObjectPool as DefaultObjectPool : null;
        if (defaultPool == null || _cloudManager == null || _cloudManager.cloudPrefabs == null) return;

        _cloudPoolKeys.Clear();
        int retainedClouds = 0;
        foreach (GameObject prefab in _cloudManager.cloudPrefabs)
        {
            var nob = prefab != null ? prefab.GetComponent<NetworkObject>() : null;
            if (nob == null) continue;

            (ushort collectionId, int prefabId) key = (nob.SpawnableCollectionId, nob.PrefabId);
            if (!_cloudPoolKeys.Add(key)) continue;

            var cache = defaultPool.GetCache(key.collectionId, key.prefabId, createIfMissing: false);
            if (cache != null) retainedClouds += cache.Count;
        }

        while (retainedClouds >= _nextServerPoolWarning)
        {
            Debug.LogWarning($"[Info] Cloud object pool reached {_nextServerPoolWarning} retained clouds.");
            _nextServerPoolWarning += 50;
        }
    }

    // ── Server lifecycle ──────────────────────────────────────────────────────

    public override void OnStartServer()
    {
        base.OnStartServer();
        _serverRunning = true;
        _loggedFirstServerCloud = false;
        if (_cloudManager != null)
        {
            SetServerDelegates();
            _cloudManager.enabled = true;
            if (_cloudManager.settings == null || _cloudManager.cloudPrefabs == null || _cloudManager.cloudPrefabs.Length == 0)
                Debug.LogError("NetworkCloudManager: server cloud simulation is missing settings or cloud prefabs.");
            else
                Debug.Log("NetworkCloudManager: server cloud simulation enabled; lanes will activate when a server player registers.");
        }
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        _serverRunning = false;
        if (_cloudManager != null && !_offlineMode)
            _cloudManager.enabled = false;
    }

    // ── Client lifecycle ──────────────────────────────────────────────────────

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (!_serverRunning && !_offlineMode)
        {
            // Pure client: FishNet replicates cloud NetworkObjects from server
            if (_cloudManager != null) _cloudManager.enabled = false;
        }
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        // FishNet automatically despawns all NetworkObjects when client disconnects —
        // no manual cleanup needed here.
    }

    // ── Offline fallback ──────────────────────────────────────────────────────

    /// <summary>
    /// Called by GameManagerM when the network connection times out.
    /// Re-enables CloudManager for local single-player cloud spawning,
    /// and prevents OnStartClient from disabling it again if it fires late.
    /// </summary>
    public void ActivateOfflineMode()
    {
        _offlineMode = true;

        // If the whole GameObject was disabled in the scene, bring it back
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        // Re-acquire reference in case Awake never ran (GO was inactive at start)
        if (_cloudManager == null)
            _cloudManager = GetComponent<CloudManager>();

        if (_cloudManager != null)
        {
            RestoreSceneCloudsForOffline();
            _cloudManager.CollectSceneClouds();
            SetOfflineDelegates();
            _cloudManager.enabled = true;
        }
    }

    void RestoreSceneCloudsForOffline()
    {
        var sceneClouds = FindObjectsByType<CloudPlatform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var cloud in sceneClouds)
        {
            if (!cloud.wasActiveAtStart || cloud.pooledSourcePrefab != null) continue;

            NetworkOfflineUtil.StripNetworkComponents(cloud.gameObject);
            cloud.enabled = true;
            cloud.gameObject.SetActive(true);
        }
    }
}
