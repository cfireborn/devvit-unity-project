using System.Collections.Generic;
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

    /// <summary>All active goals for this player.</summary>
    public IReadOnlyList<Goal> Goals => goals;
    /// <summary>The goal the direction indicator points to.</summary>
    public Goal PrimaryGoal => primaryGoal;

    private Item _carriedItem;

    private Rigidbody2D rb;
    private bool isOnLadder;
    private bool isGliding;
    private bool goalReached;
    private int jumpsRemaining;

    // input capture (new Input System)
    private float moveInput;
    private float verticalInput;
    private bool jumpPressed;
    private bool jumpHeld;

    // Input System actions
    private InputActionMap actionMap;
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
    [Header("Simple Sprite States")]
    [Tooltip("SpriteRenderer used to change sprites for idle/walk/glide. Optional; if empty designers can still use spriteTransform visuals.")]
    public SpriteRenderer spriteRenderer;
    [Tooltip("Sprite shown when idle")]
    public Sprite idleSprite;
    [Tooltip("Sprite shown when gliding/falling")]
    public Sprite glideSprite;
    [Tooltip("Sprites used for simple walk cycling (designer can add frames)")]
    public Sprite[] walkSprites;
    [Tooltip("Frames per second for simple walk cycle")]
    public float walkFrameRate = 8f;

    // runtime walk animation state
    private int walkIndex = 0;
    private float walkTimer = 0f;

    // ground check (FixedUpdate), coyote time, jump buffer
    private bool _isGroundedFixed;
    private float _coyoteTimeRemaining;
    private float _jumpBufferRemaining;

    // when true, Player action map is disabled and input is zeroed (e.g. during dialogue)
    private bool _gameplayInputSuspended;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb != null)
            rb.constraints |= RigidbodyConstraints2D.FreezeRotation;
    }

    void OnEnable()
    {
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
        }
    }

    void OnDisable()
    {
        if (jumpAction != null) jumpAction.performed -= OnJumpPerformed;
        if (activeMap != null)
        {
            activeMap.Disable();
            activeMap = null;
        }
        if (actionMap != null)
        {
            actionMap.Disable();
            actionMap = null;
        }
        moveAction = null;
        jumpAction = null;
    }

    void Start()
    {
        var gameServices = FindFirstObjectByType<GameServices>();
        if (gameServices != null)
            gameServices.RegisterPlayer(this);

        if (settings != null)
        {
            rb.gravityScale = settings.normalGravityScale;
            jumpsRemaining = settings.maxJumps;

            if (groundChecker != null)
            {
                groundChecker.platformTag = settings.groundTag;
            }
        }
        else
        {
            rb.gravityScale = 3f;
            jumpsRemaining = 1;
        }
    }

    void Update()
    {
        ReadInput();
        UpdateSprite();
    }

    void FixedUpdate()
    {
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
    }

    void ApplyMovement()
    {
        if (settings == null || goalReached) return;

        if (isOnLadder)
        {
            // On ladder: up/down (verticalInput) climbs, left/right (moveInput) moves horizontally. No gravity.
            rb.linearVelocity = new Vector2(moveInput * settings.moveSpeed, verticalInput * settings.ladderClimbSpeed);
            rb.gravityScale = 0f;
            return;
        }

        // Authoritative ground check in FixedUpdate (overlap at feet)
        _isGroundedFixed = DoGroundCheck();
        if (groundChecker != null)
            groundChecker.isGrounded = _isGroundedFixed;

        // Coyote time: extend "can jump" briefly after leaving ground
        if (_isGroundedFixed)
            _coyoteTimeRemaining = settings.coyoteTime;
        else
            _coyoteTimeRemaining = Mathf.Max(0f, _coyoteTimeRemaining - Time.fixedDeltaTime);

        // Jump buffer: decay so we only trigger if we land within the window
        _jumpBufferRemaining = Mathf.Max(0f, _jumpBufferRemaining - Time.fixedDeltaTime);

        bool canJump = (_isGroundedFixed || _coyoteTimeRemaining > 0f) && (jumpPressed || _jumpBufferRemaining > 0f);

        // Horizontal movement (interpolate if in air); jump is independent of L/R input
        float targetVx = moveInput * settings.moveSpeed;
        float lerpFactor = _isGroundedFixed ? 1f : settings.airControlMultiplier;
        float newVx = Mathf.Lerp(rb.linearVelocity.x, targetVx, lerpFactor);
        rb.linearVelocity = new Vector2(newVx, rb.linearVelocity.y);

        // Jumping (works when moving left/right; consume buffer and coyote on jump)
        if (canJump)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, settings.jumpForce);
            isGliding = false;
            _jumpBufferRemaining = 0f;
            _coyoteTimeRemaining = 0f;
        }

        // Gliding: when falling and the jump button is held
        if (!_isGroundedFixed && rb.linearVelocity.y < 0f && jumpHeld)
        {
            isGliding = true;
        }

        if (isGliding)
        {
            rb.gravityScale = settings.glideGravityScale;
        }
        else
        {
            rb.gravityScale = settings.normalGravityScale;
        }
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

        // Sprite swapping: choose sprite based on state (glide, walk, idle)
        if (spriteRenderer != null)
        {
            if (!CheckGround() && isGliding)
            {
                if (glideSprite != null) spriteRenderer.sprite = glideSprite;
            }
            else if (CheckGround() && Mathf.Abs(moveInput) > 0.1f)
            {
                // walking: simple frame cycle
                if (walkSprites != null && walkSprites.Length > 0)
                {
                    walkTimer += Time.deltaTime;
                    float frameTime = Mathf.Max(0.001f, 1f / Mathf.Max(0.01f, walkFrameRate));
                    if (walkTimer >= frameTime)
                    {
                        walkTimer = 0f;
                        walkIndex = (walkIndex + 1) % walkSprites.Length;
                    }
                    spriteRenderer.sprite = walkSprites[walkIndex];
                }
                else if (idleSprite != null)
                {
                    spriteRenderer.sprite = idleSprite;
                }
            }
            else
            {
                if (idleSprite != null) spriteRenderer.sprite = idleSprite;
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        if (settings != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere((Vector2)transform.position + settings.groundCheckOffset, settings.groundCheckRadius);
        }
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
        }

        print($"PlayerController: Added goal {goal.name}");
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

    /// <summary>Set the primary goal (e.g. for the direction indicator).</summary>
    public void SetPrimaryGoal(Goal goal)
    {
        primaryGoal = goal;
    }

    /// <summary>Check if the player has a specific goal.</summary>
    public bool HasGoal(Goal goal)
    {
        return goal != null && goals.Contains(goal);
    }

    /// <summary>Set the currently carried item (from ItemPickupTrigger).</summary>
    public void SetCarriedItem(Item item)
    {
        _carriedItem = item;
    }

    /// <summary>Get the currently carried item.</summary>
    public Item GetCarriedItem()
    {
        return _carriedItem;
    }

    /// <summary>Clear the carried item (e.g. after delivery).</summary>
    public void ClearCarriedItem()
    {
        _carriedItem = null;
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
    /// Move player to spawn and reset movement state only. Goals, carried item, and trigger states are preserved.
    /// </summary>
    public void ResetForRespawn(Vector3 spawnPosition)
    {
        // reposition and clear velocities only; keep goals, carried item, and trigger states
        transform.position = spawnPosition;
        rb.linearVelocity = Vector2.zero;

        // reset movement related state
        if (settings != null)
        {
            jumpsRemaining = settings.maxJumps;
            rb.gravityScale = settings.normalGravityScale;
        }
        else
        {
            jumpsRemaining = 1;
            rb.gravityScale = 3f;
        }

        isOnLadder = false;
        isGliding = false;
        _coyoteTimeRemaining = 0f;
        _jumpBufferRemaining = 0f;
    }


    private bool CheckGround()
    {
        return _isGroundedFixed;
    }

    /// <summary>Ground check used in FixedUpdate: overlap circle at feet. Uses settings.groundCheckOffset, groundCheckRadius, groundTag.</summary>
    private bool DoGroundCheck()
    {
        if (settings == null || rb == null) return false;
        Vector2 origin = (Vector2)transform.position + settings.groundCheckOffset;
        float radius = settings.groundCheckRadius;
        string tagToMatch = settings.groundTag;
        var ourColliders = GetComponentsInChildren<Collider2D>();

        var hits = Physics2D.OverlapCircleAll(origin, radius);
        for (int i = 0; i < hits.Length; i++)
        {
            var c = hits[i];
            if (c == null) continue;
            bool isOurs = false;
            for (int j = 0; j < ourColliders.Length; j++)
            {
                if (ourColliders[j] == c) { isOurs = true; break; }
            }
            if (isOurs) continue;
            if (c.CompareTag(tagToMatch))
                return true;
        }
        return false;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Ladder"))
        {
            isOnLadder = true;
            rb.linearVelocity = Vector2.zero;
            rb.gravityScale = 0f;
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Ladder"))
        {
            isOnLadder = false;
            if (settings != null)
                rb.gravityScale = settings.normalGravityScale;
            else
                rb.gravityScale = 3f;
        }
    }
}
