using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Holds a delivery goal and optional item. When EnableGoal is called, adds the goal to the player
/// and optionally sets it as primary. Wire ItemDeliveryTrigger.onDeliverySuccess to NotifyGoalCompleted.
/// </summary>
public class DeliveryGoalTrigger : MonoBehaviour
{
    [Header("Goal")]
    [Tooltip("The goal to add to the player when EnableGoal is called.")]
    public Goal deliveryGoal;
    [Tooltip("If true, the goal is set as the player's primary goal when added.")]
    public bool makePrimaryGoalOnReceive = true;
    [Tooltip("Optional item associated with this goal (e.g. the item to deliver).")]
    public Item associatedItem;

    [Header("Events")]
    public UnityEvent onGoalAdded;
    public UnityEvent onMadePrimaryGoal;
    public UnityEvent onGoalCompleted;

    /// <summary>Add the delivery goal to the player. Call from dialogue complete, triggers, etc.</summary>
    public void EnableGoal()
    {
        if (deliveryGoal == null) return;

        var gs = FindFirstObjectByType<GameServices>();
        var player = gs != null ? gs.GetPlayer() : null;
        if (player == null) return;

        player.AddGoal(deliveryGoal);
        onGoalAdded?.Invoke();

        if (makePrimaryGoalOnReceive)
        {
            player.SetPrimaryGoal(deliveryGoal);
            onMadePrimaryGoal?.Invoke();
        }
    }

    /// <summary>Call when the goal is completed (e.g. wire from ItemDeliveryTrigger.onDeliverySuccess).</summary>
    public void NotifyGoalCompleted()
    {
        onGoalCompleted?.Invoke();
    }
}
