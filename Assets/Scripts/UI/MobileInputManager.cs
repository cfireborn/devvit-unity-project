using UnityEngine;

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
