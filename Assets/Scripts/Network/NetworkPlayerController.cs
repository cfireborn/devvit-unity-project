using FishNet.Object;
using UnityEngine;

/// <summary>
/// NetworkBehaviour wrapper for PlayerControllerM.
/// - Owner: enables input + physics, syncs visual state to all other clients at 15Hz.
/// - Remote: disables input + physics, applies received visual state to Animator + SpriteRenderer.
/// - Owner: syncs camera orthographic size and aspect to server for CloudManager viewport.
/// </summary>
public class NetworkPlayerController : NetworkBehaviour
{
    const float MinViewportOrthoSize = 1f;
    const float MaxViewportOrthoSize = 8f;
    const float MinViewportAspect = 0.35f;
    const float MaxViewportAspect = 3f;
    const float ViewportRefreshRequestInterval = 0.1f;

    PlayerControllerM _controller;
    Rigidbody2D _rb;
    SpriteRenderer _spriteRenderer;
    CloudManager _serverCloudManager;

    float _serverOrthoSize = 5f;
    float _serverAspect = 16f / 9f;
    int _lastScreenW;
    int _lastScreenH;
    float _lastSentOrtho;
    float _lastSentAspect;
    float _nextServerViewportRefreshRequestTime;

    // Visual sync state (owner writes, remotes read)
    float _syncedMoveDir;
    bool _syncedGliding;
    bool _syncedGrounded;
    bool _syncedJumping;
    Vector2 _lastRemoteVisualPosition;
    bool _remoteVisualPositionInitialized;

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
            _lastRemoteVisualPosition = transform.position;
            _remoteVisualPositionInitialized = true;

            // Apply rainbow tint to remote players
            if (_spriteRenderer != null)
            {
                // Consistent color based on player's connection ID
                int colorIndex = OwnerId % RemotePlayerColors.Length;
                _spriteRenderer.color = RemotePlayerColors[colorIndex];
            }
        }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        _serverCloudManager = FindFirstObjectByType<CloudManager>();
        if (_serverCloudManager != null)
            _serverCloudManager.RegisterPlayer(transform);
        else
        {
            Debug.LogError($"NetworkPlayerController: no CloudManager found for server player {OwnerId}; dynamic clouds cannot activate.");
        }
    }

    public override void OnStopServer()
    {
        if (_serverCloudManager != null)
            _serverCloudManager.UnregisterPlayer(transform);
        _serverCloudManager = null;

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
        // Viewports select the server-side world region where dynamic clouds are
        // allowed to exist. Reject non-finite input and clamp modified clients so
        // one RPC cannot activate the entire level's lanes/slots.
        if (float.IsNaN(orthographicSize) || float.IsInfinity(orthographicSize) ||
            float.IsNaN(aspect) || float.IsInfinity(aspect))
            return;

        float nextOrtho = Mathf.Clamp(orthographicSize, MinViewportOrthoSize, MaxViewportOrthoSize);
        float nextAspect = Mathf.Clamp(aspect, MinViewportAspect, MaxViewportAspect);
        if (Mathf.Approximately(_serverOrthoSize, nextOrtho) &&
            Mathf.Approximately(_serverAspect, nextAspect))
            return;

        _serverOrthoSize = nextOrtho;
        _serverAspect = nextAspect;
        // CloudManager already refreshes at 10 Hz. Match that cadence so even an
        // owner alternating two valid values cannot force the expensive lifecycle
        // pass on every received RPC.
        if (Time.unscaledTime >= _nextServerViewportRefreshRequestTime)
        {
            _nextServerViewportRefreshRequestTime = Time.unscaledTime + ViewportRefreshRequestInterval;
            _serverCloudManager?.RequestViewportFill();
        }
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
        bool grounded = _controller.IsGrounded;
        bool jumping = _controller.IsJumping;

        // Only send if state changed
        if (Mathf.Abs(moveDir - _syncedMoveDir) > 0.05f
            || gliding != _syncedGliding
            || grounded != _syncedGrounded
            || jumping != _syncedJumping)
        {
            _syncedMoveDir = moveDir;
            _syncedGliding = gliding;
            _syncedGrounded = grounded;
            _syncedJumping = jumping;
            CmdSendVisuals(moveDir, gliding, grounded, jumping);
        }
    }

    /// <summary>Owner → Server: relay visual state to all observers.</summary>
    [ServerRpc(RequireOwnership = true)]
    void CmdSendVisuals(float moveDir, bool isGliding, bool isGrounded, bool isJumping)
    {
        RpcReceiveVisuals(moveDir, isGliding, isGrounded, isJumping);
    }

    // ── Remote ────────────────────────────────────────────────────────────────

    /// <summary>Server → All clients: apply received visual state.</summary>
    [ObserversRpc(ExcludeServer = false)]
    void RpcReceiveVisuals(float moveDir, bool isGliding, bool isGrounded, bool isJumping)
    {
        if (IsOwner) return; // Owner already has correct visuals
        _syncedMoveDir = moveDir;
        _syncedGliding = isGliding;
        _syncedGrounded = isGrounded;
        _syncedJumping = isJumping;
    }

    void RemoteUpdate()
    {
        if (_controller == null) return;

        // Facing direction
        if (_spriteRenderer != null && Mathf.Abs(_syncedMoveDir) > 0.05f)
            _spriteRenderer.flipX = _syncedMoveDir < 0f;

        Transform spriteTransform = _controller.spriteTransform;
        if (spriteTransform != null && spriteTransform != transform)
        {
            Vector2 currentPosition = transform.position;
            Vector2 visualVelocity = Vector2.zero;
            if (_remoteVisualPositionInitialized)
                visualVelocity = (currentPosition - _lastRemoteVisualPosition) /
                    Mathf.Max(0.0001f, Time.deltaTime);
            _lastRemoteVisualPosition = currentPosition;
            _remoteVisualPositionInitialized = true;

            float desiredAngle = -_syncedMoveDir * _controller.maxRotationAngle - visualVelocity.y * 0.5f;
            desiredAngle = Mathf.Clamp(desiredAngle,
                -Mathf.Abs(_controller.maxRotationAngle), Mathf.Abs(_controller.maxRotationAngle));
            Quaternion target = Quaternion.Euler(0f, 0f, desiredAngle);
            spriteTransform.localRotation = Quaternion.Lerp(
                spriteTransform.localRotation, target, Time.deltaTime * 10f);
        }

        _controller.SetSpriteAnimatorState(_syncedMoveDir, _syncedGliding, _syncedGrounded, _syncedJumping);
    }
}
