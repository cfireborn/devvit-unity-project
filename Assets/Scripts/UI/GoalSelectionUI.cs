using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Full-screen goal picker: lists active goals, highlights primary, tap another to set primary.
/// Gameplay suspend is handled by <see cref="GameUIManager"/> while this overlay is open.
/// </summary>
public class GoalSelectionUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Root panel for this overlay (usually deactivated in the scene until opened).")]
    public GameObject overlayRoot;
    [Tooltip("Parent for instantiated rows (e.g. Scroll View → Viewport → Content with Vertical Layout Group).")]
    public RectTransform listContent;
    [Tooltip("Prefab with GoalListRow on the root.")]
    public GoalListRow rowPrefab;
    [Tooltip("Optional: tap dimmed area to close.")]
    public Button backdropCloseButton;

    PlayerControllerM _player;
    bool _open = false;

    void Awake()
    {
        if (overlayRoot != null)
           //overlayRoot.SetActive(false);

        if (backdropCloseButton != null)
            backdropCloseButton.onClick.AddListener(Close);
    }

    void OnDestroy()
    {
        if (backdropCloseButton != null)
            backdropCloseButton.onClick.RemoveListener(Close);

        if (_open)
        {
            _open = false;
            if (overlayRoot != null)
                overlayRoot.SetActive(false);
            GameUIManager.Instance?.PopGameplaySuspend();
        }
    }

    /// <summary>Open the overlay if the player has at least one goal.</summary>
    public void Open()
    {
        if (_open) return;
        if (!TryResolvePlayer()) return;
        if (GameUIManager.Instance == null) return;

        _open = true;
        GameUIManager.Instance.PushGameplaySuspend();
        RebuildList();
        if (overlayRoot != null)
            overlayRoot.SetActive(true);
    }

    /// <summary>Close the overlay and restore gameplay input.</summary>
    public void Close()
    {
        if (!_open) return;
        _open = false;

        if (overlayRoot != null)
            overlayRoot.SetActive(false);

        GameUIManager.Instance?.PopGameplaySuspend();
        _player = null;
    }

    bool TryResolvePlayer()
    {
        if (_player != null)
            return true;

        var gs = FindFirstObjectByType<GameServices>();
        _player = gs != null ? gs.GetPlayer() : null;
        return _player != null;
    }

    void RebuildList()
    {
        if (listContent == null || rowPrefab == null || _player == null)
            return;

        for (int i = listContent.childCount - 1; i >= 0; i--)
            Destroy(listContent.GetChild(i).gameObject);

        Goal primary = _player.PrimaryGoal;
        foreach (Goal goal in _player.Goals)
        {
            if (goal == null) continue;
            Goal g = goal;

            GoalListRow row = Instantiate(rowPrefab, listContent);
            bool isPrimary = g == primary;
            row.Bind(g, isPrimary, () => OnGoalRowClicked(g));
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(listContent);
    }

    void OnGoalRowClicked(Goal goal)
    {
        if (_player == null || goal == null) return;
        if (goal != _player.PrimaryGoal)
            _player.SetPrimaryGoal(goal);
        RebuildList();
    }
}
