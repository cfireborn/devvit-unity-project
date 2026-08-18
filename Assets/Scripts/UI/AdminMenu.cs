using System;
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
///   - In Editor / Standalone: press backtick — handled by <see cref="GameUIManager"/> (same object tree as mobile UI)
///   - In WebGL: tap the top-right corner 5 times quickly (detected in <see cref="MobileInputManager"/>, opens via <see cref="GameUIManager"/>)
///
/// To add new action buttons: add a public method here, wire it to a Button in the Inspector.
/// </summary>
public class AdminMenu : MonoBehaviour
{
    public enum StoryStage
    {
        BeforeGray = 0,
        FirstLetterActive = 1,
        ReturnLetterActive = 2,
        Ending = 3,
        CompersionTitle = 4
    }

    public enum StoryTeleportAnchor
    {
        Spawn,
        Gray,
        Spike,
        Ending
    }

    [Serializable]
    public sealed class StoryCheckpointDefinition
    {
        public string label;
        public StoryStage stage;
        public StoryTeleportAnchor teleportAnchor;
        [Tooltip("World-space offset from the resolved marker. Keep approach checkpoints outside auto-enter trigger colliders.")]
        public Vector2 teleportOffset;

        public StoryCheckpointDefinition(string label, StoryStage stage, StoryTeleportAnchor teleportAnchor, Vector2 teleportOffset)
        {
            this.label = label;
            this.stage = stage;
            this.teleportAnchor = teleportAnchor;
            this.teleportOffset = teleportOffset;
        }
    }

    [Header("Panel")]
    [Tooltip("The root GameObject of the admin panel to show/hide.")]
    [SerializeField] GameObject adminPanel;
    [Tooltip("Optional authored close button. When blank, a top-right close button is created at runtime.")]
    [SerializeField] Button closeAdminPanelButton;
    [SerializeField] TMP_Text versionText;
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

    [Header("Story Checkpoints")]
    [Tooltip("Ordered debugger snapshots used by Previous/Apply/Next. Stage determines exact trigger and goal state; anchor + offset determine the local-player teleport.")]
    [SerializeField] StoryCheckpointDefinition[] storyCheckpoints =
    {
        new("Spawn - Before Gray", StoryStage.BeforeGray, StoryTeleportAnchor.Spawn, Vector2.zero),
        new("Gray - Letter for Spike", StoryStage.FirstLetterActive, StoryTeleportAnchor.Gray, new Vector2(1.25f, 0f)),
        new("Spike - Before delivery", StoryStage.FirstLetterActive, StoryTeleportAnchor.Spike, new Vector2(-1.25f, 0f)),
        new("Spike - COMPERSION title", StoryStage.CompersionTitle, StoryTeleportAnchor.Spike, new Vector2(1.25f, 0f)),
        new("Spike - Reply for Gray", StoryStage.ReturnLetterActive, StoryTeleportAnchor.Spike, new Vector2(1.25f, 0f)),
        new("Gray - Before return", StoryStage.ReturnLetterActive, StoryTeleportAnchor.Gray, new Vector2(-1.25f, 0f)),
        new("Ending - Thank-you UI", StoryStage.Ending, StoryTeleportAnchor.Ending, new Vector2(1.25f, 0f))
    };
    [Tooltip("Optional scene marker override. Falls back to NetworkPlayerSpawner.SpawnPoint.")]
    [SerializeField] Transform storySpawnMarker;
    [Tooltip("Optional platform-relative marker override. Falls back to Gray's opening trigger.")]
    [SerializeField] Transform storyGrayMarker;
    [Tooltip("Optional platform-relative marker override. Falls back to Spike's first completion trigger.")]
    [SerializeField] Transform storySpikeMarker;
    [Tooltip("Optional ending marker override. Falls back to the Gray marker.")]
    [SerializeField] Transform storyEndingMarker;
    [Header("Story Checkpoint UI (all optional; runtime fallback when incomplete)")]
    [SerializeField] GameObject storyCheckpointControlsRoot;
    [SerializeField] TMP_Text storyCheckpointLabel;
    [SerializeField] Button previousStoryCheckpointButton;
    [SerializeField] Button applyStoryCheckpointButton;
    [SerializeField] Button nextStoryCheckpointButton;

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
    const int MaxLogLines = 200;
    const float DebugLogBottomThreshold = 0.01f;
    bool _logDirty;
    bool _followNewestLog = true;
    bool _scrollToNewestWhenVisible;
    bool _debugLogPositionInitialized;
    int _storyCheckpointIndex;
    bool _storyCheckpointAppliedThisSession;
    TMP_Text _storyCheckpointLabel;
    Button _previousStoryCheckpointButton;
    Button _nextStoryCheckpointButton;
    Button _adminCloseButton;
    bool _usingAuthoredStoryCheckpointControls;
    public bool IsOpen => adminPanel != null && adminPanel.activeSelf;

