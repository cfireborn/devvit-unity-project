using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Defines a rectangular play boundary (safe zone). When the player leaves it, a reset event fires.
/// Intended to sit on the same prefab as GameManager. CloudManager uses the boundary to derive
/// lane count and to treat outside as no-spawn / stop or despawn for clouds.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class BoundaryManager : MonoBehaviour
{
    [Tooltip("World units to extend the boundary left/right on each side for lane/cloud extent .")]
    public float marginX = 5f;

    [Tooltip("World units to extend the boundary up/down on each side for lane/cloud extent.")]
    public float marginY = 5f;

    [Header("Events")]
    [Tooltip("Fired when a collider tagged 'Player' exits the boundary trigger (e.g. GameManager resets the level).")]
    public UnityEvent<GameObject, Vector2> onPlayerExitedBoundary;

    BoxCollider2D _box;

    void Awake()
    {
        _box = GetComponent<BoxCollider2D>();
        if (_box != null)
            _box.isTrigger = true;
    }

    /// <summary>World-space bounds of the safe zone (inner boundary). From BoxCollider2D.</summary>
    public Bounds GetInnerBounds()
    {
        if (_box == null) _box = GetComponent<BoxCollider2D>();
        return _box != null ? _box.bounds : new Bounds(transform.position, Vector3.zero);
    }

    /// <summary>Inner bounds expanded by margin on all sides. Used by CloudManager for lane extent and spawn/travel limits.</summary>
    public Bounds GetExtendedBounds()
    {
        Bounds inner = GetInnerBounds();
        Vector3 size = new Vector3(inner.size.x + 2f * marginX, inner.size.y + 2f * marginY, 0f);
        Vector3 center = inner.center;
        return new Bounds(center, size);
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other == null || !other.CompareTag("Player")) return;
        Vector2 contactPoint = other.ClosestPoint(transform.position);
        onPlayerExitedBoundary?.Invoke(other.gameObject, contactPoint);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        var box = GetComponent<BoxCollider2D>();
        if (box == null) return;
        Bounds inner = box.bounds;
        Gizmos.color = new Color(0f, 1f, 0.3f, 0.25f);
        Gizmos.DrawCube(inner.center, inner.size);
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(inner.center, inner.size);
        Bounds extended = new Bounds(inner.center, inner.size + new Vector3(2f * marginX, 2f * marginY, 0f));
        Gizmos.color = new Color(1f, 1f, 0f, 0.15f);
        Gizmos.DrawWireCube(extended.center, extended.size);
    }
#endif
}
