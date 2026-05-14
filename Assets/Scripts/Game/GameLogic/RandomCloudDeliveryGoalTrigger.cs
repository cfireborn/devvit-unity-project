using System.Collections.Generic;
using FishNet;
using UnityEngine;

/// <summary>
/// Spawns a random delivery cloud prefab on a lane (optionally constrained to a collider's bounds), enables completion on it,
/// and adds its Goal to the player. Inherits goal options from <see cref="ItemDeliveryTrigger"/> (manual goal or runtime-generated from spawned <see cref="GoalCompletionTrigger"/>).
/// Prefabs must include <see cref="DeliveryCloudPlatform"/> (with wired <see cref="GoalCompletionTrigger"/>), and have the completion trigger disabled until spawn.
/// </summary>
public class RandomCloudDeliveryGoalTrigger : ItemDeliveryTrigger
{
    [Header("Cloud spawn")]
    [Tooltip("Random choice each time EnableGoal runs. Each prefab must use DeliveryCloudPlatform.")]
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

    protected override bool HasSpawnAuthorityForDelivery()
    {
        if (InstanceFinder.IsServerStarted) return true;
        var nm = InstanceFinder.NetworkManager;
        if (nm != null && nm.IsClientStarted && !nm.IsServerStarted) return false;
        return true;
    }

    protected override bool RequiresSceneDeliveryCompletion => false;

    protected override bool CanEnableGoal()
    {
        return base.CanEnableGoal() && cloudPrefabs != null && cloudPrefabs.Length > 0;
    }

    protected override void OnEnableGoalImmediate(PlayerControllerM player)
    {
        TrySpawnAndApplyGoal(player);
    }

    protected override void OnEnableGoalAfterAnimation(PlayerControllerM player)
    {
        TrySpawnAndApplyGoal(player);
    }

    void TrySpawnAndApplyGoal(PlayerControllerM player)
    {
        GameObject instance;
        DeliveryCloudPlatform delivery;
        if (goalDeliveryCompletionTrigger == null)
        {
            instance = GenerateGoal(player);
            delivery = instance.GetComponentInChildren<DeliveryCloudPlatform>(true);
        }
        else
        {
            instance = goalDeliveryCompletionTrigger.gameObject;
            delivery = instance.GetComponentInParent<DeliveryCloudPlatform>(true);
        }

        if (delivery == null)
        {
            string prefabName = instance != null ? instance.name : "null";
            Debug.LogError("RandomCloudDeliveryGoalTrigger: prefab " + prefabName + " must include DeliveryCloudPlatform.");
            Destroy(instance);
            return;
        }

        if (delivery.GoalCompletionTrigger == null)
        {
            string prefabName = instance != null ? instance.name : "null";
            Debug.LogError("RandomCloudDeliveryGoalTrigger: DeliveryCloudPlatform on " + prefabName + " must reference GoalCompletionTrigger.");
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
                Destroy(instance);
                return;
            }
        }

        /*var gs = FindFirstObjectByType<GameServices>();
        var cm = gs != null ? gs.GetCloudManager() : null;
        delivery.ConfigureAsStationaryDelivery(cm);

        if (!cm.ActivateNonPooledCloud(instance))
        {
            Destroy(instance);
            if (!manuallySetGoal && _runtimeGeneratedGoal != null)
            {
                Destroy(_runtimeGeneratedGoal.gameObject);
                _runtimeGeneratedGoal = null;
            }
            return;
        }*/

        _spawnedCloud = instance;
        _spawnedDelivery = delivery;
        delivery.EnableDeliveryCompletion(OnSpawnedDeliverySuccess);

        ApplyGoalToPlayer(player, goalToAssign);
    }

    GameObject GenerateGoal(PlayerControllerM player)
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

        GameObject instance = Instantiate(prefab, spawnPos, prefab.transform.rotation);

        return instance;
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
