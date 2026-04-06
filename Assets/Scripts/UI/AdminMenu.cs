using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Developer admin/cheat menu. Attach to a child GameObject of your MobileUI canvas.
///
/// Show/hide:
///   - Call TogglePanel() from a Button's OnClick
///   - In Editor / Standalone: press backtick (sampleInputList) key
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

    [Header("Mute")]
    [SerializeField] TMP_Text muteButtonLabel;
    [Tooltip("Assign the AudioSource to mute. If blank, falls back to Camera.main's AudioSource.")]
    [SerializeField] AudioSource targetAudioSource;

    [Header("Edgegap Runtime Overrides")]
    [Tooltip("Input field for editing Edgegap server address at runtime.")]
    [SerializeField] TMP_InputField edgegapAddressInput;
    [Tooltip("Input field for editing Edgegap Tugboat (UDP) port at runtime.")]
    [SerializeField] TMP_InputField edgegapTugboatPortInput;
    [Tooltip("Input field for editing Edgegap Bayou (TCP/WS) port at runtime.")]
    [SerializeField] TMP_InputField edgegapBayouPortInput;

    [Header("Cloud Controls")]
    [Tooltip("Optional — auto-found at runtime if blank.")]
    [SerializeField] CloudManager cloudManager;
    [Tooltip("Optional — auto-found at runtime if blank.")]
    [SerializeField] CloudLadderController cloudLadderController;
    [Tooltip("Label on the freeze button — auto-updated when toggled.")]
    [SerializeField] TMP_Text freezeCloudsLabel;
    [Tooltip("Label on the ladder building toggle button — auto-updated when toggled.")]
    [SerializeField] TMP_Text ladderBuildingLabel;

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


    void Awake()
    {
        if (bootstrapper == null)
            bootstrapper = FindFirstObjectByType<NetworkBootstrapper>();

        // Restore panel visibility if we just reloaded from a button press.
        bool wasOpen = AdminMenuPrefs.KeepPanelOpen;
        AdminMenuPrefs.KeepPanelOpen = false;
        adminPanel.SetActive(wasOpen);
        if (wasOpen) PopulateEdgegapInputs();

        if (debugLogPanel != null)
            debugLogPanel.SetActive(false);

        Application.logMessageReceived += OnLogMessage;
    }

    void OnDestroy()
    {
        Application.logMessageReceived -= OnLogMessage;
    }

    void Update()
    {
        // ── Keyboard shortcut (editor / standalone) ───────────────
#if UNITY_EDITOR || UNITY_STANDALONE
        if (Keyboard.current != null && Keyboard.current.backquoteKey.wasPressedThisFrame)
            TogglePanel();
#endif

        // ── Iaapa fading status text ───────────────────────────────
        _timeSinceStatus += Time.deltaTime;
        if (statusFeedbackText != null && _timeSinceStatus < FadeDuration)
        {
            statusFeedbackText.faceColor = _statusIsError
                ? Color.white - (_invisibleCyan    * (1f - _timeSinceStatus / FadeDuration))
                : Color.white - (_invisibleMagenta * (1f - _timeSinceStatus / FadeDuration));
        }

        // ── Live address display (only while panel is open) ───────
        if (adminPanel.activeSelf)
            RefreshAddressDisplay();

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
            PopulateEdgegapInputs();
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
        FlushEdgegapInputs();
        bool currentlyLocal = IsCurrentlyLocal();
        AdminMenuPrefs.UseLocalOverride  = !currentlyLocal;
        AdminMenuPrefs.KeepPanelOpen    = true;
        AdminMenuPrefs.AttemptConnection = true;

        string dest = currentlyLocal ? "Edgegap" : "Local";
        ShowStatus($"Switching to {dest} — reloading...", isError: false);
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    /// <summary>
    /// Reloads the scene, retrying the connection with current settings intact.
    /// </summary>
    public void RetryConnection()
    {
        FlushEdgegapInputs();
        AdminMenuPrefs.KeepPanelOpen    = true;
        AdminMenuPrefs.AttemptConnection = true;
        ShowStatus("Retrying — reloading scene...", isError: false);
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    // ── Audio ─────────────────────────────────────────────────────

    public void ToggleMute()
    {
        AudioSource audio = targetAudioSource != null
            ? targetAudioSource
            : Camera.main?.GetComponent<AudioSource>();
        if (audio == null)
        {
            ShowStatus("No AudioSource found — assign one to Target Audio Source.", isError: true);
            return;
        }
        audio.mute = !audio.mute;
        if (muteButtonLabel != null)
            muteButtonLabel.text = audio.mute ? "Unmute" : "Mute";
        ShowStatus(audio.mute ? "Muted." : "Unmuted.", isError: false);
    }

    // ── Cloud action buttons ──────────────────────────────────────
    // Wire each method to its Button's OnClick in the Inspector.

    CloudManager GetCloudManager() =>
        cloudManager != null ? cloudManager : cloudManager = FindFirstObjectByType<CloudManager>();

    CloudLadderController GetLadderController() =>
        cloudLadderController != null ? cloudLadderController : cloudLadderController = FindFirstObjectByType<CloudLadderController>();

    /// <summary>Freeze or resume all cloud movement. Button label auto-updates.</summary>
    public void ToggleFreezeClouds()
    {
        var cm = GetCloudManager();
        if (cm == null) { ShowStatus("CloudManager not found.", isError: true); return; }
        cm.ToggleCloudFreeze();
        bool frozen = cm.CloudsFrozen;
        if (freezeCloudsLabel != null) freezeCloudsLabel.text = frozen ? "Resume Clouds" : "Freeze Clouds";
        ShowStatus(frozen ? "Clouds frozen." : "Clouds resumed.", isError: false);
    }

    /// <summary>Flip the travel direction of every active cloud lane.</summary>
    public void ReverseCloudDirections()
    {
        var cm = GetCloudManager();
        if (cm == null) { ShowStatus("CloudManager not found.", isError: true); return; }
        cm.ReverseAllLaneSpeeds();
        ShowStatus("Cloud directions reversed.", isError: false);
    }

    /// <summary>Enable or disable the CloudLadderController (stops building new ladders and removes existing ones).</summary>
    public void ToggleLadderBuilding()
    {
        var lc = GetLadderController();
        if (lc == null) { ShowStatus("CloudLadderController not found.", isError: true); return; }
        lc.enabled = !lc.enabled;
        bool active = lc.enabled;
        if (ladderBuildingLabel != null) ladderBuildingLabel.text = active ? "Stop Ladders" : "Start Ladders";
        ShowStatus(active ? "Ladder building enabled." : "Ladder building stopped.", isError: false);
    }

    // ── Edgegap input field handlers ─────────────────────────────
    // Wire each TMP_InputField's OnEndEdit event to the matching method.

    // Called before every scene reload to capture whatever is currently typed,
    // even if OnEndEdit hasn't fired yet (e.g. button clicked without tabbing away).
    void FlushEdgegapInputs()
    {
        if (edgegapAddressInput != null)
            OnEdgegapAddressEndEdit(edgegapAddressInput.text);
        if (edgegapTugboatPortInput != null)
            OnEdgegapTugboatPortEndEdit(edgegapTugboatPortInput.text);
        if (edgegapBayouPortInput != null)
            OnEdgegapBayouPortEndEdit(edgegapBayouPortInput.text);
    }

    public void OnEdgegapAddressEndEdit(string value)
    {
        AdminMenuPrefs.EdgegapAddressOverride = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        ShowStatus(string.IsNullOrWhiteSpace(value) ? "Address cleared — using Inspector value." : $"Address set: {value.Trim()}", isError: false);
    }

    public void OnEdgegapTugboatPortEndEdit(string value)
    {
        if (ushort.TryParse(value.Trim(), out ushort port) && port > 0)
        {
            AdminMenuPrefs.EdgegapTugboatPortOverride = port;
            ShowStatus($"Tugboat port set: {port}", isError: false);
        }
        else
        {
            ShowStatus($"Invalid Tugboat port \"{value}\" — must be 1–65535.", isError: true);
            if (edgegapTugboatPortInput != null)
                edgegapTugboatPortInput.text = (AdminMenuPrefs.EdgegapTugboatPortOverride ?? bootstrapper?.edgegapTugboatPort ?? 0).ToString();
        }
    }

    public void OnEdgegapBayouPortEndEdit(string value)
    {
        if (ushort.TryParse(value.Trim(), out ushort port) && port > 0)
        {
            AdminMenuPrefs.EdgegapBayouPortOverride = port;
            ShowStatus($"Bayou port set: {port}", isError: false);
        }
        else
        {
            ShowStatus($"Invalid Bayou port \"{value}\" — must be 1–65535.", isError: true);
            if (edgegapBayouPortInput != null)
                edgegapBayouPortInput.text = (AdminMenuPrefs.EdgegapBayouPortOverride ?? bootstrapper?.edgegapBayouPort ?? 0).ToString();
        }
    }

    // ── Internals ─────────────────────────────────────────────────

    // Populates the input fields with current effective values (override > inspector).
    // Call when panel opens so fields show the right starting values.
    void PopulateEdgegapInputs()
    {
        if (bootstrapper == null) return;
        if (edgegapAddressInput != null)
            edgegapAddressInput.text    = AdminMenuPrefs.EdgegapAddressOverride
                                          ?? bootstrapper.edgegapAddress;
        if (edgegapTugboatPortInput != null)
            edgegapTugboatPortInput.text = (AdminMenuPrefs.EdgegapTugboatPortOverride
                                           ?? bootstrapper.edgegapTugboatPort).ToString();
        if (edgegapBayouPortInput != null)
            edgegapBayouPortInput.text   = (AdminMenuPrefs.EdgegapBayouPortOverride
                                           ?? bootstrapper.edgegapBayouPort).ToString();
    }

    void RefreshAddressDisplay()
    {
        if (activeAddressText == null) return;
        if (bootstrapper == null)
        {
            activeAddressText.text = "[No Bootstrapper found]";
            return;
        }

        bool local = IsCurrentlyLocal();

        // Show effective values: AdminMenuPrefs override takes priority over inspector fields.
        string bayouAddr   = local ? bootstrapper.localAddress
                                   : (AdminMenuPrefs.EdgegapAddressOverride ?? bootstrapper.edgegapAddress);
        string tugboatAddr = local ? bootstrapper.localAddress
                                   : (AdminMenuPrefs.EdgegapAddressOverride
                                      ?? (string.IsNullOrWhiteSpace(bootstrapper.edgegapTugboatAddress)
                                          ? bootstrapper.edgegapAddress
                                          : bootstrapper.edgegapTugboatAddress));
        ushort tPort = local ? bootstrapper.localTugboatPort
                             : (AdminMenuPrefs.EdgegapTugboatPortOverride ?? bootstrapper.edgegapTugboatPort);
        ushort bPort = local ? bootstrapper.localBayouPort
                             : (AdminMenuPrefs.EdgegapBayouPortOverride ?? bootstrapper.edgegapBayouPort);

        if (!local && string.IsNullOrWhiteSpace(bayouAddr))
            bayouAddr = "<i>(edgegapAddress not set)</i>";

        activeAddressText.text =
            $"<b>[{(local ? "LOCAL" : "EDGEGAP")}]</b>\n" +
            $"WSS  (Bayou)   : {bayouAddr}:{bPort}\n" +
            $"UDP  (Tugboat) : {tugboatAddr}:{tPort}";

        if (serverToggleLabel != null)
            serverToggleLabel.text = local ? "Switch to Edgegap" : "Switch to Local";
    }

    bool IsCurrentlyLocal()
    {
        // If the admin menu has set an override, that's the authoritative answer.
        if (AdminMenuPrefs.UseLocalOverride.HasValue)
            return AdminMenuPrefs.UseLocalOverride.Value;

        // Otherwise fall back to the compile-time default.
#if UNITY_EDITOR || UNITY_STANDALONE_OSX || UNITY_SERVER
        return true;
#else
        return false;
#endif
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

    /// <summary>
    /// When true, AdminMenu reopens itself after the next scene reload.
    /// Reset to false after reading in Awake.
    /// </summary>
    public static bool KeepPanelOpen = false;

    /// <summary>
    /// WebGL starts offline by default. Set true before a scene reload to allow
    /// NetworkBootstrapper to attempt a server connection on the next Start().
    /// </summary>
    public static bool AttemptConnection = false;

    // Edgegap runtime overrides — edited via the admin menu input fields.
    // null = use the inspector field value on NetworkBootstrapper.
    public static string  EdgegapAddressOverride      = null;
    public static ushort? EdgegapTugboatPortOverride  = null;
    public static ushort? EdgegapBayouPortOverride    = null;
}
