using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Developer admin/cheat menu. Attach to a child GameObject of your MobileUI canvas.
///
/// Show/hide:
///   - Call TogglePanel() from a Button's OnClick
///   - In Editor / Standalone: press backtick (`) key
///   - In WebGL: tap the top-right corner 5 times quickly
///
/// To add new action buttons: add a public method here, wire it to a Button in the Inspector.
/// </summary>
public class AdminMenu : MonoBehaviour
{
    [Header("Panel")]
    [Tooltip("The root GameObject of the admin panel to show/hide.")]
    [SerializeField] GameObject adminPanel;
    [SerializeField] NetworkBootstrapper bootstrapper;

    [Header("Connection Display")]
    [Tooltip("Shows the current resolved address and ports.")]
    [SerializeField] TMP_Text activeAddressText;
    [Tooltip("Fading red/green status feedback line (Iaapa pattern).")]
    [SerializeField] TMP_Text statusFeedbackText;
    [Tooltip("Label on the server toggle button — auto-updated.")]
    [SerializeField] TMP_Text serverToggleLabel;

    [Header("Debug Log")]
    [SerializeField] GameObject debugLogPanel;
    [SerializeField] TMP_Text debugLogText;
    [SerializeField] ScrollRect debugLogScroll;

    // ── Iaapa-style fading status text ───────────────────────────
    // Pattern from VideoController.cs:
    //   Color.white - (invisibleMagenta * (1-t)) → starts GREEN, fades to WHITE
    //   Color.white - (invisibleCyan    * (1-t)) → starts RED,   fades to WHITE
    float _timeSinceStatus = float.PositiveInfinity;
    bool  _statusIsError;
    readonly Color _invisibleMagenta = new Color(1f, 0f, 1f, 0f);
    readonly Color _invisibleCyan    = new Color(0f, 1f, 1f, 0f);
    const float FadeDuration = 3f;

    // ── Debug log capture ─────────────────────────────────────────
    readonly List<string> _logLines = new();
    const int MaxLogLines = 60;
    bool _logDirty;

    // ── WebGL corner-tap detection ────────────────────────────────
    int   _cornerTapCount;
    float _cornerTapTimer;
    const float CornerTapWindow  = 1.5f;
    const int   CornerTapsNeeded = 5;

    void Awake()
    {
        if (bootstrapper == null)
            bootstrapper = FindFirstObjectByType<NetworkBootstrapper>();

        adminPanel.SetActive(false);
        if (debugLogPanel != null)
            debugLogPanel.SetActive(false);

        Application.logMessageReceived += OnLogMessage;
    }

    void OnDestroy()
    {
        Application.logMessageReceived -= OnLogMessage;
    }

    void Start()
    {
        RefreshAddressDisplay();
    }

    void Update()
    {
        // ── Keyboard shortcut (editor / standalone) ───────────────
#if UNITY_EDITOR || UNITY_STANDALONE
        if (Input.GetKeyDown(KeyCode.BackQuote))
            TogglePanel();
#endif

        // ── WebGL: 5-tap top-right corner ─────────────────────────
#if UNITY_WEBGL
        _cornerTapTimer -= Time.deltaTime;
        if (_cornerTapTimer <= 0f)
            _cornerTapCount = 0;

        if (Input.touchCount > 0)
        {
            Touch t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Began)
            {
                bool inCorner = t.position.x > Screen.width  * 0.8f &&
                                t.position.y > Screen.height * 0.8f;
                if (inCorner)
                {
                    _cornerTapCount++;
                    _cornerTapTimer = CornerTapWindow;
                    if (_cornerTapCount >= CornerTapsNeeded)
                    {
                        _cornerTapCount = 0;
                        TogglePanel();
                    }
                }
            }
        }
#endif

        // ── Iaapa fading status text ───────────────────────────────
        _timeSinceStatus += Time.deltaTime;
        if (statusFeedbackText != null && _timeSinceStatus < FadeDuration)
        {
            statusFeedbackText.faceColor = _statusIsError
                ? Color.white - (_invisibleCyan    * (1f - _timeSinceStatus / FadeDuration))
                : Color.white - (_invisibleMagenta * (1f - _timeSinceStatus / FadeDuration));
        }

