using System.Collections;
using System.Collections.Generic;
using FishNet;
using UnityEngine;

/// <summary>
/// Spawns a random delivery cloud prefab on a lane (optionally constrained to a collider's bounds), enables completion on it,
/// and adds its Goal to the player. Uses <see cref="GoalAssignmentTrigger"/> for goal source, events, and activation rules.
/// Set <see cref="GoalAssignmentTrigger.completionTrigger"/> to use a pre-placed delivery in the scene; leave null to spawn from <see cref="cloudPrefabs"/>.
/// Prefabs must include <see cref="DeliveryCloudPlatform"/> with a wired <see cref="GoalCompletionTrigger"/>.
/// </summary>
public class RandomCloudDeliveryGoalTrigger : GoalAssignmentTrigger
{
    [Header("Cloud spawn")]
    [Tooltip("Random choice each time EnableGoal runs when completionTrigger is not set. Each prefab must use DeliveryCloudPlatform.")]
    public GameObject[] cloudPrefabs;
    [Tooltip("Optional: restrict spawn to lanes whose baseline Y falls inside this collider's world bounds (e.g. BoxCollider2D).")]
    public Collider2D vicinity;
    [Tooltip("Only when vicinity is unset: reject samples closer than this to the player (2D distance).")]
    [Min(0f)]
    public float minDistanceFromPlayer = 3f;
    [Min(1)]
    public int maxPlacementAttempts = 48;

    GameObject _spawnedCloud;
    DeliveryCloudPlatform _spawnedDelivery;

    protected override bool HasSpawnAuthorityForGoalAssignment()
    {
        if (InstanceFinder.IsServerStarted) return true;
        var nm = InstanceFinder.NetworkManager;
        if (nm != null && nm.IsClientStarted && !nm.IsServerStarted) return false;
        return true;
    }

    protected override bool CanEnableGoal()
    {
        if (manuallySetGoal)
            return goal != null;
        if (string.IsNullOrWhiteSpace(generatedGoalDisplayName))
            return false;
        if (completionTrigger != null)
            return true;
        return cloudPrefabs != null && cloudPrefabs.Length > 0;
    }

    protected override Goal TryGetGoalForEnableAnimation(PlayerControllerM player) =>
        manuallySetGoal ? goal : null;

    protected override void ExecuteEnableGoalBody(PlayerControllerM player)
    {
        if (waitForEnableAnimation && enableGoalAnimator != null)
            StartCoroutine(EnableSpawnAfterAnimation(player));
        else
            TrySpawnAndApplyGoal(player);
    }

    IEnumerator EnableSpawnAfterAnimation(PlayerControllerM player)
    {
        _enableGoalRoutineRunning = true;
        Goal iconGoal = TryGetGoalForEnableAnimation(player);
        if (animSpriteRenderer != null && iconGoal != null && iconGoal.item != null && iconGoal.item.icon != null)
            animSpriteRenderer.sprite = iconGoal.item.icon;
        if (!string.IsNullOrEmpty(enableGoalTrigger))
            enableGoalAnimator.SetTrigger(enableGoalTrigger);
        if (enableAnimationDelay > 0f)
            yield return new WaitForSeconds(enableAnimationDelay);
        else
            yield return null;
        TrySpawnAndApplyGoal(player);
        MarkActivationConsumedAndMaybeDisable();
        _enableGoalRoutineRunning = false;
    }