    sealed class StorySpine
    {
        public DialogueTrigger grayOpening;
        public GoalAssignmentTrigger firstAssignment;
        public GoalCompletionTrigger spikeCompletion;
        public DialogueTrigger compersionTitle;
        public DialogueTrigger spikeReply;
        public GoalAssignmentTrigger returnAssignment;
        public GoalCompletionTrigger grayCompletion;
        public DialogueTrigger grayReturn;
        public GameServices gameServices;
        public GameUIManager gameUI;
    }


    void Awake()
    {
        TextAsset version = Resources.Load<TextAsset>("BuildVersion");
        if (versionText != null)
            versionText.text = version != null ? version.text.Trim() : Application.version;

        if (bootstrapper == null)
            bootstrapper = FindFirstObjectByType<NetworkBootstrapper>();

        // Restore panel visibility if we just reloaded from a button press.
        bool wasOpen = AdminMenuPrefs.KeepPanelOpen;
        AdminMenuPrefs.KeepPanelOpen = false;
        adminPanel.SetActive(wasOpen);
        if (wasOpen) PopulateEdgegapInputs();

        if (debugLogPanel != null)
            debugLogPanel.SetActive(wasOpen);

        EnsureAdminCloseButton();
        EnsureStoryCheckpointControls();
        if (wasOpen)
            BringToFrontIfOpen();

        Application.logMessageReceived += OnLogMessage;
    }

    void OnDestroy()
    {
        Application.logMessageReceived -= OnLogMessage;
        if (_adminCloseButton != null)
            _adminCloseButton.onClick.RemoveListener(ClosePanel);
        if (_usingAuthoredStoryCheckpointControls)
        {
            previousStoryCheckpointButton.onClick.RemoveListener(PreviousStoryCheckpoint);
            applyStoryCheckpointButton.onClick.RemoveListener(ApplySelectedStoryCheckpoint);
            nextStoryCheckpointButton.onClick.RemoveListener(NextStoryCheckpoint);
        }
    }

    void Update()
    {
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

        bool debugScrollIsVisible = debugLogScroll != null
            && debugLogScroll.gameObject.activeInHierarchy;

        // Remember the user's position even while the panel is later hidden.
        // A pending auto-scroll owns the position until its post-layout update.
        if (debugScrollIsVisible && !_scrollToNewestWhenVisible)
            CaptureDebugLogFollowState();

        // ── Rebuild debug log text ─────────────────────────────────
        if (_logDirty && debugLogText != null)
        {
            debugLogText.text = string.Join("\n", _logLines);
            _logDirty = false;
            if (debugLogScroll != null && _followNewestLog)
                _scrollToNewestWhenVisible = true;
        }

        // Inactive ContentSizeFitters do not rebuild reliable bounds. Defer the
        // jump until the panel is visible, then apply it after layout updates.
        if (_scrollToNewestWhenVisible && debugScrollIsVisible)
        {
            Canvas.ForceUpdateCanvases();
            debugLogScroll.verticalNormalizedPosition = 0f;
            _scrollToNewestWhenVisible = false;
            _debugLogPositionInitialized = true;
        }
    }

    // ── Public panel controls ─────────────────────────────────────

