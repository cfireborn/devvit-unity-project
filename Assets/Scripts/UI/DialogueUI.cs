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
    InputAction _subscribedAdvanceAction;
    VirtualJoystick _virtualJoystick;

    void Start()
    {
        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);

        var gs = FindFirstObjectByType<GameServices>();
        if (gs != null)
            gs.RegisterDialogueUI(this);

        // Find virtual joystick to exclude it from tap-to-advance
        _virtualJoystick = FindFirstObjectByType<VirtualJoystick>();
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
        // Mobile input: tap anywhere EXCEPT joystick to advance
        if (MobileInputManager.Instance != null && MobileInputManager.Instance.IsMobileControlsActive())
        {
            Vector2 touchPosition = Vector2.zero;
            bool hasTouchInput = false;

            // Touch input
            var touchscreen = Touchscreen.current;
            if (touchscreen != null && touchscreen.primaryTouch.press.wasPressedThisFrame)
            {
                touchPosition = touchscreen.primaryTouch.position.ReadValue();
                hasTouchInput = true;
                Debug.Log($"DialogueUI: Touch detected at {touchPosition}");
            }

            // Mouse input (for testing "Show On Desktop For Testing")
            if (!hasTouchInput)
            {
                var mouse = Mouse.current;
                if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                {
                    touchPosition = mouse.position.ReadValue();
                    hasTouchInput = true;
                    Debug.Log($"DialogueUI: Mouse click at {touchPosition}");
                }
            }

            // If we have a touch/click, check if it's NOT on the joystick
            if (hasTouchInput)
            {
                bool isOverJoystick = IsTouchOverJoystick(touchPosition);
                Debug.Log($"DialogueUI: IsTouchOverJoystick = {isOverJoystick}");

                if (!isOverJoystick)
                {
                    Debug.Log("DialogueUI: Advancing dialogue from touch!");
                    AdvanceDialogue();
                    return;
                }
                else
                {
                    Debug.Log("DialogueUI: Touch in joystick zone, ignoring.");
                }
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

    /// <summary>Show dialogue. First step is displayed immediately.</summary>
    public void ShowDialogue(DialogueInstance instance)
    {
        if (instance == null || instance.steps == null || instance.steps.Length == 0)
        {
            CloseDialogue();
            return;
        }

        _currentInstance = instance;
        _stepIndex = 0;
        _isShowing = true;

        if (dialoguePanel != null)
            dialoguePanel.SetActive(true);

        var player = GetPlayer();
        if (player != null)
            player.SetGameplayInputEnabled(false);

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

        var player = GetPlayer();
        if (player != null)
            player.SetGameplayInputEnabled(true);
    }

    /// <summary>
    /// Check if a screen position should advance dialogue.
    /// Returns false if position is in joystick zone (bottom portion of screen).
    /// Returns true if position is in dialogue zone (top portion of screen) or if joystick not found.
    /// </summary>
    bool IsTouchOverJoystick(Vector2 screenPosition)
    {
        if (_virtualJoystick == null)
            return false; // No joystick = entire screen advances dialogue

        // Check if position is in joystick zone (bottom of screen)
        // This returns true if in joystick zone, meaning we should NOT advance dialogue
        return _virtualJoystick.IsScreenPositionOverJoystick(screenPosition);
    }
}
