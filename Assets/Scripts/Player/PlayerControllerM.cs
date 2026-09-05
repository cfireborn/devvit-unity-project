using System;
using System.Collections;
using System.Collections.Generic;
using FishNet;
using FishNet.Component.Transforming;
using FishNet.Managing.Timing;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class PlayerControllerM : MonoBehaviour
{
    [Header("Config")]
    public PlayerSettingsM settings;
    public GameState gameState;
    public GroundChecker groundChecker;

    [Header("Goals")]
    [Tooltip("All active goals for this player.")] 
    private List<Goal> goals = new List<Goal>();
    [Tooltip("The goal the direction indicator points to (e.g. current delivery target).")]
    private Goal primaryGoal;
    private int _completedGoalsCount;

    /// <summary>All active goals for this player.</summary>
    public IReadOnlyList<Goal> Goals => goals;
    /// <summary>The goal the direction indicator points to.</summary>
    public Goal PrimaryGoal => primaryGoal;
    /// <summary>Number of goals completed this session (e.g. successful deliveries).</summary>
    public int CompletedGoalsCount => _completedGoalsCount;

    /// <summary>Fired after <see cref="CompletedGoalsCount"/> increments from <see cref="CompleteGoal"/>.</summary>
    public event Action CompletedGoalsCountChanged;

    private Rigidbody2D rb;
    private Collider2D playerCollider;
    private bool isGliding;
    private bool goalReached;
    private bool _wasOnLadder;

    // input capture (new Input System)
    private float moveInput;
    private float verticalInput;
    private bool jumpPressed;
    private bool jumpHeld;

    // Input System actions
    private InputAction moveAction;
    private InputAction jumpAction;
    private bool jumpPressedFlag;
    private InputActionMap activeMap;

    [Header("Input")]
    [Tooltip("Assign the generated Input Actions asset (contains a 'Player' action map with Move and Jump actions)")]
    public UnityEngine.InputSystem.InputActionAsset inputActionAsset;

    [Header("Visuals")]
    [Tooltip("Assign the Transform that contains the sprite visuals (use a child object so flipping/tilt doesn't affect physics). If null the root transform will be used.")]
    public Transform spriteTransform;
    [Tooltip("If true, flip the visuals by scaling X when moving left/right.")]
    public bool flipSpriteWithMovement = true;
    [Tooltip("Maximum tilt angle of the sprite when moving.")]
    public float maxRotationAngle = 15f;
    [Header("Sprite")]
    [Tooltip("SpriteRenderer used for facing (flipX). Optional; if empty, scale flip on spriteTransform is used instead.")]
    public SpriteRenderer spriteRenderer;
    [Tooltip("Animator driving player idle/walk/jump/glide sprites. Expects bool IsGrounded, bool IsJumping, bool IsGliding, float Speed.")]
    public Animator playerSpriteAnimator;

    [Header("Goal feedback")]
    [Tooltip("Optional Animator (e.g. on player or child) — receives addGoalAnimationTrigger when AddGoal runs.")]
    public Animator goalFeedbackAnimator;
    [Tooltip("Animator trigger parameter name fired when a goal is added.")]
    public string addGoalAnimationTrigger = "GoalAdded";

    static readonly int AnimIsGrounded = Animator.StringToHash("IsGrounded");
    static readonly int AnimIsJumping = Animator.StringToHash("IsJumping");
    static readonly int AnimIsGliding = Animator.StringToHash("IsGliding");
    static readonly int AnimSpeed = Animator.StringToHash("Speed");

    private bool _isJumping;

    // ground check (FixedUpdate), coyote time, jump buffer
    private bool _isGroundedFixed;
    private float _coyoteTimeRemaining;
    private float _jumpBufferRemaining;
    private float _dropThroughBufferRemaining;
    private readonly List<Collider2D> _dropThroughColliders = new List<Collider2D>();
    private Coroutine _dropThroughCoroutine;
    private bool _dropThroughInputHeld;
    private TimeManager _subscribedTimeManager;

    // when true, Player action map is disabled and input is zeroed (e.g. during dialogue)
    private bool _gameplayInputSuspended;

    // Moving platform: apply platform/ladder delta so player moves with clouds and ladders
    private IMovingPlatform _lastMovingPlatform;
    private Vector2 _lastMovingPlatformPosition;
    private Vector2 _currentPlatformVelocity;
    private Vector2 _pendingPlatformVelocity;
    private bool _platformDeltaAppliedManually;

    // Read-only access for network visual sync
    public float MoveInputX => moveInput;
    public bool IsGliding => isGliding;
    public bool IsGrounded => _isGroundedFixed;
    public bool IsJumping => _isJumping;

    /// <summary>Drive playerSpriteAnimator from local or networked visual state.</summary>
    public void SetSpriteAnimatorState(float moveX, bool gliding, bool grounded, bool jumping)
    {
        if (playerSpriteAnimator == null) return;
        playerSpriteAnimator.SetBool(AnimIsGrounded, grounded);
        playerSpriteAnimator.SetBool(AnimIsJumping, jumping);
        playerSpriteAnimator.SetBool(AnimIsGliding, gliding);
        playerSpriteAnimator.SetFloat(AnimSpeed, Mathf.Abs(moveX));
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        playerCollider = GetComponent<Collider2D>();
        if ((spriteTransform == null || spriteTransform == transform) &&
            spriteRenderer != null && spriteRenderer.transform != transform)
        {
            spriteTransform = spriteRenderer.transform;
        }
        if (rb != null)
            rb.constraints |= RigidbodyConstraints2D.FreezeRotation;
    }

    void OnEnable()
    {
        // When FishNet PhysicsMode is set to TimeManager, physics is stepped during the
        // network tick rather than Unity's FixedUpdate. Subscribe here so physics-based
        // movement stays in sync with NetworkTransform updates.
        SubscribeToNetworkPhysicsClock();

        if (inputActionAsset != null)
        {
            // use the assigned asset's Player map
            activeMap = inputActionAsset.FindActionMap("Player", true);
            if (activeMap == null)
            {
                Debug.LogWarning("PlayerController: InputActionAsset does not contain a 'Player' action map. Falling back to code-built map.");
            }
            else
            {
                moveAction = activeMap.FindAction("Move", true);
                jumpAction = activeMap.FindAction("Jump", true);
                if (jumpAction != null) jumpAction.performed += OnJumpPerformed;
                if (!_gameplayInputSuspended) activeMap.Enable();
                return;
            }
        }

        Debug.Log("PlayerController: No Action Map assigned");
    }

    /// <summary>Disable or re-enable gameplay input (Move, Jump). Used e.g. when dialogue UI is open.</summary>
    public void SetGameplayInputEnabled(bool enabled)
    {
        _gameplayInputSuspended = !enabled;
        if (activeMap != null)
        {
            if (enabled) activeMap.Enable(); else activeMap.Disable();
        }
        if (!enabled)
        {
            moveInput = 0f;
            verticalInput = 0f;
            jumpPressedFlag = false;
            _jumpBufferRemaining = 0f;
            _dropThroughBufferRemaining = 0f;
            isGliding = false;
            _dropThroughInputHeld = false;
        }
    }

    void OnDisable()
    {
        RestoreDropThroughPlatform();
        _dropThroughInputHeld = false;

        UnsubscribeFromNetworkPhysicsClock();

        if (jumpAction != null) jumpAction.performed -= OnJumpPerformed;
        if (activeMap != null)
        {
            activeMap.Disable();
            activeMap = null;
        }
        moveAction = null;
        jumpAction = null;
    }

    void OnDestroy()
    {
        var gs = FindFirstObjectByType<GameServices>();
        if (gs != null)
            gs.DeregisterPlayer(this);
    }

    void Start()
    {
        SubscribeToNetworkPhysicsClock();

        var gameServices = FindFirstObjectByType<GameServices>();
        if (gameServices != null)
            gameServices.RegisterPlayer(this);

        if (settings != null)
        {
            rb.gravityScale = settings.normalGravityScale;

            if (groundChecker != null)
            {
                groundChecker.platformTag = settings.groundTag;
                groundChecker.groundCheckOffset = settings.groundCheckOffset;
                groundChecker.groundCheckRadius = settings.groundCheckRadius;
            }
        }
        else
        {
            rb.gravityScale = 3f;
        }

    }

    void Update()
    {
        ReadInput();
        UpdateSprite();
    }

    void OnTick()
    {
        // Used when FishNet PhysicsMode = TimeManager (networked play).
        ApplyMovement();
    }

    void FixedUpdate()
    {
        SubscribeToNetworkPhysicsClock();
        if (_subscribedTimeManager == null)
            ApplyMovement();
    }

    void OnJumpPerformed(InputAction.CallbackContext ctx)
    {
        jumpPressedFlag = true;
    }

    void ReadInput()
    {
        if (_gameplayInputSuspended)
        {
            moveInput = 0f;
            verticalInput = 0f;
            jumpPressed = false;
            jumpHeld = false;
            isGliding = false;
            return;
        }

        // Read keyboard/gamepad input from Input System
        if (moveAction != null)
        {
            Vector2 mv = moveAction.ReadValue<Vector2>();
            moveInput = mv.x;
            verticalInput = mv.y;
        }
        else
        {
            moveInput = 0f;
            verticalInput = 0f;
        }

        if (jumpAction != null)
        {
            jumpHeld = jumpAction.ReadValue<float>() > 0.5f;
        }
        else
        {
            jumpHeld = false;
        }

        // Add mobile joystick input (if available)
        if (MobileInputManager.Instance != null && MobileInputManager.Instance.IsMobileControlsActive())
        {
            Vector2 mobileInput = MobileInputManager.Instance.GetMobileInputVector();

            // Combine mobile and keyboard/gamepad input (take the stronger input)
            if (Mathf.Abs(mobileInput.x) > Mathf.Abs(moveInput))
                moveInput = mobileInput.x;
            if (Mathf.Abs(mobileInput.y) > Mathf.Abs(verticalInput))
                verticalInput = mobileInput.y;

            // Mobile jump detection (joystick pushed up significantly)
            bool mobileJumpPressed = MobileInputManager.Instance.GetMobileJumpPressed();
            if (mobileJumpPressed && !jumpPressedFlag)
            {
                jumpPressedFlag = true;
            }

            // Mobile glide detection (joystick held up)
            bool mobileGlideHeld = MobileInputManager.Instance.GetMobileGlideHeld();
            if (mobileGlideHeld)
            {
                jumpHeld = true;
            }
        }

        jumpPressed = jumpPressedFlag;
        if (jumpPressedFlag && settings != null)
            _jumpBufferRemaining = settings.jumpBufferTime;
        jumpPressedFlag = false;

        bool dropThroughHeld = verticalInput < -0.5f;
        if (dropThroughHeld && !_dropThroughInputHeld && settings != null)
            _dropThroughBufferRemaining = settings.jumpBufferTime;
        _dropThroughInputHeld = dropThroughHeld;
    }

    void ApplyMovement()
    {
        if (settings == null || goalReached) return;

        groundChecker?.SelectLadder(verticalInput);
        ApplyMovingPlatformDelta();

        // Refresh before the ladder branch so Down can pass through a platform at a ladder opening.
        if (groundChecker != null)
        {
            groundChecker.RefreshCheck();
            _isGroundedFixed = groundChecker.isGrounded;
        }
        else
            _isGroundedFixed = false;

        // ApplyMovingPlatformDelta uses the contact from the previous simulation.
        // A head first detected by this refresh must expose its current velocity now
        // so a buffered landing-jump inherits and retains both axes immediately.
        if (_isGroundedFixed && groundChecker.CurrentPlatform is PlayerHeadPlatformSurface refreshedHeadSurface)
        {
            _currentPlatformVelocity = refreshedHeadSurface.GetVelocity();
            _pendingPlatformVelocity = _currentPlatformVelocity;
            _platformDeltaAppliedManually = false;
        }

        bool isInsideLadder = groundChecker != null && groundChecker.IsOnLadder;
        bool dropThroughHeld = verticalInput < -0.5f;
        bool dropThroughRequested = _dropThroughBufferRemaining > 0f || (isInsideLadder && dropThroughHeld);
        bool droppedThrough = _isGroundedFixed && dropThroughRequested && TryDropThroughCurrentPlatform();
        if (droppedThrough)
            _dropThroughBufferRemaining = 0f;
        bool jumpRequested = jumpPressed || _jumpBufferRemaining > 0f;
        // Up/W/mobile-up is deliberately bound to both Jump and vertical movement. From a
        // grounded cloud, keep the expected jump. Once airborne (or already climbing), the
        // ladder must win immediately instead of waiting for the shared jump buffer to expire.
        bool upwardLadderPriority = isInsideLadder && verticalInput > 0.5f &&
            (_wasOnLadder || !_isGroundedFixed);
        if (upwardLadderPriority)
        {
            jumpPressed = false;
            _jumpBufferRemaining = 0f;
            jumpRequested = false;
        }
        bool isOnLadder = isInsideLadder && !jumpRequested && !droppedThrough &&
            (_wasOnLadder || Mathf.Abs(verticalInput) > 0.5f);
        if (droppedThrough || (isOnLadder && dropThroughHeld))
        {
            jumpPressed = false;
            _jumpBufferRemaining = 0f;
            jumpRequested = false;
            _dropThroughBufferRemaining = 0f;
            _coyoteTimeRemaining = 0f;
        }

        if (isOnLadder)
        {
            if (!_wasOnLadder)
            {
                rb.linearVelocity = Vector2.zero;
                rb.gravityScale = 0f;
            }
            _wasOnLadder = true;
            isGliding = false;
            // On ladder: up/down (verticalInput) climbs, left/right (moveInput) moves horizontally. No gravity.
            rb.linearVelocity = new Vector2(moveInput * settings.moveSpeed, verticalInput * settings.ladderClimbSpeed);
            rb.gravityScale = 0f;
            return;
        }
        _wasOnLadder = false;

        // Capture support before its timer advances so a press on the last positive
        // coyote-time tick is still honored during this movement step.
        bool hasJumpSupport = _isGroundedFixed || _coyoteTimeRemaining > 0f;

        // Coyote time: extend "can jump" briefly after leaving ground
        if (_isGroundedFixed)
            _coyoteTimeRemaining = settings.coyoteTime;
        else
            _coyoteTimeRemaining = Mathf.Max(0f, _coyoteTimeRemaining - TickOrFixedDelta());

        // Jump buffer: decay so we only trigger if we land within the window
        _jumpBufferRemaining = Mathf.Max(0f, _jumpBufferRemaining - TickOrFixedDelta());
        _dropThroughBufferRemaining = Mathf.Max(0f, _dropThroughBufferRemaining - TickOrFixedDelta());

        bool canJump = hasJumpSupport && jumpRequested;

        // Horizontal movement (interpolate if in air); jump is independent of L/R input
        float carryVx = _isGroundedFixed
            ? (_platformDeltaAppliedManually ? 0f : _currentPlatformVelocity.x)
            : _pendingPlatformVelocity.x;
        float targetVx = moveInput * settings.moveSpeed + carryVx;
        float lerpFactor = _isGroundedFixed ? 1f : settings.airControlMultiplier;
        float newVx = Mathf.Lerp(rb.linearVelocity.x, targetVx, lerpFactor);
        rb.linearVelocity = new Vector2(newVx, rb.linearVelocity.y);

        // Jumping (works when moving left/right; consume buffer and coyote on jump)
        if (canJump)
        {
            float carryVy = _isGroundedFixed ? _currentPlatformVelocity.y : _pendingPlatformVelocity.y;
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, settings.jumpForce + carryVy);
            isGliding = false;
            jumpPressed = false;
            _jumpBufferRemaining = 0f;
            _coyoteTimeRemaining = 0f;
        }

        // Gliding is active only while falling and the jump/up input remains held.
        isGliding = !_isGroundedFixed && rb.linearVelocity.y < 0f && jumpHeld;

        if (isGliding)
        {
            rb.gravityScale = settings.glideGravityScale;
        }
        else
        {
            rb.gravityScale = settings.normalGravityScale;
        }

        if (_isGroundedFixed)
        {
            _pendingPlatformVelocity = _currentPlatformVelocity;
        }
        else
        {
            float decay = 8f * TickOrFixedDelta();
            _pendingPlatformVelocity = Vector2.MoveTowards(_pendingPlatformVelocity, Vector2.zero, decay);
        }
    }

    bool TryDropThroughCurrentPlatform()
    {
        Collider2D ground = groundChecker != null ? groundChecker.CurrentGroundCollider : null;
        if (ground == null) return false;

        PlatformEffector2D effector = ground.GetComponent<PlatformEffector2D>() ?? ground.GetComponentInParent<PlatformEffector2D>();
        if (effector == null || !effector.useOneWay || !ground.usedByEffector || playerCollider == null) return false;
        bool alreadyDropping = _dropThroughCoroutine != null;

        Bounds playerBounds = playerCollider.bounds;
        Bounds groundBounds = ground.bounds;
        float seamTolerance = Physics2D.defaultContactOffset * 2f;
        Collider2D[] candidates = effector.GetComponents<Collider2D>();
        int addedCount = 0;
        for (int i = 0; i < candidates.Length; i++)
        {
            Collider2D candidate = candidates[i];
            if (candidate == null || !candidate.enabled || candidate.isTrigger || !candidate.usedByEffector) continue;

            bool touching = candidate == ground || playerCollider.IsTouching(candidate);
            bool sameSupportSeam = Mathf.Abs(candidate.bounds.max.y - groundBounds.max.y) <= seamTolerance &&
                candidate.bounds.max.x >= playerBounds.min.x - seamTolerance &&
                candidate.bounds.min.x <= playerBounds.max.x + seamTolerance;
            if (!touching && !sameSupportSeam) continue;
            if (_dropThroughColliders.Contains(candidate)) continue;

            Physics2D.IgnoreCollision(playerCollider, candidate, true);
            _dropThroughColliders.Add(candidate);
            addedCount++;
        }

        if (addedCount == 0) return false;
        if (alreadyDropping)
            StopCoroutine(_dropThroughCoroutine);
        _dropThroughCoroutine = StartCoroutine(RestoreDropThroughPlatformAfterClearance());
        return true;
    }

    IEnumerator RestoreDropThroughPlatformAfterClearance()
    {
        float minimumDuration = settings != null ? settings.dropThroughDuration : 0.25f;
        float elapsed = 0f;
        while (true)
        {
            yield return null;
            elapsed += Time.deltaTime;
            if (elapsed < minimumDuration || playerCollider == null) continue;

            Bounds playerBounds = playerCollider.bounds;
            bool cleared = true;
            for (int i = 0; i < _dropThroughColliders.Count; i++)
            {
                Collider2D platform = _dropThroughColliders[i];
                if (platform != null && platform.enabled)
                {
                    Bounds platformBounds = platform.bounds;
                    bool separated = playerBounds.max.y <= platformBounds.min.y ||
                        playerBounds.min.y >= platformBounds.max.y ||
                        playerBounds.max.x <= platformBounds.min.x ||
                        playerBounds.min.x >= platformBounds.max.x;
                    if (!separated)
                    {
                        cleared = false;
                        break;
                    }
                }
            }
            if (cleared) break;
        }

        _dropThroughCoroutine = null;
        RestoreDropThroughPlatform();
    }

    void RestoreDropThroughPlatform()
    {
        if (_dropThroughCoroutine != null)
            StopCoroutine(_dropThroughCoroutine);
        for (int i = 0; i < _dropThroughColliders.Count; i++)
        {
            Collider2D platform = _dropThroughColliders[i];
            if (playerCollider != null && platform != null)
                Physics2D.IgnoreCollision(playerCollider, platform, false);
        }
        _dropThroughColliders.Clear();
        _dropThroughCoroutine = null;
    }

    private void UpdateSprite()
    {

        if (spriteTransform == null) spriteTransform = transform;

        float verticalTilt = 0f;
        if (rb != null) verticalTilt = -rb.linearVelocity.y * 0.5f; // tune multiplier as needed

        float desiredAngle = -moveInput * maxRotationAngle + verticalTilt;
        desiredAngle = Mathf.Clamp(desiredAngle, -Mathf.Abs(maxRotationAngle), Mathf.Abs(maxRotationAngle));

        Quaternion target = Quaternion.Euler(0f, 0f, desiredAngle);
        spriteTransform.localRotation = Quaternion.Lerp(spriteTransform.localRotation, target, Time.deltaTime * 10f);

        // Flip the visuals to face left/right while moving (keeps the tilt)
        if (spriteRenderer != null)
        {
            if (Mathf.Abs(moveInput) > 0.1f)
                spriteRenderer.flipX = moveInput < 0f;
        }
        else if (flipSpriteWithMovement && Mathf.Abs(moveInput) > 0.1f)
        {
            Vector3 s = spriteTransform.localScale;
            s.x = Mathf.Abs(s.x) * (moveInput < 0f ? -1f : 1f);
            spriteTransform.localScale = s;
        }

        bool onLadder = _wasOnLadder;
        _isJumping = !_isGroundedFixed && !isGliding && !onLadder
            && rb != null && rb.linearVelocity.y > 0.01f;

        SetSpriteAnimatorState(moveInput, isGliding, _isGroundedFixed, _isJumping);
    }

    void OnDrawGizmosSelected()
    {
        if (gameState != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(gameState.goalPosition, 0.1f);
        }
    }

    public void SetGoal(Transform goal)
    {
        // legacy support: set GameState goal position if available
        if (gameState != null && goal != null) gameState.goalPosition = goal.position;
    }

    /// <summary>Add a goal to the player's list.</summary>
    public void AddGoal(Goal goal)
    {
        if (goal != null && !goals.Contains(goal))
        {
            goals.Add(goal);
            print($"AddGoal: {goal.displayName}");
            if (goalFeedbackAnimator != null && !string.IsNullOrEmpty(addGoalAnimationTrigger))
                goalFeedbackAnimator.SetTrigger(addGoalAnimationTrigger);
        }
    }

    /// <summary>Remove a goal from the player's list.</summary>
    public void RemoveGoal(Goal goal)
    {
        if (goal != null)
        {
            goals.Remove(goal);
            if (primaryGoal == goal)
            {
                primaryGoal = goals.Count > 0 ? goals[0] : null;
            }
        }
    }

    /// <summary>Complete an active goal (increments completed count and removes it from the list).</summary>
    public void CompleteGoal(Goal goal)
    {
        if (goal == null || !goals.Contains(goal)) return;
        _completedGoalsCount++;
        RemoveGoal(goal);
        CompletedGoalsCountChanged?.Invoke();
    }

    /// <summary>Set the primary goal (e.g. for the direction indicator). Ignores goals not in the active list.</summary>
    public void SetPrimaryGoal(Goal goal)
    {
        if (goal != null && !goals.Contains(goal))
            return;
        primaryGoal = goal;
    }

    /// <summary>Check if the player has a specific goal.</summary>
    public bool HasGoal(Goal goal)
    {
        return goal != null && goals.Contains(goal);
    }

    /// <summary>
    /// Atomically replaces local story progress without replaying goal animations or completion events.
    /// The admin story-checkpoint debugger intentionally supports one active narrative delivery at a time.
    /// </summary>
    public void ApplyStoryCheckpointGoals(Goal activeGoal, int completedGoalsCount)
    {
        goals.Clear();
        if (activeGoal != null)
            goals.Add(activeGoal);
        primaryGoal = activeGoal;
        _completedGoalsCount = Mathf.Max(0, completedGoalsCount);
        CompletedGoalsCountChanged?.Invoke();
    }

    /// <summary>
    /// Called by the GameManager (or InteractionTrigger) when the goal trigger fires.
    /// </summary>
    public void OnGoalTriggered(GameObject source, Vector2 contactPoint)
    {
        if (goalReached) return;
        goalReached = true;
        // stop movement and optionally play feedback
        rb.linearVelocity = Vector2.zero;
        Debug.Log($"PlayerController: Goal reached at {contactPoint} by {source?.name}");
    }

    /// <summary>
    /// Optional callback used by GameManager upon registering the goal with the player.
    /// </summary>
    public void OnGoalRegistered(GameObject goalObject)
    {
        // reserved for potential UI/feedback hooks; no-op for now
    }

    /// <summary>
    /// Move player to spawn and reset movement state only. Goals and trigger states are preserved.
    /// </summary>
    public void ResetForRespawn(Vector3 spawnPosition)
    {
        RestoreDropThroughPlatform();

        // reposition and clear velocities only; keep goals and trigger states
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.position = spawnPosition;
        }
        transform.position = spawnPosition;

        // Tell FishNet that this large position change is an intentional teleport so
        // interpolation does not visually pull the player back across the level.
        var networkTransform = GetComponent<NetworkTransform>();
        if (networkTransform != null)
            networkTransform.Teleport();

        // reset movement related state
        if (settings != null)
            rb.gravityScale = settings.normalGravityScale;
        else
            rb.gravityScale = 3f;

        isGliding = false;
        _wasOnLadder = false;
        _lastMovingPlatform = null;
        _lastMovingPlatformPosition = Vector2.zero;
        _currentPlatformVelocity = Vector2.zero;
        _pendingPlatformVelocity = Vector2.zero;
        _platformDeltaAppliedManually = false;
        _isGroundedFixed = false;
        _coyoteTimeRemaining = 0f;
        _jumpBufferRemaining = 0f;
        _dropThroughBufferRemaining = 0f;
        _dropThroughInputHeld = false;
        groundChecker?.ClearGroundState();
        groundChecker?.ClearLadderState();
    }

    void LateUpdate()
    {
        // ApplyMovingPlatformDelta moved to ApplyMovement (FixedUpdate/OnTick) to prevent drift and jitter.
    }

    void ApplyMovingPlatformDelta()
    {
        if (groundChecker == null || rb == null) return;

        _platformDeltaAppliedManually = false;

        IMovingPlatform current = _wasOnLadder ? groundChecker.CurrentLadder : groundChecker.CurrentPlatform;
        if (current == null)
        {
            _currentPlatformVelocity = Vector2.zero;
            _lastMovingPlatform = null;
            return;
        }

        Vector2 pos = current.GetPosition();
        bool physicsDrivenPlatform = false;
        if (current is Component component)
        {
            var platformRb = component.GetComponent<Rigidbody2D>();
            physicsDrivenPlatform = platformRb != null && platformRb.simulated &&
                platformRb.bodyType == RigidbodyType2D.Kinematic;
            if (current is CloudPlatform cloudPlatform && !cloudPlatform.enabled)
                physicsDrivenPlatform = false;
        }
        if (_lastMovingPlatform != current)
        {
            _lastMovingPlatform = current;
            _lastMovingPlatformPosition = pos;
            _currentPlatformVelocity = current is PlayerHeadPlatformSurface initialHeadSurface
                ? initialHeadSurface.GetVelocity()
                : Vector2.zero;
            _pendingPlatformVelocity = _currentPlatformVelocity;
            return;
        }

        Vector2 delta = pos - _lastMovingPlatformPosition;
        _lastMovingPlatformPosition = pos;

        // Only manually move the player if it's NOT a physics-driven platform (which handles it automatically)
        // OR if the player is on a ladder (ladders are usually triggers, so no physics contact movement)
        if (_wasOnLadder || !physicsDrivenPlatform)
        {
            rb.position += delta;
            _platformDeltaAppliedManually = true;
        }

        float dt = Mathf.Max(0.0001f, TickOrFixedDelta());
        _currentPlatformVelocity = current is PlayerHeadPlatformSurface movingHeadSurface
            ? movingHeadSurface.GetVelocity()
            : delta / dt;
    }

    // Returns the correct timestep whether we're running in OnTick (networked) or FixedUpdate (offline).
    float TickOrFixedDelta()
    {
        if (_subscribedTimeManager != null)
            return (float)_subscribedTimeManager.TickDelta;
        return Time.fixedDeltaTime;
    }

    void SubscribeToNetworkPhysicsClock()
    {
        if (_subscribedTimeManager != null) return;
        TimeManager timeManager = InstanceFinder.TimeManager;
        if (timeManager == null || timeManager.PhysicsMode != PhysicsMode.TimeManager) return;
        _subscribedTimeManager = timeManager;
        _subscribedTimeManager.OnTick += OnTick;
    }

    void UnsubscribeFromNetworkPhysicsClock()
    {
        if (_subscribedTimeManager == null) return;
        _subscribedTimeManager.OnTick -= OnTick;
        _subscribedTimeManager = null;
    }

}
