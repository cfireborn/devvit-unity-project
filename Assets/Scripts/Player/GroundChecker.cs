using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Performs ground detection using a list of colliders (e.g. feet). The object is considered grounded
/// if any of those colliders overlap something tagged with <see cref="platformTag"/>.
/// Also tracks ladder trigger entry/exit and exposes <see cref="CurrentPlatform"/> and
/// <see cref="CurrentLadder"/> (IMovingPlatform) for the player to apply movement delta.
/// Assign <see cref="groundCheckColliders"/> in the inspector; if empty, uses a single point at
/// transform.position + groundCheckOffset with groundCheckRadius (e.g. from PlayerSettings).
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class GroundChecker : MonoBehaviour
{
    public const string LadderTag = "Ladder";

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

    /// <summary>Platform we are standing on (if any and it implements IMovingPlatform). Null when not grounded or platform is static.</summary>
    public IMovingPlatform CurrentPlatform { get; private set; }

    /// <summary>True when the player is inside at least one trigger tagged "Ladder".</summary>
    public bool IsOnLadder => _ladderTriggers.Count > 0;

    /// <summary>Ladder we are inside (if any and it implements IMovingPlatform). Null when not on a ladder.</summary>
    public IMovingPlatform CurrentLadder { get; private set; }

    private Collider2D[] _overlapBuffer;
    private ContactFilter2D _overlapFilter;
    private Collider2D[] _ourColliders;
    private const int OverlapBufferSize = 16;
    private readonly List<Collider2D> _ladderTriggers = new List<Collider2D>();

    void Awake()
    {
        _overlapBuffer = new Collider2D[OverlapBufferSize];
        _overlapFilter = new ContactFilter2D();
        _overlapFilter.NoFilter();
        _ourColliders = GetComponentsInChildren<Collider2D>();
    }

    /// <summary>Run the ground check and update isGrounded and CurrentPlatform. Call once per physics step (e.g. from player's FixedUpdate/OnTick).</summary>
    public void RefreshCheck()
    {
        if (_overlapBuffer == null) return;

        isGrounded = false;
        CurrentPlatform = null;

        if (groundCheckColliders != null && groundCheckColliders.Length > 0)
        {
            foreach (var c in groundCheckColliders)
            {
                if (c == null) continue;
                Vector2 origin = new Vector2(c.bounds.center.x, c.bounds.min.y);
                int hitCount = Physics2D.OverlapCircle(origin, groundCheckRadius, _overlapFilter, _overlapBuffer);
                for (int i = 0; i < hitCount; i++)
                {
                    var other = _overlapBuffer[i];
                    if (other == null) continue;
                    if (IsOurCollider(other)) continue;
                    if (other.CompareTag(platformTag))
                    {
                        isGrounded = true;
                        CurrentPlatform = other.GetComponent<IMovingPlatform>() ?? other.GetComponentInParent<IMovingPlatform>();
                        return;
                    }
                }
            }
        }
        else
        {
            Vector2 origin = (Vector2)transform.position + groundCheckOffset;
            int hitCount = Physics2D.OverlapCircle(origin, groundCheckRadius, _overlapFilter, _overlapBuffer);
            for (int i = 0; i < hitCount; i++)
            {
                var other = _overlapBuffer[i];
                if (other == null) continue;
                if (IsOurCollider(other)) continue;
                if (other.CompareTag(platformTag))
                {
                    isGrounded = true;
                    CurrentPlatform = other.GetComponent<IMovingPlatform>() ?? other.GetComponentInParent<IMovingPlatform>();
                    return;
                }
            }
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag(LadderTag))
        {
            if (!_ladderTriggers.Contains(other))
                _ladderTriggers.Add(other);
            UpdateCurrentLadder();
        }
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag(LadderTag))
        {
            _ladderTriggers.Remove(other);
            UpdateCurrentLadder();
        }
    }

    void UpdateCurrentLadder()
    {
        CurrentLadder = null;
        for (int i = 0; i < _ladderTriggers.Count; i++)
        {
            var c = _ladderTriggers[i];
            if (c == null) continue;
            var moving = c.GetComponent<IMovingPlatform>() ?? c.GetComponentInParent<IMovingPlatform>();
            if (moving != null)
            {
                CurrentLadder = moving;
                return;
            }
        }
    }

    /// <summary>Clear ladder state (e.g. after respawn so we don't think we're still on a ladder).</summary>
    public void ClearLadderState()
    {
        _ladderTriggers.Clear();
        UpdateCurrentLadder();
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
