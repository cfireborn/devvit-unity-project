using UnityEngine;

/// <summary>
/// Interaction trigger that opens a dialogue popup when the player interacts.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class DialogueTrigger : InteractionTrigger
{
    [Header("Dialogue")]
    [Tooltip("The dialogue to show when triggered.")]
    public DialogueInstance dialogueInstance;
    [Tooltip("The dialogue UI. If null, will try FindFirstObjectByType.")]
    public DialogueUI dialogueUI;

    void Awake()
    {
        onInteract.AddListener(HandleInteract);
    }

    void OnDestroy()
    {
        onInteract.RemoveListener(HandleInteract);
    }

    void HandleInteract(GameObject source, Vector2 contactPoint)
    {
        var ui = dialogueUI != null ? dialogueUI : FindFirstObjectByType<DialogueUI>();
        if (ui != null && dialogueInstance != null)
        {
            ui.ShowDialogue(dialogueInstance);
        }
    }
}
