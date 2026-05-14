using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Generic 2D interaction trigger.
/// - Fires UnityEvents on enter/exit and explicit interact
/// - Can filter by tag
/// - Optionally requires a button press to activate
/// - Optional activation delay (enter / button path only); <see cref="CancelActivation"/> or destroy clears it
/// - <see cref="TriggerNow"/> is always immediate (ignores delay)
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

    [Header("Randomization")]
    [Tooltip("0–100. Chance this trigger runs when an activation is attempted (enter or button). 100 = always.")]
    [Range(0f, 100f)]
    public float activationChancePercent = 100f;

    [Header("Activation timing")]
    [Tooltip("Seconds to wait after a successful activation attempt before onInteract fires. 0 = immediate. Does not apply to TriggerNow.")]
    [Min(0f)]
    public float activationDelaySeconds = 0f;

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
    bool _activationDelayPending;
    Coroutine _activationDelayCoroutine;
    Collider2D _current;
    float _lastInteractTime;
    public float interactCooldown = 0f;

    void Reset()
    {
        var c = GetComponent<Collider2D>();
        if (c != null) c.isTrigger = true;
    }

    public void TryInvokeActivation()
    {
        Vector2 contactPoint = _current != null ? _current.ClosestPoint(transform.position) : (Vector2)transform.position;
        TryActivate(contactPoint);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsAllowed(other)) return;
        _isOverlapping = true;
        _current = other;
        onEnter?.Invoke();

        if (!requireButtonPress)
        {
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
        if (_activationDelayPending) return;

        float chance = Mathf.Clamp01(activationChancePercent / 100f);
        if (chance <= 0f) return;
        if (chance < 1f && Random.value > chance) return;

        if (activationDelaySeconds <= 0f)
            FireActivation(contactPoint);
        else
        {
            _activationDelayPending = true;
            if (_activationDelayCoroutine != null)
                StopCoroutine(_activationDelayCoroutine);
            _activationDelayCoroutine = StartCoroutine(CoDelayedActivation(contactPoint));
        }
    }

    IEnumerator CoDelayedActivation(Vector2 contactPoint)
    {
        yield return new WaitForSeconds(activationDelaySeconds);
        _activationDelayCoroutine = null;
        FireActivation(contactPoint);
    }

    void FireActivation(Vector2 contactPoint)
    {
        _activationDelayPending = false;
        _activationDelayCoroutine = null;
        _used = true;
        _lastInteractTime = Time.time;
        onInteract?.Invoke(_current != null ? _current.gameObject : gameObject, contactPoint);

        if (singleUse && autoDisableAfterUse)
            gameObject.SetActive(false);
    }

    /// <summary>
    /// Stops a pending delayed activation (from <see cref="activationDelaySeconds"/>). No-op if nothing is pending.
    /// </summary>
    public void CancelActivation()
    {
        if (_activationDelayCoroutine != null)
        {
            StopCoroutine(_activationDelayCoroutine);
            _activationDelayCoroutine = null;
        }
        _activationDelayPending = false;
    }

    void OnDestroy()
    {
        CancelActivation();
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
        CancelActivation();
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
