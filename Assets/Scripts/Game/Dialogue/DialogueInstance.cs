using System;
using UnityEngine;

[CreateAssetMenu(fileName = "DialogueInstance", menuName = "Scriptable Objects/DialogueInstance")]
public class DialogueInstance : ScriptableObject
{
    public enum DialoguePresentation
    {
        Standard = 0,
        CompersionTitleCard = 1
    }

    [Serializable]
    public class DialogueStep
    {
        [Tooltip("Character portrait sprite for this line.")]
        public Sprite characterSprite;
        [TextArea(2, 5)]
        [Tooltip("Dialogue text for this line.")]
        public string text;
    }

    [Tooltip("Selects the visual presentation while preserving the same dialogue completion chain.")]
    public DialoguePresentation presentation = DialoguePresentation.Standard;
    public DialogueStep[] steps = Array.Empty<DialogueStep>();
}
