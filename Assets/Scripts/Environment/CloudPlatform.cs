using System;
using System.Collections;
using System.Collections.Generic;
using FishNet;
using FishNet.Managing.Timing;
using UnityEngine;

/// <summary>
/// Pooled clouds: CloudManager drives Rigidbody2D.MovePosition on the active physics clock
/// (isPooled, isMoving false).
/// Non-pooled scene clouds move themselves here when isMoving.
/// Stops and despawns when entering CloudNoSpawnZone with blockEntry (non-pooled or when zones enabled).
///
/// The Rigidbody2D must be set to Kinematic. Movement is applied via
/// Rigidbody2D.MovePosition so that players (Dynamic rigidbodies) standing
/// on the cloud are carried along correctly by Unity's physics solver.
/// A velocity-driven Dynamic body does NOT transfer motion to standing bodies.
/// Also implements IMovingPlatform so the player can apply platform delta on clients.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class CloudPlatform : MonoBehaviour, IMovingPlatform
{
    [HideInInspector]
    public float moveSpeed;
    [HideInInspector]
    public bool isPooled = true;

    public bool isMoving = true;

    public bool ignoreNoSpawnZones = false;

    public bool canBuildLadder = true;

    [Header("Ladder")]
    [Tooltip("Collider treated as the core of the cloud for ladder overlap and placement. If unset, combined bounds of all colliders are used.")]
    public Collider2D mainCollider;

    [Header("Despawn")]
    [Tooltip("Prevents this cloud from being faded or deactivated. Use for permanent scene platforms such as player spawn clouds.")]
    public bool isPersistent = false;
    [Tooltip("When set, despawn fires this Animator trigger then waits for the current state to finish before DeactivateCloud. When null, despawn is immediate.")]
    public Animator despawnAnimator;
    [Tooltip("Animator trigger name (ignored when Despawn Animator is null).")]
    public string despawnTrigger = "Despawn";
    /// <summary>Set by CloudManager on spawn. Identifies which prefab this cloud was created from (for network sync).</summary>
    [HideInInspector]
    public int networkPrefabIndex = 0;

    /// <summary>Index of the lane this cloud belongs to. Set by CloudManager on spawn. -1 = not assigned to a lane (e.g. pre-placed scene cloud).</summary>
    [HideInInspector]
    public int laneIndex = -1;

    /// <summary>Slot along the lane loop. Set by CloudManager for pooled clouds. -1 = n/a.</summary>
    [HideInInspector]
    public int slotIndex = -1;

    /// <summary>Pooled: Y position CloudManager uses when driving the cloud (set once at spawn).</summary>
    [HideInInspector]
    public float pooledWorldY;

    /// <summary>Prefab asset this instance was built from (pool key). Set by CloudManager for pooled clouds.</summary>
    [HideInInspector]
    public GameObject pooledSourcePrefab;

    CloudManager _cloudManager;
    bool _isInBlockEntryZone;
    bool _despawnRequested;
    bool _isDespawning;
    int _activationVersion;
    public bool wasActiveAtStart;
    Coroutine _despawnCoroutine;
    Rigidbody2D _rb;
    TimeManager _subscribedTimeManager;
    Collider2D[] _boundsColliders;
    PlatformEffector2D _platformEffector;
    float _platformEffectorOffset;
    readonly HashSet<(Collider2D player, Collider2D platform)> _playerContacts =
        new HashSet<(Collider2D, Collider2D)>();
    readonly HashSet<CloudNoSpawnZone> _overlappingBlockEntryZones = new HashSet<CloudNoSpawnZone>();

    /// <summary>Fires once when a pending despawn actually begins (after riders have cleared).</summary>
    public event Action DespawnStarted;

    void Awake()
    {
        wasActiveAtStart = gameObject.activeSelf && enabled;
        _rb = GetComponent<Rigidbody2D>();
        _boundsColliders = GetComponentsInChildren<Collider2D>();
        _platformEffector = GetComponent<PlatformEffector2D>();
        if (_platformEffector != null)
            _platformEffectorOffset = _platformEffector.rotationalOffset;
        if (_rb != null)
        {
            _rb.bodyType = RigidbodyType2D.Kinematic;
            _rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        }
    }

    void OnEnable()
    {
        SubscribeToNetworkPhysicsClock();
        _activationVersion++;
        if (_platformEffector != null)
            _platformEffector.rotationalOffset = _platformEffectorOffset;

        if (_despawnCoroutine != null)
        {
            StopCoroutine(_despawnCoroutine);
            _despawnCoroutine = null;
        }

        _playerContacts.Clear();
        _overlappingBlockEntryZones.Clear();
        _isInBlockEntryZone = false;
        _despawnRequested = false;
        _isDespawning = false;
        if (isPooled)
        {
            slotIndex = -1;
            pooledWorldY = 0f;
        }
    }

    void Start()
    {
        // OnEnable can precede NetworkManager.Awake depending on scene/component order.
        // Retry after all Awake calls so an already-enabled scene cloud cannot remain
        // on Unity FixedUpdate while FishNet manually simulates physics.
        SubscribeToNetworkPhysicsClock();
    }

    protected virtual void OnDisable()
    {
        UnsubscribeFromNetworkPhysicsClock();
        if (_despawnCoroutine != null)
        {
            StopCoroutine(_despawnCoroutine);
            _despawnCoroutine = null;
        }
        _playerContacts.Clear();
        _overlappingBlockEntryZones.Clear();
        _despawnRequested = false;
        _isDespawning = false;
    }

    void FixedUpdate()
    {
        if (_subscribedTimeManager != null) return;
        AdvancePlatformPhysics(Time.fixedDeltaTime);
    }

    void OnNetworkPrePhysicsSimulation(float deltaTime)
    {
        if (!isActiveAndEnabled || _subscribedTimeManager == null) return;
        AdvancePlatformPhysics(deltaTime);
    }

    void AdvancePlatformPhysics(float deltaTime)
    {
        if (_rb == null) return;
        if (_despawnRequested && !_isDespawning && !IsPlayerOnCloud)
            BeginDespawnAnimation();
        if (isPooled) return;
        if (_isInBlockEntryZone || _isDespawning || !isMoving) return;

        // MovePosition on a Kinematic body is processed by the physics solver so
        // Dynamic bodies (players) in contact are correctly carried along.
        _rb.MovePosition(_rb.position + new Vector2(moveSpeed * deltaTime, 0f));
    }

    void SubscribeToNetworkPhysicsClock()
    {
        if (_subscribedTimeManager != null || isPooled) return;

        TimeManager timeManager = InstanceFinder.TimeManager;
        if (timeManager == null || timeManager.PhysicsMode != PhysicsMode.TimeManager) return;

        _subscribedTimeManager = timeManager;
        _subscribedTimeManager.OnPrePhysicsSimulation += OnNetworkPrePhysicsSimulation;
    }

    void UnsubscribeFromNetworkPhysicsClock()
    {
        if (_subscribedTimeManager == null) return;
        _subscribedTimeManager.OnPrePhysicsSimulation -= OnNetworkPrePhysicsSimulation;
        _subscribedTimeManager = null;
    }

    /// <summary>True when the player is in contact with this cloud. Used by CloudManager for boundary stop vs despawn.</summary>
    public bool IsPlayerOnCloud
    {
        get
        {
            _playerContacts.RemoveWhere(IsInvalidPlayerContact);
            return _playerContacts.Count > 0;
        }
    }
    /// <summary>True while a despawn is in progress (animator wait or same-frame immediate handoff).</summary>
    public bool IsDespawning => _isDespawning;
    public int ActivationVersion => _activationVersion;
    internal Collider2D[] BoundsColliders => _boundsColliders;

    /// <summary>
    /// Starts despawn: optional <see cref="despawnAnimator"/> trigger then <see cref="CloudManager.DeactivateCloud"/> when the animator state completes;
    /// if <see cref="despawnAnimator"/> is null, deactivates immediately. Does not use boundary-zone state.
    /// If a player is standing on the cloud, the request remains pending until the last contact leaves.
    /// </summary>
    public void BeginDespawnAnimation()
    {
        if (isPersistent) return;
        _despawnRequested = true;
        isMoving = false;
        if (_isDespawning || IsPlayerOnCloud) return;

        _isDespawning = true;
        DespawnStarted?.Invoke();

        if (despawnAnimator == null || string.IsNullOrEmpty(despawnTrigger))
        {
            _cloudManager?.DeactivateCloud(gameObject);
            return;
        }

        if (_despawnCoroutine != null)
            StopCoroutine(_despawnCoroutine);
        _despawnCoroutine = StartCoroutine(CoDespawnAfterAnimator());
    }

    IEnumerator CoDespawnAfterAnimator()
    {
        despawnAnimator.SetTrigger(despawnTrigger);

        yield return null;
        int waitTransition = 0;
        while (despawnAnimator.IsInTransition(0) && waitTransition++ < 120)
            yield return null;

        AnimatorStateInfo st = despawnAnimator.GetCurrentAnimatorStateInfo(0);
        float len = Mathf.Max(0.01f, st.length);
        if (st.loop)
            yield return new WaitForSeconds(len);
        else
        {
            int frames = 0;
            const int maxFrames = 600;
            while (frames++ < maxFrames && despawnAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime < 0.99f)
                yield return null;
            if (frames >= maxFrames)
                Debug.LogWarning($"CloudPlatform: despawn animator timed out on '{name}'; forcing lifecycle completion.");
        }

        _despawnCoroutine = null;
        if (_cloudManager != null)
            _cloudManager.DeactivateCloud(gameObject);
        else
            gameObject.SetActive(false);
    }

    /// <summary>True after boundary/exit stop (CloudManager skips driving pooled motion).</summary>
    public bool IsBoundaryStopped => _isInBlockEntryZone;

    /// <summary>Boundary exit / despawn handoff (always applies). CloudNoSpawnZone volumes still respect ignoreNoSpawnZones.</summary>
    public void TriggerBlockEntryFromBoundary()
    {
        EnterBlockEntryZone();
    }

    void EnterBlockEntryZone()
    {
        if (isPersistent) return;
        _isInBlockEntryZone = true;
        isMoving = false;
        BeginDespawnAnimation();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        var zone = other.GetComponent<CloudNoSpawnZone>();
        if (zone == null || !zone.blockEntry || ignoreNoSpawnZones) return;
        _overlappingBlockEntryZones.Add(zone);
        EnterBlockEntryZone();
    }

    void OnTriggerExit2D(Collider2D other)
    {
        var zone = other.GetComponent<CloudNoSpawnZone>();
        if (zone == null || ignoreNoSpawnZones) return;

        _overlappingBlockEntryZones.Remove(zone);
        _isInBlockEntryZone = _overlappingBlockEntryZones.Count > 0;
        if (!_isInBlockEntryZone && !_despawnRequested && !_isDespawning)
            isMoving = true;
    }

    void OnCollisionEnter2D(Collision2D other)
    {
        if (TryGetPlayerContact(other, out Collider2D playerCollider))
        {
            _playerContacts.Add((playerCollider, other.otherCollider));
        }
    }

    void OnCollisionExit2D(Collision2D other)
    {
        if (TryGetPlayerContact(other, out Collider2D playerCollider))
        {
            _playerContacts.Remove((playerCollider, other.otherCollider));
            if (!IsPlayerOnCloud && _despawnRequested)
                BeginDespawnAnimation();
        }
    }

    static bool TryGetPlayerContact(Collision2D collision, out Collider2D playerCollider)
    {
        playerCollider = collision.collider;
        if (playerCollider == null) return false;
        if (playerCollider.gameObject.CompareTag("Player")) return true;
        if (playerCollider.attachedRigidbody != null &&
            playerCollider.attachedRigidbody.gameObject.CompareTag("Player")) return true;
        return playerCollider.GetComponentInParent<PlayerControllerM>() != null;
    }

    static bool IsInvalidPlayerContact((Collider2D player, Collider2D platform) contact) =>
        contact.player == null || !contact.player.enabled || !contact.player.gameObject.activeInHierarchy ||
        contact.platform == null || !contact.platform.enabled || !contact.platform.gameObject.activeInHierarchy;

    /// <summary>Client-side visual only; lifecycle ownership remains on the server CloudManager.</summary>
    public void PlayDespawnVisualOnly()
    {
        if (isPersistent) return;
        if (despawnAnimator != null && !string.IsNullOrEmpty(despawnTrigger))
            despawnAnimator.SetTrigger(despawnTrigger);
    }

    void SetMoving(bool moving)
    {
        isMoving = moving;
    }

    /// <summary>Set by CloudManager when spawning.</summary>
    public void SetMovementSpeed(float speed)
    {
        moveSpeed = speed;
    }

    /// <summary>Set by CloudManager when spawning. Required for ReturnCloudToPool.</summary>
    public void SetCloudManager(CloudManager mgr)
    {
        _cloudManager = mgr;
    }

    public Vector2 GetPosition() => (Vector2)transform.position;

    /// <summary>Combined bounds of enabled, non-trigger Collider2D components on this cloud.</summary>
    public Bounds GetBounds()
    {
        var colliders = _boundsColliders;
        if (colliders == null || colliders.Length == 0)
            return new Bounds(transform.position, Vector3.zero);

        Bounds bounds = new Bounds(transform.position, Vector3.zero);
        bool found = false;
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D collider = colliders[i];
            if (collider == null || !collider.enabled || collider.isTrigger) continue;
            if (!found)
            {
                bounds = GetCurrentColliderBounds(collider);
                found = true;
            }
            else
                bounds.Encapsulate(GetCurrentColliderBounds(collider));
        }
        return bounds;
    }

    /// <summary>Bounds of the main collider (core of cloud). Used by CloudLadderController for overlap and ladder placement. Falls back to GetBounds() if mainCollider is unset.</summary>
    public Bounds GetMainBounds()
    {
        if (mainCollider != null)
            return GetCurrentColliderBounds(mainCollider);
        return GetBounds();
    }

    /// <summary>
    /// Returns bounds at the collider's current Transform pose. NetworkTransform updates
    /// remote cloud Transforms during render frames, while Physics2D.autoSyncTransforms is
    /// intentionally disabled; Collider2D.bounds can therefore lag the visible cloud until
    /// the next simulation. Managed cloud collision shapes are boxes, so derive their AABB
    /// directly without forcing a costly global Physics2D.SyncTransforms call.
    /// </summary>
    internal static Bounds GetCurrentColliderBounds(Collider2D collider)
    {
#if UNITY_SERVER && !UNITY_EDITOR
        // Dedicated servers do not render interpolated NetworkTransform poses, and
        // ladder CPU is the limiting deployment resource. Keep their native fast path.
        return collider.bounds;
#else
        if (collider is not BoxCollider2D box)
            return collider.bounds;

        Vector2 half = box.size * 0.5f + Vector2.one * box.edgeRadius;
        Vector2 offset = box.offset;
        Transform colliderTransform = box.transform;
        Vector3 corner = colliderTransform.TransformPoint(
            new Vector3(offset.x - half.x, offset.y - half.y, 0f));
        Bounds bounds = new Bounds(corner, Vector3.zero);
        bounds.Encapsulate(colliderTransform.TransformPoint(
            new Vector3(offset.x - half.x, offset.y + half.y, 0f)));
        bounds.Encapsulate(colliderTransform.TransformPoint(
            new Vector3(offset.x + half.x, offset.y - half.y, 0f)));
        bounds.Encapsulate(colliderTransform.TransformPoint(
            new Vector3(offset.x + half.x, offset.y + half.y, 0f)));
        return bounds;
#endif
    }

    public void SetCanBuildLadder(bool canBuildLadder)
    {
        this.canBuildLadder = canBuildLadder;
    }

    /// <summary>
    /// Forcibly try to build a ladder between this cloud and another. Uses CloudLadderController from GameServices.
    /// Returns true if a ladder exists or was created; false if controller missing, invalid, or at max ladders.
    /// </summary>
    public bool TryBuildLadderTo(CloudPlatform other)
    {
        var gs = FindFirstObjectByType<GameServices>();
        if (gs == null) return false;
        var ladderController = gs.GetCloudLadderController();
        if (ladderController == null) return false;
        return ladderController.TryBuildLadder(this, other);
    }
}
