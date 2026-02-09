using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Dialogue popup with character portrait and text. Space/Submit advances; closes when no more steps.
/// </summary>
public class DialogueUI : MonoBehaviour
{
    [Header("References")]
    public GameObject dialoguePanel;
    public Image characterPortrait;
    public TMP_Text dialogueText;
    [Tooltip("Optional: disable player input while dialogue is open.")]
    public PlayerControllerM playerController;

    [Header("Input")]
    [Tooltip("Button name for advancing (Unity Input Manager).")]
    public string advanceButton = "Submit";

    public UnityEvent onDialogueComplete;

    DialogueInstance _currentInstance;
    int _stepIndex;
    bool _isShowing;

    void Start()
    {
        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);
    }

    void Update()
    {
        if (!_isShowing || _currentInstance == null) return;

        if (Input.GetButtonDown(advanceButton) || Input.GetKeyDown(KeyCode.Space))
        {
            AdvanceDialogue();
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

        if (playerController != null)
            playerController.enabled = false;

        ShowStep(0);
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

        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);

        if (playerController != null)
            playerController.enabled = true;
    }
}
