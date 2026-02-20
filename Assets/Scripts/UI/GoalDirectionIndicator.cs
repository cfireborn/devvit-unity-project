using UnityEngine;

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

    [Header("Settings")]
    [Tooltip("Inset from screen edges (0-0.5).")]
    [Range(0f, 0.5f)]
    public float screenEdgeInset = 0.05f;
    [Tooltip("Camera for world-to-screen conversion. If null, uses Camera.main.")]
    public Camera cam;

    RectTransform _rect;
    Canvas _canvas;
    CanvasGroup _canvasGroup;
    bool _hidden;

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

        if (player == null || playerController == null)
        {
            SetVisible(false);
            var gameServices = FindFirstObjectByType<GameServices>();
            if (gameServices != null)
            {
                gameServices.onPlayerRegistered += TrySetPlayerInformation;
            }
        }
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
        if (player == null || playerController == null)
        {
            SetVisible(false);
            return;
        }

        var goal = playerController.PrimaryGoal;
        if (goal == null)
        {
            SetVisible(false);
            return;
        }

        if (cam == null)
        {
            var gs = FindFirstObjectByType<GameServices>();
            cam = gs != null ? gs.GetCamera() : null;
        }
        if (cam == null) cam = Camera.main;
        if (cam == null)
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);

        Vector3 playerViewport = cam.WorldToViewportPoint(player.position);
        Vector3 goalViewport = cam.WorldToViewportPoint(goal.Location);

        float minX = screenEdgeInset;
        float maxX = 1f - screenEdgeInset;
        float minY = screenEdgeInset;
        float maxY = 1f - screenEdgeInset;

        bool goalOnScreen = goalViewport.x >= 0 && goalViewport.x <= 1 && goalViewport.y >= 0 && goalViewport.y <= 1;
        Vector2 arrowViewport;

        if (goalOnScreen)
        {
            arrowViewport = new Vector2(Mathf.Clamp(goalViewport.x, minX, maxX), Mathf.Clamp(goalViewport.y, minY, maxY));
        }
        else
        {
            Vector2 dir = ((Vector2)goalViewport - (Vector2)playerViewport).normalized;
            if (dir.sqrMagnitude < 0.001f)
            {
                arrowViewport = new Vector2(0.5f, 0.5f);
            }
            else
            {
                float t = float.MaxValue;
                if (Mathf.Abs(dir.x) > 0.001f)
                {
                    float tx = dir.x > 0 ? (maxX - playerViewport.x) / dir.x : (minX - playerViewport.x) / dir.x;
                    if (tx > 0 && tx < t) t = tx;
                }
                if (Mathf.Abs(dir.y) > 0.001f)
                {
                    float ty = dir.y > 0 ? (maxY - playerViewport.y) / dir.y : (minY - playerViewport.y) / dir.y;
                    if (ty > 0 && ty < t) t = ty;
                }
                arrowViewport = t < float.MaxValue
                    ? new Vector2(playerViewport.x + dir.x * t, playerViewport.y + dir.y * t)
                    : new Vector2(0.5f, 0.5f);
            }
        }

        _rect.anchorMin = arrowViewport;
        _rect.anchorMax = arrowViewport;
        _rect.anchoredPosition = Vector2.zero;

        float angle = Mathf.Atan2(goalViewport.y - playerViewport.y, goalViewport.x - playerViewport.x) * Mathf.Rad2Deg;
        _rect.localRotation = Quaternion.Euler(0f, 0f, angle - 90f);
    }

    void SetVisible(bool visible)
    {
        if (_hidden == !visible) return;
        _hidden = !visible;
        if (_canvasGroup != null)
            _canvasGroup.alpha = visible ? 1f : 0f;
        else
            gameObject.SetActive(visible);
    }
}
