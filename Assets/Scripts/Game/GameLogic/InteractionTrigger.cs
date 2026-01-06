using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Generic 2D interaction trigger.
/// - Fires UnityEvents on enter/exit and explicit interact
/// - Can filter by tag
/// - Optionally requires a button press to activate
/// - Can be single-use or reusable
/// Useful as a replacement for simple goal points and other interaction volumes.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class InteractionTrigger : MonoBehaviour
{
    [Header("Filter")]
    [Tooltip("If set, only colliders with this tag will trigger events. Leave empty to allow any collider.")]
    public string requiredTag = "Player";

    [Header("Interaction")]
    [Tooltip("If true, the player must press the interact button while inside the trigger to activate.")]
    public bool requireButtonPress = false;
    [Tooltip("Input button name used for interaction. Uses Unity Input Manager button names (e.g. 'Submit','Jump' or a custom name).")]
    public string interactButton = "Submit";
    [Tooltip("If true the trigger will only activate once.")]
    public bool singleUse = true;
    [Tooltip("If true the GameObject will be deactivated after the trigger fires (when singleUse=true).")]
    public bool autoDisableAfterUse = false;

    [Header("Events")]
    public UnityEvent onEnter;
    public UnityEvent onExit;

    [System.Serializable]
    public class GameObjectPointEvent : UnityEngine.Events.UnityEvent<GameObject, Vector2> { }

    public GameObjectPointEvent onInteract;

    [Header("Debug")]
    [Tooltip("Radius drawn in the editor for visualizing a small handle (no physical effect).")]
    public float gizmoRadius = 0.15f;

    bool _isOverlapping;
    bool _used;
    Collider2D _current;
    float _lastInteractTime;
    public float interactCooldown = 0f;

    void Reset()
    {
        // Ensure collider is a trigger for clean behaviour in most cases
        var c = GetComponent<Collider2D>();
        if (c != null) c.isTrigger = true;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsAllowed(other)) return;
        _isOverlapping = true;
        _current = other;
        onEnter?.Invoke();

        if (!requireButtonPress)
        {
            // compute an approximate contact point from the overlapping collider
            Vector2 contactPoint = other != null ? other.ClosestPoint(transform.position) : (Vector2)transform.position;
            TryActivate(contactPoint);
        }
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (_current == other)
        {
            _isOverlapping = false;
            _current = null;
            onExit?.Invoke();
        }
    }

    void Update()
    {
        if (requireButtonPress && _isOverlapping && !_used && Time.time - _lastInteractTime >= interactCooldown)
        {
            if (!string.IsNullOrEmpty(interactButton))
            {
                if (Input.GetButtonDown(interactButton))
                {
                    Vector2 contactPoint = _current != null ? _current.ClosestPoint(transform.position) : (Vector2)transform.position;
                    TryActivate(contactPoint);
                }
            }
            else
            {
                // fallback to common keys
                if (Input.GetButtonDown("Submit") || Input.GetButtonDown("Jump"))
                {
                    Vector2 contactPoint = _current != null ? _current.ClosestPoint(transform.position) : (Vector2)transform.position;
                    TryActivate(contactPoint);
                }
            }
        }
    }

    bool IsAllowed(Collider2D other)
    {
        if (string.IsNullOrEmpty(requiredTag)) return true;
        return other.CompareTag(requiredTag);
    }

    void TryActivate(Vector2 contactPoint)
    {
        if (_used && singleUse) return;
        _used = true;
        _lastInteractTime = Time.time;
        onInteract?.Invoke(_current != null ? _current.gameObject : gameObject, contactPoint);

        if (singleUse && autoDisableAfterUse)
        {
            gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Programmatically trigger the interaction (ignores input and overlap checks).
    /// </summary>
    public void TriggerNow()
    {
        Vector2 contactPoint = _current != null ? _current.ClosestPoint(transform.position) : (Vector2)transform.position;
        TriggerNow(gameObject, contactPoint);
    }

    /// <summary>
    /// Programmatically trigger the interaction and pass the source GameObject and contact point.
    /// </summary>
    public void TriggerNow(GameObject source, Vector2 contactPoint)
    {
        if (_used && singleUse) return;
        _used = true;
        _lastInteractTime = Time.time;
        onInteract?.Invoke(source != null ? source : gameObject, contactPoint);
        if (singleUse && autoDisableAfterUse) gameObject.SetActive(false);
    }

    /// <summary>
    /// Reset the trigger so it can be used again (useful for level restart or reusable pickups).
    /// </summary>
    public void ResetTrigger()
    {
        _used = false;
        _isOverlapping = false;
        _current = null;
        _lastInteractTime = 0f;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, gizmoRadius);
    }
}
