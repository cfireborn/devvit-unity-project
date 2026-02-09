using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Interaction trigger that accepts delivery of an item. Only succeeds if the player has the matching goal.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class ItemDeliveryTrigger : InteractionTrigger
{
    [Header("Item Delivery")]
    [Tooltip("The goal this trigger satisfies. Must match the goal given by ItemPickupTrigger.")]
    public Goal deliveryGoal;

    [Header("Events")]
    public UnityEvent onDeliverySuccess;
    [Tooltip("Fired when player interacts but does not have the required goal.")]
    public UnityEvent onDeliveryFailed;

    void Start()
    {
        if (deliveryGoal != null)
            deliveryGoal.location = transform.position;
    }

    void Awake()
    {
        onInteract.AddListener(HandleInteract);
    }

    void OnDestroy()
    {
        onInteract.RemoveListener(HandleInteract);
    }

    void HandleInteract(GameObject source, Vector2 contactPoint)
    {
        if (deliveryGoal == null)
        {
            ResetTrigger();
            onDeliveryFailed?.Invoke();
            return;
        }

        var player = source != null ? source.GetComponentInParent<PlayerControllerM>() : null;
        if (player != null && player.HasGoal(deliveryGoal))
        {
            player.RemoveGoal(deliveryGoal);
            onDeliverySuccess?.Invoke();
        }
        else
        {
            ResetTrigger(); // Allow retry when player doesn't have the goal
            onDeliveryFailed?.Invoke();
        }
    }
}
