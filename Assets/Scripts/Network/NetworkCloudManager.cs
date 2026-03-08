using System;
using FishNet;
using FishNet.Object;
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
/// This replaces the previous manual RPC/dictionary/position-sync approach.
/// </summary>
public class NetworkCloudManager : NetworkBehaviour
{
    CloudManager _cloudManager;

    // Prevents OnStartClient from re-disabling CloudManager after offline fallback
    bool _offlineMode;

    // Cached flags — IsServerStarted/IsClientStarted crash in offline mode when
    // the NetworkObject's internal manager is null
    bool _serverRunning;

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
        _cloudManager._onCloudActivated = (go, scale) =>
        {
            go.transform.SetParent(null);  // FishNet requires root-level NetworkObject at Spawn
            var nob = go.GetComponent<NetworkObject>();
            if (nob != null)
            {
                InstanceFinder.ServerManager.Spawn(nob);
                var nc = go.GetComponent<NetworkCloud>();
                if (nc != null) nc.SyncScale(scale);
            }
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
        _cloudManager._onCloudActivated = (go, scale) =>
        {
            foreach (var nb in go.GetComponentsInChildren<NetworkBehaviour>(true))
                DestroyImmediate(nb);
            var nob = go.GetComponent<NetworkObject>();
            if (nob != null) DestroyImmediate(nob);
        };
        _cloudManager._onCloudDeactivated = null;  // pool path handles it
    }

    // ── Server lifecycle ──────────────────────────────────────────────────────

    public override void OnStartServer()
    {
        base.OnStartServer();
        _serverRunning = true;
        if (_cloudManager != null)
        {
            SetServerDelegates();
            _cloudManager.enabled = true;
        }
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        _serverRunning = false;
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
            _cloudManager.CollectSceneClouds();
            SetOfflineDelegates();
            _cloudManager.enabled = true;
        }
    }
}
