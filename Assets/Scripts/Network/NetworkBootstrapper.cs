using UnityEngine;
using FishNet;
using FishNet.Managing;
#if UNITY_EDITOR
using Unity.Multiplayer.Playmode;
#endif

/// <summary>
/// Attach to NetworkManager GameObject.
/// Automatically starts server (headless builds) or client (WebGL/editor builds).
///
/// Multiplayer Play Mode (MPPM) support:
/// - Main Editor window = Host (server + client)
/// - Virtual Player windows = Client only (connects to Host)
/// </summary>
public class NetworkBootstrapper : MonoBehaviour
{
    [Header("Connection Settings")]
    [Tooltip("Address to connect to when running as a client.")]
    public string serverAddress = "localhost";

    [Header("Editor Testing")]
    [Tooltip("Start as Host in the main editor window. Virtual players (MPPM) always start as clients regardless of this setting.")]
    public bool editorStartAsHost = true;

    void Start()
    {
        NetworkManager nm = InstanceFinder.NetworkManager;
        if (nm == null)
        {
            Debug.LogError("NetworkBootstrapper: No NetworkManager found!");
            return;
        }

#if UNITY_SERVER
        // Dedicated server build: start server only
        Debug.Log("NetworkBootstrapper: Starting as dedicated server.");
        nm.ServerManager.StartConnection();

#elif UNITY_EDITOR
        // CurrentPlayer.IsMainEditor = true  → main editor window (Host)
        //                             = false → MPPM virtual player window (Client)
        bool isHost = editorStartAsHost && CurrentPlayer.IsMainEditor;

        if (isHost)
        {
            Debug.Log("NetworkBootstrapper: Editor (Main) — starting as Host.");
            nm.ServerManager.StartConnection();
            nm.ClientManager.StartConnection(serverAddress);
        }
        else
        {
            Debug.Log($"NetworkBootstrapper: Editor (Virtual Player) — starting as Client, connecting to {serverAddress}.");
            nm.ClientManager.StartConnection(serverAddress);
        }

#elif UNITY_WEBGL
        // WebGL build: always client only, connect to server
        Debug.Log($"NetworkBootstrapper: WebGL — connecting to {serverAddress}.");
        nm.ClientManager.StartConnection(serverAddress);

#else
        // Standalone: start as host
        nm.ServerManager.StartConnection();
        nm.ClientManager.StartConnection(serverAddress);
#endif
    }
}