    public void TogglePanel()
    {
        bool opening = !adminPanel.activeSelf;
        if (!opening)
        {
            ClosePanel();
            return;
        }

        if (_logDirty && _followNewestLog)
            _scrollToNewestWhenVisible = true;

        adminPanel.SetActive(true);
        if (!_storyCheckpointAppliedThisSession)
            SelectCheckpointFromCurrentProgress();
        BringToFrontIfOpen();
        PopulateEdgegapInputs();
        if (debugLogPanel != null)
            debugLogPanel.SetActive(true);
    }

    /// <summary>Close the debugger while preserving the user's debug-log scroll-follow state.</summary>
    public void ClosePanel()
    {
        if (adminPanel == null || !adminPanel.activeSelf) return;
        CaptureDebugLogFollowState();
        adminPanel.SetActive(false);
    }

    /// <summary>
    /// Places the admin panel in its own high-sorting nested canvas so the ending overlay cannot cover or intercept it.
    /// </summary>
    public void BringToFrontIfOpen()
    {
        if (adminPanel == null || !adminPanel.activeInHierarchy) return;

        Canvas priorityCanvas = adminPanel.GetComponent<Canvas>();
        if (priorityCanvas == null)
            priorityCanvas = adminPanel.AddComponent<Canvas>();
        priorityCanvas.overrideSorting = true;
        priorityCanvas.sortingOrder = 32000;
        if (adminPanel.GetComponent<GraphicRaycaster>() == null)
            adminPanel.AddComponent<GraphicRaycaster>();
        adminPanel.transform.SetAsLastSibling();
    }

    // ── Admin/story controls ─────────────────────────────────────

    void EnsureAdminCloseButton()
    {
        if (adminPanel == null || _adminCloseButton != null) return;

        if (closeAdminPanelButton != null)
        {
            _adminCloseButton = closeAdminPanelButton;
            _adminCloseButton.onClick.AddListener(ClosePanel);
            return;
        }

        var buttonObject = new GameObject("AdminCloseButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(adminPanel.transform, false);
        var rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.one;
        rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one;
        rect.sizeDelta = new Vector2(34f, 34f);
        rect.anchoredPosition = new Vector2(-8f, -8f);
        buttonObject.GetComponent<Image>().color = new Color(0.3f, 0.35f, 0.45f, 1f);
        _adminCloseButton = buttonObject.GetComponent<Button>();
        _adminCloseButton.onClick.AddListener(ClosePanel);

        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(buttonObject.transform, false);
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        var label = labelObject.GetComponent<TextMeshProUGUI>();
        label.text = "X";
        label.fontSize = 18f;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null)
            label.font = TMP_Settings.defaultFontAsset;
    }

    public void PreviousStoryCheckpoint()
    {
        if (storyCheckpoints == null || storyCheckpoints.Length == 0) return;
        _storyCheckpointIndex = Mathf.Max(0, _storyCheckpointIndex - 1);
        ApplySelectedStoryCheckpoint();
    }

    public void NextStoryCheckpoint()
    {
        if (storyCheckpoints == null || storyCheckpoints.Length == 0) return;
        _storyCheckpointIndex = Mathf.Min(storyCheckpoints.Length - 1, _storyCheckpointIndex + 1);
        ApplySelectedStoryCheckpoint();
    }

