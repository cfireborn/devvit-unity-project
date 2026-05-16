using System.Collections.Generic;
using System.Collections;
using FishNet;
using FishNet.Managing;
using FishNet.Object;
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
    [Tooltip("After delivery completes, despawn runs only after no Player-collider overlaps this trigger. If unset, falls back to standing collision (IsPlayerOnCloud).")]
    [SerializeField] Collider2D postDeliveryLeaveTrigger;

    Coroutine _despawnAfterLeaveRoutine;
    readonly List<Collider2D> _leaveOverlapScratch = new List<Collider2D>(8);
    ContactFilter2D _leaveOverlapFilter;
    bool _leaveOverlapFilterReady;

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
        LogNetworkRegistrationDebug("after ConfigureAsStationaryDelivery (before ActivateNonPooledCloud)");
    }

    /// <summary>Called by the spawn pipeline after <see cref="CloudManager.ActivateNonPooledCloud"/> so logs run before FishNet may disable the root.</summary>
    public void LogNetworkRegistrationDebug(string phase)
    {
        var nob = GetComponent<NetworkObject>();
        if (nob == null)
        {
            Debug.Log($"[DeliveryCloudPlatform] {phase}: no NetworkObject on '{gameObject.name}'.", this);
            return;
        }

        NetworkManager nm = nob.NetworkManager;
        Debug.Log(
            $"[DeliveryCloudPlatform] {phase}: activeSelf={gameObject.activeSelf} IsSceneObject={nob.IsSceneObject} " +
            $"IsSpawned={nob.IsSpawned} NO.NetworkManager={(nm != null ? nm.name : "null")} " +
            $"nm.IsServerStarted={(nm != null && nm.IsServerStarted)} nm.IsClientStarted={(nm != null && nm.IsClientStarted)} " +
            $"InstanceFinder.IsServerStarted={InstanceFinder.IsServerStarted}",
            this);
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
    /// Call when the delivery goal is satisfied. Completion is disabled; despawn runs after the player leaves
    /// <see cref="postDeliveryLeaveTrigger"/> (if set), else after <see cref="CloudPlatform.IsPlayerOnCloud"/> is false.
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
        if (postDeliveryLeaveTrigger != null)
        {
            while (PlayerOverlapsPostDeliveryLeaveTrigger())
                yield return new WaitForFixedUpdate();
        }
        else
        {
            while (IsPlayerOnCloud)
                yield return new WaitForFixedUpdate();
        }
        yield return new WaitForFixedUpdate();
        BeginDespawnAnimation();
        _despawnAfterLeaveRoutine = null;
    }

    bool PlayerOverlapsPostDeliveryLeaveTrigger()
    {
        if (postDeliveryLeaveTrigger == null || !postDeliveryLeaveTrigger.enabled || !postDeliveryLeaveTrigger.gameObject.activeInHierarchy)
            return false;

        _leaveOverlapScratch.Clear();
        if (!_leaveOverlapFilterReady)
        {
            _leaveOverlapFilter = ContactFilter2D.noFilter;
            _leaveOverlapFilter.useTriggers = true;
            _leaveOverlapFilterReady = true;
        }

        int n = postDeliveryLeaveTrigger.Overlap(_leaveOverlapFilter, _leaveOverlapScratch);
        for (int i = 0; i < n; i++)
        {
            Collider2D c = _leaveOverlapScratch[i];
            if (c == null) continue;
            Transform t = c.transform;
            while (t != null)
            {
                if (t.CompareTag("Player"))
                    return true;
                t = t.parent;
            }
        }
        return false;
    }
}
