using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Use on delivery-goal cloud prefabs instead of a plain <see cref="CloudPlatform"/>.
/// Holds the <see cref="GoalCompletionTrigger"/> reference, applies stationary delivery state for <see cref="CloudManager"/>,
/// and enables the completion collider when the delivery is activated.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public class DeliveryCloudPlatform : CloudPlatform
{
    [Header("Delivery")]
    [Tooltip("Completes the goal when the player interacts here. Must live on this prefab hierarchy.")]
    [SerializeField] GoalCompletionTrigger goalCompletionTrigger;

    Coroutine _despawnAfterLeaveRoutine;

#if UNITY_EDITOR
    void OnValidate()
    {
        if (goalCompletionTrigger == null)
            goalCompletionTrigger = GetComponentInChildren<GoalCompletionTrigger>(true);
    }
#endif

    public GoalCompletionTrigger GoalCompletionTrigger => goalCompletionTrigger;

    public Goal GetDeliveryGoal() =>
        goalCompletionTrigger != null ? goalCompletionTrigger.goal : null;

    /// <summary>
    /// Non-pooled stationary flags for <see cref="CloudManager"/>; call after instantiate, before <see cref="CloudManager.ActivateNonPooledCloud"/>.
    /// </summary>
    public void ConfigureAsStationaryDelivery(CloudManager cloudManager)
    {
        isPooled = false;
        isMoving = false;
        laneIndex = -1;
        ignoreNoSpawnZones = true;
        SetCloudManager(cloudManager);
    }

    /// <summary>
    /// Enables the completion trigger and its collider; subscribes once to <see cref="GoalCompletionTrigger.onCompletionSucceeded"/>.
    /// </summary>
    public void EnableDeliveryCompletion(UnityAction onSucceededOnce)
    {
        if (goalCompletionTrigger == null) return;
        goalCompletionTrigger.enabled = true;
        var col = goalCompletionTrigger.GetComponent<Collider2D>();
        if (col != null)
            col.enabled = true;
        if (onSucceededOnce != null)
            goalCompletionTrigger.onCompletionSucceeded.AddListener(onSucceededOnce);
    }

    public void RemoveDeliveryCompletionListener(UnityAction listener)
    {
        if (goalCompletionTrigger != null && listener != null)
            goalCompletionTrigger.onCompletionSucceeded.RemoveListener(listener);
    }

    /// <summary>
    /// Call when the delivery goal is satisfied. The cloud stays stationary, completion is disabled,
    /// then <see cref="CloudPlatform.BeginDespawnAnimation"/> runs only after <see cref="CloudPlatform.IsPlayerOnCloud"/> is false
    /// (player must have collided with this platform using the <c>Player</c> tag — same as <see cref="CloudPlatform"/>).
    /// </summary>
    public void BeginPostDeliveryDespawnWhenPlayerLeaves()
    {
        isMoving = false;
        if (goalCompletionTrigger != null)
        {
            goalCompletionTrigger.enabled = false;
            var col = goalCompletionTrigger.GetComponent<Collider2D>();
            if (col != null)
                col.enabled = false;
        }

        if (_despawnAfterLeaveRoutine != null)
            StopCoroutine(_despawnAfterLeaveRoutine);
        _despawnAfterLeaveRoutine = StartCoroutine(CoDespawnAfterPlayerLeaves());
    }

    void OnDisable()
    {
        if (_despawnAfterLeaveRoutine != null)
        {
            StopCoroutine(_despawnAfterLeaveRoutine);
            _despawnAfterLeaveRoutine = null;
        }
    }

    IEnumerator CoDespawnAfterPlayerLeaves()
    {
        yield return new WaitForFixedUpdate();
        while (IsPlayerOnCloud)
            yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        BeginDespawnAnimation();
        _despawnAfterLeaveRoutine = null;
    }
}
