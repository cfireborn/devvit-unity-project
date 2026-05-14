using UnityEngine;

/// <summary>
/// Static data for an item type (icon, score). Used by <see cref="Goal"/> and assignment triggers.
/// </summary>
[CreateAssetMenu(menuName = "Scriptable Objects/Item Definition", fileName = "ItemDefinition")]
public class ItemDefinition : ScriptableObject
{
    public Sprite icon;
    [Min(1)]
    public int points = 1;
}
