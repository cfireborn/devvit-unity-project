using System;
using System.Collections;
using FishNet;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Transporting.Multipass;
using UnityEngine;
#if UNITY_EDITOR
using Unity.Multiplayer.Playmode;
#endif

/// <summary>
/// Attach to NetworkManager GameObject.
/// Starts server/host/client based on build target and MPPM player index.
/// Falls back to offline single-player on any connection failure.
/// </summary>
public class NetworkBootstrapper : MonoBehaviour
{
    [Header("Local Testing")]
    public string localAddress     = "localhost";
    public ushort localTugboatPort = 7777;
    public ushort localBayouPort   = 7771;

    [Header("Edgegap")]
    public string edgegapAddress     = "5963fffe3b47.pr.edgegap.net";
    public ushort edgegapTugboatPort = 32647;
    public ushort edgegapBayouPort   = 31672;

    [Header("Editor Testing")]
    [Tooltip("Start as Host in the main editor window. Virtual players (MPPM) always start as clients.")]
    public bool editorStartAsHost = true;

    [Header("Offline Fallback")]
    [Tooltip("Seconds to wait for a server connection before falling back to offline single-player mode.")]
    public float connectionTimeoutSeconds = 5f;

    [Header("UI Feedback")]
    [Tooltip("Tinted orange when the client successfully connects to the server.")]
    [SerializeField] VirtualJoystick joystick;

    // Read-only accessors for AdminMenu to display current resolved values.
    public string ActiveAddress     => _serverAddress;
    public ushort ActiveTugboatPort => _tugboatPort;
    public ushort ActiveBayouPort   => _bayouPort;

    bool _connectionEstablished;
    bool _offlineTriggered;

    // Resolved at startup from build target.
    string _serverAddress;
    ushort _tugboatPort;
    ushort _bayouPort;

    void Start()
    {
        // Compile flag sets the default; AdminMenu can override at runtime.
#if UNITY_EDITOR || UNITY_STANDALONE_OSX || UNITY_SERVER
        bool useLocal = true;
#else
        bool useLocal = false;
#endif
        if (AdminMenuPrefs.UseLocalOverride.HasValue)
            useLocal = AdminMenuPrefs.UseLocalOverride.Value;

        _serverAddress = useLocal ? localAddress      : edgegapAddress;
        _tugboatPort   = useLocal ? localTugboatPort  : edgegapTugboatPort;
        _bayouPort     = useLocal ? localBayouPort    : edgegapBayouPort;

        // Apply any per-field Edgegap overrides set from the admin menu.
        if (!useLocal)
        {
            if (!string.IsNullOrWhiteSpace(AdminMenuPrefs.EdgegapAddressOverride))
                _serverAddress = AdminMenuPrefs.EdgegapAddressOverride;
            if (AdminMenuPrefs.EdgegapTugboatPortOverride.HasValue)
                _tugboatPort = AdminMenuPrefs.EdgegapTugboatPortOverride.Value;
            if (AdminMenuPrefs.EdgegapBayouPortOverride.HasValue)
                _bayouPort = AdminMenuPrefs.EdgegapBayouPortOverride.Value;
        }

        Debug.Log($"NetworkBootstrapper: {(useLocal ? "LOCAL" : "EDGEGAP")} — {_serverAddress}  UDP:{_tugboatPort}  WS:{_bayouPort}");

        NetworkManager nm = InstanceFinder.NetworkManager;
        if (nm == null)
        {
            Debug.LogError("NetworkBootstrapper: No NetworkManager found — going offline.");
            TriggerOfflineFallback();
            return;
        }

        nm.ClientManager.OnClientConnectionState += OnClientConnectionState;

#if UNITY_SERVER
        Debug.Log("NetworkBootstrapper: Starting as dedicated server.");
        TryStartServer(nm);

#elif UNITY_EDITOR
        bool isHost = editorStartAsHost && CurrentPlayer.IsMainEditor;
        SetClientTransport<FishNet.Transporting.Tugboat.Tugboat>(nm);
        if (isHost)
        {
            Debug.Log("NetworkBootstrapper: Editor (Main) — starting as Host.");
            TryStartServer(nm);
            TryConnectClient(nm, _serverAddress, _tugboatPort);
        }
        else
        {
            Debug.Log($"NetworkBootstrapper: Editor (Virtual Player) — connecting to {_serverAddress}:{_tugboatPort}.");
            TryConnectClient(nm, _serverAddress, _tugboatPort);
        }

#elif UNITY_WEBGL
        Debug.Log($"NetworkBootstrapper: WebGL — connecting via Bayou to {_serverAddress}:{_bayouPort}.");
        SetClientTransport<FishNet.Transporting.Bayou.Bayou>(nm);
        TryConnectClient(nm, _serverAddress, _bayouPort);

#else
        TryStartServer(nm);
        TryConnectClient(nm, _serverAddress, _tugboatPort);
#endif
    }

