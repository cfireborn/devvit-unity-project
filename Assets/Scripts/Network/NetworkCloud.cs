using FishNet.Object;
using UnityEngine;

/// <summary>
/// Attach to every cloud prefab alongside NetworkObject + NetworkTransform.
///
/// - Server: CloudPlatform runs normally, moves the cloud via linearVelocity.
///           NetworkTransform broadcasts the resulting position to all clients.
/// - Clients: CloudPlatform is disabled, Rigidbody2D set to Kinematic.
///            NetworkTransform drives position — same engine as player sync.
///
/// Scale is synced via a BufferLast ObserversRpc so clients get the correct random
/// scale on spawn, and late-joining clients receive the last-sent value automatically.
/// (FishNet v4 removed [SyncVar] attribute — BufferLast RPC is the v4 equivalent.)
/// </summary>
public class NetworkCloud : NetworkBehaviour
{
    CloudPlatform _platform;
    Rigidbody2D _rb;

    void Awake()
    {
        _platform = GetComponent<CloudPlatform>();
        _rb = GetComponent<Rigidbody2D>();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (IsServerStarted) return; // host already running natively

        // Pure client: NetworkTransform drives position, so CloudPlatform must not fight it
        if (_platform != null) _platform.enabled = false;
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
