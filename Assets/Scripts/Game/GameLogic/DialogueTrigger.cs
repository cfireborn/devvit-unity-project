using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Interaction trigger that opens a dialogue popup when the player interacts.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class DialogueTrigger : InteractionTrigger
{
    [Header("Dialogue")]
    [Tooltip("The dialogue to show when triggered.")]
    public DialogueInstance dialogueInstance;

    [Header("Events")]
    [Tooltip("Fired when the dialogue opened by this trigger is completed.")]
    public UnityEvent onDialogueComplete;

    protected override void OnInteractInvoked(GameObject source, Vector2 contactPoint)
    {
        ShowDialogue();
    }

    public void ShowDialogue()
    {
        var gs = FindFirstObjectByType<GameServices>();
        var ui = gs != null ? gs.GetDialogueUI() : null;
        if (ui != null && dialogueInstance != null)
        {
            UnityAction handler = null;
            handler = () =>
            {
                ui.onDialogueComplete.RemoveListener(handler);
                var uiManager = GameUIManager.Instance != null
                    ? GameUIManager.Instance
                    : FindFirstObjectByType<GameUIManager>();
                uiManager?.ApplyGameplayInputFromSuspendCount();
                onDialogueComplete?.Invoke();
            };
            ui.onDialogueComplete.AddListener(handler);
            ui.ShowDialogue(dialogueInstance);
        }
    }
}
