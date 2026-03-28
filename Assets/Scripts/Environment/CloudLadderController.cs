using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns ladders between clouds when they are within range and one is above the other (with a gap).
/// Each ladder is a root with tiled children: one bottom cap, N middle segments, one top cap.
///
/// Networking: In a networked server context, ladders are spawned as NetworkObjects via
/// ServerManager.Spawn() so FishNet replicates them to all clients automatically.
/// NetworkLadder.SyncCloudIds() tells each client which two clouds the ladder bridges,
/// so NetworkCloudLadderController can re-derive the correct geometry each LateUpdate.
/// In offline mode, ladders use a simple GameObject pool.
/// </summary>
public class CloudLadderController : MonoBehaviour
{
    const string ChildNameBottom = "Bottom";
    const string ChildNameTop = "Top";
    const string ChildNameMiddlePrefix = "Middle_";
    const int MaxLadderMiddleSegments = 64;
    const float HorizontalEdgeTolerance = 0.001f;

    [Header("References")]
    public CloudManager cloudManager;
    [Tooltip("Root prefab: tag 'Ladder', BoxCollider2D (trigger). Visuals are built as children at runtime.")]
    public GameObject ladderPrefab;
    [Tooltip("Sprite for the cap touching the lower cloud.")]
    public Sprite ladderBottomSprite;
    [Tooltip("Tileable sprite for the middle section. Repeated vertically to fill the gap.")]
    public Sprite ladderMiddleSprite;
    [Tooltip("Sprite for the cap touching the upper cloud.")]
    public Sprite ladderTopSprite;

    [Header("Params")]
    [Tooltip("Width of the ladder collider and visual (world units).")]
    public float ladderWidth = 0.3f;
    [Tooltip("Overlap between adjacent middle segments in world units. Increase to remove gaps or create overlap.")]
    [Range(0f, 2f)]
    public float middleOverlap = 0f;
    [Tooltip("Ladder appears when clouds are within this horizontal distance.")]
    public float maxDistance = 4f;
    [Tooltip("Minimum vertical gap between clouds. No ladder if they overlap or touch.")]
    public float minVerticalGap = 0.5f;
    [Tooltip("Maximum vertical gap. Clouds too far apart don't get a ladder.")]
    public float maxVerticalGap = 8f;
    [Tooltip("Maximum number of ladders that can be active. Pool prevents spawning beyond this.")]
    public int maxLadders = 10;
    [Tooltip("Distance (world units) the ladder extends inside each cloud from the polygon edge. 0 = use AABB edge.")]
    [Min(0f)]
    public float ladderInsetIntoCloud = 0.2f;

    // Injected by NetworkCloudLadderController before CloudLadderController is enabled.
    internal Action<GameObject, CloudPlatform, CloudPlatform> _onLadderActivated;
    // null = ReturnLadderToPool path. Server: FishNet Despawn or Destroy.
    internal Action<GameObject> _onLadderDeactivated;

    readonly List<CloudPlatform> _cachedPlatformList = new List<CloudPlatform>();
    readonly HashSet<(CloudPlatform, CloudPlatform)> _validPairsScratch = new HashSet<(CloudPlatform, CloudPlatform)>();
    readonly HashSet<CloudPlatform> _hasLadderAboveScratch = new HashSet<CloudPlatform>();
    readonly HashSet<CloudPlatform> _hasLadderBelowScratch = new HashSet<CloudPlatform>();
    readonly HashSet<GameObject> _activeSetScratch = new HashSet<GameObject>();
    readonly List<(CloudPlatform, CloudPlatform)> _toRemoveScratch = new List<(CloudPlatform, CloudPlatform)>();

    readonly Dictionary<(CloudPlatform, CloudPlatform), GameObject> _ladders = new Dictionary<(CloudPlatform, CloudPlatform), GameObject>();
    readonly HashSet<(CloudPlatform, CloudPlatform)> _forcedPairs = new HashSet<(CloudPlatform, CloudPlatform)>();
    readonly Queue<GameObject> _pool = new Queue<GameObject>();
    Transform _ladderParent;

    void Start()
    {
        _ladderParent = new GameObject("Ladders").transform;
        _ladderParent.SetParent(transform);
    }

    void LateUpdate()
    {
        if (cloudManager == null || ladderPrefab == null) return;

        var platformList = GetActiveCloudPlatforms();
        var validPairs = ComputeValidPairs(platformList);
        var activeSet = _activeSetScratch;
        activeSet.Clear();
        foreach (var go in cloudManager.GetActiveClouds())
            if (go != null) activeSet.Add(go);

        RemoveInvalidLadders(validPairs, activeSet);
        UpdateAllLadderPositions();
    }

