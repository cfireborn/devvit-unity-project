using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [SerializeField] private float jumpForce = 10f;
    [SerializeField] private float horizontalForce = 8f;
    [SerializeField] private float groundCheckDistance = 0.1f;
    [SerializeField] private float rotationTorque = 5f;
    [SerializeField] private float minSwipeDistance = 50f; // Minimum pixels to register as swipe

    private Rigidbody rb;
    private bool isGrounded;

    // Swipe detection
    private Vector2 swipeStartPos;
    private bool isTracking = false;
    private int jumpCount = 0;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        // Check if grounded
        CheckGrounded();

        // Handle swipe input with mouse
        if (Mouse.current != null)
        {
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                swipeStartPos = Mouse.current.position.ReadValue();
                isTracking = true;
            }

            if (Mouse.current.leftButton.wasReleasedThisFrame && isTracking)
            {
                Vector2 swipeEndPos = Mouse.current.position.ReadValue();
                ProcessSwipe(swipeStartPos, swipeEndPos);
                isTracking = false;
            }
        }

        // Handle touch input (mobile)
        if (Touchscreen.current != null)
        {
            var touch = Touchscreen.current.primaryTouch;

            if (touch.press.wasPressedThisFrame)
            {
                swipeStartPos = touch.position.ReadValue();
                isTracking = true;
            }

            if (touch.press.wasReleasedThisFrame && isTracking)
            {
                Vector2 swipeEndPos = touch.position.ReadValue();
                ProcessSwipe(swipeStartPos, swipeEndPos);
                isTracking = false;
            }
        }
    }

    void ProcessSwipe(Vector2 startPos, Vector2 endPos)
    {
        Vector2 swipeDelta = endPos - startPos;
        float swipeDistance = swipeDelta.magnitude;

        if (swipeDistance < minSwipeDistance)
        {
            Debug.Log("Swipe too short, ignored");
            return;
        }

        // Normalize to get direction
        Vector2 swipeDirection = swipeDelta.normalized;

        // Timer should start when the player actually jumps (handled in Jump())

        Jump(swipeDirection);
    }

    void CheckGrounded()
    {
        // Raycast down to check if on ground
        isGrounded = Physics.Raycast(transform.position, Vector3.down, transform.localScale.y / 2f + groundCheckDistance);
        if (!isGrounded)
        {
            // Handle the cube getting stuck in a corner
            if (rb.linearVelocity == Vector3.zero)
            {
                isGrounded = true;
            }
        }
    }

    void Jump(Vector2 swipeDirection)
    {
        if (!isGrounded)
        {
            Debug.Log("Can't jump - not grounded!");
            return;
        }

        // Start the timer on the first successful jump
        if (jumpCount == 0)
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.StartRound();
            }
        }
        jumpCount++;

        // Reset current velocity
        rb.linearVelocity = Vector3.zero;

        // Convert 2D swipe direction to 3D movement on x,z plane
        // Swipe up (positive Y) = forward (positive Z)
        // Swipe right (positive X) = right (positive X)
        Vector3 moveDirection = new Vector3(swipeDirection.x, 0f, swipeDirection.y).normalized;

        // Apply forces: vertical jump + horizontal movement
        Vector3 jumpVector = Vector3.up * jumpForce;
        Vector3 horizontalVector = moveDirection * horizontalForce;

        rb.AddForce(jumpVector + horizontalVector, ForceMode.Impulse);

        // Calculate rotation torque
        Vector3 torque;
        // Use movement direction with some randomness
        Vector3 movementTorque = Vector3.Cross(Vector3.up, moveDirection) * rotationTorque;
        Vector3 randomTorque = new Vector3(
            Random.Range(-rotationTorque, rotationTorque),
            Random.Range(-rotationTorque, rotationTorque),
            Random.Range(-rotationTorque, rotationTorque)
        );
        torque = Vector3.Lerp(movementTorque, randomTorque, 0.5f);

        rb.AddTorque(torque, ForceMode.Impulse);
    }
}
