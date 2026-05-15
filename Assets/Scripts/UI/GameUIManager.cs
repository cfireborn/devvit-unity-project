using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Coordinates gameplay HUD, modal overlays, and stacked gameplay-input suspension.
/// Attach on the same root as <see cref="MobileInputManager"/> (or parent of gameplay UI).
/// </summary>
public sealed class GameUIManager : MonoBehaviour
{
    public static GameUIManager Instance { get; private set; }

    [Header("Optional explicit refs (resolved under this root when null)")]
    [SerializeField] MobileInputManager mobileInputManager;
    [SerializeField] GoalSelectionUI goalSelectionUI;
    [SerializeField] AdminMenu adminMenu;
    [SerializeField] InventoryMenuUI inventoryMenu;
    [SerializeField] TMP_Text completedGoalsCountText;
    [SerializeField] Button inventoryOpenButton;

    int _gameplaySuspendCount;
    GameServices _gameServices;
    PlayerControllerM _boundPlayer;
    public bool IsGameplaySuspended => _gameplaySuspendCount > 0;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        ResolveReferences();
        EnsureRuntimeUiIfNeeded();
    }

    void Start()
    {
        if (mobileInputManager == null)
            mobileInputManager = GetComponent<MobileInputManager>();

        BindGameServices();

        if (inventoryOpenButton != null)
            inventoryOpenButton.onClick.AddListener(OpenInventory);
    }

    void Update()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        if (Keyboard.current != null && Keyboard.current.backquoteKey.wasPressedThisFrame && adminMenu != null)
            adminMenu.TogglePanel();
