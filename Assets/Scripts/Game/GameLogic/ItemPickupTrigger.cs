using UnityEngine;

/// <summary>
/// Interaction trigger that adds a delivery goal to the player when interacted with.
/// The player receives the goal and the delivery target becomes the primary goal for the direction indicator.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class ItemPickupTrigger : InteractionTrigger
{
    [Header("Item Pickup")]
    [Tooltip("The goal representing the delivery target. Its location should match the delivery trigger's position.")]
    public Goal deliveryGoal;

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
        if (deliveryGoal == null) return;

        var player = source != null ? source.GetComponentInParent<PlayerControllerM>() : null;
        if (player != null)
        {
            player.AddGoal(deliveryGoal);
            player.SetPrimaryGoal(deliveryGoal);
        }
    }
}
