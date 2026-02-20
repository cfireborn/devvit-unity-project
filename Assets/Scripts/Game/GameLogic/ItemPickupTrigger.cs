using UnityEngine;

/// <summary>
/// Interaction trigger that adds a delivery goal to the player when interacted with.
/// Works with Item component: uses item.goal if present, calls item.OnPickedUp(), and sets carried item.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class ItemPickupTrigger : InteractionTrigger
{
    [Header("Item Pickup")]
    [Tooltip("The goal representing the delivery target. Overridden by Item.goal if an Item component is present.")]
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
        var item = GetComponent<Item>();
        var goal = item != null && item.goal != null ? item.goal : deliveryGoal;
        if (goal == null) return;

        var player = source != null ? source.GetComponentInParent<PlayerControllerM>() : null;
        if (player != null)
        {
            player.AddGoal(goal);
            player.SetPrimaryGoal(goal);
            if (item != null)
            {
                item.OnPickedUp();
                player.SetCarriedItem(item);
            }
        }
    }
}
