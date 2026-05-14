using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Assigns a <see cref="Goal"/> to the player (dialogue, NPC, scripted enable).
/// Either references an existing Goal (<see cref="manuallySetGoal"/>), or creates one at runtime from
/// <see cref="completionTrigger"/> and <see cref="generatedGoalDisplayName"/>.
/// </summary>
public class GoalAssignmentTrigger : MonoBehaviour
{
    [Header("Goal source")]
    [Tooltip("When true, assign the Goal reference below. When false, a Goal is created when EnableGoal runs (see Completion Trigger + Display Name).")]
    public bool manuallySetGoal = false;
    [Tooltip("Used only when manuallySetGoal is true.")]
    public Goal goal;
    [Tooltip("Used only when manuallySetGoal is false — receives the generated Goal and supplies location via Location Marker Source / transform.")]
    public GoalCompletionTrigger completionTrigger;
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

    [Header("Goal Animation")]
    [Tooltip("When true, fires enableGoalTrigger on enableGoalAnimator and waits enableAnimationDelay before AddGoal.")]
    public bool waitForEnableAnimation;
    [Tooltip("SpriteRenderer used to change sprites for goal animation.")]
    public SpriteRenderer animSpriteRenderer;
    public Animator enableGoalAnimator;
    public string enableGoalTrigger = "EnableGoal";
    [Tooltip("Seconds to wait after SetTrigger before adding the goal; match your clip length.")]
    [Min(0f)]
    public float enableAnimationDelay = 1f;

    bool _enableGoalRoutineRunning;
    Goal _runtimeGeneratedGoal;

    void OnDestroy()
    {
        if (_runtimeGeneratedGoal != null)
        {
            Destroy(_runtimeGeneratedGoal.gameObject);
            _runtimeGeneratedGoal = null;
        }
    }

    /// <summary>Add the goal to the player. Call from dialogue complete, UnityEvents, etc.</summary>
    public void EnableGoal()
    {
        if (_enableGoalRoutineRunning) return;
        if (!CanEnableGoal()) return;

        var gs = FindFirstObjectByType<GameServices>();
        var player = gs != null ? gs.GetPlayer() : null;
        if (player == null) return;

        Goal g = ResolveGoalForAssignment();

        if (waitForEnableAnimation && enableGoalAnimator != null)
        {
            StartCoroutine(EnableGoalAfterAnimation(player, g));
            return;
        }

        ApplyGoalToPlayer(player, g);
    }

    bool CanEnableGoal()
    {
        if (manuallySetGoal)
            return goal != null;
        if (completionTrigger == null)
            return false;
        return !string.IsNullOrWhiteSpace(generatedGoalDisplayName);
    }

    Goal ResolveGoalForAssignment()
    {
        if (manuallySetGoal)
            return goal;
        return GetOrCreateGeneratedGoal();
    }

    Goal GetOrCreateGeneratedGoal()
    {
        if (_runtimeGeneratedGoal != null)
            return _runtimeGeneratedGoal;

        var completion = completionTrigger;
        if (completion == null)
            return null;

        var go = new GameObject($"Goal_{generatedGoalDisplayName}");
        go.transform.SetParent(completion.transform, false);
        var g = go.AddComponent<Goal>();
        g.displayName = generatedGoalDisplayName.Trim();
        g.item = associatedItem;
        g.assignmentTrigger = this;
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

    void ApplyGoalToPlayer(PlayerControllerM player, Goal goal)
    {
        if (goal == null) return;

        if (manuallySetGoal && associatedItem != null && goal.item == null)
            goal.item = associatedItem;

        player.AddGoal(goal);
        onGoalAdded?.Invoke();

        if (makePrimaryGoalOnReceive)
        {
            player.SetPrimaryGoal(goal);
            onMadePrimaryGoal?.Invoke();
        }
    }

    IEnumerator EnableGoalAfterAnimation(PlayerControllerM player, Goal goal)
    {
        _enableGoalRoutineRunning = true;
        if (animSpriteRenderer != null && goal != null && goal.item != null && goal.item.icon != null)
            animSpriteRenderer.sprite = completionTrigger.goal.item.icon;
        if (!string.IsNullOrEmpty(enableGoalTrigger))
            enableGoalAnimator.SetTrigger(enableGoalTrigger);
        if (enableAnimationDelay > 0f)
            yield return new WaitForSeconds(enableAnimationDelay);
        else
            yield return null;
        ApplyGoalToPlayer(player, goal);
        _enableGoalRoutineRunning = false;
    }

    /// <summary>Call when the goal is completed (e.g. wire from <see cref="GoalCompletionTrigger.onCompletionSucceeded"/>).</summary>
    public void NotifyGoalCompleted()
    {
        onGoalCompleted?.Invoke();
    }
}