    List<CloudPlatform> GetActiveCloudPlatforms()
    {
        _cachedPlatformList.Clear();
        foreach (var go in cloudManager.GetActiveClouds())
        {
            if (go == null) continue;
            var p = go.GetComponent<CloudPlatform>();
            if (p != null) _cachedPlatformList.Add(p);
        }
        return _cachedPlatformList;
    }

    HashSet<(CloudPlatform, CloudPlatform)> ComputeValidPairs(List<CloudPlatform> platformList)
    {
        _validPairsScratch.Clear();
        _hasLadderAboveScratch.Clear();
        _hasLadderBelowScratch.Clear();

        foreach (var kvp in _ladders)
        {
            var (lower, upper) = kvp.Key;
            if (lower != null) _hasLadderAboveScratch.Add(lower);
            if (upper != null) _hasLadderBelowScratch.Add(upper);
        }

        for (int i = 0; i < platformList.Count; i++)
        {
            for (int j = i + 1; j < platformList.Count; j++)
            {
                var a = platformList[i];
                var b = platformList[j];
                if (!a.canBuildLadder || !b.canBuildLadder) continue;
                var pair = OrderPair(a, b);
                var lower = pair.Item1;
                var upper = pair.Item2;
                if (_hasLadderAboveScratch.Contains(lower) || _hasLadderBelowScratch.Contains(upper))
                    continue;
                if (ShouldHaveLadder(a, b))
                {
                    _validPairsScratch.Add(pair);
                    _hasLadderAboveScratch.Add(lower);
                    _hasLadderBelowScratch.Add(upper);
                    if (!_ladders.ContainsKey(pair) && _ladders.Count < maxLadders)
                        CreateLadder(lower, upper);
                }
            }
        }

        foreach (var pair in _forcedPairs)
            _validPairsScratch.Add(pair);

        foreach (var kvp in _ladders)
        {
            if (kvp.Key.Item1 == null || kvp.Key.Item2 == null) continue;
            if (ShouldHaveLadder(kvp.Key.Item1, kvp.Key.Item2))
                _validPairsScratch.Add(kvp.Key);
        }

        return _validPairsScratch;
    }

    void RemoveInvalidLadders(HashSet<(CloudPlatform, CloudPlatform)> validPairs, HashSet<GameObject> activeSet)
    {
        _toRemoveScratch.Clear();
        foreach (var pair in _forcedPairs)
        {
            if (pair.Item1 == null || pair.Item2 == null ||
                !activeSet.Contains(pair.Item1.gameObject) || !activeSet.Contains(pair.Item2.gameObject))
                _toRemoveScratch.Add(pair);
        }
        foreach (var pair in _toRemoveScratch)
        {
            _forcedPairs.Remove(pair);
            if (_ladders.TryGetValue(pair, out var ladder) && ladder != null)
                DespawnLadder(ladder);
            _ladders.Remove(pair);
        }

        _toRemoveScratch.Clear();
        foreach (var kvp in _ladders)
        {
            if (!validPairs.Contains(kvp.Key))
                _toRemoveScratch.Add(kvp.Key);
        }
        foreach (var pair in _toRemoveScratch)
        {
            if (_ladders.TryGetValue(pair, out var ladder) && ladder != null)
                DespawnLadder(ladder);
            _ladders.Remove(pair);
        }
    }

    void UpdateAllLadderPositions()
    {
        foreach (var kvp in _ladders)
        {
            if (kvp.Value != null)
                UpdateLadderPosition(kvp.Key.Item1, kvp.Key.Item2, kvp.Value);
        }
    }

    /// <summary>True if cloud has a ladder and some partner overlaps any merged horizontal viewport interval (neither despawning).</summary>
    public bool ShouldKeepCloudActiveForLadders(GameObject cloud, List<(float left, float right)> mergedHorizontalIntervals)
    {
        if (cloud == null || mergedHorizontalIntervals == null || mergedHorizontalIntervals.Count == 0) return false;
        var platform = cloud.GetComponent<CloudPlatform>();
        if (platform == null) return false;

        foreach (var kvp in _ladders)
        {
            var (lower, upper) = kvp.Key;
            if (lower == null || upper == null) continue;
            CloudPlatform other = null;
            if (lower == platform) other = upper;
            else if (upper == platform) other = lower;
            if (other == null) continue;

            if (platform.IsDespawning || other.IsDespawning) continue;
            Bounds ob = other.GetMainBounds();
            bool inAny = false;
            foreach (var (left, right) in mergedHorizontalIntervals)
            {
                if (ob.max.x >= left && ob.min.x <= right) { inAny = true; break; }
            }
            if (!inAny) continue;
            return true;
        }
        return false;
    }

