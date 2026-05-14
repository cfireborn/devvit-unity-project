using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Interaction trigger that assigns a goal when the player interacts (physical pickup zone).
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class ItemPickupTrigger : InteractionTrigger
{
    [Header("Pickup")]
    [Tooltip("Goal granted when the player interacts.")]
    public Goal goal;

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
        if (goal == null) return;

        var player = source != null ? source.GetComponentInParent<PlayerControllerM>() : null;
        if (player != null)
        {
            player.AddGoal(goal);
            player.SetPrimaryGoal(goal);
        }
    }
}
