using FishNet.Object;
using UnityEngine;

/// <summary>
/// Shared helpers for switching to offline mode. Server and offline are distinguished
/// by which delegates are set on the core components; the core component is disabled
/// until the server or offline mode enables it.
/// </summary>
public static class NetworkOfflineUtil
{
    /// <summary>Strips FishNet components from a GameObject so it can be used in offline pooling (no network spawn).</summary>
    public static void StripNetworkComponents(GameObject go)
    {
        if (go == null) return;
        foreach (var nb in go.GetComponentsInChildren<NetworkBehaviour>(true))
            UnityEngine.Object.DestroyImmediate(nb);
        var nob = go.GetComponent<NetworkObject>();
        if (nob != null)
            UnityEngine.Object.DestroyImmediate(nob);
    }
}
