using FishNet.Object;
using UnityEngine;

/// <summary>
/// Attach to the ladder prefab alongside NetworkObject.
/// (No NetworkTransform needed — position is re-derived from cloud positions
///  every LateUpdate by NetworkCloudLadderController on clients.)
///
/// Stores which two cloud NetworkObjects this ladder bridges so clients
/// can reconstruct the correct geometry without any extra RPCs.
///
/// SyncCloudIds uses BufferLast = true so late-joining clients automatically
/// receive the correct cloud IDs without a separate late-joiner sync pass.
/// (Same pattern as NetworkCloud.SyncScale — FishNet v4 equivalent of SyncVar.)
///
/// INSPECTOR SETUP REQUIRED:
/// - Add NetworkObject + NetworkLadder to the ladder prefab.
/// - Register the ladder prefab in NetworkManager's Spawnable Prefabs list.
/// - Do NOT add NetworkTransform — position is client-derived from cloud positions.
/// </summary>
public class NetworkLadder : NetworkBehaviour
{
    /// <summary>FishNet ObjectId of the lower cloud. Set by server via SyncCloudIds.</summary>
    public int CloudAObjectId { get; private set; } = -1;

    /// <summary>FishNet ObjectId of the upper cloud. Set by server via SyncCloudIds.</summary>
    public int CloudBObjectId { get; private set; } = -1;

    /// <summary>
    /// Called by CloudLadderController on the server right after ServerManager.Spawn().
    /// BufferLast = true ensures late-joining clients receive the correct cloud IDs.
    /// RunLocally = true applies it on the host too.
    /// </summary>
    [ObserversRpc(RunLocally = true, BufferLast = true)]
    public void SyncCloudIds(int cloudAObjectId, int cloudBObjectId)
    {
        CloudAObjectId = cloudAObjectId;
        CloudBObjectId = cloudBObjectId;
    }
}
