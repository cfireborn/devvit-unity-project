using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Base component for items. Handles item data, links to delivery goal, and animation hooks for spawn/pickup/use.
/// Works with ItemPickupTrigger and ItemSpawner.
/// </summary>
public class Item : MonoBehaviour
{
    [Header("Item Data")]
    [Tooltip("The goal this item gives when picked up (delivery target). Used by ItemPickupTrigger.")]
    public Goal goal;

    [Header("Animation")]
    [Tooltip("Optional Animator for item animations.")]
    public Animator animator;
    [Tooltip("Animator trigger fired when item is spawned.")]
    public string spawnTrigger = "Spawn";
    [Tooltip("Animator trigger fired when item is picked up.")]
    public string pickupTrigger = "Pickup";
    [Tooltip("Animator trigger fired when item is used (e.g. delivered).")]
    public string useTrigger = "Use";

    [Header("Events")]
    public UnityEvent onSpawned;
    public UnityEvent onPickedUp;
    public UnityEvent onUsed;

    /// <summary>Called by ItemSpawner after spawning. Override or use onSpawned event.</summary>
    public virtual void OnSpawned()
    {
        if (animator != null && !string.IsNullOrEmpty(spawnTrigger))
            animator.SetTrigger(spawnTrigger);
        onSpawned?.Invoke();
    }

    /// <summary>Called by ItemPickupTrigger when the player picks up this item. Override or use onPickedUp event.</summary>
    public virtual void OnPickedUp()
    {
        if (animator != null && !string.IsNullOrEmpty(pickupTrigger))
            animator.SetTrigger(pickupTrigger);
        onPickedUp?.Invoke();
    }

    /// <summary>Called when the item is used (e.g. delivered at ItemDeliveryTrigger). Override or use onUsed event.</summary>
    public virtual void OnUsed()
    {
        if (animator != null && !string.IsNullOrEmpty(useTrigger))
            animator.SetTrigger(useTrigger);
        onUsed?.Invoke();
    }
}
