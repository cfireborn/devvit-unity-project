using FishNet;
using FishNet.Managing.Timing;
using UnityEngine;

/// <summary>
/// Drives the player's head collider as an independent kinematic surface. The surface is detached
/// at runtime so rider contacts cannot transfer weight or momentum into the player's dynamic body.
/// </summary>
[DefaultExecutionOrder(-100)]
public sealed class PlayerHeadPlatform : MonoBehaviour
{
    [SerializeField] Rigidbody2D surfaceBody;
    [SerializeField] EdgeCollider2D surfaceCollider;
    [SerializeField, Min(0f)] float teleportSnapDistance = 0.75f;

    Transform _surfaceTransform;
    PlayerHeadPlatformSurface _surface;
    Rigidbody2D _ownerBody;
    TimeManager _subscribedTimeManager;
    Vector2 _localOffset;
    bool _initialized;

    public Rigidbody2D SurfaceBody => surfaceBody;
    public EdgeCollider2D SurfaceCollider => surfaceCollider;

    void Awake()
    {
        _ownerBody = GetComponent<Rigidbody2D>();
        if (surfaceBody == null)
            surfaceBody = transform.Find("HeadPlatform")?.GetComponent<Rigidbody2D>();
        if (surfaceCollider == null && surfaceBody != null)
            surfaceCollider = surfaceBody.GetComponent<EdgeCollider2D>();

        if (surfaceBody == null || surfaceCollider == null)
        {
            Debug.LogError("PlayerHeadPlatform requires the HeadPlatform Rigidbody2D and EdgeCollider2D.", this);
            enabled = false;
            return;
        }

        _surfaceTransform = surfaceBody.transform;
        _localOffset = _surfaceTransform.localPosition;

        // The prefab contains two bodies only to keep rider impulses isolated from the player.
        // Ignore every owner collider explicitly before detaching the surface from the hierarchy.
        Collider2D[] ownerColliders = GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < ownerColliders.Length; i++)
        {
            Collider2D ownerCollider = ownerColliders[i];
            if (ownerCollider != null && ownerCollider != surfaceCollider)
                Physics2D.IgnoreCollision(ownerCollider, surfaceCollider, true);
        }

        _surfaceTransform.SetParent(null, true);
        surfaceBody.bodyType = RigidbodyType2D.Kinematic;
        surfaceBody.simulated = true;
        surfaceBody.gravityScale = 0f;
        surfaceBody.interpolation = RigidbodyInterpolation2D.None;
        surfaceBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        surfaceBody.constraints = RigidbodyConstraints2D.FreezeRotation;

        Vector2 target = TargetPosition();
        surfaceBody.position = target;
        surfaceBody.rotation = 0f;
        surfaceBody.linearVelocity = Vector2.zero;
        surfaceBody.angularVelocity = 0f;