    public void ApplySelectedStoryCheckpoint()
    {
        if (storyCheckpoints == null || storyCheckpoints.Length == 0)
        {
            ShowStatus("No story checkpoints configured.", isError: true);
            return;
        }

        _storyCheckpointIndex = Mathf.Clamp(_storyCheckpointIndex, 0, storyCheckpoints.Length - 1);
        StoryCheckpointDefinition checkpoint = storyCheckpoints[_storyCheckpointIndex];
        if (checkpoint == null)
        {
            ShowStatus($"Story checkpoint {_storyCheckpointIndex + 1} is null.", isError: true);
            return;
        }
        if (!TryResolveStorySpine(out StorySpine spine, out string error))
        {
            ShowStatus(error, isError: true);
            return;
        }

        PlayerControllerM player = spine.gameServices.GetPlayer();
        if (player == null || !player.enabled)
        {
            ShowStatus("Local player is not ready yet; wait for spawn and retry.", isError: true);
            return;
        }

        Transform marker = ResolveStoryTeleportMarker(checkpoint.teleportAnchor, spine);
        if (marker == null)
        {
            ShowStatus($"No teleport marker resolved for {checkpoint.teleportAnchor}.", isError: true);
            return;
        }

        bool afterGray = checkpoint.stage != StoryStage.BeforeGray;
        bool afterSpikeDelivery = checkpoint.stage == StoryStage.CompersionTitle
            || checkpoint.stage == StoryStage.ReturnLetterActive
            || checkpoint.stage == StoryStage.Ending;
        bool afterSpikeReply = checkpoint.stage == StoryStage.ReturnLetterActive
            || checkpoint.stage == StoryStage.Ending;
        bool atEnding = checkpoint.stage == StoryStage.Ending;

        Goal activeGoal = null;
        int completedGoals = 0;
        if (checkpoint.stage == StoryStage.FirstLetterActive)
            activeGoal = spine.firstAssignment.PrepareGoalForCheckpoint();
        else if (checkpoint.stage == StoryStage.ReturnLetterActive)
        {
            activeGoal = spine.returnAssignment.PrepareGoalForCheckpoint();
            completedGoals = 1;
        }
        else if (checkpoint.stage == StoryStage.CompersionTitle)
            completedGoals = 1;
        else if (atEnding)
            completedGoals = 2;

        if ((checkpoint.stage == StoryStage.FirstLetterActive || checkpoint.stage == StoryStage.ReturnLetterActive)
            && activeGoal == null)
        {
            ShowStatus("Checkpoint goal could not be prepared; story scene wiring is incomplete.", isError: true);
            return;
        }

        // Apply a snapshot transaction. Never replay the authored UnityEvent chain: doing so would
        // run dialogue, animation delays, and goal-completion side effects during backward travel.
        // All fallible resolution/preparation happens above so a failed checkpoint leaves play state intact.
        spine.gameUI.CloseTransientUiForStoryCheckpoint();
        spine.gameUI.ResetEndOfDemoForStoryCheckpoint();

        spine.grayOpening.ApplyCheckpointActivationState(afterGray, componentEnabled: true);
        spine.firstAssignment.ApplyCheckpointActivationState(afterGray, componentEnabled: !afterGray);
        spine.spikeCompletion.ApplyCheckpointActivationState(afterSpikeDelivery, componentEnabled: true);
        spine.compersionTitle.ApplyCheckpointActivationState(afterSpikeDelivery, componentEnabled: false);
        spine.spikeReply.ApplyCheckpointActivationState(afterSpikeReply, componentEnabled: false);
        spine.returnAssignment.ApplyCheckpointActivationState(afterSpikeReply, componentEnabled: !afterSpikeReply);
        spine.grayCompletion.ApplyCheckpointActivationState(atEnding, componentEnabled: true);
        spine.grayReturn.ApplyCheckpointActivationState(atEnding, componentEnabled: false);

        player.ApplyStoryCheckpointGoals(activeGoal, completedGoals);
        Vector3 destination = marker.position + (Vector3)checkpoint.teleportOffset;
        player.ResetForRespawn(destination);
        Physics2D.SyncTransforms();

        if (atEnding)
            spine.gameUI.ShowEndOfDemo();
        else if (checkpoint.stage == StoryStage.CompersionTitle)
            spine.compersionTitle.ShowDialogue();

        _storyCheckpointAppliedThisSession = true;
        UpdateStoryCheckpointControls();
        BringToFrontIfOpen();
        ShowStatus($"Story {_storyCheckpointIndex + 1}/{storyCheckpoints.Length}: {checkpoint.label}", isError: false);
    }

