using UnityEngine;

/// <summary>
/// Inspector-configurable relay to show/hide a specific SpeechBubbleUI with SpeechBubbleSettings.
/// Add to a GameObject and wire InteractionTrigger.onEnter/onExit to Show/Hide for designer setup.
/// </summary>
public class SpeechBubbleTrigger : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("The SpeechBubbleUI to show/hide. If null, uses GetComponent on this GameObject.")]
    public SpeechBubbleUI targetSpeechBubble;

    [Header("Settings")]
    [Tooltip("Settings used when Show is called.")]
    public SpeechBubbleSettings settings = new SpeechBubbleSettings { bubbleType = SpeechBubbleType.Happy, duration = 3f };

    void Awake()
    {
        if (targetSpeechBubble == null)
            targetSpeechBubble = GetComponent<SpeechBubbleUI>();
    }

    /// <summary>Show the target SpeechBubbleUI with the configured settings. Wire to InteractionTrigger.onEnter.</summary>
    public void Show()
    {
        if (targetSpeechBubble != null)
            targetSpeechBubble.Show(settings);
    }

    /// <summary>Hide the target SpeechBubbleUI immediately. Wire to InteractionTrigger.onExit.</summary>
    public void Hide()
    {
        if (targetSpeechBubble != null)
            targetSpeechBubble.Hide();
    }
}
