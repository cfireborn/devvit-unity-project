using FishNet.Object;
using UnityEngine;

/// <summary>
/// NetworkBehaviour wrapper for PlayerControllerM.
/// - On the owning client: enables input + physics so the player can be controlled normally.
/// - On all other instances (server copy, remote clients): disables input and physics sim
///   so NetworkTransform can drive position without fighting Rigidbody2D.
///
/// Add this component (plus NetworkObject and NetworkTransform) to your NetworkPlayer prefab.
/// The base player prefab used for single-player should NOT have this component.
/// </summary>
public class NetworkPlayerController : NetworkBehaviour
{
    PlayerControllerM _controller;
    Rigidbody2D _rb;

    void Awake()
    {
        _controller = GetComponent<PlayerControllerM>();
        _rb = GetComponent<Rigidbody2D>();

        // Disable input until OnStartClient confirms ownership.
        // This prevents PlayerControllerM.Start() from running before we know
        // if this is the local player or a remote player.
        if (_controller != null)
            _controller.enabled = false;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (IsOwner)
        {
            // This is our player — enable input and physics.
            if (_controller != null)
                _controller.enabled = true;

            if (_rb != null)
                _rb.simulated = true;

            Debug.Log("NetworkPlayerController: Local player started.");
        }
        else
        {
            // Remote player — NetworkTransform drives position; no local physics or input.
            if (_controller != null)
                _controller.enabled = false;

            if (_rb != null)
                _rb.simulated = false;

            Debug.Log($"NetworkPlayerController: Remote player started (conn {OwnerId}).");
        }
    }

    public override void OnStopClient()
    {
        base.OnStopClient();

        // Clean up physics when the object is despawned.
        if (_rb != null)
            _rb.simulated = false;
    }
}
