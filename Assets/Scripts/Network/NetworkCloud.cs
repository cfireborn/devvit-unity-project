using FishNet.Object;
using UnityEngine;

/// <summary>
/// Attach to every cloud prefab alongside NetworkObject + NetworkTransform.
///
/// - Server: CloudPlatform runs normally, moves the cloud via MovePosition.
///           NetworkTransform broadcasts the resulting position to all clients.
///           Scene clouds that were active at load are re-enabled in OnStartServer
///           so they begin moving once the network is up.
/// - Clients: CloudPlatform is disabled (NetworkTransform drives position).
///            Only clouds that were originally enabled have their CloudPlatform
///            suppressed — designer-disabled clouds are left untouched.
///
/// Scale is synced via a BufferLast ObserversRpc so clients get the correct random
/// scale on spawn, and late-joining clients receive the last-sent value automatically.
/// (FishNet v4 removed [SyncVar] attribute — BufferLast RPC is the v4 equivalent.)
/// </summary>
public class NetworkCloud : NetworkBehaviour
{
    CloudPlatform _platform;
    Rigidbody2D _rb;

    // Whether CloudPlatform was enabled when the scene loaded, recorded before any
    // network lifecycle callback can change it. Used to distinguish:
    //   true  — active cloud; suppress on clients, re-enable on server
    //   false — designer-disabled cloud; leave untouched by networking
    public bool _platformWasEnabledAtStart;

    void Awake()
    {
        _platform = GetComponent<CloudPlatform>();
        _rb = GetComponent<Rigidbody2D>();
        _platformWasEnabledAtStart = _platform != null && _platform.enabled;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        // Re-enable CloudPlatform for scene clouds that were active at load.
        // Pool-spawned clouds are already enabled; this specifically covers scene
        // NetworkObjects whose CloudPlatform may have been left in an indeterminate
        // state during the pre-network startup window.
        if (_platformWasEnabledAtStart && _platform != null)
            _platform.enabled = true;

        if (_rb != null)
        {
            _rb.bodyType = RigidbodyType2D.Kinematic;
            _rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        }
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
            _rb.interpolation = RigidbodyInterpolation2D.Interpolate;
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
}
