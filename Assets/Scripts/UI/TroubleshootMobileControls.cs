using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// Use new Input System explicitly
using Mouse = UnityEngine.InputSystem.Mouse;
using Touchscreen = UnityEngine.InputSystem.Touchscreen;

/// <summary>
/// Diagnostic tool to help troubleshoot mobile control issues.
/// Add this to any GameObject and check the Console for diagnostic info.
/// </summary>
public class TroubleshootMobileControls : MonoBehaviour
{
    void Start()
    {
        Debug.Log("=== MOBILE CONTROLS DIAGNOSTICS ===");

        // Check Canvas
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("❌ NO CANVAS FOUND! Create a Canvas in your scene.");
        }
        else
        {
            Debug.Log("✅ Canvas found: " + canvas.name);

            // Check GraphicRaycaster
            GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
            if (raycaster == null)
            {
                Debug.LogError("❌ Canvas missing GraphicRaycaster component! Add it.");
            }
            else
            {
                Debug.Log("✅ GraphicRaycaster found on Canvas");
            }
        }

        // Check EventSystem
        EventSystem eventSystem = FindFirstObjectByType<EventSystem>();
        if (eventSystem == null)
        {
            Debug.LogError("❌ NO EVENTSYSTEM FOUND! Create one: GameObject → UI → Event System");
        }
        else
        {
            Debug.Log("✅ EventSystem found: " + eventSystem.name);
        }

        // Check MobileInputManager
        MobileInputManager mobileInput = FindFirstObjectByType<MobileInputManager>();
        if (mobileInput == null)
        {
            Debug.LogError("❌ NO MobileInputManager FOUND! Add it to your MobileUI GameObject.");
        }
        else
        {
            Debug.Log("✅ MobileInputManager found on: " + mobileInput.gameObject.name);
            Debug.Log("   - Is Mobile Controls Active: " + mobileInput.IsMobileControlsActive());
        }

        // Check VirtualJoystick
        VirtualJoystick joystick = FindFirstObjectByType<VirtualJoystick>();
        if (joystick == null)
        {
            Debug.LogError("❌ NO VirtualJoystick FOUND! Create the joystick UI.");
        }
        else
        {
            Debug.Log("✅ VirtualJoystick found on: " + joystick.gameObject.name);
        }

        // Check MobileTouchReceiver
        MobileTouchReceiver touchReceiver = FindFirstObjectByType<MobileTouchReceiver>();
        if (touchReceiver == null)
        {
            Debug.LogWarning("⚠️ NO MobileTouchReceiver found. Should be auto-created if 'Auto Create Touch Receiver' is enabled.");
        }
        else
        {
            Debug.Log("✅ MobileTouchReceiver found on: " + touchReceiver.gameObject.name);

            // Check its size
            RectTransform rect = touchReceiver.GetComponent<RectTransform>();
            if (rect != null)
            {
                Debug.Log($"   - TouchReceiver size: {rect.rect.width}x{rect.rect.height}");
                Debug.Log($"   - Screen size: {Screen.width}x{Screen.height}");
                Debug.Log($"   - Anchors: Min({rect.anchorMin.x}, {rect.anchorMin.y}) Max({rect.anchorMax.x}, {rect.anchorMax.y})");

                if (rect.rect.width < Screen.width * 0.9f || rect.rect.height < Screen.height * 0.9f)
                {
                    Debug.LogWarning("⚠️ TouchReceiver is NOT full-screen! It won't detect touches everywhere.");
                }
            }

            // Check Image component
            Image img = touchReceiver.GetComponent<Image>();
            if (img == null)
            {
                Debug.LogError("❌ TouchReceiver missing Image component!");
            }
            else if (!img.raycastTarget)
            {
                Debug.LogError("❌ TouchReceiver Image raycastTarget is FALSE! It won't detect touches.");
            }
            else
            {
                Debug.Log("✅ TouchReceiver Image configured correctly (transparent, raycastTarget=true)");
            }
        }

        Debug.Log("=== END DIAGNOSTICS ===");
        Debug.Log("\nIf you see ❌ errors above, fix those first!");
        Debug.Log("If you see ⚠️ warnings, check MobileInputManager settings.");
    }

    void Update()
    {
        // Log touches for debugging (New Input System)
        var touchscreen = Touchscreen.current;
        if (touchscreen != null && touchscreen.primaryTouch.press.wasPressedThisFrame)
        {
            Vector2 touchPos = touchscreen.primaryTouch.position.ReadValue();
            Debug.Log($"Touch detected at: {touchPos} (Screen: {Screen.width}x{Screen.height})");

            CheckZones(touchPos);
        }

        // Log mouse clicks for testing in editor (New Input System)
        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            Vector2 mousePos = mouse.position.ReadValue();
            Debug.Log($"Mouse click at: {mousePos} (Screen: {Screen.width}x{Screen.height})");

            CheckZones(mousePos);
        }
    }

    void CheckZones(Vector2 position)
    {
        // Check if in joystick zone
        VirtualJoystick joystick = FindFirstObjectByType<VirtualJoystick>();
        if (joystick != null)
        {
            bool inJoystickZone = joystick.IsInJoystickZone(position);
            bool inDialogueZone = joystick.IsInDialogueZone(position);
            Debug.Log($"   - In Joystick Zone (bottom 1/3): {inJoystickZone}");
            Debug.Log($"   - In Dialogue Zone (top 2/3): {inDialogueZone}");
        }
    }
}