    /// <summary>True if the player is on any cloud connected to this cloud by a ladder.</summary>
    public bool IsPlayerOnAnyLadderPartner(GameObject cloud)
    {
        if (cloud == null) return false;
        var platform = cloud.GetComponent<CloudPlatform>();
        if (platform == null) return false;

        foreach (var kvp in _ladders)
        {
            var (lower, upper) = kvp.Key;
            if (lower == null || upper == null) continue;
            CloudPlatform other = null;
            if (lower == platform) other = upper;
            else if (upper == platform) other = lower;
            if (other != null && other.IsPlayerOnCloud) return true;
        }
        return false;
    }

    /// <summary>Returns (lower, upper) by vertical position. Used by NetworkCloudLadderController for client ladder rebuild.</summary>
    public static (CloudPlatform, CloudPlatform) OrderPair(CloudPlatform a, CloudPlatform b)
    {
        Bounds ba = a.GetMainBounds();
        Bounds bb = b.GetMainBounds();
        return ba.min.y < bb.min.y ? (a, b) : (b, a);
    }

    bool ShouldHaveLadder(CloudPlatform a, CloudPlatform b)
    {
        Bounds ba = a.GetMainBounds();
        Bounds bb = b.GetMainBounds();

        float dx = Mathf.Abs(ba.center.x - bb.center.x);
        if (dx > maxDistance) return false;

        float overlapMin = Mathf.Max(ba.min.x, bb.min.x);
        float overlapMax = Mathf.Min(ba.max.x, bb.max.x);
        if (overlapMin >= overlapMax) return false;

        CloudPlatform lower, upper;
        if (ba.min.y < bb.min.y) { lower = a; upper = b; } else { lower = b; upper = a; }
        Bounds bl = lower.GetMainBounds();
        Bounds bu = upper.GetMainBounds();

        float gap = bu.min.y - bl.max.y;
        if (gap < minVerticalGap) return false;
        if (gap > maxVerticalGap) return false;

        return true;
    }

    /// <summary>
    /// Forcibly try to build a ladder between two clouds. Still respects at most two ladders per cloud (one up, one down).
    /// Returns true if a ladder exists or was created; false if invalid (same cloud, null, over max, geometry not valid, or cloud already has ladder in that direction).
    /// </summary>
    public bool TryBuildLadder(CloudPlatform a, CloudPlatform b)
    {
        if (a == null || b == null || a == b) return false;
        if (cloudManager == null || ladderPrefab == null) return false;

        var pair = OrderPair(a, b);
        if (_ladders.ContainsKey(pair)) return true;
        if (!ShouldHaveLadder(a, b)) return false;

        bool lowerHasAbove = false, upperHasBelow = false;
        foreach (var kvp in _ladders)
        {
            if (kvp.Key.Item1 == pair.Item1) lowerHasAbove = true;
            if (kvp.Key.Item2 == pair.Item2) upperHasBelow = true;
        }
        if (lowerHasAbove || upperHasBelow) return false;

        _forcedPairs.Add(pair);
        CreateLadder(pair.Item1, pair.Item2);
        return true;
    }

    GameObject GetLadderFromPool()
    {
        if (_pool.Count > 0)
        {
            var ladder = _pool.Dequeue();
            ladder.SetActive(true);
            EnsureMovingPlatformLadder(ladder);
            return ladder;
        }
        var newLadder = Instantiate(ladderPrefab, _ladderParent);
        newLadder.tag = "Ladder";
        var col = newLadder.GetComponent<BoxCollider2D>();
        if (col != null) col.isTrigger = true;
        var rootRenderer = newLadder.GetComponent<SpriteRenderer>();
        if (rootRenderer != null)
            Destroy(rootRenderer);
        EnsureMovingPlatformLadder(newLadder);
        return newLadder;
    }

    static void EnsureMovingPlatformLadder(GameObject ladder)
    {
        if (ladder != null && ladder.GetComponent<MovingPlatformLadder>() == null)
            ladder.AddComponent<MovingPlatformLadder>();
    }