        _surface = surfaceBody.GetComponent<PlayerHeadPlatformSurface>();
        if (_surface == null)
            _surface = surfaceBody.gameObject.AddComponent<PlayerHeadPlatformSurface>();
        _surface.Initialize(surfaceBody, target, this);
        _initialized = true;
    }

    void OnEnable()
    {
        if (_initialized && surfaceBody != null)
            surfaceBody.simulated = true;
        SubscribeToPhysicsClock();
    }

    void Start()
    {
        // OnEnable can precede NetworkManager.Awake for a scene-placed player.
        SubscribeToPhysicsClock();
    }

    void FixedUpdate()
    {
        if (_subscribedTimeManager != null) return;
        PrepareSurfaceForSimulation(Time.fixedDeltaTime);
    }

    void LateUpdate()
    {
        // Offline fallback: the next FixedUpdate also realigns between catch-up
        // simulations; this closes the final rendered frame after physics.
        if (_subscribedTimeManager == null)
            AlignSurfaceAfterSimulation();
    }

    void OnPrePhysicsSimulation(float deltaTime)
    {
        if (!isActiveAndEnabled || _subscribedTimeManager == null) return;
        PrepareSurfaceForSimulation(deltaTime);
    }

    void OnPostPhysicsSimulation(float deltaTime)
    {
        if (!isActiveAndEnabled || _subscribedTimeManager == null) return;
        AlignSurfaceAfterSimulation();
    }

    void PrepareSurfaceForSimulation(float deltaTime)
    {
        if (!_initialized || surfaceBody == null) return;

        Vector2 target = TargetPosition();
        float snapDistance = Mathf.Max(0f, teleportSnapDistance);
        bool snap = snapDistance == 0f ||
            (target - surfaceBody.position).sqrMagnitude > snapDistance * snapDistance;

        if (snap)
        {
            surfaceBody.position = target;
            surfaceBody.linearVelocity = Vector2.zero;
        }
        else if (_ownerBody != null && !_ownerBody.simulated)
        {
            // FishNet drives remote roots by interpolating their Transform while the
            // root Rigidbody2D is unsimulated. MovePosition preserves real kinematic
            // travel through this simulation so a local rider is carried instead of
            // seeing the surface teleport before the solver runs.
            surfaceBody.MovePosition(target);
        }
        else
        {
            // Start every simulation at the exact owner offset. A matching velocity
            // lets the kinematic surface carry riders during the simulation itself;
            // the post-simulation alignment closes any gravity/contact divergence.
            surfaceBody.position = target;
            surfaceBody.linearVelocity = SurfaceVelocityForSimulation();
        }

        surfaceBody.angularVelocity = 0f;
        surfaceBody.SetRotation(0f);
        _surface?.SetTargetPosition(target);
    }

    void AlignSurfaceAfterSimulation()
    {
        if (!_initialized || surfaceBody == null) return;

        Vector2 target = TargetPosition();
        surfaceBody.position = target;
        surfaceBody.SetRotation(0f);
        surfaceBody.linearVelocity = Vector2.zero;
        surfaceBody.angularVelocity = 0f;
        _surface?.SetTargetPosition(target);
    }

    Vector2 SurfaceVelocityForSimulation()
    {
        if (_ownerBody == null || !_ownerBody.simulated)
            return Vector2.zero;

        // Use only solved/current velocity. Predicting gravity here makes the head
        // move down while a grounded owner is collision-constrained, then snap back.
        return _ownerBody.linearVelocity;
    }

    void SubscribeToPhysicsClock()
    {
        if (_subscribedTimeManager != null) return;

        TimeManager timeManager = InstanceFinder.TimeManager;
        if (timeManager == null) return;

        _subscribedTimeManager = timeManager;
        _subscribedTimeManager.OnPrePhysicsSimulation += OnPrePhysicsSimulation;
        _subscribedTimeManager.OnPostPhysicsSimulation += OnPostPhysicsSimulation;
    }

    void UnsubscribeFromPhysicsClock()
    {
        if (_subscribedTimeManager == null) return;
        _subscribedTimeManager.OnPrePhysicsSimulation -= OnPrePhysicsSimulation;
        _subscribedTimeManager.OnPostPhysicsSimulation -= OnPostPhysicsSimulation;
        _subscribedTimeManager = null;
    }

    void OnDisable()
    {
        UnsubscribeFromPhysicsClock();
        if (_initialized && surfaceBody != null)
        {
            surfaceBody.linearVelocity = Vector2.zero;
            surfaceBody.angularVelocity = 0f;
            surfaceBody.simulated = false;
        }
    }

    void OnDestroy()
    {
        UnsubscribeFromPhysicsClock();
        if (_surfaceTransform == null) return;

        GameObject surfaceObject = _surfaceTransform.gameObject;
        surfaceObject.SetActive(false);
        if (Application.isPlaying)
            Destroy(surfaceObject);
        else
            DestroyImmediate(surfaceObject);
    }

    Vector2 TargetPosition()
    {
        // Player gameplay keeps the root upright; using world-space offset also guarantees the
        // one-way line remains directly above the squirrel if physics briefly rotates the body.
        Vector2 ownerPosition = _ownerBody != null && _ownerBody.simulated
            ? _ownerBody.position
            : (Vector2)transform.position;
        return ownerPosition + _localOffset;
    }

    internal Vector2 GetReportedPlatformPosition()
    {
        if (!_initialized || surfaceBody == null)
            return TargetPosition();

        Vector2 target = TargetPosition();
        float snapDistance = Mathf.Max(0f, teleportSnapDistance);
        bool pendingSnap = snapDistance == 0f ||
            (target - surfaceBody.position).sqrMagnitude > snapDistance * snapDistance;

        // Ordinary remote interpolation is reported immediately so a rider's
        // OnTick carry velocity matches this tick's MovePosition sweep. For a
        // respawn/teleport, keep reporting the old physical pose until the solver
        // processes contact exit instead of producing a huge delta/TickDelta burst.
        return pendingSnap ? surfaceBody.position : target;
    }
}

/// <summary>Moving-platform view attached to the detached kinematic head surface.</summary>
public sealed class PlayerHeadPlatformSurface : MonoBehaviour, IMovingPlatform
{
    Rigidbody2D _body;
    Vector2 _targetPosition;
    PlayerHeadPlatform _driver;

    public void Initialize(Rigidbody2D body, Vector2 initialPosition, PlayerHeadPlatform driver)
    {
        _body = body;
        _targetPosition = initialPosition;
        _driver = driver;
    }

    public void SetTargetPosition(Vector2 position) => _targetPosition = position;

    public Vector2 GetPosition()
    {
        if (_driver != null)
            return _driver.GetReportedPlatformPosition();
        return _body != null ? _body.position : _targetPosition;
    }
}
