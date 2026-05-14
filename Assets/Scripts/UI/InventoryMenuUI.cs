using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Placeholder inventory overlay. Open/Close toggles <see cref="overlayRoot"/> and pairs with
/// <see cref="GameUIManager"/> gameplay suspend stack.
/// </summary>
public sealed class InventoryMenuUI : MonoBehaviour
{
    [SerializeField] GameObject overlayRoot;
    [SerializeField] Button closeButton;

    bool _open;

    void Awake()
    {
        if (overlayRoot != null)
            //overlayRoot.SetActive(false);
        if (closeButton != null)
            closeButton.onClick.AddListener(Close);
    }

    void OnDestroy()
    {
        if (closeButton != null)
            closeButton.onClick.RemoveListener(Close);

        if (_open)
        {
            _open = false;
            if (GameUIManager.Instance != null)
                GameUIManager.Instance.PopGameplaySuspend();
        }
    }

    public void SetOverlayRoot(GameObject root) => overlayRoot = root;

    public void SetCloseButton(Button button)
    {
        if (closeButton != null)
            closeButton.onClick.RemoveListener(Close);
        closeButton = button;
        if (closeButton != null)
            closeButton.onClick.AddListener(Close);
    }

    public void Open()
    {
        if (_open || overlayRoot == null || GameUIManager.Instance == null) return;
        _open = true;
        GameUIManager.Instance.PushGameplaySuspend();
        overlayRoot.SetActive(true);
    }

    public void Close()
    {
        if (!_open) return;
        _open = false;
        if (overlayRoot != null)
            overlayRoot.SetActive(false);
        if (GameUIManager.Instance != null)
            GameUIManager.Instance.PopGameplaySuspend();
    }

    public bool IsOpen => _open;
}