    bool TryResolveStorySpine(out StorySpine spine, out string error)
    {
        spine = new StorySpine
        {
            gameServices = FindFirstObjectByType<GameServices>(),
            gameUI = GameUIManager.Instance != null ? GameUIManager.Instance : FindFirstObjectByType<GameUIManager>(),
            grayOpening = FindDialogueTrigger("KoiTutorialDialogue"),
            compersionTitle = FindDialogueTrigger("CompersionTitleDialogue"),
            spikeReply = FindDialogueTrigger("SpikeTutorialDialogue_1"),
            grayReturn = FindDialogueTrigger("GrayReturnDialogue"),
            firstAssignment = FindGoalAssignment("Deliver Gray's Letter to Spike", "Deliver Letter to Spike"),
            returnAssignment = FindGoalAssignment("Return Spike's Reply to Gray")
        };
        spine.spikeCompletion = spine.firstAssignment != null ? spine.firstAssignment.completionTrigger : null;
        spine.grayCompletion = spine.returnAssignment != null ? spine.returnAssignment.completionTrigger : null;

        var missing = new List<string>();
        if (spine.gameServices == null) missing.Add("GameServices");
        if (spine.gameUI == null) missing.Add("GameUIManager");
        if (spine.grayOpening == null) missing.Add("Gray opening dialogue");
        if (spine.firstAssignment == null) missing.Add("first letter assignment");
        if (spine.spikeCompletion == null) missing.Add("Spike completion");
        if (spine.compersionTitle == null) missing.Add("COMPERSION title");
        if (spine.spikeReply == null) missing.Add("Spike reply");
        if (spine.returnAssignment == null) missing.Add("return letter assignment");
        if (spine.grayCompletion == null) missing.Add("Gray return completion");
        if (spine.grayReturn == null) missing.Add("Gray return dialogue");

        error = missing.Count == 0
            ? null
            : $"Story checkpoint unavailable; missing {string.Join(", ", missing)}.";
        return missing.Count == 0;
    }

    static DialogueTrigger FindDialogueTrigger(string dialogueAssetName)
    {
        foreach (DialogueTrigger trigger in FindObjectsByType<DialogueTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (trigger.dialogueInstance != null
                && string.Equals(trigger.dialogueInstance.name, dialogueAssetName, StringComparison.Ordinal))
                return trigger;
        }
        return null;
    }

    static GoalAssignmentTrigger FindGoalAssignment(params string[] displayNames)
    {
        GoalAssignmentTrigger[] assignments = FindObjectsByType<GoalAssignmentTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        // Display-name arguments are ordered by preference. Search all components for the
        // scene-specific name before accepting a legacy prefab fallback with a similar route.
        foreach (string displayName in displayNames)
        {
            foreach (GoalAssignmentTrigger assignment in assignments)
            {
                if (string.Equals(assignment.generatedGoalDisplayName?.Trim(), displayName, StringComparison.Ordinal))
                    return assignment;
            }
        }
        return null;
    }

    Transform ResolveStoryTeleportMarker(StoryTeleportAnchor anchor, StorySpine spine)
    {
        switch (anchor)
        {
            case StoryTeleportAnchor.Spawn:
                if (storySpawnMarker != null) return storySpawnMarker;
                NetworkPlayerSpawner spawner = FindFirstObjectByType<NetworkPlayerSpawner>();
                return spawner != null ? spawner.SpawnPoint : null;
            case StoryTeleportAnchor.Gray:
                return storyGrayMarker != null ? storyGrayMarker : spine.grayOpening.transform;
            case StoryTeleportAnchor.Spike:
                return storySpikeMarker != null ? storySpikeMarker : spine.spikeCompletion.transform;
            case StoryTeleportAnchor.Ending:
                if (storyEndingMarker != null) return storyEndingMarker;
                return storyGrayMarker != null ? storyGrayMarker : spine.grayOpening.transform;
            default:
                return null;
        }
    }

