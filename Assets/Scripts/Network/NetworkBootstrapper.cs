using UnityEngine;
using FishNet;
using FishNet.Managing;

/// <summary>
/// Attach to NetworkManager GameObject.
/// Automatically starts server (headless builds) or client (WebGL/editor builds).
/// Also handles connecting in editor for testing.
/// </summary>
public class NetworkBootstrapper : MonoBehaviour
{
    [Header("Connection Settings")]
    [Tooltip("Address to connect to when running as a client.")]
    public string serverAddress = "localhost";

    [Header("Editor Testing")]
    [Tooltip("Start as Host (server+client) in editor. Useful for testing without a second build.")]
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
        if (editorStartAsHost)
        {
            // Editor: start as host (server + client) for easy testing
            Debug.Log("NetworkBootstrapper: Editor mode — starting as Host.");
            nm.ServerManager.StartConnection();
            nm.ClientManager.StartConnection(serverAddress);
        }
        else
        {
            // Editor: start as client only
            Debug.Log($"NetworkBootstrapper: Editor mode — starting as Client, connecting to {serverAddress}.");
            nm.ClientManager.StartConnection(serverAddress);
        }

#elif UNITY_WEBGL
        // WebGL build: always client only, connect to server
        Debug.Log($"NetworkBootstrapper: WebGL — connecting to {serverAddress}.");
        nm.ClientManager.StartConnection(serverAddress);

#else
        // Standalone: start as host for now (can be made client-only later)
        nm.ServerManager.StartConnection();
        nm.ClientManager.StartConnection(serverAddress);
#endif
    }
}
