using FishNet;
using UnityEngine;

public class GameManagerM : MonoBehaviour
{
    [Header("References")]
    public PlayerSettingsM playerSettings;
    public GameState gameState;
    [Tooltip("Either assign a PlayerController prefab or place a PlayerController in the scene (will use existing if prefab not set)")]
    public PlayerControllerM playerPrefab;
    public CloudManager cloudManager;
    public Transform startPoint;

    [Header("Goal Trigger (replace goalPoint)")]
    [Tooltip("Assign an InteractionTrigger used as the level goal. The GameManager will subscribe to its onInteract event.")]
    public InteractionTrigger goalTrigger;

    [Header("Reset Triggers")]
    [Tooltip("InteractionTriggers that will reset the level when activated by the player.")]
    public InteractionTrigger[] resetTriggers;

    [Header("Services")]
    public GameServices gameServices;

    // Always the LOCAL owned player — set via GameServices.onPlayerRegistered.
    // Works for both networked (NetworkPlayerController registers on ownership confirmed)
    // and offline (SpawnPlayerLocal registers immediately).
    private PlayerControllerM playerInstance;

    void Start()
    {
        if (gameServices == null) gameServices = FindFirstObjectByType<GameServices>();
        if (gameServices == null) gameServices = gameObject.AddComponent<GameServices>();

        if (cloudManager != null) gameServices.RegisterCloudManager(cloudManager);

        if (gameState != null)
        {
            if (startPoint != null) gameState.startPosition = startPoint.position;
            if (goalTrigger != null) gameState.goalPosition = goalTrigger.transform.position;
        }

        // Subscribe to reset triggers
        if (resetTriggers != null)
        {
            foreach (var t in resetTriggers)
            {
                if (t != null) t.onInteract.AddListener(HandleResetTriggered);
            }
        }

        // onPlayerRegistered fires when the LOCAL player is ready (both networked and offline).
        // This is the single source of truth for playerInstance in all modes.
        gameServices.onPlayerRegistered += OnPlayerRegistered;

        SpawnPlayer();
    }

    // ── Player registration (called by GameServices when local player is ready) ──

    void OnPlayerRegistered()
    {
        var player = gameServices.GetPlayer();
        if (player == null) return;

        playerInstance = player;

        if (playerSettings != null) playerInstance.settings = playerSettings;
        if (gameState != null) playerInstance.gameState = gameState;

        // Subscribe goal trigger now that we have a real player reference.
        if (goalTrigger != null)
        {
            goalTrigger.onInteract.RemoveListener(HandleGoalTriggered); // guard against double-subscribe
            goalTrigger.onInteract.AddListener(HandleGoalTriggered);
            playerInstance.OnGoalRegistered(goalTrigger.gameObject);
        }
    }

    // ── Spawn ─────────────────────────────────────────────────────────────────

    void SpawnPlayer()
    {
        // If a NetworkManager exists, NetworkPlayerSpawner handles spawning.
        // When the spawned player's PlayerControllerM.Start() runs (owner only),
        // it calls GameServices.RegisterPlayer() → onPlayerRegistered → OnPlayerRegistered().
        if (InstanceFinder.NetworkManager != null)
        {
            Debug.Log("GameManagerM: NetworkManager present — deferring spawn to network.");
            return;
        }

        // No NetworkManager = true offline build, spawn immediately.
        SpawnPlayerLocal();
    }

    /// <summary>
    /// Offline fallback: spawns a local player when network connection times out.
    /// Tints the player grey to signal offline/disconnected state.
    /// Re-enables CloudManager so clouds spawn normally in single-player.
    /// </summary>
    public void ActivateOfflineMode()
    {
        Debug.Log("GameManagerM: Activating offline mode (no server connection).");

        // Spawn player first so CloudManager.Start() can find it via GameServices immediately.
        SpawnPlayerLocal();

        // Delegate to NetworkCloudManager so it sets its _offlineMode flag (prevents
        // OnStartClient from re-disabling CloudManager if it fires late) and re-enables
        // CloudManager. Falls back to a scene search in case the inspector ref isn't assigned.
        var ncm = FindFirstObjectByType<NetworkCloudManager>(FindObjectsInactive.Include);
        if (ncm != null)
        {
            ncm.ActivateOfflineMode();
        }
        else if (cloudManager != null)
        {
            // No NetworkCloudManager in scene — just re-enable directly (pure offline build).
            cloudManager.enabled = true;
        }

        if (playerInstance != null && playerInstance.spriteRenderer != null)
            playerInstance.spriteRenderer.color = new Color(0.55f, 0.55f, 0.55f, 1f);
    }

    void SpawnPlayerLocal()
    {
        if (playerInstance != null) return; // already spawned

        if (playerPrefab != null && startPoint != null)
        {
            var spawned = Instantiate(playerPrefab, startPoint.position, Quaternion.identity);
            // RegisterPlayer fires onPlayerRegistered → OnPlayerRegistered() sets playerInstance
            gameServices?.RegisterPlayer(spawned);
        }
        else
        {
            var existing = FindFirstObjectByType<PlayerControllerM>();
            if (existing != null)
            {
                if (startPoint != null) existing.transform.position = startPoint.position;
                gameServices?.RegisterPlayer(existing);
            }
            else
            {
                Debug.LogWarning("GameManager: No PlayerController found or prefab assigned. Player not spawned.");
            }
        }
    }

    // ── Triggers ──────────────────────────────────────────────────────────────

    public void OnDeliveryComplete()
    {
        Debug.Log("GameManager: Delivery complete.");
    }

    void HandleGoalTriggered(GameObject source, Vector2 contactPoint)
    {
        ResetGame();
    }

    void HandleResetTriggered(GameObject source, Vector2 contactPoint)
    {
        Debug.Log("GameManager: Reset trigger activated. Resetting level.");
        ResetGame();
    }

    // ── Reset ─────────────────────────────────────────────────────────────────

    void ResetGame()
    {
        if (gameState != null)
            gameState.levelComplete = false;

        // Use GameServices as authoritative source for the local player.
        // Avoids FindFirstObjectByType which can return remote/disabled players in multiplayer.
        var player = playerInstance ?? gameServices?.GetPlayer();

        if (player != null)
        {
            Vector3 spawnPos = startPoint != null ? startPoint.position : player.transform.position;
            player.ResetForRespawn(spawnPos);
            if (playerSettings != null) player.settings = playerSettings;
            if (gameState != null) player.gameState = gameState;
        }
        else
        {
            Debug.LogWarning("GameManager.ResetGame: No local player found to respawn.");
        }
    }

    void OnDestroy()
    {
        if (gameServices != null)
            gameServices.onPlayerRegistered -= OnPlayerRegistered;

        if (resetTriggers != null)
        {
            foreach (var t in resetTriggers)
            {
                if (t != null) t.onInteract.RemoveListener(HandleResetTriggered);
            }
        }

        if (goalTrigger != null)
            goalTrigger.onInteract.RemoveListener(HandleGoalTriggered);
    }
}
