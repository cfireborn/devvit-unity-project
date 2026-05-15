using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Assigns a <see cref="Goal"/> to the player (dialogue, NPC, scripted enable).
/// Inherits <see cref="ActivationTriggerBase"/> for chance, activation delay, and single-use.
/// Either references an existing Goal (<see cref="manuallySetGoal"/>), or creates one at runtime from
/// <see cref="completionTrigger"/> and <see cref="generatedGoalDisplayName"/>.
/// </summary>
public class GoalAssignmentTrigger : ActivationTriggerBase
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

    protected bool _enableGoalRoutineRunning;
    protected Goal _runtimeGeneratedGoal;

    protected override void OnDestroy()
    {
        if (_runtimeGeneratedGoal != null)
        {
            Destroy(_runtimeGeneratedGoal.gameObject);
            _runtimeGeneratedGoal = null;
        }
        base.OnDestroy();
    }

    /// <summary>Add the goal to the player. Call from dialogue complete, UnityEvents, etc.</summary>
    public virtual void EnableGoal()
    {
        if (_enableGoalRoutineRunning) return;
        if (!HasSpawnAuthorityForGoalAssignment()) return;
        if (!CanEnableGoal()) return;

        var gs = FindFirstObjectByType<GameServices>();
        var player = gs != null ? gs.GetPlayer() : null;
        if (player == null) return;

        if (!TryBeginActivationPipeline()) return;

        RunDelayedOrImmediate(() => ExecuteEnableGoalBody(player));
    }

    protected virtual bool HasSpawnAuthorityForGoalAssignment() => true;

    protected virtual bool CanEnableGoal()
    {
        if (manuallySetGoal)
            return goal != null;
        if (completionTrigger == null)
            return false;
        return !string.IsNullOrWhiteSpace(generatedGoalDisplayName);
    }

    protected virtual Goal TryGetGoalForEnableAnimation(PlayerControllerM player) =>
        manuallySetGoal ? goal : (_runtimeGeneratedGoal != null ? _runtimeGeneratedGoal : null);

    /// <summary>Runs after activation chance and optional <see cref="ActivationTriggerBase.activationDelaySeconds"/>.</summary>
    protected virtual void ExecuteEnableGoalBody(PlayerControllerM player)
    {
        Goal g = ResolveGoalForAssignment();

        if (waitForEnableAnimation && enableGoalAnimator != null)
            StartCoroutine(EnableGoalAfterAnimation(player, g));
        else
        {
            ApplyGoalToPlayer(player, g);
            MarkActivationConsumedAndMaybeDisable();
        }
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

    /// <summary>Create a runtime goal parented under <paramref name="completion"/> (e.g. spawned delivery).</summary>
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

    IEnumerator EnableGoalAfterAnimation(PlayerControllerM player, Goal animGoal)
    {
        _enableGoalRoutineRunning = true;
        Goal iconGoal = animGoal ?? TryGetGoalForEnableAnimation(player);
        if (animSpriteRenderer != null && iconGoal != null && iconGoal.item != null && iconGoal.item.icon != null)
            animSpriteRenderer.sprite = iconGoal.item.icon;
        if (!string.IsNullOrEmpty(enableGoalTrigger))
            enableGoalAnimator.SetTrigger(enableGoalTrigger);
        if (enableAnimationDelay > 0f)
            yield return new WaitForSeconds(enableAnimationDelay);
        else
            yield return null;
        ApplyGoalToPlayer(player, animGoal);
        MarkActivationConsumedAndMaybeDisable();
        _enableGoalRoutineRunning = false;
    }

    /// <summary>Call when the goal is completed (e.g. wire from <see cref="GoalCompletionTrigger.onCompletionSucceeded"/>).</summary>
    public void NotifyGoalCompleted()
    {
        onGoalCompleted?.Invoke();
    }
}
