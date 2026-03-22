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
    [Tooltip("Duration of despawn animation before returning to pool.")]
    public float despawnAnimationDuration = 1f;

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

    CloudManager _cloudManager;
    bool _playerOnCloud;
    bool _isInBlockEntryZone;
    bool _isDespawning;
    public bool wasActiveAtStart; 
    float _despawnTimer;
    Vector3 _scaleAtDespawnStart;
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
        _playerOnCloud = false;
        _isInBlockEntryZone = false;
        _isDespawning = false;
        _despawnTimer = 0f;
        if (isPooled)
        {
            slotIndex = -1;
            pooledWorldY = 0f;
        }
    }

    void Update()
    {
        if (_isDespawning)
        {
            _despawnTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_despawnTimer / despawnAnimationDuration);
            transform.localScale = _scaleAtDespawnStart * (1f - t);
            if (_despawnTimer >= despawnAnimationDuration)
            {
                _cloudManager?.DeactivateCloud(gameObject);
            }
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
    /// <summary>True while the cloud is playing the despawn animation. Used by CloudLadderController for keep-active logic.</summary>
    public bool IsDespawning => _isDespawning;

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
        {
            _isDespawning = true;
            _despawnTimer = 0f;
            _scaleAtDespawnStart = transform.localScale;
        }
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
        {
            isMoving = true;
        }
    }

    void OnCollisionEnter2D(Collision2D other)
    {
        if (other.gameObject.CompareTag("Player"))
        {
            _playerOnCloud = true;
            if (_isDespawning)
            {
                _isDespawning = false;
                _despawnTimer = 0f;
            }
        }
    }

    void OnCollisionExit2D(Collision2D other)
    {
        if (other.gameObject.CompareTag("Player"))
        {
            _playerOnCloud = false;
            if (_isInBlockEntryZone)
            {
                _isDespawning = true;
                _despawnTimer = 0f;
                _scaleAtDespawnStart = transform.localScale;
            }
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
        {
            bounds.Encapsulate(colliders[i].bounds);
        }
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
