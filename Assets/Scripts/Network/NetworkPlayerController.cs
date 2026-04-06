using FishNet.Object;
using UnityEngine;

/// <summary>
/// NetworkBehaviour wrapper for PlayerControllerM.
/// - Owner: enables input + physics, syncs visual state to all other clients at 15Hz.
/// - Remote: disables input + physics, applies received visual state to SpriteRenderer.
/// - Owner: syncs camera orthographic size and aspect to server for CloudManager viewport.
/// </summary>
public class NetworkPlayerController : NetworkBehaviour
{
    PlayerControllerM _controller;
    Rigidbody2D _rb;
    SpriteRenderer _spriteRenderer;

    float _serverOrthoSize = 5f;
    float _serverAspect = 16f / 9f;
    int _lastScreenW;
    int _lastScreenH;
    float _lastSentOrtho;
    float _lastSentAspect;

    // Visual sync state (owner writes, remotes read)
    float _syncedMoveDir;
    bool _syncedGliding;

    // Walk animation state for remote players (runs locally)
    float _remoteWalkTimer;
    int _remoteWalkIndex;

    // Throttle: sync visuals at 15Hz, not every frame
    float _visualSyncTimer;
    const float VisualSyncInterval = 1f / 15f;

    // Remote player colors: Red, Blue, Indigo, Purple
    private static readonly Color[] RemotePlayerColors = new Color[]
    {
        new Color(1f, 0.2f, 0.2f),    // Red
        new Color(0.2f, 0.6f, 1f),    // Blue
        new Color(0.3f, 0.2f, 0.8f),  // Indigo
        new Color(0.8f, 0.2f, 1f)     // Purple/Violet
    };

    void Awake()
    {
        _controller = GetComponent<PlayerControllerM>();
        _rb = GetComponent<Rigidbody2D>();

        // Disable input until OnStartClient confirms ownership.
        if (_controller != null)
            _controller.enabled = false;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // Find SpriteRenderer (may be on a child object via spriteTransform)
        _spriteRenderer = _controller != null && _controller.spriteRenderer != null
            ? _controller.spriteRenderer
            : GetComponentInChildren<SpriteRenderer>();

        if (IsOwner)
        {
            if (_controller != null) _controller.enabled = true;
            if (_rb != null) _rb.simulated = true;
            _lastScreenW = Screen.width;
            _lastScreenH = Screen.height;
            TrySendViewportToServer(true);
        }
        else
        {
            if (_controller != null) _controller.enabled = false;
            if (_rb != null) _rb.simulated = false;

            // Apply rainbow tint to remote players
            if (_spriteRenderer != null)
            {
                // Consistent color based on player's connection ID
                int colorIndex = OwnerId % RemotePlayerColors.Length;
                _spriteRenderer.color = RemotePlayerColors[colorIndex];
            }
        }
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        var gs = FindFirstObjectByType<GameServices>();
        if (gs != null && _controller != null)
            gs.DeregisterPlayer(_controller);
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        if (_rb != null) _rb.simulated = false;
    }

    void Update()
    {
        if (!IsSpawned) return;

        if (IsOwner)
        {
            OwnerViewportSync();
            OwnerUpdate();
        }
        else
            RemoteUpdate();
    }

    void OwnerViewportSync()
    {
        if (Screen.width != _lastScreenW || Screen.height != _lastScreenH)
        {
            _lastScreenW = Screen.width;
            _lastScreenH = Screen.height;
            TrySendViewportToServer(true);
            return;
        }
        TrySendViewportToServer(false);
    }

    void TrySendViewportToServer(bool force)
    {
        var cam = Camera.main;
        if (cam == null || !cam.orthographic) return;
        float ortho = cam.orthographicSize;
        float aspect = cam.aspect;
        if (!force && Mathf.Approximately(ortho, _lastSentOrtho) && Mathf.Approximately(aspect, _lastSentAspect))
            return;
        _lastSentOrtho = ortho;
        _lastSentAspect = aspect;
        CmdSyncViewport(ortho, aspect);
    }

    [ServerRpc(RequireOwnership = true)]
    void CmdSyncViewport(float orthographicSize, float aspect)
    {
        _serverOrthoSize = orthographicSize;
        _serverAspect = Mathf.Max(0.1f, aspect);
    }

    /// <summary>Server-only: orthographic half-height and half-width in world units (for CloudManager).</summary>
    public void GetWorldCameraHalfExtents(out float halfWidth, out float halfHeight)
    {
        halfHeight = _serverOrthoSize;
        halfWidth = _serverOrthoSize * _serverAspect;
    }

    // ── Owner ─────────────────────────────────────────────────────────────────

    void OwnerUpdate()
    {
        if (_controller == null) return;

        _visualSyncTimer += Time.deltaTime;
        if (_visualSyncTimer < VisualSyncInterval) return;
        _visualSyncTimer = 0f;

        float moveDir = _controller.MoveInputX;
        bool gliding = _controller.IsGliding;

        // Only send if state changed
        if (Mathf.Abs(moveDir - _syncedMoveDir) > 0.05f || gliding != _syncedGliding)
        {
            _syncedMoveDir = moveDir;
            _syncedGliding = gliding;
            CmdSendVisuals(moveDir, gliding);
        }
    }

    /// <summary>Owner → Server: relay visual state to all observers.</summary>
    [ServerRpc(RequireOwnership = true)]
    void CmdSendVisuals(float moveDir, bool isGliding)
    {
        RpcReceiveVisuals(moveDir, isGliding);
    }

    // ── Remote ────────────────────────────────────────────────────────────────

    /// <summary>Server → All clients: apply received visual state.</summary>
    [ObserversRpc(ExcludeServer = false)]
    void RpcReceiveVisuals(float moveDir, bool isGliding)
    {
        if (IsOwner) return; // Owner already has correct visuals
        _syncedMoveDir = moveDir;
        _syncedGliding = isGliding;
    }

    void RemoteUpdate()
    {
        if (_spriteRenderer == null || _controller == null) return;

        // Facing direction
        if (Mathf.Abs(_syncedMoveDir) > 0.05f)
            _spriteRenderer.flipX = _syncedMoveDir < 0f;

        // Sprite state: glide > walk > idle
        if (_syncedGliding && _controller.glideSprite != null)
        {
            _spriteRenderer.sprite = _controller.glideSprite;
        }
        else if (Mathf.Abs(_syncedMoveDir) > 0.05f
                 && _controller.walkSprites != null
                 && _controller.walkSprites.Length > 0)
        {
            // Run walk cycle locally using synced move speed as a proxy
            _remoteWalkTimer += Time.deltaTime;
            float frameTime = Mathf.Max(0.001f, 1f / Mathf.Max(0.01f, _controller.walkFrameRate));
            if (_remoteWalkTimer >= frameTime)
            {
                _remoteWalkTimer = 0f;
                _remoteWalkIndex = (_remoteWalkIndex + 1) % _controller.walkSprites.Length;
            }
            _spriteRenderer.sprite = _controller.walkSprites[_remoteWalkIndex];
        }
        else
        {
            _remoteWalkTimer = 0f;
            if (_controller.idleSprite != null)
                _spriteRenderer.sprite = _controller.idleSprite;
        }
    }
}
