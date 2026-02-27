using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Full-screen invisible panel that receives all touch/click input.
/// Forwards input to VirtualJoystick for screen zone detection.
/// This allows the joystick to detect touches anywhere on screen, not just on the joystick image.
/// </summary>
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(Image))] // Needed for raycasting
public class MobileTouchReceiver : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    [Header("References")]
    [SerializeField] private VirtualJoystick virtualJoystick;

    [Header("Auto-Setup")]
    public bool autoConfigureOnStart = true;

    void Awake()
    {
        if (autoConfigureOnStart)
        {
            SetupFullScreenReceiver();
        }
    }

    void Start()
    {
        // Auto-find VirtualJoystick if not assigned
        if (virtualJoystick == null)
        {
            virtualJoystick = FindFirstObjectByType<VirtualJoystick>();
        }

        if (virtualJoystick == null)
        {
            Debug.LogWarning("MobileTouchReceiver: VirtualJoystick not found! Touch input won't work.");
        }
    }

    void SetupFullScreenReceiver()
    {
        // Ensure this RectTransform is full-screen
        RectTransform rect = GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        // Setup Image component (required for raycasting)
        Image img = GetComponent<Image>();
        if (img == null)
        {
            img = gameObject.AddComponent<Image>();
        }

        // Make it invisible but still receive input
        img.color = new Color(0, 0, 0, 0); // Fully transparent
        img.raycastTarget = true; // CRITICAL: Must be true to receive input

        // Move to back of UI hierarchy (so it doesn't block other UI)
        transform.SetAsFirstSibling();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (virtualJoystick != null)
        {
            virtualJoystick.OnPointerDown(eventData);
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (virtualJoystick != null)
        {
            virtualJoystick.OnPointerUp(eventData);
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (virtualJoystick != null)
        {
            virtualJoystick.OnDrag(eventData);
        }
    }
}