    void TrySpawnAndApplyGoal(PlayerControllerM player)
    {
        DestroyPriorSpawn();

        GameObject instance;
        DeliveryCloudPlatform delivery;

        if (completionTrigger == null)
        {
            instance = GenerateSpawnedInstance(player);
            if (instance == null)
                return;
            delivery = instance.GetComponent<DeliveryCloudPlatform>() ?? instance.GetComponentInChildren<DeliveryCloudPlatform>(true);
        }
        else
        {
            delivery = completionTrigger.GetComponentInParent<DeliveryCloudPlatform>(true);
            instance = delivery.gameObject;
        }

        if (delivery == null)
        {
            Debug.LogError("RandomCloudDeliveryGoalTrigger: instance must include DeliveryCloudPlatform.");
            if (completionTrigger == null && instance != null)
                Destroy(instance);
            return;
        }

        if (delivery.GoalCompletionTrigger == null)
        {
            Debug.LogError("RandomCloudDeliveryGoalTrigger: DeliveryCloudPlatform must reference GoalCompletionTrigger.");
            if (completionTrigger == null && instance != null)
                Destroy(instance);
            return;
        }

        GoalCompletionTrigger completion = delivery.GoalCompletionTrigger;

        Goal goalToAssign;
        if (manuallySetGoal)
        {
            goalToAssign = goal;
            WireManualGoalToCompletion(goal, completion);
        }
        else
        {
            goalToAssign = CreateGeneratedGoalForCompletion(completion);
            if (goalToAssign == null)
            {
                Debug.LogError("RandomCloudDeliveryGoalTrigger: could not create generated goal.");
                if (completionTrigger == null && instance != null)
                    Destroy(instance);
                return;
            }
        }

        var gs = FindFirstObjectByType<GameServices>();
        var cm = gs != null ? gs.GetCloudManager() : null;
        if (cm == null || cm.settings == null)
        {
            Debug.LogError("RandomCloudDeliveryGoalTrigger: CloudManager or CloudBehaviorSettings missing.");
            if (completionTrigger == null && instance != null)
                Destroy(instance);
            if (!manuallySetGoal && _runtimeGeneratedGoal != null)
            {
                Destroy(_runtimeGeneratedGoal.gameObject);
                _runtimeGeneratedGoal = null;
            }
            return;
        }

        delivery.ConfigureAsStationaryDelivery(cm);

        if (!cm.ActivateNonPooledCloud(instance))
        {
            if (completionTrigger == null && instance != null)
                Destroy(instance);
            if (!manuallySetGoal && _runtimeGeneratedGoal != null)
            {
                Destroy(_runtimeGeneratedGoal.gameObject);
                _runtimeGeneratedGoal = null;
            }
            return;
        }

        _spawnedCloud = instance;
        _spawnedDelivery = delivery;
        delivery.EnableDeliveryCompletion(OnSpawnedDeliverySuccess);

        ApplyGoalToPlayer(player, goalToAssign);

        if (!(waitForEnableAnimation && enableGoalAnimator != null))
        {
            MarkActivationConsumedAndMaybeDisable();
        }
    }

    GameObject GenerateSpawnedInstance(PlayerControllerM player)
    {
        var gs = FindFirstObjectByType<GameServices>();
        var cm = gs != null ? gs.GetCloudManager() : null;
        if (cm == null || cm.settings == null)
        {
            Debug.LogError("RandomCloudDeliveryGoalTrigger: CloudManager or CloudBehaviorSettings missing.");
            return null;
        }

        if (!cm.TryGetLaneLayout(out float baseY, out int laneCount, out float laneSpacing))
        {
            Debug.LogError("RandomCloudDeliveryGoalTrigger: could not resolve lane layout.");
            return null;
        }

        GameObject prefab = cloudPrefabs[UnityEngine.Random.Range(0, cloudPrefabs.Length)];
        if (prefab == null)
        {
            Debug.LogError("RandomCloudDeliveryGoalTrigger: null entry in cloudPrefabs.");
            return null;
        }

        List<int> candidateLanes = null;
        if (vicinity != null)
        {
            candidateLanes = new List<int>();
            Bounds vb = vicinity.bounds;
            for (int i = 0; i < laneCount; i++)
            {
                float laneBaseline = baseY + i * laneSpacing;
                if (laneBaseline >= vb.min.y && laneBaseline <= vb.max.y)
                    candidateLanes.Add(i);
            }

            if (candidateLanes.Count == 0)
            {
                Debug.LogError("RandomCloudDeliveryGoalTrigger: no lane baselines intersect vicinity bounds.");
                return null;
            }
        }

        if (!cm.TryPickDeliverySpawnWorldPosition(
                prefab,
                baseY,
                laneCount,
                laneSpacing,
                candidateLanes,
                vicinity,
                player.transform.position,
                minDistanceFromPlayer,
                maxPlacementAttempts,
                out int _,
                out Vector3 spawnPos))
        {
            Debug.LogError("RandomCloudDeliveryGoalTrigger: could not find spawn position.");
            return null;
        }

        return Instantiate(prefab, spawnPos, prefab.transform.rotation);
    }

    void DestroyPriorSpawn()
    {
        if (_spawnedDelivery != null)
            _spawnedDelivery.RemoveDeliveryCompletionListener(OnSpawnedDeliverySuccess);
        if (_spawnedCloud != null)
        {
            Destroy(_spawnedCloud);
            _spawnedCloud = null;
            _spawnedDelivery = null;
            _runtimeGeneratedGoal = null;
        }
    }

    void OnSpawnedDeliverySuccess()
    {
        if (_spawnedDelivery != null)
            _spawnedDelivery.RemoveDeliveryCompletionListener(OnSpawnedDeliverySuccess);

        if (_spawnedDelivery != null)
            _spawnedDelivery.BeginPostDeliveryDespawnWhenPlayerLeaves();

        NotifyGoalCompleted();

        _spawnedCloud = null;
        _spawnedDelivery = null;
    }
}
