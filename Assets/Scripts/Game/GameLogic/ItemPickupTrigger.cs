using UnityEngine;

/// <summary>
/// Interaction trigger that assigns a goal when the player interacts (physical pickup zone).
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class ItemPickupTrigger : InteractionTrigger
{
    [Header("Pickup")]
    [Tooltip("Goal granted when the player interacts.")]
    public Goal goal;

    protected override void OnInteractInvoked(GameObject source, Vector2 contactPoint)
    {
        if (goal == null) return;

        var player = source != null ? source.GetComponentInParent<PlayerControllerM>() : null;
        if (player != null)
        {
            player.AddGoal(goal);
            player.SetPrimaryGoal(goal);
        }
    }
}
