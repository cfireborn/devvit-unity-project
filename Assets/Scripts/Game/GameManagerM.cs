using UnityEngine;

public class GameManagerM : MonoBehaviour
{
    [Header("References")]
    public PlayerSettingsM playerSettings;
    public GameState gameState;
    [Tooltip("Either assign a PlayerController prefab or place a PlayerController in the scene (will use existing if prefab not set)")]
    public PlayerControllerM playerPrefab;
    public Transform startPoint;
    [Header("Goal Trigger (replace goalPoint)")]
    [Tooltip("Assign an InteractionTrigger used as the level goal. The GameManager will subscribe to its onInteract event.")]
    public InteractionTrigger goalTrigger;

    [Header("Reset Triggers")]
    [Tooltip("InteractionTriggers that will reset the level when activated by the player.")]
    public InteractionTrigger[] resetTriggers;

    private PlayerControllerM playerInstance;

    void Start()
    {
        if (gameState != null)
        {
            if (startPoint != null) gameState.startPosition = startPoint.position;
            if (goalTrigger != null) gameState.goalPosition = goalTrigger.transform.position;
        }

        // subscribe to reset triggers
        if (resetTriggers != null)
        {
            foreach (var t in resetTriggers)
            {
                if (t != null) t.onInteract.AddListener(HandleResetTriggered);
            }
        }

        SpawnPlayer();
    }

    void SpawnPlayer()
    {
        if (playerPrefab != null && startPoint != null)
        {
            playerInstance = Instantiate(playerPrefab, startPoint.position, Quaternion.identity);
        }
        else
        {
            playerInstance = FindObjectOfType<PlayerControllerM>();
            if (playerInstance != null && startPoint != null)
            {
                playerInstance.transform.position = startPoint.position;
            }
        }

        if (playerInstance != null)
        {
            if (playerSettings != null) playerInstance.settings = playerSettings;
            if (gameState != null) playerInstance.gameState = gameState;

            // If a goal trigger is present, subscribe to it so we can mark level complete and notify the player
            if (goalTrigger != null)
            {
                goalTrigger.onInteract.AddListener(HandleGoalTriggered);
                // Optionally notify the player for local feedback
                playerInstance.OnGoalRegistered(goalTrigger.gameObject);
            }
        }
        else
        {
            Debug.LogWarning("GameManager: No PlayerController found or prefab assigned. Player not spawned.");
        }
    }

    void HandleGoalTriggered(GameObject source, Vector2 contactPoint)
    {
        ResetGame();
        // if (gameState != null && !gameState.levelComplete)
        // {
        //     gameState.levelComplete = true;
        //     Debug.Log("GameManager: Goal triggered. Level complete.");
        // }

        // if (playerInstance != null)
        // {
        //     playerInstance.OnGoalTriggered(source, contactPoint);
        // }
    }

    void HandleResetTriggered(GameObject source, Vector2 contactPoint)
    {
        Debug.Log("GameManager: Reset trigger activated. Resetting level.");
        ResetGame();
    }

    void ResetGame()
    {
        // reset simple game state
        if (gameState != null)
        {
            gameState.levelComplete = false;
            gameState.lettersCollected = 0;
        }

        // reset/reset triggers so they can be used again
        if (resetTriggers != null)
        {
            foreach (var t in resetTriggers)
            {
                if (t != null) t.ResetTrigger();
            }
        }

        if (goalTrigger != null)
        {
            goalTrigger.ResetTrigger();
        }

        // respawn the player at the start point
        if (playerInstance == null)
        {
            playerInstance = FindObjectOfType<PlayerControllerM>();
        }

        if (playerInstance != null)
        {
            Vector3 spawnPos = startPoint != null ? startPoint.position : playerInstance.transform.position;
            // call into the player to reset internal state if available
            playerInstance.ResetForRespawn(spawnPos);

            // re-assign settings/state in case they were changed
            if (playerSettings != null) playerInstance.settings = playerSettings;
            if (gameState != null) playerInstance.gameState = gameState;
        }
        else
        {
            Debug.LogWarning("GameManager.ResetGame: No PlayerController found to respawn.");
        }
    }

    void OnDestroy()
    {
        if (resetTriggers != null)
        {
            foreach (var t in resetTriggers)
            {
                if (t != null) t.onInteract.RemoveListener(HandleResetTriggered);
            }
        }

        if (goalTrigger != null)
        {
            goalTrigger.onInteract.RemoveListener(HandleGoalTriggered);
        }
    }
}