    // Attempts to start the server. Any exception triggers offline fallback.
    void TryStartServer(NetworkManager nm)
    {
        try
        {
#if UNITY_EDITOR
            StartServerTransport<FishNet.Transporting.Tugboat.Tugboat>(nm);
#else
            nm.ServerManager.StartConnection();
#endif
        }
        catch (Exception e)
        {
            Debug.LogError($"NetworkBootstrapper: Server failed to start ({e.Message}) — going offline.");
            TriggerOfflineFallback();
        }
    }

    // Validates address/port, then attempts client connection.
    // On bad config or exception, falls back to offline immediately.
    void TryConnectClient(NetworkManager nm, string address, ushort port)
    {
        if (!ValidateClientTarget(address, port))
        {
            TriggerOfflineFallback();
            return;
        }

        try
        {
            nm.ClientManager.StartConnection(address, port);
            StartCoroutine(ConnectionTimeoutRoutine());
        }
        catch (Exception e)
        {
            Debug.LogError($"NetworkBootstrapper: StartConnection threw ({e.Message}) — going offline.");
            TriggerOfflineFallback();
        }
    }

    // Returns false (and logs) if address or port are obviously invalid.
    static bool ValidateClientTarget(string address, ushort port)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            Debug.LogWarning("NetworkBootstrapper: Server address is empty — going offline.");
            return false;
        }
        if (port == 0)
        {
            Debug.LogWarning("NetworkBootstrapper: Port is 0 — going offline.");
            return false;
        }
        return true;
    }

    // Selects which sub-transport Multipass uses for the client connection.
    static void SetClientTransport<T>(NetworkManager nm) where T : Transport
    {
        if (nm.TransportManager.Transport is Multipass mp)
            mp.SetClientTransport<T>();
    }

    // Starts only a specific sub-transport's server within Multipass.
    static void StartServerTransport<T>(NetworkManager nm) where T : Transport
    {
        if (nm.TransportManager.Transport is Multipass mp)
        {
            for (int i = 0; i < mp.Transports.Count; i++)
            {
                if (mp.Transports[i] is T)
                {
                    mp.StartConnection(true, i);
                    return;
                }
            }
        }
        nm.ServerManager.StartConnection();
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
            if (joystick != null)
                joystick.SetConnectedTint(new Color(1f, 0.78f, 0.52f)); // soft warm tint
        }
        else if (args.ConnectionState == LocalConnectionState.Stopped && !_connectionEstablished)
        {
            // Stopped before ever reaching Started = connection failed.
            // Fall back immediately rather than waiting for the timeout.
            Debug.LogWarning("NetworkBootstrapper: Client connection failed — going offline immediately.");
            TriggerOfflineFallback();
        }
    }

    IEnumerator ConnectionTimeoutRoutine()
    {
        yield return new WaitForSeconds(connectionTimeoutSeconds);

        if (!_connectionEstablished)
        {
            Debug.LogWarning($"NetworkBootstrapper: No connection after {connectionTimeoutSeconds}s — going offline.");
            TriggerOfflineFallback();
        }
    }

    void TriggerOfflineFallback()
    {
        if (_offlineTriggered) return;
        _offlineTriggered = true;

        // Unsubscribe before StopConnection so the Stopped event doesn't re-enter.
        var nm = InstanceFinder.NetworkManager;
        if (nm != null)
        {
            nm.ClientManager.OnClientConnectionState -= OnClientConnectionState;
            nm.ClientManager.StopConnection();
            if (nm.IsServerStarted)
                nm.ServerManager.StopConnection(sendDisconnectMessage: true);
        }

        var gm = FindFirstObjectByType<GameManagerM>();
        if (gm != null)
            gm.ActivateOfflineMode();
        else
            Debug.LogWarning("NetworkBootstrapper: No GameManagerM found for offline fallback.");
    }
}
