using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Virtual joystick for mobile controls.
/// Based on donkeytetris Joystick.gd reference implementation.
/// Touch and drag to control movement, hold up to glide.
/// </summary>
public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    [Header("Joystick Components")]
    [SerializeField] private RectTransform bigCircle;
    [SerializeField] private RectTransform smallCircle;
    [SerializeField] private Image bigCircleImage;
    [SerializeField] private Image smallCircleImage;

    [Header("Settings")]
    [SerializeField] private float maxDistance = 100f; // Max radius for small circle movement
    [SerializeField] private bool showDebugText = false;

    [Header("Joystick Behavior")]
    [Tooltip("Fixed: Joystick stays in one position (like donkeytetris). Dynamic: Joystick appears wherever you first touch.")]
    [SerializeField] private bool useDynamicPosition = true;
    [Tooltip("If dynamic, should the joystick snap back to original position when released?")]
    [SerializeField] private bool returnToOriginOnRelease = true;

    [Header("Visual Feedback")]
    [SerializeField] private float maxAlpha = 0.8f; // Max alpha for big circle when fully pressed
    [SerializeField] private float minAlpha = 0.3f; // Min alpha when not touched

    private Vector2 inputVector = Vector2.zero;
    private bool touched = false;
    private Color bigCircleMaxModulate;
    private Vector2 bigCircleOriginalPos; // Original position to return to

    // Edge detection for jump (only trigger once when first pushed up)
    private bool wasAboveJumpThreshold = false;

    // Canvas for screen-to-world conversions
    private Canvas parentCanvas;

    void Start()
    {
        if (bigCircle == null)
            bigCircle = transform.Find("BigCircle")?.GetComponent<RectTransform>();

        if (smallCircle == null)
            smallCircle = bigCircle?.Find("SmallCircle")?.GetComponent<RectTransform>();

        if (bigCircleImage == null && bigCircle != null)
            bigCircleImage = bigCircle.GetComponent<Image>();

        if (smallCircleImage == null && smallCircle != null)
            smallCircleImage = smallCircle.GetComponent<Image>();

        if (bigCircleImage != null)
        {
            bigCircleMaxModulate = bigCircleImage.color;
            SetBigCircleAlpha(useDynamicPosition ? 0f : minAlpha); // Hide if dynamic, show if fixed
        }

        if (bigCircle != null)
            bigCircleOriginalPos = bigCircle.anchoredPosition;

        // Find parent canvas for screen space conversions
        parentCanvas = GetComponentInParent<Canvas>();
    }

    void Update()
    {
        // Update visual feedback based on input magnitude
        if (touched && bigCircleImage != null)
        {
            float alpha = Mathf.Lerp(minAlpha, maxAlpha, inputVector.magnitude);
            SetBigCircleAlpha(alpha);
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (useDynamicPosition)
        {
            // Dynamic mode: Move joystick to touch position
            Vector2 localPoint;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                bigCircle.parent as RectTransform,
                eventData.position,
                eventData.pressEventCamera,
                out localPoint
            );

            bigCircle.anchoredPosition = localPoint;
            touched = true;

            // Show joystick
            if (bigCircleImage != null)
                SetBigCircleAlpha(minAlpha);

            OnDrag(eventData);
        }
        else
        {
            // Fixed mode (donkeytetris style): Check if touch is within big circle radius
            Vector2 localPoint;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                bigCircle,
                eventData.position,
                eventData.pressEventCamera,
                out localPoint
            );

            float distance = localPoint.magnitude;
            if (distance < maxDistance)
            {
                touched = true;
                OnDrag(eventData);
            }
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        touched = false;
        smallCircle.anchoredPosition = Vector2.zero;
        inputVector = Vector2.zero;
        wasAboveJumpThreshold = false; // Reset jump edge detection

        if (useDynamicPosition)
        {
            // Hide joystick in dynamic mode
            if (bigCircleImage != null)
                SetBigCircleAlpha(0f);

            // Return to original position if enabled
            if (returnToOriginOnRelease)
            {
                bigCircle.anchoredPosition = bigCircleOriginalPos;
            }
        }
        else
        {
            // Fixed mode: just reduce alpha
            if (bigCircleImage != null)
                SetBigCircleAlpha(minAlpha);
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!touched) return;

        // Get local position relative to big circle
        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            bigCircle,
            eventData.position,
            eventData.pressEventCamera,
            out localPoint
        );

        // Clamp the position to max distance
        Vector2 clampedPosition = Vector2.ClampMagnitude(localPoint, maxDistance);
        smallCircle.anchoredPosition = clampedPosition;

        // Calculate input vector (normalized -1 to 1)
        inputVector.x = clampedPosition.x / maxDistance;
        inputVector.y = clampedPosition.y / maxDistance;
    }

    private void SetBigCircleAlpha(float alpha)
    {
        Color color = bigCircleMaxModulate;
        color.a = alpha;
        bigCircleImage.color = color;
    }

    /// <summary>
    /// Get the current joystick input vector.
    /// Returns Vector2 with x and y values from -1 to 1.
    /// </summary>
    public Vector2 GetInputVector()
    {
        return inputVector;
    }

    /// <summary>
    /// Get horizontal input (-1 to 1).
    /// </summary>
    public float GetHorizontal()
    {
        return inputVector.x;
    }

    /// <summary>
    /// Get vertical input (-1 to 1).
    /// </summary>
    public float GetVertical()
    {
        return inputVector.y;
    }

    /// <summary>
    /// Check if joystick is currently being touched.
    /// </summary>
    public bool IsTouched()
    {
        return touched;
    }

    /// <summary>
    /// Check if joystick is held above horizontal axis (for gliding).
    /// </summary>
    public bool IsHeldUp()
    {
        return touched && inputVector.y > 0f;
    }

    /// <summary>
    /// Check if player should jump (joystick pushed up significantly).
    /// Uses edge detection - only returns true on the FIRST frame when threshold is crossed.
    /// This mimics pressing the Space key (not holding it).
    /// </summary>
    public bool ShouldJump()
    {
        bool isAboveThreshold = touched && inputVector.y > 0.5f;

        // Edge detection: only trigger when transitioning from below to above threshold
        bool shouldJump = isAboveThreshold && !wasAboveJumpThreshold;

        // Update state for next frame
        wasAboveJumpThreshold = isAboveThreshold;

        return shouldJump;
    }

    void OnDrawGizmos()
    {
        if (!showDebugText || !Application.isPlaying) return;

        // Debug visualization would go here
    }
}
