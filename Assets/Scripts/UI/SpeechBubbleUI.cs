using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Speech bubble types that can be displayed. Each maps to a sprite configured on the component.
/// </summary>
public enum SpeechBubbleType
{
    Happy,
    Surprised,
    Confused,
    Love
}

/// <summary>
/// Settings for displaying a speech bubble. Used by NPCController.ShowSpeechBubble.
/// </summary>
[Serializable]
public struct SpeechBubbleSettings
{
    public SpeechBubbleType bubbleType;
    public float duration;
}

/// <summary>
/// Simple speech bubble that shows a sprite for a duration. Attach to a Canvas (World Space) child of an NPC.
/// </summary>
public class SpeechBubbleUI : MonoBehaviour
{
    [Serializable]
    public class BubbleTypeEntry
    {
        public SpeechBubbleType type;
        public Sprite sprite;
    }

    [Header("References")]
    [Tooltip("Image used to display the speech bubble sprite. If null, uses GetComponentInChildren<Image>().")]
    public SpriteRenderer spriteDisplay;

    [Header("Sprite Map")]
    [Tooltip("Map of bubble types to sprites. Assign a sprite for each type you want to display.")]
    public BubbleTypeEntry[] bubbleSprites = Array.Empty<BubbleTypeEntry>();

    [Header("Trigger Relay")]
    [Tooltip("Settings used when ShowFromTrigger is called (e.g. from InteractionTrigger.onEnter).")]
    public SpeechBubbleSettings triggerSettings = new SpeechBubbleSettings { bubbleType = SpeechBubbleType.Happy, duration = 3f };

    Coroutine _hideCoroutine;

    void Awake()
    {
        if (spriteDisplay == null) spriteDisplay = GetComponentInChildren<SpriteRenderer>();
        Hide();
    }

    /// <summary>Show a speech bubble sprite for the given duration (seconds).</summary>
    public void Show(SpeechBubbleType bubbleType, float duration)
    {
        Show(new SpeechBubbleSettings { bubbleType = bubbleType, duration = duration });
    }

    /// <summary>Show a speech bubble using the given settings.</summary>
    public void Show(SpeechBubbleSettings settings)
    {
        if (_hideCoroutine != null) StopCoroutine(_hideCoroutine);

        Sprite sprite = GetSpriteForType(settings.bubbleType);
        if (spriteDisplay != null)
        {
            spriteDisplay.sprite = sprite;
            spriteDisplay.enabled = sprite != null;
        }
        gameObject.SetActive(true);

        if (settings.duration > 0f)
            _hideCoroutine = StartCoroutine(HideAfter(settings.duration));
    }

    Sprite GetSpriteForType(SpeechBubbleType bubbleType)
    {
        if (bubbleSprites == null) return null;
        foreach (var entry in bubbleSprites)
        {
            if (entry != null && entry.type == bubbleType && entry.sprite != null)
                return entry.sprite;
        }
        return null;
    }

    IEnumerator HideAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        _hideCoroutine = null;
        Hide();
    }

    /// <summary>Show using triggerSettings. Wire to InteractionTrigger.onEnter for designer-friendly setup.</summary>
    public void ShowFromTrigger()
    {
        Show(triggerSettings);
    }

    /// <summary>Hide the speech bubble immediately.</summary>
    public void Hide()
    {
        if (_hideCoroutine != null)
        {
            StopCoroutine(_hideCoroutine);
            _hideCoroutine = null;
        }
        gameObject.SetActive(false);
    }
}
