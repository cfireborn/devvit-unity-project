using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Attach to the ladder prefab alongside NetworkObject.
/// (No NetworkTransform needed — position is re-derived from cloud positions
///  every LateUpdate by NetworkCloudLadderController on clients.)
///
/// Stores which two cloud NetworkObjects this ladder bridges so clients
/// can reconstruct the correct geometry without any extra RPCs.
///
/// SyncCloudIds uses BufferLast = true so late-joining clients automatically
/// receive the correct cloud IDs without a separate late-joiner sync pass.
/// (Same pattern as NetworkCloud.SyncScale — FishNet v4 equivalent of SyncVar.)
///
/// INSPECTOR SETUP REQUIRED:
/// - Add NetworkObject + NetworkLadder to the ladder prefab.
/// - Register the ladder prefab in NetworkManager's Spawnable Prefabs list.
/// - Do NOT add NetworkTransform — position is client-derived from cloud positions.
/// </summary>
public class NetworkLadder : NetworkBehaviour
{
    static readonly HashSet<NetworkLadder> _activeClientLadders = new HashSet<NetworkLadder>();
    internal static HashSet<NetworkLadder> ActiveClientLadders => _activeClientLadders;

    SpriteRenderer _rootRenderer;
    BoxCollider2D _rootCollider;
    MovingPlatformLadder _presentation;
    bool _presentationInitialized;
    bool _presentationActive;

    /// <summary>FishNet ObjectId of the lower cloud. Set by server via SyncCloudIds.</summary>
    public int CloudAObjectId { get; private set; } = -1;

    /// <summary>FishNet ObjectId of the upper cloud. Set by server via SyncCloudIds.</summary>
    public int CloudBObjectId { get; private set; } = -1;

    void Awake()
    {
        _rootRenderer = GetComponent<SpriteRenderer>();
        _rootCollider = GetComponent<BoxCollider2D>();
        _presentation = GetComponent<MovingPlatformLadder>();
        if (_presentation == null)
            _presentation = gameObject.AddComponent<MovingPlatformLadder>();

        // The prefab sprite is an authoring placeholder. Runtime geometry is built
        // exclusively from Bottom/Middle/Top children on both server and clients.
        if (_rootRenderer != null)
            _rootRenderer.enabled = false;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        _activeClientLadders.Add(this);

        // Hosts already have authoritative geometry and collision. Pure clients
        // must fail closed until the buffered endpoint RPC can be resolved.
        if (!IsServerStarted)
            SetPresentationActive(false);
    }

    public override void OnStopClient()
    {
        _activeClientLadders.Remove(this);
        if (!IsServerStarted)
            SetPresentationActive(false);
        base.OnStopClient();
    }

    void OnDestroy()
    {
        _activeClientLadders.Remove(this);
    }

    /// <summary>
    /// Called by CloudLadderController on the server right after ServerManager.Spawn().
    /// BufferLast = true ensures late-joining clients receive the correct cloud IDs.
    /// RunLocally = true applies it on the host too.
    /// </summary>
    [ObserversRpc(RunLocally = true, BufferLast = true)]
    public void SyncCloudIds(int cloudAObjectId, int cloudBObjectId)
    {
        CloudAObjectId = cloudAObjectId;
        CloudBObjectId = cloudBObjectId;
    }

    /// <summary>
    /// Enables client-derived geometry only after both endpoint clouds are live.
    /// Disabling also removes the trigger immediately, preventing an unbound ladder
    /// from remaining climbable during spawn/despawn packet reordering.
    /// </summary>
    public void SetPresentationActive(bool active)
    {
        if (_rootRenderer == null)
            _rootRenderer = GetComponent<SpriteRenderer>();
        if (_rootCollider == null)
            _rootCollider = GetComponent<BoxCollider2D>();
        if (_presentation == null)
            _presentation = GetComponent<MovingPlatformLadder>();

        if (_rootRenderer != null)
            _rootRenderer.enabled = false;

        if (_presentationInitialized && _presentationActive == active)
            return;

        _presentationInitialized = true;
        _presentationActive = active;
        if (_presentation != null)
            _presentation.SetPresentationActive(active);
        else if (_rootCollider != null)
            _rootCollider.enabled = active;
    }
}