    static float GetSpriteWorldHeight(Sprite sprite)
    {
        return sprite != null ? sprite.bounds.size.y : 0f;
    }

    static SpriteRenderer GetOrCreateLadderPart(GameObject root, string childName, Sprite sprite)
    {
        var existing = root.transform.Find(childName);
        if (existing != null)
        {
            var sr = existing.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.sprite = sprite;
                sr.enabled = sprite != null;
                return sr;
            }
        }
        var go = new GameObject(childName);
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localScale = Vector3.one;
        go.transform.localRotation = Quaternion.identity;
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.enabled = sprite != null;
        return renderer;
    }

    void ReturnLadderToPool(GameObject ladder)
    {
        ladder.SetActive(false);
        ladder.transform.SetParent(_ladderParent);
        _pool.Enqueue(ladder);
    }

    void DespawnLadder(GameObject ladder)
    {
        if (ladder == null) return;

        if (_onLadderDeactivated != null)
        {
            _onLadderDeactivated(ladder);
            return;
        }

        ReturnLadderToPool(ladder);
    }

    void CreateLadder(CloudPlatform lower, CloudPlatform upper)
    {
        var ladder = GetLadderFromPool();
        UpdateLadderPosition(lower, upper, ladder);
        _ladders[(lower, upper)] = ladder;
        _onLadderActivated?.Invoke(ladder, lower, upper);
    }

    /// <summary>Top or bottom Y of colliders intersecting a vertical line at worldX. Considers all non-trigger colliders.</summary>
    static float GetEdgeYAtX(CloudPlatform platform, float worldX, bool top)
    {
        float bestY = top ? float.MinValue : float.MaxValue;
        bool found = false;
        var colliders = platform.GetComponentsInChildren<Collider2D>();
        for (int i = 0; i < colliders.Length; i++)
        {
            var col = colliders[i];
            if (col == null || !col.enabled || col.isTrigger) continue;
            Bounds cb = col.bounds;
            if (worldX < cb.min.x - HorizontalEdgeTolerance || worldX > cb.max.x + HorizontalEdgeTolerance)
                continue;

            float edgeY;
            if (col is PolygonCollider2D poly && TryGetPolygonEdgeY(poly, worldX, top, out float polyY))
            {
                edgeY = polyY;
            }
            else
            {
                edgeY = top ? cb.max.y : cb.min.y;
            }

            bestY = top ? Mathf.Max(bestY, edgeY) : Mathf.Min(bestY, edgeY);
            found = true;
        }

        if (found) return bestY;

        Bounds fallback = platform.GetBounds();
        return top ? fallback.max.y : fallback.min.y;
    }

    /// <summary>Rebuilds ladder visuals and collider between two cloud platforms.
    /// Public so NetworkCloudLadderController can call it on clients.</summary>
    public void UpdateLadderPosition(CloudPlatform lower, CloudPlatform upper, GameObject ladder)
    {
        Bounds bl = lower.GetMainBounds();
        Bounds bu = upper.GetMainBounds();

        float overlapMin, overlapMax;
        bool hasOverlap = TryGetHorizontalOverlap(lower, upper, out overlapMin, out overlapMax);
        float centerX = (bl.center.x + bu.center.x) * 0.5f;
        float x = hasOverlap ? Mathf.Clamp(centerX, overlapMin, overlapMax) : centerX;
        float yMin, yMax;
        if (ladderInsetIntoCloud > 0f)
        {
            float lowerTopY = GetEdgeYAtX(lower, x, true);
            float upperBottomY = GetEdgeYAtX(upper, x, false);
            yMin = lowerTopY - ladderInsetIntoCloud;
            yMax = upperBottomY + ladderInsetIntoCloud;
        }
        else
        {
            yMin = bl.max.y;
            yMax = bu.min.y;
        }
        float height = Mathf.Max(0.1f, yMax - yMin);
        float y = (yMin + yMax) * 0.5f;

        ladder.transform.position = new Vector3(x, y, ladder.transform.position.z);
        ladder.transform.localScale = Vector3.one;

        float topH = GetSpriteWorldHeight(ladderTopSprite);
        float bottomH = GetSpriteWorldHeight(ladderBottomSprite);
        float middleH = GetSpriteWorldHeight(ladderMiddleSprite);

        float middleTotal = height - topH - bottomH;
        float step = middleH - Mathf.Clamp(middleOverlap, 0f, middleH - 0.001f);
        int middleCount = 0;
        if (middleTotal > 0.001f && step > 0.001f)
            middleCount = Mathf.Max(1, 1 + Mathf.CeilToInt((middleTotal - middleH) / step));

        var bottomTr = GetOrCreateLadderPart(ladder, ChildNameBottom, ladderBottomSprite).transform;
        bottomTr.localPosition = new Vector3(0f, -height * 0.5f + bottomH * 0.5f, 0f);
        bottomTr.localScale = Vector3.one;

        for (int i = 0; i < middleCount; i++)
        {
            float localY = -height * 0.5f + bottomH + middleH * 0.5f + i * step;
            var partName = ChildNameMiddlePrefix + i;
            var middleSr = GetOrCreateLadderPart(ladder, partName, ladderMiddleSprite);
            middleSr.transform.localPosition = new Vector3(0f, localY, 0f);
            middleSr.transform.localScale = Vector3.one;
            middleSr.gameObject.SetActive(true);
        }
        for (int i = middleCount; i < MaxLadderMiddleSegments; i++)
        {
            var excess = ladder.transform.Find(ChildNameMiddlePrefix + i);
            if (excess != null) excess.gameObject.SetActive(false);
            else break;
        }

        var topTr = GetOrCreateLadderPart(ladder, ChildNameTop, ladderTopSprite).transform;
        topTr.localPosition = new Vector3(0f, height * 0.5f - topH * 0.5f, 0f);
        topTr.localScale = Vector3.one;

        var col = ladder.GetComponent<BoxCollider2D>();
        if (col != null)
        {
            col.size = new Vector2(ladderWidth, height);
            col.offset = Vector2.zero;
        }
    }

    static bool TryGetPolygonEdgeY(PolygonCollider2D poly, float worldX, bool top, out float edgeY)
    {
        edgeY = top ? float.MinValue : float.MaxValue;
        var path = poly.GetPath(0);
        if (path != null && path.Length >= 2)
        {
            var t = poly.transform;
            for (int i = 0; i < path.Length; i++)
            {
                int j = (i + 1) % path.Length;
                Vector2 p0 = t.TransformPoint(path[i]);
                Vector2 p1 = t.TransformPoint(path[j]);
                float x0 = p0.x;
                float x1 = p1.x;
                if (!((x0 <= worldX && worldX <= x1) || (x1 <= worldX && worldX <= x0)))
                    continue;

                if (Mathf.Abs(x1 - x0) < 0.0001f)
                {
                    edgeY = top ? Mathf.Max(edgeY, Mathf.Max(p0.y, p1.y)) : Mathf.Min(edgeY, Mathf.Min(p0.y, p1.y));
                    continue;
                }

                float tSeg = Mathf.Clamp01((worldX - x0) / (x1 - x0));
                float y = Mathf.Lerp(p0.y, p1.y, tSeg);
                edgeY = top ? Mathf.Max(edgeY, y) : Mathf.Min(edgeY, y);
            }
            if (top && edgeY > float.MinValue) return true;
            if (!top && edgeY < float.MaxValue) return true;
        }
        return false;
    }

    static bool TryGetHorizontalOverlap(CloudPlatform lower, CloudPlatform upper, out float overlapMin, out float overlapMax)
    {
        overlapMin = float.MaxValue;
        overlapMax = float.MinValue;
        bool found = false;
        var lowerCols = lower.GetComponentsInChildren<Collider2D>();
        var upperCols = upper.GetComponentsInChildren<Collider2D>();

        for (int i = 0; i < lowerCols.Length; i++)
        {
            var lc = lowerCols[i];
            if (lc == null || !lc.enabled || lc.isTrigger) continue;
            Bounds lb = lc.bounds;
            for (int j = 0; j < upperCols.Length; j++)
            {
                var uc = upperCols[j];
                if (uc == null || !uc.enabled || uc.isTrigger) continue;
                Bounds ub = uc.bounds;
                float min = Mathf.Max(lb.min.x, ub.min.x);
                float max = Mathf.Min(lb.max.x, ub.max.x);
                if (min < max)
                {
                    float width = max - min;
                    if (!found || width > (overlapMax - overlapMin))
                    {
                        overlapMin = min;
                        overlapMax = max;
                        found = true;
                    }
                }
            }
        }

        if (found) return true;

        Bounds bl = lower.GetMainBounds();
        Bounds bu = upper.GetMainBounds();
        overlapMin = Mathf.Max(bl.min.x, bu.min.x);
        overlapMax = Mathf.Min(bl.max.x, bu.max.x);
        return overlapMin < overlapMax;
    }
}
