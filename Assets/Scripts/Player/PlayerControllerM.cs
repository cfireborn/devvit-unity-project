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
    [Tooltip("Optional: assign the generated Input Actions asset (contains a 'Player' action map with Move and Jump actions). If left empty the controller will build a small map in code.")]
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
                activeMap.Enable();
                return;
            }
        }

        Debug.Log("PlayerController: No Action Map assigned");
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

        jumpPressed = jumpPressedFlag;
        jumpPressedFlag = false;
    }

    void ApplyMovement()
    {
        if (settings == null || goalReached) return;

        if (isOnLadder)
        {
            // climb ladder by controlling velocity directly and disabling gravity
            rb.linearVelocity = new Vector2(moveInput * settings.moveSpeed, verticalInput * settings.ladderClimbSpeed);
            rb.gravityScale = 0f;
            return;
        }

        // Horizontal movement (interpolate if in air)
        float targetVx = moveInput * settings.moveSpeed;
        float lerpFactor = CheckGround() ? 1f : settings.airControlMultiplier;
        float newVx = Mathf.Lerp(rb.linearVelocity.x, targetVx, lerpFactor);
        rb.linearVelocity = new Vector2(newVx, rb.linearVelocity.y);

        // Jumping
        if (jumpPressed && CheckGround())
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, settings.jumpForce);
            isGliding = false;
        }

        // Gliding: when falling and the jump button is held
        if (!CheckGround() && rb.linearVelocity.y < 0f && jumpHeld)
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
    /// Reset player internal state and move to spawn point on respawn.
    /// </summary>
    public void ResetForRespawn(Vector3 spawnPosition)
    {
        // clear goal state
        goalReached = false;
        goals.Clear();
        primaryGoal = null;

        // reposition and clear velocities
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
    }


    private bool CheckGround()
    {
        if (groundChecker != null)
        {
            return groundChecker.isGrounded;
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
