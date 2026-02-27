using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages mobile input controls and detects when to show/hide virtual joystick.
/// Integrates with PlayerControllerM to provide touch-based movement.
/// </summary>
public class MobileInputManager : MonoBehaviour
{
    [Header("Components")]
    [SerializeField] private VirtualJoystick virtualJoystick;
    [SerializeField] private GameObject mobileUIContainer;

    [Header("Settings")]
    [SerializeField] private bool forceEnableOnWebGL = true;
    [SerializeField] private bool showOnDesktopForTesting = false;

    [Header("Auto-Setup")]
    [Tooltip("Automatically create a full-screen touch receiver at runtime (recommended for screen zones).")]
    [SerializeField] private bool autoCreateTouchReceiver = true;

    private bool isMobilePlatform = false;
    private static MobileInputManager _instance;

    public static MobileInputManager Instance => _instance;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;

        // Detect mobile platform
        isMobilePlatform = IsMobileDevice();

        // Show/hide mobile UI based on platform
        if (mobileUIContainer != null)
        {
            bool shouldShow = isMobilePlatform || showOnDesktopForTesting;
            mobileUIContainer.SetActive(shouldShow);
        }
    }

    void Start()
    {
        if (virtualJoystick == null)
            virtualJoystick = FindFirstObjectByType<VirtualJoystick>();

        // Auto-create touch receiver if enabled
        if (autoCreateTouchReceiver && (isMobilePlatform || showOnDesktopForTesting))
        {
            CreateTouchReceiver();
        }
    }

    /// <summary>
    /// Automatically create a full-screen touch receiver for screen zone detection.
    /// This allows touches anywhere on screen to be detected, not just on the joystick image.
    /// </summary>
    private void CreateTouchReceiver()
    {
        // Check if one already exists
        if (FindFirstObjectByType<MobileTouchReceiver>() != null)
        {
            Debug.Log("MobileInputManager: TouchReceiver already exists, skipping auto-creation.");
            return;
        }

        // Find the Canvas to parent the touch receiver to
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            canvas = FindFirstObjectByType<Canvas>();
        }

        if (canvas == null)
        {
            Debug.LogError("MobileInputManager: Cannot create TouchReceiver - no Canvas found!");
            return;
        }

        // Create the touch receiver GameObject
        GameObject touchReceiverObj = new GameObject("TouchReceiver_Auto");

        // Parent to Canvas (not MobileUI) to ensure full-screen coverage
        touchReceiverObj.transform.SetParent(canvas.transform, false);
        touchReceiverObj.transform.SetAsFirstSibling(); // Put it behind other UI

        // Setup RectTransform (full-screen)
        RectTransform rect = touchReceiverObj.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.anchorMin = new Vector2(0, 0);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;

        // Setup Image (invisible but receives raycasts)
        Image img = touchReceiverObj.AddComponent<Image>();
        img.color = new Color(0, 0, 0, 0); // Fully transparent
        img.raycastTarget = true; // CRITICAL: Must receive input

        // Setup MobileTouchReceiver component
        MobileTouchReceiver receiver = touchReceiverObj.AddComponent<MobileTouchReceiver>();
        receiver.autoConfigureOnStart = false; // We already configured it

        Debug.Log($"MobileInputManager: Auto-created full-screen TouchReceiver. Size: {rect.rect.width}x{rect.rect.height}");
    }

    /// <summary>
    /// Check if running on a mobile device or WebGL (which could be mobile browser).
    /// </summary>
    private bool IsMobileDevice()
    {
        #if UNITY_ANDROID || UNITY_IOS
            return true;
        #elif UNITY_WEBGL
            // WebGL could be mobile browser, enable if forceEnableOnWebGL is true
            return forceEnableOnWebGL;
        #else
            return showOnDesktopForTesting;
        #endif
    }

    /// <summary>
    /// Get horizontal input from virtual joystick if on mobile, otherwise 0.
    /// </summary>
    public float GetMobileHorizontal()
    {
        if (!isMobilePlatform && !showOnDesktopForTesting)
            return 0f;

        if (virtualJoystick == null)
            return 0f;

        return virtualJoystick.GetHorizontal();
    }

    /// <summary>
    /// Get vertical input from virtual joystick if on mobile, otherwise 0.
    /// </summary>
    public float GetMobileVertical()
    {
        if (!isMobilePlatform && !showOnDesktopForTesting)
            return 0f;

        if (virtualJoystick == null)
            return 0f;

        return virtualJoystick.GetVertical();
    }

    /// <summary>
    /// Check if jump button is pressed on mobile joystick.
    /// Returns true when joystick is pushed up significantly.
    /// </summary>
    public bool GetMobileJumpPressed()
    {
        if (!isMobilePlatform && !showOnDesktopForTesting)
            return false;

        if (virtualJoystick == null)
            return false;

        return virtualJoystick.ShouldJump();
    }

    /// <summary>
    /// Check if glide should be active (joystick held above horizontal).
    /// </summary>
    public bool GetMobileGlideHeld()
    {
        if (!isMobilePlatform && !showOnDesktopForTesting)
            return false;

        if (virtualJoystick == null)
            return false;

        return virtualJoystick.IsHeldUp();
    }

    /// <summary>
    /// Check if mobile controls are active.
    /// </summary>
    public bool IsMobileControlsActive()
    {
        return isMobilePlatform || showOnDesktopForTesting;
    }

    /// <summary>
    /// Get the full input vector from the joystick.
    /// </summary>
    public Vector2 GetMobileInputVector()
    {
        if (!isMobilePlatform && !showOnDesktopForTesting)
            return Vector2.zero;

        if (virtualJoystick == null)
            return Vector2.zero;

        return virtualJoystick.GetInputVector();
    }
}
