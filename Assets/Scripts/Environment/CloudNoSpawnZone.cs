using UnityEngine;

/// <summary>
/// Zone where clouds should not spawn and/or enter. Place on GameObject with Collider2D (trigger).
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class CloudNoSpawnZone : MonoBehaviour
{
    [Header("Zone Flags")]
    [Tooltip("If true, CloudManager will not spawn lane clouds whose main bounds overlap this zone. If false, spawns may overlap; use with blockEntry for travel-within vs enter-from-outside behavior.")]
    public bool blockSpawn;
    [Tooltip("If true, pooled lane clouds stop at this zone (despawn if no player). With blockSpawn, overlap stops immediately. With blockSpawn off, only crossing in from outside stops; spawned or traveling inside does not.")]
    public bool blockEntry;

    Collider2D _collider;

    void Awake()
    {
        _collider = GetComponent<Collider2D>();
    }

    void Reset()
    {
        var c = GetComponent<Collider2D>();
        if (c != null) c.isTrigger = true;
    }

    /// <summary>World-space AABB for overlap tests (same as Unity uses for 2D colliders).</summary>
    public bool TryGetWorldBounds(out Bounds bounds)
    {
        if (_collider == null) _collider = GetComponent<Collider2D>();
        if (_collider == null)
        {
            bounds = default;
            return false;
        }
        bounds = _collider.bounds;
        return true;
    }

    void Start()
    {
        if (!blockSpawn && !blockEntry) return;
        var gameServices = FindFirstObjectByType<GameServices>();
        var cloudManager = gameServices != null ? gameServices.GetCloudManager() : null;
        cloudManager?.RegisterNoSpawnZone(this);
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
