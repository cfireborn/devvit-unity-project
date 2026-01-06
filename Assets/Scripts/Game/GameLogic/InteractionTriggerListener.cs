using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Helper component that listens to an InteractionTrigger and re-publishes its events
/// as UnityEvents you can wire up in the inspector. Useful to decouple receivers from the trigger.
/// </summary>
[RequireComponent(typeof(InteractionTrigger))]
public class InteractionTriggerListener : MonoBehaviour
{
    [System.Serializable]
    public class GameObjectPointEvent : UnityEvent<GameObject, Vector2> { }

    [Tooltip("InteractionTrigger to listen to (auto-assigned from same GameObject if empty)")]
    public InteractionTrigger trigger;

    [Header("Forwarded Events")]
    public UnityEvent onEnter;
    public UnityEvent onExit;
    public GameObjectPointEvent onInteract;

    void Reset()
    {
        if (trigger == null) trigger = GetComponent<InteractionTrigger>();
    }

    void Awake()
    {
        if (trigger == null) trigger = GetComponent<InteractionTrigger>();
        if (trigger != null)
        {
            trigger.onEnter.AddListener(HandleEnterSimple);
            trigger.onExit.AddListener(HandleExitSimple);
            trigger.onInteract.AddListener(HandleInteract);
        }
    }

    void OnDestroy()
    {
        if (trigger != null)
        {
            trigger.onEnter.RemoveListener(HandleEnterSimple);
            trigger.onExit.RemoveListener(HandleExitSimple);
            trigger.onInteract.RemoveListener(HandleInteract);
        }
    }

    void HandleEnterSimple()
    {
        onEnter?.Invoke();
    }

    void HandleExitSimple()
    {
        onExit?.Invoke();
    }

    void HandleInteract(GameObject src, Vector2 pt)
    {
        onInteract?.Invoke(src, pt);
    }
}
