using UnityEngine;

/// <summary>
/// Moves a cloud horizontally. CloudManager sets the speed on spawn.
/// Stops and despawns when entering CloudNoSpawnZone with blockEntry.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class CloudPlatform : MonoBehaviour
{
    [HideInInspector]
    public float moveSpeed;

    public bool isMoving = true;

    public bool ignoreNoSpawnZones = false;

    [Header("Despawn")]
    [Tooltip("Duration of despawn animation before returning to pool.")]
    public float despawnAnimationDuration = 1f;

    CloudManager _cloudManager;
    bool _playerOnCloud;
    bool _isInBlockEntryZone;
    bool _isDespawning;
    float _despawnTimer;
    Vector3 _scaleAtDespawnStart;
    Rigidbody2D _rb;

    void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
    }

    void OnEnable()
    {
        _playerOnCloud = false;
        _isInBlockEntryZone = false;
        _isDespawning = false;
        _despawnTimer = 0f;
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
                _cloudManager?.ReturnCloudToPool(gameObject);
            }
        }
    }

    void FixedUpdate()
    {
        if (_rb == null) return;
        if (_isInBlockEntryZone || _isDespawning)
        {
            _rb.linearVelocity = Vector2.zero;
            return;
        }
        if (isMoving)
        {
            _rb.linearVelocity = new Vector2(moveSpeed, 0f);
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        var zone = other.GetComponent<CloudNoSpawnZone>();
        if (zone == null || !zone.blockEntry || ignoreNoSpawnZones) return;

        _isInBlockEntryZone = true;
        isMoving = false;
        _rb.linearVelocity = Vector2.zero;

        if (!_playerOnCloud)
        {
            _isDespawning = true;
            _despawnTimer = 0f;
            _scaleAtDespawnStart = transform.localScale;
        }
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

    void SetMoving(bool moving, float speed)
    {
        isMoving = moving;
        SetMovementSpeed(speed);

        if (!moving && _rb != null)
        {
            _rb.linearVelocity = Vector2.zero;
        }
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

    /// <summary>Combined bounds of all Collider2D on this cloud. Used by CloudLadderController.</summary>
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
}
