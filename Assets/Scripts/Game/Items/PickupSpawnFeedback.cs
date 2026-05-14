using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Optional spawn feedback for prefabs spawned by <see cref="ItemSpawner"/> (replaces the old Item MonoBehaviour hook).
/// </summary>
public class PickupSpawnFeedback : MonoBehaviour
{
    public Animator animator;
    public string spawnTrigger = "Spawn";
    public UnityEvent onSpawned;

    public void NotifySpawned()
    {
        if (animator != null && !string.IsNullOrEmpty(spawnTrigger))
            animator.SetTrigger(spawnTrigger);
        onSpawned?.Invoke();
    }
}
