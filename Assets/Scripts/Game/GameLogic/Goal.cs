using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Describes a task the player should complete: navigation hint, optional item data, and linked triggers.
/// </summary>
public class Goal : MonoBehaviour
{
    [Header("Task")]
    [Tooltip("Shown in goal selection UI (fallback: GameObject name).")]
    public string displayName = "delivery";

    [Header("Navigation")]
    [Tooltip("When set, location is read from this transform at runtime.")]
    public Transform locationSource;
    [Tooltip("Fallback position when locationSource is null.")]
    public Vector3 location;
    [Tooltip("Optional override for UI / direction indicator.")]
    public Sprite goalIcon;

    [Header("Item")]
    public ItemDefinition item;

    [Header("Triggers")]
    [Tooltip("Trigger that assigns this goal to the player (optional scene reference).")]
    public GoalAssignmentTrigger assignmentTrigger;
    [Tooltip("Trigger where the player completes this goal (optional scene reference).")]
    public GoalCompletionTrigger completionTrigger;

    /// <summary>Current world position: locationSource.position when set, otherwise location.</summary>
    public Vector3 Location => locationSource != null ? locationSource.position : location;
}
