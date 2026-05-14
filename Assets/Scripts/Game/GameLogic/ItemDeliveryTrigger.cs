using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Base for item-delivery flows: assign a <see cref="Goal"/> that completes at a <see cref="GoalCompletionTrigger"/>.
/// Either use an existing <see cref="goal"/> (<see cref="manuallySetGoal"/>), or create one at runtime from
/// <see cref="generatedGoalDisplayName"/> and a completion trigger supplied when activation runs.
/// Subclasses that resolve completion only from the scene set <see cref="goalDeliveryCompletionTrigger"/> and
/// override <see cref="RequiresSceneDeliveryCompletion"/> to return true.
/// </summary>
public abstract class ItemDeliveryTrigger : MonoBehaviour
{
    [Header("Goal source")]
    [Tooltip("When true, assign the Goal reference below. When false, a Goal is created when activation runs (display name + completion trigger).")]
    public bool manuallySetGoal;
    [Tooltip("Used only when manuallySetGoal is true.")]
    public Goal goal;
    [Tooltip("When RequiresSceneDeliveryCompletion is true: the GoalCompletionTrigger that receives the generated goal (e.g. static delivery volume).")]
    public GoalCompletionTrigger goalDeliveryCompletionTrigger;
    [Tooltip("Used only when manuallySetGoal is false — sets Goal.displayName on the generated goal.")]
    public string generatedGoalDisplayName = "Task";

    [Header("Assignment")]
    [Tooltip("If true, the goal is set as the player's primary goal when added.")]
    public bool makePrimaryGoalOnReceive = true;
    [Tooltip("Optional item metadata on generated goals, or copy context when assigning manual goals.")]
    public ItemDefinition associatedItem;

    [Header("Events")]
    public UnityEvent onGoalAdded;
    public UnityEvent onMadePrimaryGoal;
    public UnityEvent onGoalCompleted;

    [Header("Goal animation")]
    [Tooltip("When true, fires enableGoalTrigger on enableGoalAnimator and waits enableAnimationDelay before activation.")]
    public bool waitForEnableAnimation;
    [Tooltip("SpriteRenderer used to change sprites for goal animation when a goal is available before the delay.")]
    public SpriteRenderer animSpriteRenderer;
    public Animator enableGoalAnimator;
    public string enableGoalTrigger = "EnableGoal";
    [Tooltip("Seconds to wait after SetTrigger before activation; match your clip length.")]
    [Min(0f)]
    public float enableAnimationDelay = 1f;

    protected bool _enableGoalRoutineRunning;
    protected Goal _runtimeGeneratedGoal;

    void OnDestroy()
    {
        if (_runtimeGeneratedGoal != null)
        {
            Destroy(_runtimeGeneratedGoal.gameObject);
            _runtimeGeneratedGoal = null;
        }
    }

    /// <summary>Add / activate delivery and goal. Call from dialogue, UnityEvents, etc.</summary>
    public void EnableGoal()
    {
        if (_enableGoalRoutineRunning) return;
        if (!HasSpawnAuthorityForDelivery()) return;
        if (!CanEnableGoal()) return;

        var gs = FindFirstObjectByType<GameServices>();
        var player = gs != null ? gs.GetPlayer() : null;
        if (player == null) return;

        if (waitForEnableAnimation && enableGoalAnimator != null)
        {
            StartCoroutine(DelayedEnableGoal(player));
            return;
        }

        OnEnableGoalImmediate(player);
    }

    /// <summary>Override for server-only / offline-only delivery activation (e.g. FishNet).</summary>
    protected virtual bool HasSpawnAuthorityForDelivery() => true;

    /// <summary>
    /// When true and not <see cref="manuallySetGoal"/>, <see cref="goalDeliveryCompletionTrigger"/> must be set before EnableGoal.
    /// Random / spawn-time completion triggers override this to false.
    /// </summary>
    protected virtual bool RequiresSceneDeliveryCompletion => false;

    protected virtual bool CanEnableGoal()
    {
        if (manuallySetGoal)
            return goal != null;
        if (string.IsNullOrWhiteSpace(generatedGoalDisplayName))
            return false;
        if (RequiresSceneDeliveryCompletion)
            return goalDeliveryCompletionTrigger != null;
        return true;
    }

    protected abstract void OnEnableGoalImmediate(PlayerControllerM player);

    protected IEnumerator DelayedEnableGoal(PlayerControllerM player)
    {
        _enableGoalRoutineRunning = true;
        SetupGoalAnimationVisual(player);
        if (!string.IsNullOrEmpty(enableGoalTrigger) && enableGoalAnimator != null)
            enableGoalAnimator.SetTrigger(enableGoalTrigger);
        if (enableAnimationDelay > 0f)
            yield return new WaitForSeconds(enableAnimationDelay);
        else
            yield return null;
        OnEnableGoalAfterAnimation(player);
        _enableGoalRoutineRunning = false;
    }

    protected virtual void SetupGoalAnimationVisual(PlayerControllerM player)
    {
        Goal animGoal = TryGetGoalForEnableAnimation(player);
        if (animSpriteRenderer != null && animGoal != null && animGoal.item != null && animGoal.item.icon != null)
            animSpriteRenderer.sprite = animGoal.item.icon;
    }

    /// <summary>Override when a goal exists before the enable animation (e.g. scene-based generated goal).</summary>
    protected virtual Goal TryGetGoalForEnableAnimation(PlayerControllerM player) => null;

    protected abstract void OnEnableGoalAfterAnimation(PlayerControllerM player);

    protected Goal CreateGeneratedGoalForCompletion(GoalCompletionTrigger completion)
    {
        if (completion == null)
            return null;
        if (_runtimeGeneratedGoal != null)
            return _runtimeGeneratedGoal;

        var go = new GameObject($"Goal_{generatedGoalDisplayName}");
        go.transform.SetParent(completion.transform, false);
        var g = go.AddComponent<Goal>();
        g.displayName = generatedGoalDisplayName.Trim();
        g.item = associatedItem;
        g.completionTrigger = completion;
        g.goalIcon = completion.goalIcon;

        if (completion.locationMarkerSource != null)
            g.locationSource = completion.locationMarkerSource;
        else
        {
            g.locationSource = null;
            g.location = completion.transform.position;
        }

        completion.goal = g;
        _runtimeGeneratedGoal = g;
        return g;
    }

    protected void WireManualGoalToCompletion(Goal manualGoal, GoalCompletionTrigger completion)
    {
        if (manualGoal == null || completion == null) return;
        completion.goal = manualGoal;
        manualGoal.completionTrigger = completion;
    }

    protected void ApplyGoalToPlayer(PlayerControllerM player, Goal goalToAdd)
    {
        if (goalToAdd == null) return;

        if (manuallySetGoal && associatedItem != null && goalToAdd.item == null)
            goalToAdd.item = associatedItem;

        player.AddGoal(goalToAdd);
        onGoalAdded?.Invoke();

        if (makePrimaryGoalOnReceive)
        {
            player.SetPrimaryGoal(goalToAdd);
            onMadePrimaryGoal?.Invoke();
        }
    }

    public void NotifyGoalCompleted()
    {
        onGoalCompleted?.Invoke();
    }
}
