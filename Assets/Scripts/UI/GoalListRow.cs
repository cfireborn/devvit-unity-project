using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// One row in the goal selection overlay. Assign label, optional highlight image, and root <see cref="Button"/>.
/// </summary>
public class GoalListRow : MonoBehaviour
{
    [SerializeField] TMP_Text label;
    [SerializeField] Image primaryHighlight;
    [SerializeField] Button button;

    public void Bind(Goal goal, bool isPrimary, UnityAction onClick)
    {
        if (label != null)
        {
            string text = "?";
            if (goal != null)
                text = !string.IsNullOrEmpty(goal.displayName) ? goal.displayName : goal.gameObject.name;
            label.text = text;
        }

        if (primaryHighlight != null)
            primaryHighlight.enabled = isPrimary;

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            if (onClick != null)
                button.onClick.AddListener(onClick);
        }
    }
}
