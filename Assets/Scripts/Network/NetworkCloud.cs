using FishNet.Object;
using FishNet.Component.Transforming;
using UnityEngine;

/// <summary>
/// Attach to every cloud prefab alongside NetworkObject + NetworkTransform.
///
/// - Server / host: Pooled clouds are moved once per active physics tick by CloudManager
///   (Rigidbody2D.MovePosition).
///   FishNet NetworkTransform replicates transform/Rigidbody state to clients at 20 Hz;
///   its render interpolation fills the frames between authoritative poses.
///   Non-pooled scene clouds use the same physics clock through CloudPlatform when isPooled is false.
///   Scene clouds that were active at load are re-enabled in OnStartServer so they behave once the network is up.
/// - Clients: CloudPlatform is disabled so local physics does not fight replication; NetworkTransform applies positions.
///
/// Scale is synced via a BufferLast ObserversRpc so clients get the correct random
/// scale on spawn, and late-joining clients receive the last-sent value automatically.
/// (FishNet v4 removed [SyncVar] attribute — BufferLast RPC is the v4 equivalent.)
/// </summary>
public class NetworkCloud : NetworkBehaviour
{
    const float TargetTransformSendRate = 20f;
    CloudPlatform _platform;
    Rigidbody2D _rb;
    NetworkTransform _networkTransform;
#if UNITY_SERVER && !UNITY_EDITOR
    Animator _serverDespawnAnimator;
#endif

    // Whether CloudPlatform was enabled when the scene loaded, recorded before any
    // network lifecycle callback can change it. Used to distinguish:
    //   true  — active cloud; suppress on clients, re-enable on server
    //   false — designer-disabled cloud; leave untouched by networking
    public bool _platformWasEnabledAtStart;

    void Awake()
    {
        _platform = GetComponent<CloudPlatform>();
        _rb = GetComponent<Rigidbody2D>();
        _networkTransform = GetComponent<NetworkTransform>();
        _platformWasEnabledAtStart = _platform != null && _platform.enabled;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        // Clouds move continuously but do not need a full-tick global transform
        // stream. Preserve an approximately 15-20 Hz stream if a scene lowers its
        // simulation tick rate; FishNet interpolates between those poses.
        if (_networkTransform != null)
        {
            ushort tickRate = TimeManager != null ? TimeManager.TickRate : (ushort)60;
            byte interval = (byte)Mathf.Clamp(
                Mathf.CeilToInt(tickRate / TargetTransformSendRate), 1, byte.MaxValue);
            _networkTransform.SetInterval(interval);
        }

        if (_platform != null)
            _platform.DespawnStarted += OnServerDespawnStarted;

#if UNITY_SERVER && !UNITY_EDITOR
        // Dedicated servers need the animator only as the authoritative despawn
        // timer. Disable its steady-state sprite/material evaluation while the
        // cloud is moving; OnServerDespawnStarted re-enables it before the
        // CloudPlatform coroutine fires the trigger and waits for completion.
        _serverDespawnAnimator = _platform != null ? _platform.despawnAnimator : null;
        if (_serverDespawnAnimator != null)
            _serverDespawnAnimator.enabled = false;
#endif

        // Re-enable CloudPlatform for scene clouds that were active at load.
        // Pool-spawned clouds are already enabled; this specifically covers scene
        // NetworkObjects whose CloudPlatform may have been left in an indeterminate
        // state during the pre-network startup window.
        if (_platformWasEnabledAtStart && _platform != null)
            _platform.enabled = true;

        if (_rb != null)
        {
            _rb.bodyType = RigidbodyType2D.Kinematic;
            // The authoritative root is moved only by the physics pipeline. Let the
            // Rigidbody interpolate those physics poses for the host's rendered view;
            // NetworkTransform only samples this object for remote observers.
            _rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        }
    }

    public override void OnStopServer()
    {
        if (_platform != null)
            _platform.DespawnStarted -= OnServerDespawnStarted;
#if UNITY_SERVER && !UNITY_EDITOR
        if (_serverDespawnAnimator != null)
            _serverDespawnAnimator.enabled = true;
        _serverDespawnAnimator = null;
#endif
        base.OnStopServer();
    }

    void OnServerDespawnStarted()
    {
#if UNITY_SERVER && !UNITY_EDITOR
        if (_serverDespawnAnimator != null)
            _serverDespawnAnimator.enabled = true;
#endif
        SyncDespawnVisual();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // Host already running natively via OnStartServer — nothing extra needed.
        if (IsServerStarted) return;

        // Pure client: NetworkTransform drives position, so CloudPlatform must not
        // fight it. Only disable platforms that were originally active — designer-
        // disabled clouds are left as-is so their disabled state is preserved.
        if (_platform != null)
            _platform.enabled = false;

        if (_rb != null)
        {
            _rb.bodyType = RigidbodyType2D.Kinematic;
            // NetworkTransform already interpolates the replicated root each rendered
            // update. Rigidbody interpolation here would add a second transform writer.
            _rb.interpolation = RigidbodyInterpolation2D.None;
        }
    }

    /// <summary>
    /// Called by CloudManager on the server right after ServerManager.Spawn().
    /// BufferLast = true ensures late-joining clients receive the correct scale.
    /// RunLocally = true applies it on the host too.
    /// </summary>
    [ObserversRpc(RunLocally = true, BufferLast = true)]
    public void SyncScale(float scale)
    {
        transform.localScale = new Vector3(scale, scale, scale);
    }

    /// <summary>Starts the same evaporation visual on pure clients while the server owns despawn timing.</summary>
    [ObserversRpc(ExcludeServer = true, BufferLast = true)]
    public void SyncDespawnVisual()
    {
        _platform?.PlayDespawnVisualOnly();
    }
}
