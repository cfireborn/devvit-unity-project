using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Simple trigger that builds a ladder between two specified clouds when activated.
/// Can be activated by the player entering the trigger zone, or by calling BuildLadder() from a UnityEvent.
/// </summary>
public class LadderTrigger : MonoBehaviour
{
    [Header("Clouds")]
    [Tooltip("First cloud (order does not affect ladder placement).")]
    public CloudPlatform cloudA;
    [Tooltip("Second cloud.")]
    public CloudPlatform cloudB;

    [Header("Events")]
    public UnityEvent<bool> onLadderBuilt;

    /// <summary>
    /// Try to build a ladder between cloudA and cloudB. Returns true if a ladder was created or already exists.
    /// Can be called from UnityEvents (e.g. from an InteractionTrigger).
    /// </summary>
    public void BuildLadder()
    {
        if (cloudA == null || cloudB == null)
        {
            onLadderBuilt?.Invoke(false);
            return;
        }

        bool success = cloudA.TryBuildLadderTo(cloudB);
        onLadderBuilt?.Invoke(success);
    }
}
