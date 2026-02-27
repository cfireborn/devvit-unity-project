using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

/// <summary>
/// Attach to the NetworkManager GameObject.
/// Spawns a NetworkPlayer prefab for every client that connects, including the host client.
///
/// Setup:
/// 1. Assign the NetworkPlayer prefab (must have NetworkObject component).
/// 2. Assign the SpawnPoint transform (the level start point).
/// 3. Register the NetworkPlayer prefab in the NetworkManager's Spawnable Prefabs list.
/// </summary>
public class NetworkPlayerSpawner : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The NetworkPlayer prefab to spawn. Must have NetworkObject component and be registered in NetworkManager's Spawnable Prefabs.")]
    [SerializeField] private NetworkObject playerPrefab;

    [Tooltip("Where to spawn players. If unset, spawns at world origin.")]
    [SerializeField] private Transform spawnPoint;

    void Start()
    {
        var nm = InstanceFinder.NetworkManager;
        if (nm == null)
        {
            Debug.LogWarning("NetworkPlayerSpawner: No NetworkManager found.");
            return;
        }

        nm.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
    }

    void OnDestroy()
    {
        var nm = InstanceFinder.NetworkManager;
        if (nm == null) return;
        nm.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
    }

    void OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
    {
        if (args.ConnectionState == RemoteConnectionState.Started)
            SpawnPlayer(conn);
    }

    void SpawnPlayer(NetworkConnection conn)
    {
        if (!InstanceFinder.IsServerStarted) return;

        if (playerPrefab == null)
        {
            Debug.LogError("NetworkPlayerSpawner: playerPrefab is not assigned!");
            return;
        }

        Vector3 pos = spawnPoint != null ? spawnPoint.position : Vector3.zero;
        NetworkObject obj = Instantiate(playerPrefab, pos, Quaternion.identity);
        InstanceFinder.ServerManager.Spawn(obj, conn);

        Debug.Log($"NetworkPlayerSpawner: Spawned player for client {conn.ClientId} at {pos}");
    }
}