        // ── Rebuild debug log text ─────────────────────────────────
        if (_logDirty && debugLogText != null)
        {
            debugLogText.text = string.Join("\n", _logLines);
            _logDirty = false;
            if (debugLogScroll != null)
            {
                Canvas.ForceUpdateCanvases();
                debugLogScroll.verticalNormalizedPosition = 0f;
            }
        }
    }

    // ── Public panel controls ─────────────────────────────────────

    public void TogglePanel()
    {
        adminPanel.SetActive(!adminPanel.activeSelf);
        if (adminPanel.activeSelf)
            RefreshAddressDisplay();
    }

    public void ToggleDebugLog()
    {
        if (debugLogPanel != null)
            debugLogPanel.SetActive(!debugLogPanel.activeSelf);
    }

    // ── Connection controls ───────────────────────────────────────

    /// <summary>
    /// Flips between localhost and Edgegap, then reloads the scene.
    /// The override persists in AdminMenuPrefs (static) for the rest of the play session.
    /// </summary>
    public void ToggleServerTarget()
    {
        bool currentlyLocal = IsCurrentlyLocal();
        AdminMenuPrefs.UseLocalOverride = !currentlyLocal;

        string dest = currentlyLocal ? "Edgegap" : "Local";
        ShowStatus($"Switching to {dest} — reloading...", isError: false);
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    /// <summary>
    /// Reloads the scene, retrying the connection with current settings intact.
    /// </summary>
    public void RetryConnection()
    {
        ShowStatus("Retrying — reloading scene...", isError: false);
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    // ── Extensible action buttons ─────────────────────────────────
    // Add new public methods here and wire them to Buttons in the Inspector.

    /// <summary>
    /// Example: spawn an extra cloud platform. Wire to a Button.
    /// Replace the body with whatever admin action you need.
    /// </summary>
    public void SpawnCloud()
    {
        var cm = FindFirstObjectByType<CloudManager>();
        if (cm != null)
        {
            // Call whichever public spawn method CloudManager exposes.
            // cm.ForceSpawnCloud();
            ShowStatus("SpawnCloud: wire to CloudManager method.", isError: false);
        }
        else
        {
            ShowStatus("CloudManager not found.", isError: true);
        }
    }

    // ── Internals ─────────────────────────────────────────────────

    void RefreshAddressDisplay()
    {
        if (activeAddressText == null) return;

        if (bootstrapper == null)
        {
            activeAddressText.text = "[No Bootstrapper found]";
            return;
        }

        bool   local = IsCurrentlyLocal();
        string mode  = local ? "LOCAL" : "EDGEGAP";
        string addr  = bootstrapper.ActiveAddress;
        ushort tPort = bootstrapper.ActiveTugboatPort;
        ushort bPort = bootstrapper.ActiveBayouPort;

        activeAddressText.text =
            $"<b>[{mode}]</b>\n" +
            $"{addr}\n" +
            $"UDP  (Tugboat) : {tPort}\n" +
            $"TCP  (Bayou)   : {bPort}";

        if (serverToggleLabel != null)
            serverToggleLabel.text = local ? "Switch to Edgegap" : "Switch to Local";
    }

    bool IsCurrentlyLocal()
    {
        if (bootstrapper == null) return true;
        return bootstrapper.ActiveAddress == bootstrapper.localAddress;
    }

    /// <summary>
    /// Show a status message. Green = success, Red = error.
    /// Fades to white over FadeDuration seconds (Iaapa pattern).
    /// </summary>
    public void ShowStatus(string message, bool isError)
    {
        if (statusFeedbackText == null) return;
        statusFeedbackText.text = message;
        _statusIsError = isError;
        _timeSinceStatus = 0f;
    }

    void OnLogMessage(string condition, string stackTrace, LogType type)
    {
        string prefix = type switch
        {
            LogType.Error   or LogType.Exception => "<color=red>[ERR]</color> ",
            LogType.Warning                      => "<color=yellow>[WRN]</color> ",
            LogType.Assert                       => "<color=orange>[AST]</color> ",
            _                                    => "<color=white>[LOG]</color> "
        };
        _logLines.Add(prefix + condition);
        if (_logLines.Count > MaxLogLines)
            _logLines.RemoveAt(0);
        _logDirty = true;
    }
}

/// <summary>
/// Static runtime overrides for AdminMenu. Survives scene reloads within a play session.
/// Reset to null = use the compile-time #if UNITY_EDITOR default.
/// </summary>
public static class AdminMenuPrefs
{
    /// <summary>
    /// When non-null, overrides the compile-time local/remote selection in NetworkBootstrapper.
    /// true  = force local (localhost / localPorts)
    /// false = force Edgegap (edgegapAddress / edgegapPorts)
    /// null  = use compile-time default
    /// </summary>
    public static bool? UseLocalOverride = null;
}
