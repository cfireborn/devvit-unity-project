using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Generic 2D interaction trigger.
/// - Fires UnityEvents on enter/exit and explicit interact
/// - Inherits <see cref="ActivationTriggerBase"/> for chance, delay, single-use, auto-disable
/// - <see cref="TriggerNow"/> is immediate: skips chance and <see cref="ActivationTriggerBase.activationDelaySeconds"/>; still respects <see cref="ActivationTriggerBase.singleUse"/>
/// - Subclasses override <see cref="OnInteractInvoked"/> instead of subscribing to <c>onInteract</c> in code
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class InteractionTrigger : ActivationTriggerBase
{
    [Header("Filter")]
    [Tooltip("If set, only colliders with this tag will trigger events. Leave empty to allow any collider.")]
    public string requiredTag = "Player";

    [Header("Interaction")]
    [Tooltip("If true, the player must press the interact button while inside the trigger to activate.")]
    public bool requireButtonPress = false;
    [Tooltip("Input button name used for interaction. Uses Unity Input Manager button names (e.g. 'Submit','Jump' or a custom name).")]
    public string interactButton = "Submit";

    public bool printDebug = false;

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
    Collider2D _current;
    float _lastInteractTime;
    public float interactCooldown = 0f;


    /// <summary>
    /// When true after <see cref="OnInteractInvoked"/>, activation consumption (mark used / auto-disable) is skipped.
    /// Set by subclasses that call <see cref="ResetTrigger"/> on failure so the trigger can fire again.
    /// </summary>
    protected bool suppressActivationConsume;

    void OnEnable()
    {
        if (_current != null && _isOverlapping)
        {
            if (IsAllowed(_current))
            {
                OnTriggerEnter2D(_current);
            }
            else
            {
                _isOverlapping = false;
                _current = null;
            }
        }
    }

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
        if (!enabled) return;
        onEnter?.Invoke();

        if (!requireButtonPress)
        {
            if (printDebug) print("TryActivate: onEnter?.Invoke()");
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
        if (requireButtonPress && _isOverlapping && !IsBlockedBySingleUse() && Time.time - _lastInteractTime >= interactCooldown)
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

        if (printDebug) print("TryActivate: " + contactPoint);

        if (!TryBeginActivationPipeline()) return;

        if (printDebug) print("TryActivate: TryBeginActivationPipeline");

        RunDelayedOrImmediate(() => FireActivation(contactPoint));
    }

    void FireActivation(Vector2 contactPoint)
    {
        ClearActivationPendingOnly();
        suppressActivationConsume = false;
        _lastInteractTime = Time.time;
        GameObject src = _current != null ? _current.gameObject : gameObject;
        onInteract?.Invoke(src, contactPoint);
        OnInteractInvoked(src, contactPoint);
        if (!suppressActivationConsume)
            MarkActivationConsumedAndMaybeDisable();
    }

    /// <summary>Subclass logic after <see cref="onInteract"/> UnityEvent has fired.</summary>
    protected virtual void OnInteractInvoked(GameObject source, Vector2 contactPoint) { }

    /// <summary>
    /// Programmatically trigger the interaction (ignores input, overlap, chance, and activation delay).
    /// Still respects <see cref="ActivationTriggerBase.singleUse"/>.
    /// </summary>
    public void TriggerNow()
    {
        Vector2 contactPoint = _current != null ? _current.ClosestPoint(transform.position) : (Vector2)transform.position;
        TriggerNow(gameObject, contactPoint);
    }

    /// <summary>
    /// Programmatically trigger with source and contact point. Skips chance and activation delay.
    /// </summary>
    public void TriggerNow(GameObject source, Vector2 contactPoint)
    {
        if (IsBlockedBySingleUse()) return;
        suppressActivationConsume = false;
        _lastInteractTime = Time.time;
        GameObject src = source != null ? source : gameObject;
        onInteract?.Invoke(src, contactPoint);
        OnInteractInvoked(src, contactPoint);
        if (!suppressActivationConsume)
            MarkActivationConsumedAndMaybeDisable();
    }

    public void ResetTrigger()
    {
        ResetActivationState();
        suppressActivationConsume = false;
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
