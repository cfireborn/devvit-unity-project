using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Forwards trigger events to inspector UnityEvents. Wire at least one of
/// <see cref="interactionTrigger"/> or <see cref="goalAssignmentTrigger"/> (same GameObject or explicit refs).
/// </summary>
public class TriggerListener : MonoBehaviour
{
    [System.Serializable]
    public class GameObjectPointEvent : UnityEvent<GameObject, Vector2> { }

    [Tooltip("Optional: forwards enter/exit/interact from this InteractionTrigger (or subclass).")]
    public InteractionTrigger interactionTrigger;
    [Tooltip("Optional: forwards goal assignment events.")]
    public GoalAssignmentTrigger goalAssignmentTrigger;

    [Header("Forwarded: interaction")]
    public UnityEvent onEnter;
    public UnityEvent onExit;
    public GameObjectPointEvent onInteract;

    [Header("Forwarded: goal assignment")]
    public UnityEvent onGoalAdded;
    public UnityEvent onMadePrimaryGoal;
    public UnityEvent onGoalCompleted;

    void Reset()
    {
        if (interactionTrigger == null) interactionTrigger = GetComponent<InteractionTrigger>();
        if (goalAssignmentTrigger == null) goalAssignmentTrigger = GetComponent<GoalAssignmentTrigger>();
    }

    void Awake()
    {
        if (interactionTrigger == null) interactionTrigger = GetComponent<InteractionTrigger>();
        if (goalAssignmentTrigger == null) goalAssignmentTrigger = GetComponent<GoalAssignmentTrigger>();

        if (interactionTrigger != null)
        {
            interactionTrigger.onEnter.AddListener(HandleEnter);
            interactionTrigger.onExit.AddListener(HandleExit);
            interactionTrigger.onInteract.AddListener(HandleInteract);
        }

        if (goalAssignmentTrigger != null)
        {
            goalAssignmentTrigger.onGoalAdded.AddListener(HandleGoalAdded);
            goalAssignmentTrigger.onMadePrimaryGoal.AddListener(HandleMadePrimaryGoal);
            goalAssignmentTrigger.onGoalCompleted.AddListener(HandleGoalCompleted);
        }

        if (interactionTrigger == null && goalAssignmentTrigger == null)
            Debug.LogError("TriggerListener: assign interactionTrigger and/or goalAssignmentTrigger.", this);
    }

    void OnDestroy()
    {
        if (interactionTrigger != null)
        {
            interactionTrigger.onEnter.RemoveListener(HandleEnter);
            interactionTrigger.onExit.RemoveListener(HandleExit);
            interactionTrigger.onInteract.RemoveListener(HandleInteract);
        }

        if (goalAssignmentTrigger != null)
        {
            goalAssignmentTrigger.onGoalAdded.RemoveListener(HandleGoalAdded);
            goalAssignmentTrigger.onMadePrimaryGoal.RemoveListener(HandleMadePrimaryGoal);
            goalAssignmentTrigger.onGoalCompleted.RemoveListener(HandleGoalCompleted);
        }
    }

    void HandleEnter() => onEnter?.Invoke();
    void HandleExit() => onExit?.Invoke();
    void HandleInteract(GameObject src, Vector2 pt) => onInteract?.Invoke(src, pt);
    void HandleGoalAdded() => onGoalAdded?.Invoke();
    void HandleMadePrimaryGoal() => onMadePrimaryGoal?.Invoke();
    void HandleGoalCompleted() => onGoalCompleted?.Invoke();
}
