using UnityEngine;

/// <summary>
/// Zone where clouds should not spawn and/or enter. Place on GameObject with Collider2D (trigger).
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class CloudNoSpawnZone : MonoBehaviour
{
    [Header("Zone Flags")]
    [Tooltip("CloudManager will not spawn clouds inside this zone.")]
    public bool blockSpawn;
    [Tooltip("CloudPlatform will stop when entering; clouds without player will despawn.")]
    public bool blockEntry;

    void Reset()
    {
        var c = GetComponent<Collider2D>();
        if (c != null) c.isTrigger = true;
    }

    void Start()
    {
        if (blockSpawn)
        {
            var gameServices = FindFirstObjectByType<GameServices>();
            var cloudManager = gameServices != null ? gameServices.GetCloudManager() : null;
            if (cloudManager != null)
            {
                cloudManager.RegisterBlockSpawnZone(this);
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        var col = GetComponent<Collider2D>();
        if (col == null) return;

        Gizmos.color = blockSpawn && blockEntry ? new Color(1f, 0.5f, 0f, 0.3f) : blockSpawn ? new Color(1f, 0f, 0f, 0.3f) : new Color(0f, 0.5f, 1f, 0.3f);
        var bounds = col.bounds;
        Gizmos.DrawCube(bounds.center, bounds.size);
        Gizmos.color = Color.white;
        Gizmos.DrawWireCube(bounds.center, bounds.size);
    }
}
