using System.Collections;
using UnityEngine;

/// <summary>
/// Pooled clouds: CloudManager drives Rigidbody2D.MovePosition in FixedUpdate (isPooled, isMoving false).
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

    /// <summary>Last FixedUpdate: cloud main bounds overlapped a blockEntry-only zone (blockSpawn false). Used to detect entry vs travel inside.</summary>
    [HideInInspector]
    public bool pooledPrevOverlapEntryOnly;

    CloudManager _cloudManager;
    bool _playerOnCloud;
    bool _isInBlockEntryZone;
    bool _isDespawning;
    public bool wasActiveAtStart;
    Coroutine _despawnCoroutine;
    Rigidbody2D _rb;

    void Awake()
    {
        wasActiveAtStart = gameObject.activeSelf && enabled;
        _rb = GetComponent<Rigidbody2D>();
        if (_rb != null)
        {
            _rb.bodyType = RigidbodyType2D.Kinematic;
            _rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        }
    }

    void OnEnable()
    {
        if (_despawnCoroutine != null)
        {
            StopCoroutine(_despawnCoroutine);
            _despawnCoroutine = null;
        }

        _playerOnCloud = false;
        _isInBlockEntryZone = false;
        _isDespawning = false;
        if (isPooled)
        {
            slotIndex = -1;
            pooledWorldY = 0f;
        }
    }

    void OnDisable()
    {
        if (_despawnCoroutine != null)
        {
            StopCoroutine(_despawnCoroutine);
            _despawnCoroutine = null;
        }
    }

    void FixedUpdate()
    {
        if (_rb == null) return;
        if (isPooled) return;
        if (_isInBlockEntryZone || _isDespawning || !isMoving) return;

        // MovePosition on a Kinematic body is processed by the physics solver so
        // Dynamic bodies (players) in contact are correctly carried along.
        _rb.MovePosition(_rb.position + new Vector2(moveSpeed * Time.fixedDeltaTime, 0f));
    }

    /// <summary>True when the player is in contact with this cloud. Used by CloudManager for boundary stop vs despawn.</summary>
    public bool IsPlayerOnCloud => _playerOnCloud;
    /// <summary>True while a despawn is in progress (animator wait or same-frame immediate handoff).</summary>
    public bool IsDespawning => _isDespawning;

    /// <summary>
    /// Starts despawn: optional <see cref="despawnAnimator"/> trigger then <see cref="CloudManager.DeactivateCloud"/> when the animator state completes;
    /// if <see cref="despawnAnimator"/> is null, deactivates immediately. Does not use boundary-zone state.
    /// If the player is standing on the cloud, existing collision logic can cancel despawn until they leave.
    /// </summary>
    public void BeginDespawnAnimation()
    {
        if (_isDespawning) return;
        isMoving = false;
        _isDespawning = true;

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
            const int maxFrames = 6000;
            while (frames++ < maxFrames && despawnAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime < 0.99f)
                yield return null;
        }

        _despawnCoroutine = null;
        _cloudManager?.DeactivateCloud(gameObject);
        _isDespawning = false;
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
        _isInBlockEntryZone = true;
        isMoving = false;
        if (!_playerOnCloud)
            BeginDespawnAnimation();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        var zone = other.GetComponent<CloudNoSpawnZone>();
        if (zone == null || !zone.blockEntry || ignoreNoSpawnZones) return;
        EnterBlockEntryZone();
    }

    void OnTriggerExit2D(Collider2D other)
    {
        var zone = other.GetComponent<CloudNoSpawnZone>();
        if (zone == null) return;

        _isInBlockEntryZone = false;
        if (!_isDespawning)
            isMoving = true;
    }

    void OnCollisionEnter2D(Collision2D other)
    {
        if (other.gameObject.CompareTag("Player"))
        {
            _playerOnCloud = true;
            if (_isDespawning)
            {
                if (_despawnCoroutine != null)
                {
                    StopCoroutine(_despawnCoroutine);
                    _despawnCoroutine = null;
                }
                _isDespawning = false;
            }
        }
    }

    void OnCollisionExit2D(Collision2D other)
    {
        if (other.gameObject.CompareTag("Player"))
        {
            _playerOnCloud = false;
            if (_isInBlockEntryZone)
                BeginDespawnAnimation();
        }
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

    /// <summary>Combined bounds of all Collider2D on this cloud.</summary>
    public Bounds GetBounds()
    {
        var colliders = GetComponentsInChildren<Collider2D>();
        if (colliders == null || colliders.Length == 0)
            return new Bounds(transform.position, Vector3.zero);

        var bounds = colliders[0].bounds;
        for (int i = 1; i < colliders.Length; i++)
            bounds.Encapsulate(colliders[i].bounds);
        return bounds;
    }

    /// <summary>Bounds of the main collider (core of cloud). Used by CloudLadderController for overlap and ladder placement. Falls back to GetBounds() if mainCollider is unset.</summary>
    public Bounds GetMainBounds()
    {
        if (mainCollider != null)
            return mainCollider.bounds;
        return GetBounds();
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
