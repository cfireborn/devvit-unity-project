using System.Collections;
using FishNet;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;
#if UNITY_EDITOR
using Unity.Multiplayer.Playmode;
#endif

/// <summary>
/// Attach to NetworkManager GameObject.
/// Starts server/host/client based on build target and MPPM player index.
/// Falls back to offline single-player if connection isn't established within the timeout.
/// </summary>
public class NetworkBootstrapper : MonoBehaviour
{
    [Header("Connection Settings")]
    [Tooltip("Address to connect to when running as a client.")]
    public string serverAddress = "localhost";

    [Header("Editor Testing")]
    [Tooltip("Start as Host in the main editor window. Virtual players (MPPM) always start as clients.")]
    public bool editorStartAsHost = true;

    [Header("Offline Fallback")]
    [Tooltip("Seconds to wait for a server connection before falling back to offline single-player mode.")]
    public float connectionTimeoutSeconds = 5f;

    bool _connectionEstablished;

    void Start()
    {
        NetworkManager nm = InstanceFinder.NetworkManager;
        if (nm == null)
        {
            Debug.LogError("NetworkBootstrapper: No NetworkManager found!");
            TriggerOfflineFallback();
            return;
        }

        nm.ClientManager.OnClientConnectionState += OnClientConnectionState;

#if UNITY_SERVER
        Debug.Log("NetworkBootstrapper: Starting as dedicated server.");
        nm.ServerManager.StartConnection();

#elif UNITY_EDITOR
        bool isHost = editorStartAsHost && CurrentPlayer.IsMainEditor;
        if (isHost)
        {
            Debug.Log("NetworkBootstrapper: Editor (Main) — starting as Host.");
            nm.ServerManager.StartConnection();
            nm.ClientManager.StartConnection(serverAddress);
        }
        else
        {
            Debug.Log($"NetworkBootstrapper: Editor (Virtual Player) — connecting to {serverAddress}.");
            nm.ClientManager.StartConnection(serverAddress);
            StartCoroutine(ConnectionTimeoutRoutine());
        }

#elif UNITY_WEBGL
        Debug.Log($"NetworkBootstrapper: WebGL — connecting to {serverAddress}.");
        nm.ClientManager.StartConnection(serverAddress);
        StartCoroutine(ConnectionTimeoutRoutine());

#else
        nm.ServerManager.StartConnection();
        nm.ClientManager.StartConnection(serverAddress);
#endif
    }

    void OnDestroy()
    {
        var nm = InstanceFinder.NetworkManager;
        if (nm != null)
            nm.ClientManager.OnClientConnectionState -= OnClientConnectionState;
    }

    void OnClientConnectionState(ClientConnectionStateArgs args)
    {
        if (args.ConnectionState == LocalConnectionState.Started)
        {
            _connectionEstablished = true;
            Debug.Log("NetworkBootstrapper: Client connected successfully.");
        }
    }

    IEnumerator ConnectionTimeoutRoutine()
    {
        yield return new WaitForSeconds(connectionTimeoutSeconds);

        if (!_connectionEstablished)
        {
            Debug.LogWarning($"NetworkBootstrapper: No connection after {connectionTimeoutSeconds}s — falling back to offline mode.");
            TriggerOfflineFallback();
        }
    }

    void TriggerOfflineFallback()
    {
        // Unsubscribe first so the StopConnection() call below doesn't re-trigger
        // OnClientConnectionState and accidentally set _connectionEstablished or
        // fire OnStartClient on NetworkBehaviours after offline mode is active.
        var nm = InstanceFinder.NetworkManager;
        if (nm != null)
        {
            nm.ClientManager.OnClientConnectionState -= OnClientConnectionState;
            nm.ClientManager.StopConnection();
        }

        var gm = FindFirstObjectByType<GameManagerM>();
        if (gm != null)
            gm.ActivateOfflineMode();
        else
            Debug.LogWarning("NetworkBootstrapper: No GameManagerM found for offline fallback.");
    }
}
