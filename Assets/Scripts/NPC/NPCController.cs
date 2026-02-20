using UnityEngine;

/// <summary>
/// Manages state and behavior of an NPC. Handles animations, speech bubbles, fullscreen dialogue, and item spawning.
/// </summary>
public class NPCController : MonoBehaviour
{
    [Header("Animation")]
    [Tooltip("Optional Animator for playing animations.")]
    public Animator animator;

    [Header("Speech")]
    [Tooltip("Optional speech bubble for quick popups.")]
    public SpeechBubbleUI speechBubble;

    [Header("Items")]
    [Tooltip("Item spawner for this NPC. Can spawn items when giving quests, etc.")]
    public ItemSpawner itemSpawner;

    /// <summary>Play an animation trigger.</summary>
    public void PlayAnimationTrigger(string triggerName)
    {
        if (animator != null && !string.IsNullOrEmpty(triggerName))
            animator.SetTrigger(triggerName);
    }

    /// <summary>Play an animation by setting a bool parameter.</summary>
    public void SetAnimationBool(string paramName, bool value)
    {
        if (animator != null && !string.IsNullOrEmpty(paramName))
            animator.SetBool(paramName, value);
    }

    /// <summary>Play an animation by setting a float parameter.</summary>
    public void SetAnimationFloat(string paramName, float value)
    {
        if (animator != null && !string.IsNullOrEmpty(paramName))
            animator.SetFloat(paramName, value);
    }

    /// <summary>Set an animation integer parameter.</summary>
    public void SetAnimationInt(string paramName, int value)
    {
        if (animator != null && !string.IsNullOrEmpty(paramName))
            animator.SetInteger(paramName, value);
    }

    /// <summary>Show a speech bubble using the given settings.</summary>
    public void ShowSpeechBubble(SpeechBubbleType Type)
    {
        SpeechBubbleSettings settings = new SpeechBubbleSettings { bubbleType = Type, duration = 2.0f };
        if (speechBubble != null)
            speechBubble.Show(settings);
    }

    /// <summary>Hide the speech bubble immediately.</summary>
    public void HideSpeechBubble()
    {
        if (speechBubble != null)
            speechBubble.Hide();
    }

    /// <summary>Spawn an item using the attached ItemSpawner. Returns the spawned GameObject or null.</summary>
    public GameObject SpawnItem()
    {
        return itemSpawner != null ? itemSpawner.SpawnItem() : null;
    }

    /// <summary>Spawn an item at prefab index.</summary>
    public GameObject SpawnItem(int prefabIndex)
    {
        return itemSpawner != null ? itemSpawner.SpawnItem(prefabIndex) : null;
    }

    /// <summary>Spawn a specific prefab.</summary>
    public GameObject SpawnItem(GameObject prefab)
    {
        return itemSpawner != null ? itemSpawner.SpawnItem(prefab) : null;
    }
}
