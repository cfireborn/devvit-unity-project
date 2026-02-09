using System;
using UnityEngine;

[CreateAssetMenu(fileName = "DialogueInstance", menuName = "Scriptable Objects/DialogueInstance")]
public class DialogueInstance : ScriptableObject
{
    [Serializable]
    public class DialogueStep
    {
        [Tooltip("Character portrait sprite for this line.")]
        public Sprite characterSprite;
        [TextArea(2, 5)]
        [Tooltip("Dialogue text for this line.")]
        public string text;
    }

    public DialogueStep[] steps = Array.Empty<DialogueStep>();
}
