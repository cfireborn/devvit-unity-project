using UnityEngine;

/// <summary>
/// Performs ground detection using a list of colliders (e.g. feet). The object is considered grounded
/// if any of those colliders overlap something tagged with <see cref="platformTag"/>.
/// Assign <see cref="groundCheckColliders"/> in the inspector; if empty, uses a single point at
/// transform.position + groundCheckOffset with groundCheckRadius (e.g. from PlayerSettings).
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class GroundChecker : MonoBehaviour
{
    [Tooltip("Tag that platform/ground colliders use. Set by player from PlayerSettings, or assign here for standalone use.")]
    public string platformTag = "Platform";

    [Header("Check sources")]
    [Tooltip("Colliders used to check for ground (e.g. feet). If empty, uses groundCheckOffset + groundCheckRadius instead.")]
    public Collider2D[] groundCheckColliders = new Collider2D[0];

    [Tooltip("Radius for overlap circle at each check collider (or at offset when no colliders assigned).")]
    public float groundCheckRadius = 0.15f;

    [Tooltip("Used only when groundCheckColliders is empty: overlap center = transform.position + this offset.")]
    public Vector2 groundCheckOffset = new Vector2(0f, -0.6f);

    /// <summary>True when any check collider (or the fallback point) is overlapping something tagged with platformTag.</summary>
    public bool isGrounded { get; private set; }

    private Collider2D[] _overlapBuffer;
    private Collider2D[] _ourColliders;
    private const int OverlapBufferSize = 16;

    void Awake()
    {
        _overlapBuffer = new Collider2D[OverlapBufferSize];
        _ourColliders = GetComponentsInChildren<Collider2D>();
    }

    /// <summary>Run the ground check and update isGrounded. Call once per physics step (e.g. from player's FixedUpdate/OnTick).</summary>
    public void RefreshCheck()
    {
        if (_overlapBuffer == null) return;

        isGrounded = false;

        if (groundCheckColliders != null && groundCheckColliders.Length > 0)
        {
            foreach (var c in groundCheckColliders)
            {
                if (c == null) continue;
                Vector2 origin = new Vector2(c.bounds.center.x, c.bounds.min.y);
                int hitCount = Physics2D.OverlapCircleNonAlloc(origin, groundCheckRadius, _overlapBuffer);
                for (int i = 0; i < hitCount; i++)
                {
                    var other = _overlapBuffer[i];
                    if (other == null) continue;
                    if (IsOurCollider(other)) continue;
                    if (other.CompareTag(platformTag))
                    {
                        isGrounded = true;
                        return;
                    }
                }
            }
        }
        else
        {
            Vector2 origin = (Vector2)transform.position + groundCheckOffset;
            int hitCount = Physics2D.OverlapCircleNonAlloc(origin, groundCheckRadius, _overlapBuffer);
            for (int i = 0; i < hitCount; i++)
            {
                var other = _overlapBuffer[i];
                if (other == null) continue;
                if (IsOurCollider(other)) continue;
                if (other.CompareTag(platformTag))
                {
                    isGrounded = true;
                    return;
                }
            }
        }
    }

    private bool IsOurCollider(Collider2D c)
    {
        if (_ourColliders == null) return false;
        for (int i = 0; i < _ourColliders.Length; i++)
        {
            if (_ourColliders[i] == c) return true;
        }
        return false;
    }

    void OnDrawGizmosSelected()
    {
        if (groundCheckColliders != null && groundCheckColliders.Length > 0)
        {
            Gizmos.color = Color.red;
            foreach (var c in groundCheckColliders)
            {
                if (c == null) continue;
                Vector2 origin = new Vector2(c.bounds.center.x, c.bounds.min.y);
                Gizmos.DrawWireSphere(origin, groundCheckRadius);
            }
        }
        else
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere((Vector2)transform.position + groundCheckOffset, groundCheckRadius);
        }
    }
}
