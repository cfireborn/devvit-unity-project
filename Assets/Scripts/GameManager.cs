using UnityEngine;
using TMPro;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text messageText;

    [Header("Timer Data")]
    private float startTime;
    private bool timerStarted = false;
    private bool levelCompleted = false;

    void Awake()
    {
        // Singleton pattern
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        // Initialize timer text
        if (timerText != null)
        {
            timerText.text = "0.00";
        }

        // Initialize instruction/message text
        UpdateMessageText("Swipe to begin! Hit the green sphere to complete the round");
    }

    void Update()
    {
        // Update timer text if timer is running
        if (timerStarted && !levelCompleted && timerText != null)
        {
            float elapsedTime = Time.time - startTime;
            timerText.text = elapsedTime.ToString("F2");
        }
    }

    void UpdateMessageText(string message)
    {
        if (messageText != null)
        {
            messageText.text = message;
        }
    }

    // Called by PlayerController on first swipe
    public void StartRound()
    {
        if (!timerStarted)
        {
            timerStarted = true;
            startTime = Time.time;
            UpdateMessageText("");
        }
    }

    // Called by TargetTrigger when player reaches the target
    public void OnTargetReached()
    {
        if (levelCompleted)
        {
            return; // Already completed, don't send multiple times
        }

        levelCompleted = true;

        // Calculate completion time
        float completionTime = timerStarted ? Time.time - startTime : 0f;

        // Update timer text with final time
        if (timerText != null)
        {
            timerText.text = completionTime.ToString("F2");
        }

        UpdateMessageText("Game Completed.");
    }

}
