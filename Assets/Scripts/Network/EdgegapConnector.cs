using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Attach to the NetworkManager GameObject alongside NetworkBootstrapper.
/// On WebGL, NetworkBootstrapper calls GetSession() before connecting.
/// Calls the Edgegap API to allocate a server and returns the IP:port.
/// </summary>
public class EdgegapConnector : MonoBehaviour
{
    [Tooltip("Edgegap settings asset (create via right-click → Create → Compersion → Edgegap Settings).")]
    public EdgegapSettings settings;

    const string BaseUrl = "https://api.edgegap.com";
    const float PollInterval = 2f;
    const int MaxPolls = 30; // 60 seconds max wait

    /// <summary>True after GetSession() completes successfully.</summary>
    public bool IsReady { get; private set; }
    public string ServerIP   { get; private set; }
    public ushort ServerPort { get; private set; }

    /// <summary>
    /// Called by NetworkBootstrapper (WebGL only).
    /// Creates or retrieves an Edgegap session, then sets IsReady + ServerIP + ServerPort.
    /// </summary>
    public IEnumerator GetSession()
    {
        if (settings == null)
        {
            Debug.LogError("EdgegapConnector: No EdgegapSettings asset assigned.");
            yield break;
        }

        // --- Step 1: Create session ---
        string createBody = JsonUtility.ToJson(new SessionRequest
        {
            app_name     = settings.appName,
            version_name = settings.versionName
        });

        using var createReq = new UnityWebRequest($"{BaseUrl}/v1/sessions", "POST");
        createReq.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(createBody));
        createReq.downloadHandler = new DownloadHandlerBuffer();
        createReq.SetRequestHeader("Content-Type", "application/json");
        createReq.SetRequestHeader("Authorization", settings.apiKey);

        yield return createReq.SendWebRequest();

        if (createReq.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"EdgegapConnector: Session create failed — {createReq.error}\n{createReq.downloadHandler.text}");
            yield break;
        }

        var createResp = JsonUtility.FromJson<SessionCreateResponse>(createReq.downloadHandler.text);
        string sessionId = createResp.session_id;

        if (string.IsNullOrEmpty(sessionId))
        {
            Debug.LogError("EdgegapConnector: Session create returned no session_id.");
            yield break;
        }

        Debug.Log($"EdgegapConnector: Session {sessionId} created — polling for Ready state...");

        // --- Step 2: Poll until Ready ---
        for (int i = 0; i < MaxPolls; i++)
        {
            yield return new WaitForSeconds(PollInterval);

            using var pollReq = UnityWebRequest.Get($"{BaseUrl}/v1/sessions/{sessionId}");
            pollReq.SetRequestHeader("Authorization", settings.apiKey);

            yield return pollReq.SendWebRequest();

            if (pollReq.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"EdgegapConnector: Poll {i + 1} failed — {pollReq.error}");
                continue;
            }

            var status = JsonUtility.FromJson<SessionStatusResponse>(pollReq.downloadHandler.text);

            if (status.status == "Ready")
            {
                ServerIP   = status.server_ip;
                ServerPort = FindPort(pollReq.downloadHandler.text, settings.portName);

                if (string.IsNullOrEmpty(ServerIP) || ServerPort == 0)
                {
                    Debug.LogError($"EdgegapConnector: Ready but could not parse IP/port. Raw: {pollReq.downloadHandler.text}");
                    yield break;
                }

                Debug.Log($"EdgegapConnector: Server ready at {ServerIP}:{ServerPort}");
                IsReady = true;
                yield break;
            }

            Debug.Log($"EdgegapConnector: Poll {i + 1}/{MaxPolls} — status={status.status}");
        }

        Debug.LogError("EdgegapConnector: Timed out waiting for server Ready state.");
    }

    // JsonUtility can't deserialize nested dicts, so we parse the port manually.
    static ushort FindPort(string json, string portName)
    {
        // Looks for: "portName":{"external":NNNN
        string marker = $"\"{portName}\":{{\"external\":";
        int idx = json.IndexOf(marker);
        if (idx < 0) return 0;

        int start = idx + marker.Length;
        int end   = json.IndexOfAny(new[] { ',', '}' }, start);
        if (end < 0) return 0;

        if (ushort.TryParse(json.Substring(start, end - start).Trim(), out ushort port))
            return port;

        return 0;
    }

    // Minimal serialisable types for JsonUtility

    [System.Serializable]
    class SessionRequest
    {
        public string app_name;
        public string version_name;
    }

    [System.Serializable]
    class SessionCreateResponse
    {
        public string session_id;
    }

    [System.Serializable]
    class SessionStatusResponse
    {
        public string status;
        public string server_ip;
    }
}
