using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI arrow that moves/rotates along the screen edges to point toward the player's primary goal.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class GoalDirectionIndicator : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The player transform (for computing direction).")]
    public Transform player;
    [Tooltip("The player controller to read primaryGoal from.")]
    public PlayerControllerM playerController;
    [Tooltip("Optional: assign explicitly; if null, uses GetComponent<Button>().")]
    public Button openGoalMenuButton;

    [Header("Indicator Visuals")]
    [Tooltip("Optional TMP label shown while the indicator is visible when the player has more than one goal.")]
    public TMP_Text goalCountLabel;
    public Transform rotatingPortion;
    public Image iconImage;

    [Header("Settings")]
    [Tooltip("Inset from screen edges (0-0.5).")]
    [Range(0f, 0.5f)]
    public float screenEdgeInset = 0.05f;
    [Tooltip("Camera for world-to-screen conversion. If null, uses Camera.main.")]
    public Camera cam;

    [Header("On-screen hover")]
    [Tooltip("Optional: drives hover clip via bool while goal is visible on screen.")]
    public Animator hoverAnimator;
    [Tooltip("Animator bool; true while goal on screen, false when off screen.")]
    public string hoverBoolParameter = "Hover";
    [Tooltip("Local Z euler when goal is on screen (arrow points down if sprite faces up at 0°).")]
    public float onScreenArrowEulerZ = 180f;
    [Tooltip("Degrees per second — interpolate arrow rotation between on-screen and toward-goal angles.")]
    [Min(0f)]
    public float rotationLerpSpeed = 540f;
    [Tooltip("Viewport units per second — smooth indicator anchor toward its computed screen position. Set 0 for instant movement.")]
    [Min(0f)]
    public float viewportMoveSpeed = 2.5f;

    RectTransform _rect;
    Canvas _canvas;
    CanvasGroup _canvasGroup;
    bool _hidden;
    bool _wasGoalOnScreen;
    float _arrowEulerZ;
    bool _arrowEulerInitialized;
    Goal _viewportTrackedGoal;
    Vector2 _arrowViewport;

    void Awake()
    {
        _rect = GetComponent<RectTransform>();
        _canvas = GetComponentInParent<Canvas>();
        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null)
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    void Start()
    {
        TrySetPlayerInformation();
        WireOpenButton();

        if (player == null || playerController == null)
        {
            SetVisible(false);
            var gameServices = FindFirstObjectByType<GameServices>();
            if (gameServices != null)
                gameServices.onPlayerRegistered += TrySetPlayerInformation;
        }

        SyncGoalCountLabel();
    }

    void OnDestroy()
    {
        var btn = openGoalMenuButton != null ? openGoalMenuButton : GetComponent<Button>();
        if (btn != null)
            btn.onClick.RemoveListener(OnOpenGoalSelectionClicked);
    }

    void WireOpenButton()
    {
        var btn = openGoalMenuButton != null ? openGoalMenuButton : GetComponent<Button>();
        if (btn != null)
            btn.onClick.AddListener(OnOpenGoalSelectionClicked);
    }

    void OnOpenGoalSelectionClicked()
    {
        if (_hidden) return;
        if (playerController == null) return;
        GameUIManager.Instance?.TryOpenGoalSelection();
    }

    void TrySetPlayerInformation()
    {
        var gameServices = FindFirstObjectByType<GameServices>();
        if (gameServices != null)
        {
            if (player == null || playerController == null)
            {
                var p = gameServices.GetPlayer();
                if (p != null)
                {
                    if (player == null) player = p.transform;
                    if (playerController == null) playerController = p;
                }
            }
            if (cam == null)
                cam = gameServices.GetCamera();
        }
    }

    void LateUpdate()
    {
        if (!TryBeginFrame(out Goal goal))
        {
            HideIndicator();
            return;
        }

        if (!TryEnsureCamera())
        {
            HideIndicator();
            return;
        }

        SetVisible(true);
        SyncGoalCountLabel();

        Vector3 playerViewport = cam.WorldToViewportPoint(player.position);
        Vector3 goalViewport = cam.WorldToViewportPoint(goal.Location);

        GetViewportInsetBounds(out float minX, out float maxX, out float minY, out float maxY);
        bool goalOnScreen = IsGoalFullyOnScreen(goalViewport);

        SyncHoverAnimator(goalOnScreen);
        SyncIconImage();

        Vector2 targetViewport = ComputeArrowViewport(playerViewport, goalViewport, goalOnScreen, minX, maxX, minY, maxY);
        if (goal != _viewportTrackedGoal)
        {
            _viewportTrackedGoal = goal;
            _arrowViewport = targetViewport;
            _arrowEulerInitialized = false;
        }
        else if (viewportMoveSpeed <= 0f)
            _arrowViewport = targetViewport;
        else
            _arrowViewport = Vector2.MoveTowards(_arrowViewport, targetViewport, viewportMoveSpeed * Time.deltaTime);

        ApplyArrowViewport(_arrowViewport);

        float targetEulerZ = ComputeTargetEulerZ(playerViewport, goalViewport, goalOnScreen);
        ApplySmoothedArrowRotation(targetEulerZ);
    }

    bool TryBeginFrame(out Goal goal)
    {
        goal = null;
        if (player == null || playerController == null)
            return false;

        goal = playerController.PrimaryGoal;
        return goal != null;
    }

    bool TryEnsureCamera()
    {
        if (cam == null)
        {
            var gs = FindFirstObjectByType<GameServices>();
            cam = gs != null ? gs.GetCamera() : null;
        }
        if (cam == null)
            cam = Camera.main;
        return cam != null;
    }

    void HideIndicator()
    {
        _viewportTrackedGoal = null;
        ResetHoverState(false);
        SetVisible(false);
        SyncGoalCountLabel();
    }

    void SyncGoalCountLabel()
    {
        if (goalCountLabel == null) return;
        int n = playerController != null ? playerController.Goals.Count : 0;
        bool show = !_hidden && n > 0;
        goalCountLabel.gameObject.SetActive(show);
        if (show)
            goalCountLabel.text = n.ToString();
    }

    void SyncIconImage()
    {
        if (iconImage == null) return;
        if (playerController != null && playerController.PrimaryGoal != null)
            iconImage.sprite = playerController.PrimaryGoal.goalIcon;
        else
            iconImage.sprite = null;
    }

    void GetViewportInsetBounds(out float minX, out float maxX, out float minY, out float maxY)
    {
        float inset = screenEdgeInset;
        minX = inset;
        maxX = 1f - inset;
        minY = inset;
        maxY = 1f - inset;
    }

    static bool IsGoalFullyOnScreen(Vector3 goalViewport)
    {
        return goalViewport.x >= 0f && goalViewport.x <= 1f && goalViewport.y >= 0f && goalViewport.y <= 1f;
    }

    void SyncHoverAnimator(bool goalOnScreen)
    {
        if (_wasGoalOnScreen == goalOnScreen)
            return;

        _wasGoalOnScreen = goalOnScreen;
        if (hoverAnimator != null && !string.IsNullOrEmpty(hoverBoolParameter))
            hoverAnimator.SetBool(hoverBoolParameter, goalOnScreen);
    }

    static Vector2 ComputeArrowViewport(
        Vector3 playerViewport,
        Vector3 goalViewport,
        bool goalOnScreen,
        float minX,
        float maxX,
        float minY,
        float maxY)
    {
        if (goalOnScreen)
            return new Vector2(Mathf.Clamp(goalViewport.x, minX, maxX), Mathf.Clamp(goalViewport.y, minY, maxY));

        Vector2 dir = ((Vector2)goalViewport - (Vector2)playerViewport).normalized;
        if (dir.sqrMagnitude < 0.001f)
            return new Vector2(0.5f, 0.5f);

        float t = float.MaxValue;
        if (Mathf.Abs(dir.x) > 0.001f)
        {
            float tx = dir.x > 0 ? (maxX - playerViewport.x) / dir.x : (minX - playerViewport.x) / dir.x;
            if (tx > 0f && tx < t)
                t = tx;
        }
        if (Mathf.Abs(dir.y) > 0.001f)
        {
            float ty = dir.y > 0 ? (maxY - playerViewport.y) / dir.y : (minY - playerViewport.y) / dir.y;
            if (ty > 0f && ty < t)
                t = ty;
        }

        return t < float.MaxValue
            ? new Vector2(playerViewport.x + dir.x * t, playerViewport.y + dir.y * t)
            : new Vector2(0.5f, 0.5f);
    }

    void ApplyArrowViewport(Vector2 arrowViewport)
    {
        _rect.anchorMin = arrowViewport;
        _rect.anchorMax = arrowViewport;
        _rect.anchoredPosition = Vector2.zero;
    }

    float ComputeTargetEulerZ(Vector3 playerViewport, Vector3 goalViewport, bool goalOnScreen)
    {
        if (goalOnScreen)
            return onScreenArrowEulerZ;
        return Mathf.Atan2(goalViewport.y - playerViewport.y, goalViewport.x - playerViewport.x) * Mathf.Rad2Deg - 90f;
    }

    void ApplySmoothedArrowRotation(float targetEulerZ)
    {
        if (!_arrowEulerInitialized)
        {
            _arrowEulerZ = targetEulerZ;
            _arrowEulerInitialized = true;
        }
        else if (rotationLerpSpeed <= 0f)
            _arrowEulerZ = targetEulerZ;
        else
            _arrowEulerZ = Mathf.MoveTowardsAngle(_arrowEulerZ, targetEulerZ, rotationLerpSpeed * Time.deltaTime);

        rotatingPortion.localRotation = Quaternion.Euler(0f, 0f, _arrowEulerZ);
    }

    void SetVisible(bool visible)
    {
        if (_hidden == !visible) return;
        _hidden = !visible;
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = visible ? 1f : 0f;
            _canvasGroup.blocksRaycasts = visible;
            _canvasGroup.interactable = visible;
        }
        else
            gameObject.SetActive(visible);
    }

    void ResetHoverState(bool goalOnScreen)
    {
        _wasGoalOnScreen = goalOnScreen;
        _arrowEulerInitialized = false;
        if (hoverAnimator != null && !string.IsNullOrEmpty(hoverBoolParameter))
            hoverAnimator.SetBool(hoverBoolParameter, goalOnScreen);
    }
}