    void SelectCheckpointFromCurrentProgress()
    {
        GameServices services = FindFirstObjectByType<GameServices>();
        PlayerControllerM player = services != null ? services.GetPlayer() : null;
        if (player == null || storyCheckpoints == null || storyCheckpoints.Length == 0)
        {
            UpdateStoryCheckpointControls();
            return;
        }

        // Completed=1 with no active goal also exists during Spike's reply and its
        // one-frame assignment handoff; only the live card identifies the title phase.
        StoryStage inferredStage = GameUIManager.Instance != null && GameUIManager.Instance.IsCompersionTitleCardShowing
            ? StoryStage.CompersionTitle
            : player.CompletedGoalsCount >= 2
                ? StoryStage.Ending
                : player.CompletedGoalsCount >= 1
                    ? StoryStage.ReturnLetterActive
                    : player.Goals.Count > 0 ? StoryStage.FirstLetterActive : StoryStage.BeforeGray;
        for (int i = 0; i < storyCheckpoints.Length; i++)
        {
            if (storyCheckpoints[i] != null && storyCheckpoints[i].stage == inferredStage)
            {
                _storyCheckpointIndex = i;
                break;
            }
        }
        UpdateStoryCheckpointControls();
    }

    void EnsureStoryCheckpointControls()
    {
        if (adminPanel == null || _storyCheckpointLabel != null) return;

        if (storyCheckpointLabel != null
            && previousStoryCheckpointButton != null
            && applyStoryCheckpointButton != null
            && nextStoryCheckpointButton != null)
        {
            _storyCheckpointLabel = storyCheckpointLabel;
            _previousStoryCheckpointButton = previousStoryCheckpointButton;
            _nextStoryCheckpointButton = nextStoryCheckpointButton;
            previousStoryCheckpointButton.onClick.AddListener(PreviousStoryCheckpoint);
            applyStoryCheckpointButton.onClick.AddListener(ApplySelectedStoryCheckpoint);
            nextStoryCheckpointButton.onClick.AddListener(NextStoryCheckpoint);
            if (storyCheckpointControlsRoot != null)
                storyCheckpointControlsRoot.SetActive(true);
            _usingAuthoredStoryCheckpointControls = true;
            UpdateStoryCheckpointControls();
            return;
        }

        if (storyCheckpointControlsRoot != null)
            storyCheckpointControlsRoot.SetActive(false);

        var root = new GameObject("StoryCheckpointControls", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        root.transform.SetParent(adminPanel.transform, false);
        var rootRect = root.GetComponent<RectTransform>();
        // Keep the fallback at a stable panel-local position. Bottom anchoring makes
        // panel-height adjustments slide this row into the version/debug text below it.
        rootRect.anchorMin = new Vector2(0f, 0.5f);
        rootRect.anchorMax = new Vector2(1f, 0.5f);
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.sizeDelta = new Vector2(-20f, 88f);
        rootRect.anchoredPosition = new Vector2(0f, -145f);
        root.GetComponent<Image>().color = new Color(0.08f, 0.1f, 0.14f, 0.92f);

        var labelObject = new GameObject("CheckpointLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(root.transform, false);
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 0.48f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.offsetMin = new Vector2(8f, 0f);
        labelRect.offsetMax = new Vector2(-8f, -3f);
        _storyCheckpointLabel = labelObject.GetComponent<TextMeshProUGUI>();
        _storyCheckpointLabel.fontSize = 13f;
        _storyCheckpointLabel.enableAutoSizing = true;
        _storyCheckpointLabel.fontSizeMin = 9f;
        _storyCheckpointLabel.fontSizeMax = 14f;
        _storyCheckpointLabel.alignment = TextAlignmentOptions.Center;
        if (TMP_Settings.defaultFontAsset != null)
            _storyCheckpointLabel.font = TMP_Settings.defaultFontAsset;

        _previousStoryCheckpointButton = BuildStoryButton(root.transform, "PreviousButton", "< Previous", new Vector2(0.01f, 0.04f), new Vector2(0.33f, 0.45f), PreviousStoryCheckpoint);
        BuildStoryButton(root.transform, "ApplyButton", "Apply", new Vector2(0.34f, 0.04f), new Vector2(0.66f, 0.45f), ApplySelectedStoryCheckpoint);
        _nextStoryCheckpointButton = BuildStoryButton(root.transform, "NextButton", "Next >", new Vector2(0.67f, 0.04f), new Vector2(0.99f, 0.45f), NextStoryCheckpoint);
        UpdateStoryCheckpointControls();
    }

    static Button BuildStoryButton(Transform parent, string objectName, string label, Vector2 anchorMin, Vector2 anchorMax, UnityEngine.Events.UnityAction action)
    {
        var buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        var rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        buttonObject.GetComponent<Image>().color = new Color(0.24f, 0.34f, 0.48f, 1f);
        Button button = buttonObject.GetComponent<Button>();
        button.onClick.AddListener(action);

        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(buttonObject.transform, false);
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        var text = labelObject.GetComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 12f;
        text.alignment = TextAlignmentOptions.Center;
        if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;
        return button;
    }

    void UpdateStoryCheckpointControls()
    {
        int count = storyCheckpoints != null ? storyCheckpoints.Length : 0;
        if (count > 0)
            _storyCheckpointIndex = Mathf.Clamp(_storyCheckpointIndex, 0, count - 1);
        if (_storyCheckpointLabel != null)
        {
            string label = count > 0 && storyCheckpoints[_storyCheckpointIndex] != null
                ? storyCheckpoints[_storyCheckpointIndex].label
                : "No story checkpoints";
            _storyCheckpointLabel.text = count > 0
                ? $"Story {_storyCheckpointIndex + 1}/{count}: {label}"
                : label;
        }
        if (_previousStoryCheckpointButton != null)
            _previousStoryCheckpointButton.interactable = count > 0 && _storyCheckpointIndex > 0;
        if (_nextStoryCheckpointButton != null)
            _nextStoryCheckpointButton.interactable = count > 0 && _storyCheckpointIndex < count - 1;
    }

    public void ToggleDebugLog()
    {
        if (debugLogPanel != null)
        {
            bool opening = !debugLogPanel.activeSelf;
            if (!opening)
                CaptureDebugLogFollowState();
            else if (_logDirty && _followNewestLog)
                _scrollToNewestWhenVisible = true;
            debugLogPanel.SetActive(opening);
        }
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

    void CaptureDebugLogFollowState()
    {
        if (debugLogScroll != null
            && debugLogScroll.gameObject.activeInHierarchy
            && _debugLogPositionInitialized
            && !_scrollToNewestWhenVisible)
        {
            _followNewestLog = debugLogScroll.verticalNormalizedPosition <= DebugLogBottomThreshold;
        }
    }

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
                                   : (string.IsNullOrWhiteSpace(bootstrapper.edgegapTugboatAddress)
                                      ? bootstrapper.edgegapAddress
                                      : bootstrapper.edgegapTugboatAddress);
        ushort tPort = local ? bootstrapper.localTugboatPort
                             : (AdminMenuPrefs.EdgegapTugboatPortOverride ?? bootstrapper.edgegapTugboatPort);
        ushort bPort = local ? bootstrapper.localBayouPort
                             : (AdminMenuPrefs.EdgegapBayouPortOverride ?? bootstrapper.edgegapBayouPort);

        if (!local && string.IsNullOrWhiteSpace(bayouAddr))
            bayouAddr = "<i>(edgegapAddress not set)</i>";

        activeAddressText.text =
            $"<b>[{(local ? "LOCAL" : "EDGEGAP")}]</b>\n" +
            $"Web / mobile (WSS): {bayouAddr}:{bPort}\n" +
            $"Editor / desktop (UDP): {tugboatAddr}:{tPort}";

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
        _logLines.Add($"[{System.DateTime.Now:HH:mm:ss}] {prefix}{condition}");
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
    /// WebGL connects automatically. Set false to force offline mode for testing.
    /// </summary>
    public static bool AttemptConnection = true;

    // Edgegap runtime overrides — edited via the admin menu input fields.
    // null = use the inspector field value on NetworkBootstrapper.
    public static string  EdgegapAddressOverride      = null;
    public static ushort? EdgegapTugboatPortOverride  = null;
    public static ushort? EdgegapBayouPortOverride    = null;
}
