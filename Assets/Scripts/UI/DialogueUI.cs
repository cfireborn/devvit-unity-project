using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Dialogue popup with character portrait and text. Advance via Input Action (e.g. UI/Submit) or Space/Enter fallback.
/// </summary>
public class DialogueUI : MonoBehaviour
{
    [Header("References")]
    public GameObject dialoguePanel;
    public Image characterPortrait;
    public TMP_Text dialogueText;

    [Header("Input")]
    [Tooltip("Optional: advance action from your Input Action Asset (e.g. UI/Submit). If unset, Space/Enter keys are used.")]
    public InputActionReference advanceAction;

    public UnityEvent onDialogueComplete;

    DialogueInstance _currentInstance;
    int _stepIndex;
    bool _isShowing;
    bool _pushedGameplaySuspend;
    InputAction _subscribedAdvanceAction;

    void Start()
    {
        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);

        var gs = FindFirstObjectByType<GameServices>();
        if (gs != null)
            gs.RegisterDialogueUI(this);
    }

    void OnDestroy()
    {
        if (_isShowing)
            CloseDialogue();
    }

    PlayerControllerM GetPlayer()
    {
        var gs = FindFirstObjectByType<GameServices>();
        return gs != null ? gs.GetPlayer() : null;
    }

    void Update()
    {
        if (!_isShowing || _currentInstance == null) return;

        // Check mobile input first (always active on mobile)
        // Mobile input: tap anywhere to advance
        if (MobileInputManager.Instance != null && MobileInputManager.Instance.IsMobileControlsActive())
        {
            bool hasTouchInput = false;

            var touchscreen = Touchscreen.current;
            if (touchscreen != null && touchscreen.primaryTouch.press.wasPressedThisFrame)
            {
                hasTouchInput = true;
            }

            if (!hasTouchInput)
            {
                var mouse = Mouse.current;
                if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                {
                    hasTouchInput = true;
                }
            }

            if (hasTouchInput)
            {
                AdvanceDialogue();
                return;
            }
        }

        // If advanceAction is set, it handles input via callback, so we're done
        if (advanceAction != null) return;

        // Keyboard input fallback (Space/Enter) - only if no advanceAction
        var keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
        {
            AdvanceDialogue();
            return;
        }
    }

    static GameUIManager ResolveGameUIManager() =>
        GameUIManager.Instance != null ? GameUIManager.Instance : FindFirstObjectByType<GameUIManager>();

    /// <summary>Show dialogue. First step is displayed immediately.</summary>
    public void ShowDialogue(DialogueInstance instance)
    {
        if (_isShowing)
            CloseDialogue();

        if (instance == null || instance.steps == null || instance.steps.Length == 0)
        {
            CloseDialogue();
            return;
        }

        _currentInstance = instance;
        _stepIndex = 0;
        _isShowing = true;
        _pushedGameplaySuspend = false;

        if (dialoguePanel != null)
            dialoguePanel.SetActive(true);

        var uiManager = ResolveGameUIManager();
        if (uiManager != null)
        {
            uiManager.PushGameplaySuspend();
            _pushedGameplaySuspend = true;
        }

        if (advanceAction != null)
        {
            var action = advanceAction.action;
            if (action != null)
            {
                _subscribedAdvanceAction = action;
                action.Enable();
                action.performed += OnAdvancePerformed;
            }
        }

        ShowStep(0);
    }

    void OnAdvancePerformed(InputAction.CallbackContext ctx)
    {
        AdvanceDialogue();
    }

    void AdvanceDialogue()
    {
        if (_currentInstance == null) return;

        _stepIndex++;
        if (_stepIndex >= _currentInstance.steps.Length)
        {
            CloseDialogue();
            onDialogueComplete?.Invoke();
            return;
        }

        ShowStep(_stepIndex);
    }

    void ShowStep(int index)
    {
        if (_currentInstance == null || index < 0 || index >= _currentInstance.steps.Length) return;

        var step = _currentInstance.steps[index];
        if (characterPortrait != null)
        {
            characterPortrait.sprite = step.characterSprite;
            characterPortrait.enabled = step.characterSprite != null;
        }
        if (dialogueText != null)
            dialogueText.text = step.text ?? "";
    }

    void CloseDialogue()
    {
        if (_pushedGameplaySuspend)
        {
            _pushedGameplaySuspend = false;
            ResolveGameUIManager()?.PopGameplaySuspend();
        }

        _isShowing = false;
        _currentInstance = null;

        if (_subscribedAdvanceAction != null)
        {
            _subscribedAdvanceAction.performed -= OnAdvancePerformed;
            _subscribedAdvanceAction.Disable();
            _subscribedAdvanceAction = null;
        }

        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);
    }

}