#endif
    }

    void OnDestroy()
    {
        if (inventoryOpenButton != null)
            inventoryOpenButton.onClick.RemoveListener(OpenInventory);

        ForceReleaseAllGameplaySuspends();
        UnbindGameServices();

        if (Instance == this)
            Instance = null;
    }

    void ResolveReferences()
    {
        if (goalSelectionUI == null)
            goalSelectionUI = GetComponentInChildren<GoalSelectionUI>(true);
        if (adminMenu == null)
            adminMenu = FindFirstObjectByType<AdminMenu>();
        if (inventoryMenu == null)
            inventoryMenu = GetComponentInChildren<InventoryMenuUI>(true);
    }

    void EnsureRuntimeUiIfNeeded()
    {
        if (completedGoalsCountText != null && inventoryOpenButton != null && inventoryMenu != null)
            return;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
            canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
            return;

        if (inventoryMenu == null)
            inventoryMenu = BuildDefaultInventory(canvas.transform);

        if (completedGoalsCountText == null || inventoryOpenButton == null)
            BuildDefaultHud(canvas.transform);
    }

    InventoryMenuUI BuildDefaultInventory(Transform canvasRoot)
    {
        var root = new GameObject("InventoryMenu", typeof(RectTransform));
        root.transform.SetParent(canvasRoot, false);
        var rt = root.GetComponent<RectTransform>();
        StretchFull(rt);

        var overlay = new GameObject("Overlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        overlay.transform.SetParent(root.transform, false);
        var overlayRt = overlay.GetComponent<RectTransform>();
        StretchFull(overlayRt);
        var overlayImg = overlay.GetComponent<Image>();
        overlayImg.color = new Color(0f, 0f, 0f, 0.55f);
        overlayImg.raycastTarget = true;

        var panel = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panel.transform.SetParent(overlay.transform, false);
        var panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(420f, 280f);
        panelRt.anchoredPosition = Vector2.zero;
        var panelImg = panel.GetComponent<Image>();
        panelImg.color = new Color(0.15f, 0.15f, 0.18f, 1f);

        var title = new GameObject("Title", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        title.transform.SetParent(panel.transform, false);
        var titleRt = title.GetComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0f, 1f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.sizeDelta = new Vector2(-24f, 48f);
        titleRt.anchoredPosition = new Vector2(0f, -12f);
        var titleTmp = title.GetComponent<TextMeshProUGUI>();
        titleTmp.text = "Inventory";
        titleTmp.fontSize = 22f;
        titleTmp.alignment = TextAlignmentOptions.Center;
        ApplyDefaultTmpFont(titleTmp);

        var closeGo = new GameObject("CloseButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        closeGo.transform.SetParent(panel.transform, false);
        var closeRt = closeGo.GetComponent<RectTransform>();
        closeRt.anchorMin = new Vector2(0.5f, 0f);
        closeRt.anchorMax = new Vector2(0.5f, 0f);
        closeRt.pivot = new Vector2(0.5f, 0f);
        closeRt.sizeDelta = new Vector2(160f, 40f);
        closeRt.anchoredPosition = new Vector2(0f, 16f);
        var closeImg = closeGo.GetComponent<Image>();
        closeImg.color = new Color(0.3f, 0.35f, 0.45f, 1f);
        var closeBtn = closeGo.GetComponent<Button>();

        var closeLabel = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        closeLabel.transform.SetParent(closeGo.transform, false);
        var closeLabelRt = closeLabel.GetComponent<RectTransform>();
        StretchFull(closeLabelRt);
        var closeTmp = closeLabel.GetComponent<TextMeshProUGUI>();
        closeTmp.text = "Close";
        closeTmp.fontSize = 16f;
        closeTmp.alignment = TextAlignmentOptions.Center;
        ApplyDefaultTmpFont(closeTmp);

        overlay.SetActive(false);

        var inv = root.AddComponent<InventoryMenuUI>();
        inv.SetOverlayRoot(overlay);
        inv.SetCloseButton(closeBtn);
        return inv;
    }

    void BuildDefaultHud(Transform canvasRoot)
    {
        var hud = new GameObject("GameplayHud", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        hud.transform.SetParent(canvasRoot, false);
        var hudRt = hud.GetComponent<RectTransform>();
        hudRt.anchorMin = new Vector2(1f, 1f);
        hudRt.anchorMax = new Vector2(1f, 1f);
        hudRt.pivot = new Vector2(1f, 1f);
        hudRt.sizeDelta = new Vector2(220f, 48f);
        hudRt.anchoredPosition = new Vector2(-12f, -12f);
        var hlg = hud.GetComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleRight;
        hlg.spacing = 8f;
        hlg.padding = new RectOffset(8, 8, 4, 4);
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        if (completedGoalsCountText == null)
        {
            var countGo = new GameObject("GoalsCompleted", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            countGo.transform.SetParent(hud.transform, false);
            var le = countGo.AddComponent<LayoutElement>();
            le.preferredWidth = 72f;
            le.minHeight = 36f;
            var tmp = countGo.GetComponent<TextMeshProUGUI>();
            tmp.text = "0";
            tmp.fontSize = 18f;
            tmp.alignment = TextAlignmentOptions.MidlineRight;
            ApplyDefaultTmpFont(tmp);
            completedGoalsCountText = tmp;
        }

        if (inventoryOpenButton == null)
        {
            var btnGo = new GameObject("InventoryButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(hud.transform, false);
            var le = btnGo.AddComponent<LayoutElement>();
            le.preferredWidth = 100f;
            le.minHeight = 36f;
            var img = btnGo.GetComponent<Image>();
            img.color = new Color(0.25f, 0.28f, 0.38f, 1f);
            inventoryOpenButton = btnGo.GetComponent<Button>();

            var label = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            label.transform.SetParent(btnGo.transform, false);
            StretchFull(label.GetComponent<RectTransform>());
            var t = label.GetComponent<TextMeshProUGUI>();
            t.text = "Items";
            t.fontSize = 15f;
            t.alignment = TextAlignmentOptions.Center;
            ApplyDefaultTmpFont(t);
        }
    }

    static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

    static void ApplyDefaultTmpFont(TextMeshProUGUI tmp)
    {
        if (tmp == null) return;
        if (TMP_Settings.defaultFontAsset != null)
            tmp.font = TMP_Settings.defaultFontAsset;
    }

    void BindGameServices()
    {
        _gameServices = FindFirstObjectByType<GameServices>();
        if (_gameServices == null) return;

        _gameServices.onPlayerRegistered += OnPlayerRegistered;
        _gameServices.onPlayerDeregistered += OnPlayerDeregistered;

        var p = _gameServices.GetPlayer();
        if (p != null)
            BindPlayer(p);
    }

    void UnbindGameServices()
    {
        if (_gameServices == null) return;

        _gameServices.onPlayerRegistered -= OnPlayerRegistered;
        _gameServices.onPlayerDeregistered -= OnPlayerDeregistered;
        _gameServices = null;
        UnbindPlayer();
    }

    void OnPlayerRegistered()
    {
        var p = _gameServices != null ? _gameServices.GetPlayer() : null;
        if (p != null)
            BindPlayer(p);
    }

    void OnPlayerDeregistered(PlayerControllerM player)
    {
        if (player != null && player == _boundPlayer)
            UnbindPlayer();
    }

    void BindPlayer(PlayerControllerM player)
    {
        UnbindPlayer();
        _boundPlayer = player;
        if (_boundPlayer == null) return;

        _boundPlayer.CompletedGoalsCountChanged += OnCompletedGoalsCountChanged;
        RefreshCompletedGoalsLabel();
        ApplyGameplayInputFromSuspendCount();
    }

    void UnbindPlayer()
    {
        if (_boundPlayer != null)
        {
            _boundPlayer.CompletedGoalsCountChanged -= OnCompletedGoalsCountChanged;
            _boundPlayer = null;
        }

        if (completedGoalsCountText != null)
            completedGoalsCountText.text = "0";
    }

    void OnCompletedGoalsCountChanged() => RefreshCompletedGoalsLabel();

    void RefreshCompletedGoalsLabel()
    {
        if (completedGoalsCountText == null) return;
        int n = _boundPlayer != null ? _boundPlayer.CompletedGoalsCount : 0;
        completedGoalsCountText.text = n.ToString();
    }

    PlayerControllerM CurrentPlayer()
    {
        if (_boundPlayer != null)
            return _boundPlayer;
        return _gameServices != null ? _gameServices.GetPlayer() : null;
    }

    public void PushGameplaySuspend()
    {
        _gameplaySuspendCount++;
        ApplyGameplayInputFromSuspendCount();
    }

    public void PopGameplaySuspend()
    {
        if (_gameplaySuspendCount <= 0) return;
        _gameplaySuspendCount--;
        ApplyGameplayInputFromSuspendCount();
    }

    /// <summary>Re-applies input enabled/disabled from the suspend stack (e.g. after dialogue closes).</summary>
    public void ApplyGameplayInputFromSuspendCount() =>
        TrySetPlayerGameplayEnabled(_gameplaySuspendCount == 0);

    void TrySetPlayerGameplayEnabled(bool enabled)
    {
        var p = CurrentPlayer();
        if (p == null) return;
        p.SetGameplayInputEnabled(enabled);
    }

    void ForceReleaseAllGameplaySuspends()
    {
        if (_gameplaySuspendCount <= 0) return;
        _gameplaySuspendCount = 0;
        ApplyGameplayInputFromSuspendCount();
    }

    public bool TryOpenGoalSelection()
    {
        if (goalSelectionUI == null) return false;
        var p = CurrentPlayer();
        if (p == null ) return false;
        goalSelectionUI.Open();
        return true;
    }

    public void ToggleAdminFromSecretGesture()
    {
        if (adminMenu != null)
            adminMenu.TogglePanel();
    }

    public void OpenInventory()
    {
        if (inventoryMenu == null) return;
        if (inventoryMenu.IsOpen) return;
        inventoryMenu.Open();
    }

    public void CloseInventory()
    {
        if (inventoryMenu == null) return;
        inventoryMenu.Close();
    }
}
