using UnityEngine;
using TMPro;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] private DevvitBridge devvitBridge;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text messageText;

    [Header("Timer Data")]
    private float startTime;
    private bool timerStarted = false;
    private bool levelCompleted = false;
    private float previousTime = 0f;

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
        // Find DevvitBridge if not assigned
        if (devvitBridge == null)
        {
            devvitBridge = FindFirstObjectByType<DevvitBridge>();
            if (devvitBridge == null)
            {
                Debug.LogWarning("DevvitBridge not found in scene!");
            }
        }

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

        // Store as previous time
        previousTime = completionTime;

        // Update timer text with final time
        if (timerText != null)
        {
            timerText.text = completionTime.ToString("F2");
        }

        // Update message text to inform player and that time was sent
        UpdateMessageText("Game Completed, Time sent to the server. Relaunch the game to see previous time update.");

        // Send to Devvit Bridge and update messageText when the server responds
        if (devvitBridge != null)
        {
            if (messageText != null)
            {
                messageText.text = "Game Completed, sending time to server...";
            }

            devvitBridge.CompleteLevel(completionTime, success =>
            {
                if (messageText == null) return;

                if (success)
                {
                    messageText.text = "Game Completed, Time sent to the server. Relaunch the game to see previous time update.";
                }
                else
                {
                    messageText.text = "Game Completed, Failed to send time to the server. Please try again later.";
                }
            });
        }
        else
        {
            Debug.LogWarning("Cannot send level completion - DevvitBridge not found!");
            if (messageText != null)
            {
                messageText.text = "Game Completed, Time not sent (no server bridge). Relaunch the game to see previous time update.";
            }
        }
    }

}
