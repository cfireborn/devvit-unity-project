using FishNet;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Attach to the CloudLadderController GameObject (alongside CloudLadderController component).
/// Also add a NetworkObject component to that GameObject.
///
/// Responsibilities:
/// - Server: enables CloudLadderController so it manages ladders normally.
///           CreateLadder/DespawnLadder call ServerManager.Spawn/Despawn — FishNet
///           replicates each ladder NetworkObject to all clients automatically.
///           NetworkLadder.SyncCloudIds() is called right after Spawn so every client
///           knows which two clouds the ladder bridges (BufferLast covers late joiners).
/// - Clients: disables CloudLadderController. FishNet spawns ladder GOs automatically.
///            Each LateUpdate, iterates spawned NetworkLadder instances and calls
///            UpdateLadderPosition() so visuals and collider are rebuilt from already-
///            synced cloud positions. No separate position sync or manual dict needed.
/// - Offline fallback: ActivateOfflineMode() re-enables CloudLadderController.
///
/// Late-joiner sync is handled entirely by FishNet:
/// - Ladder NetworkObjects are re-spawned automatically on connect.
/// - NetworkLadder.SyncCloudIds BufferLast RPC delivers cloud IDs on connect.
/// No manual TargetRpc sync pass needed.
/// </summary>
public class NetworkCloudLadderController : NetworkBehaviour
{
    CloudLadderController _ladderController;

    bool _serverRunning;
    bool _clientRunning;
    bool _offlineMode;

    void Awake()
    {
        _ladderController = GetComponent<CloudLadderController>();

        // Disable CloudLadderController immediately in a network context.
        // OnStartServer() re-enables it for the server only.
        if (_ladderController != null && InstanceFinder.NetworkManager != null)
            _ladderController.enabled = false;
    }

    // ── Server lifecycle ──────────────────────────────────────────────────────

    public override void OnStartServer()
    {
        base.OnStartServer();
        _serverRunning = true;
        if (_ladderController != null) _ladderController.enabled = true;
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
        _clientRunning = true;
        if (!_serverRunning && !_offlineMode)
            if (_ladderController != null) _ladderController.enabled = false;
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        _clientRunning = false;
        // FishNet automatically destroys all spawned NetworkObjects (ladders) on disconnect
    }

    // ── Offline fallback ──────────────────────────────────────────────────────

    /// <summary>
    /// Called by GameManagerM when the network connection times out.
    /// Re-enables CloudLadderController for offline single-player.
    /// </summary>
    public void ActivateOfflineMode()
    {
        _offlineMode = true;
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);
        if (_ladderController == null)
            _ladderController = GetComponent<CloudLadderController>();
        if (_ladderController != null)
            _ladderController.enabled = true;
    }

    // ── LateUpdate: rebuild ladder geometry from synced cloud positions ───────

    void LateUpdate()
    {
        // Server's CloudLadderController handles its own geometry each frame.
        // Offline mode: CloudLadderController runs natively, no client work needed.
        if (_serverRunning || !_clientRunning || _offlineMode) return;
        if (_ladderController == null) return;

        var spawned = InstanceFinder.ClientManager.Objects.Spawned;

        foreach (var kvp in spawned)
        {
            var nl = kvp.Value.GetComponent<NetworkLadder>();
            if (nl == null || nl.CloudAObjectId < 0 || nl.CloudBObjectId < 0) continue;

            // Look up the two cloud GOs via FishNet's spawned registry
            if (!spawned.TryGetValue(nl.CloudAObjectId, out NetworkObject cloudANob) || cloudANob == null) continue;
            if (!spawned.TryGetValue(nl.CloudBObjectId, out NetworkObject cloudBNob) || cloudBNob == null) continue;

            var platformA = cloudANob.GetComponent<CloudPlatform>();
            var platformB = cloudBNob.GetComponent<CloudPlatform>();
            if (platformA == null || platformB == null) continue;

            // Ensure lower/upper ordering (mirrors CloudLadderController.OrderPair)
            CloudPlatform lower, upper;
            if (platformA.GetMainBounds().min.y <= platformB.GetMainBounds().min.y)
                (lower, upper) = (platformA, platformB);
            else
                (lower, upper) = (platformB, platformA);

            _ladderController.UpdateLadderPosition(lower, upper, kvp.Value.gameObject);
        }
    }
}
