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
    const float HorizontalEdgeTolerance = 0.001f;
    const float PairEvaluationInterval = 0.1f;
    const float ExistingGeometryValidationInterval = 0.05f;

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
    [Tooltip("Overlap between middle ladder sprites in world units. Also used to tuck the middle section slightly into the top/bottom caps to hide seams.")]
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
    [Tooltip("How long a ladder remains usable after a connected cloud begins its evaporation animation.")]
    [Min(0f)]
    public float ladderCloudEvaporationHoldSeconds = 0f;

    // Injected by NetworkCloudLadderController before CloudLadderController is enabled.
    internal Action<GameObject, CloudPlatform, CloudPlatform> _onLadderActivated;
    // null = ReturnLadderToPool path. Server: FishNet Despawn or Destroy.
    internal Action<GameObject> _onLadderDeactivated;

    sealed class RetiringLadder
    {
        public GameObject ladder;
        public CloudPlatform lower;
        public CloudPlatform upper;
        public int lowerActivationVersion;
        public int upperActivationVersion;
        public float removeAt;
    }

    struct LadderCandidate
    {
        public (CloudPlatform lower, CloudPlatform upper) pair;
        public float surfaceGap;
        public float horizontalDistance;
        public bool forced;
        public bool existing;
    }

    readonly List<CloudPlatform> _cachedPlatformList = new List<CloudPlatform>();
    readonly Dictionary<CloudPlatform, Bounds> _platformBoundsScratch = new Dictionary<CloudPlatform, Bounds>();
    readonly HashSet<(CloudPlatform, CloudPlatform)> _validPairsScratch = new HashSet<(CloudPlatform, CloudPlatform)>();
    readonly HashSet<CloudPlatform> _hasLadderAboveScratch = new HashSet<CloudPlatform>();
    readonly HashSet<CloudPlatform> _hasLadderBelowScratch = new HashSet<CloudPlatform>();
    readonly HashSet<GameObject> _activeSetScratch = new HashSet<GameObject>();
    readonly List<(CloudPlatform, CloudPlatform)> _toRemoveScratch = new List<(CloudPlatform, CloudPlatform)>();
    readonly List<Collider2D> _ladderOverlapScratch = new List<Collider2D>(16);
    readonly List<LadderCandidate> _candidateScratch = new List<LadderCandidate>();

    readonly Dictionary<(CloudPlatform, CloudPlatform), GameObject> _ladders = new Dictionary<(CloudPlatform, CloudPlatform), GameObject>();
    readonly Dictionary<(CloudPlatform, CloudPlatform), (int lower, int upper)> _ladderEndpointVersions =
        new Dictionary<(CloudPlatform, CloudPlatform), (int lower, int upper)>();
    readonly HashSet<(CloudPlatform, CloudPlatform)> _forcedPairs = new HashSet<(CloudPlatform, CloudPlatform)>();
    readonly List<RetiringLadder> _retiringLadders = new List<RetiringLadder>();
    readonly Queue<GameObject> _pool = new Queue<GameObject>();
    Transform _ladderParent;
    float _nextPairEvaluationTime;
    float _nextExistingGeometryValidationTime;

    void Start()
    {
        _ladderParent = new GameObject("Ladders").transform;
        _ladderParent.SetParent(transform);
    }

    void LateUpdate()
    {
        if (cloudManager == null || ladderPrefab == null) return;

        var activeSet = _activeSetScratch;
        activeSet.Clear();
        foreach (var go in cloudManager.GetActiveClouds())
            if (go != null) activeSet.Add(go);

        UpdateRetiringLadders(activeSet);
        PruneUnavailableLadders(activeSet);

        // Creation/re-ranking may wait one topology interval. Endpoint lifecycle and
        // generation still invalidate above every rendered frame; exact geometry is
        // checked halfway between topology passes. Forced creation remains immediate.
        bool topologyDue = Time.unscaledTime >= _nextPairEvaluationTime;
        if (topologyDue)
        {
            var platformList = GetActiveCloudPlatforms();
            var validPairs = ComputeValidPairs(platformList);
            RemoveInvalidLadders(validPairs, activeSet);
            CreateMissingLadders(validPairs);
            _nextPairEvaluationTime = Time.unscaledTime + PairEvaluationInterval;
            _nextExistingGeometryValidationTime = Time.unscaledTime + ExistingGeometryValidationInterval;
        }
        else if (Time.unscaledTime >= _nextExistingGeometryValidationTime)
        {
            PruneInvalidLadderGeometry(activeSet);
            _nextExistingGeometryValidationTime = Time.unscaledTime + ExistingGeometryValidationInterval;
        }
        UpdateAllLadderPositions();
    }

    List<CloudPlatform> GetActiveCloudPlatforms()
    {
        _cachedPlatformList.Clear();
        _platformBoundsScratch.Clear();
        foreach (var go in cloudManager.GetActiveClouds())
        {
            if (go == null) continue;
            var p = go.GetComponent<CloudPlatform>();
            if (p == null) continue;
            _cachedPlatformList.Add(p);
            _platformBoundsScratch[p] = p.GetMainBounds();
        }
        _cachedPlatformList.Sort((a, b) =>
        {
            // Sorting by lower surface makes the vertical early-out in
            // ComputeValidPairs mathematically safe for every later candidate.
            int byHeight = _platformBoundsScratch[a].min.y.CompareTo(_platformBoundsScratch[b].min.y);
            return byHeight != 0 ? byHeight : a.GetInstanceID().CompareTo(b.GetInstanceID());
        });
        return _cachedPlatformList;
    }

    HashSet<(CloudPlatform, CloudPlatform)> ComputeValidPairs(List<CloudPlatform> platformList)
    {
        _validPairsScratch.Clear();
        _candidateScratch.Clear();
        _hasLadderAboveScratch.Clear();
        _hasLadderBelowScratch.Clear();

        foreach (var retiring in _retiringLadders)
        {
            if (retiring.lower != null) _hasLadderAboveScratch.Add(retiring.lower);
            if (retiring.upper != null) _hasLadderBelowScratch.Add(retiring.upper);
        }

        for (int i = 0; i < platformList.Count; i++)
        {
            for (int j = i + 1; j < platformList.Count; j++)
            {
                var a = platformList[i];
                var b = platformList[j];
                Bounds boundsA = _platformBoundsScratch[a];
                Bounds boundsB = _platformBoundsScratch[b];
                // The list is sorted by min.y. Once this lower cloud is more than
                // maxVerticalGap below a candidate, every later candidate is too.
                if (boundsB.min.y - boundsA.max.y > maxVerticalGap)
                    break;
                if (Mathf.Abs(boundsA.center.x - boundsB.center.x) > maxDistance)
                    continue;
                if (boundsA.max.x <= boundsB.min.x || boundsB.max.x <= boundsA.min.x)
                    continue;
                float aabbSurfaceGap = Mathf.Max(boundsB.min.y - boundsA.max.y, boundsA.min.y - boundsB.max.y);
                if (aabbSurfaceGap > maxVerticalGap)
                    continue;
                var pair = OrderPair(a, b);
                bool forced = _forcedPairs.Contains(pair);
                if ((!forced && (!a.canBuildLadder || !b.canBuildLadder)) || a.IsDespawning || b.IsDespawning)
                    continue;
                if (!TryGetLadderGeometry(a, b, out float surfaceGap, out float horizontalDistance))
                    continue;

                _candidateScratch.Add(new LadderCandidate
                {
                    pair = pair,
                    surfaceGap = surfaceGap,
                    horizontalDistance = horizontalDistance,
                    forced = forced,
                    existing = IsCurrentBinding(pair)
                });
            }
        }

        _candidateScratch.Sort(CompareCandidates);
        int availableLadders = Mathf.Max(0, maxLadders - _retiringLadders.Count);
        for (int i = 0; i < _candidateScratch.Count && _validPairsScratch.Count < availableLadders; i++)
        {
            LadderCandidate candidate = _candidateScratch[i];
            var (lower, upper) = candidate.pair;
            if (_hasLadderAboveScratch.Contains(lower) || _hasLadderBelowScratch.Contains(upper))
                continue;
            if (!IsCurrentBinding(candidate.pair) && WouldNewLadderOverlapPlayer(lower, upper))
                continue;

            _validPairsScratch.Add(candidate.pair);
            _hasLadderAboveScratch.Add(lower);
            _hasLadderBelowScratch.Add(upper);
        }

        return _validPairsScratch;
    }

    static int CompareCandidates(LadderCandidate a, LadderCandidate b)
    {
        if (a.forced != b.forced) return a.forced ? -1 : 1;
        if (Mathf.Abs(a.surfaceGap - b.surfaceGap) > 0.05f)
            return a.surfaceGap.CompareTo(b.surfaceGap);
        if (a.existing != b.existing) return a.existing ? -1 : 1;
        int byHorizontal = a.horizontalDistance.CompareTo(b.horizontalDistance);
        if (byHorizontal != 0) return byHorizontal;
        int byLower = a.pair.lower.GetInstanceID().CompareTo(b.pair.lower.GetInstanceID());
        return byLower != 0
            ? byLower
            : a.pair.upper.GetInstanceID().CompareTo(b.pair.upper.GetInstanceID());
    }

    void CreateMissingLadders(HashSet<(CloudPlatform, CloudPlatform)> validPairs)
    {
        foreach (var pair in validPairs)
        {
            if (!_ladders.ContainsKey(pair) && TotalManagedLadderCount < maxLadders)
                CreateLadder(pair.Item1, pair.Item2);
        }
    }

    bool IsCurrentBinding((CloudPlatform lower, CloudPlatform upper) pair)
    {
        return _ladders.ContainsKey(pair) &&
            _ladderEndpointVersions.TryGetValue(pair, out var versions) &&
            pair.lower != null && pair.upper != null &&
            pair.lower.ActivationVersion == versions.lower && pair.upper.ActivationVersion == versions.upper;
    }

    void RemoveInvalidLadders(HashSet<(CloudPlatform, CloudPlatform)> validPairs, HashSet<GameObject> activeSet)
    {
        _toRemoveScratch.Clear();
        foreach (var pair in _forcedPairs)
        {
            if (pair.Item1 == null || pair.Item2 == null ||
                pair.Item1.IsDespawning || pair.Item2.IsDespawning ||
                !activeSet.Contains(pair.Item1.gameObject) || !activeSet.Contains(pair.Item2.gameObject))
                _toRemoveScratch.Add(pair);
        }
        foreach (var pair in _toRemoveScratch)
        {
            _forcedPairs.Remove(pair);
            RemoveOrRetireLadder(pair, activeSet);
        }

        _toRemoveScratch.Clear();
        foreach (var kvp in _ladders)
        {
            if (!validPairs.Contains(kvp.Key) || !IsCurrentBinding(kvp.Key))
                _toRemoveScratch.Add(kvp.Key);
        }
        foreach (var pair in _toRemoveScratch)
            RemoveOrRetireLadder(pair, activeSet);
    }

    void PruneUnavailableLadders(HashSet<GameObject> activeSet)
    {
        // Keep lifecycle/generation invalidation immediate so a pooled endpoint can
        // never leave a stale ladder behind. The full geometry and obstruction test
        // runs in ComputeValidPairs at PairEvaluationInterval; repeating its
        // Physics2D.OverlapBox for every ladder on every rendered frame is redundant.
        _toRemoveScratch.Clear();
        foreach (var kvp in _ladders)
        {
            var pair = kvp.Key;
            if (pair.Item1 == null || pair.Item2 == null ||
                !activeSet.Contains(pair.Item1.gameObject) || !activeSet.Contains(pair.Item2.gameObject) ||
                pair.Item1.IsDespawning || pair.Item2.IsDespawning || !IsCurrentBinding(pair))
                _toRemoveScratch.Add(pair);
        }
        for (int i = 0; i < _toRemoveScratch.Count; i++)
            RemoveOrRetireLadder(_toRemoveScratch[i], activeSet);
    }

    void PruneInvalidLadderGeometry(HashSet<GameObject> activeSet)
    {
        _toRemoveScratch.Clear();
        GetActiveCloudPlatforms();
        foreach (var kvp in _ladders)
        {
            var pair = kvp.Key;
            if (!TryGetLadderGeometry(pair.Item1, pair.Item2, out _, out _))
                _toRemoveScratch.Add(pair);
        }
        for (int i = 0; i < _toRemoveScratch.Count; i++)
            RemoveOrRetireLadder(_toRemoveScratch[i], activeSet);
    }

    int TotalManagedLadderCount => _ladders.Count + _retiringLadders.Count;

    void RemoveOrRetireLadder((CloudPlatform, CloudPlatform) pair, HashSet<GameObject> activeSet)
    {
        if (!_ladders.TryGetValue(pair, out var ladder)) return;
        _ladders.Remove(pair);
        _ladderEndpointVersions.Remove(pair);
        _forcedPairs.Remove(pair);
        if (ladder == null) return;

        bool lowerGone = pair.Item1 == null || !activeSet.Contains(pair.Item1.gameObject);
        bool upperGone = pair.Item2 == null || !activeSet.Contains(pair.Item2.gameObject);
        bool evaporating = (!lowerGone && pair.Item1.IsDespawning) || (!upperGone && pair.Item2.IsDespawning);

        if (evaporating && ladderCloudEvaporationHoldSeconds > 0f)
            RetireLadder(ladder, pair, ladderCloudEvaporationHoldSeconds);
        else
            DespawnLadder(ladder);
    }

    void RetireLadder(GameObject ladder, (CloudPlatform lower, CloudPlatform upper) pair, float delay)
    {
        if (ladder == null) return;
        _retiringLadders.Add(new RetiringLadder
        {
            ladder = ladder,
            lower = pair.lower,
            upper = pair.upper,
            lowerActivationVersion = pair.lower != null ? pair.lower.ActivationVersion : -1,
            upperActivationVersion = pair.upper != null ? pair.upper.ActivationVersion : -1,
            removeAt = Time.time + Mathf.Max(0f, delay)
        });
    }

    void UpdateRetiringLadders(HashSet<GameObject> activeSet)
    {
        for (int i = _retiringLadders.Count - 1; i >= 0; i--)
        {
            RetiringLadder retiring = _retiringLadders[i];
            bool endpointsActive = retiring.lower != null && retiring.upper != null &&
                activeSet.Contains(retiring.lower.gameObject) && activeSet.Contains(retiring.upper.gameObject) &&
                retiring.lower.ActivationVersion == retiring.lowerActivationVersion &&
                retiring.upper.ActivationVersion == retiring.upperActivationVersion;
            if (retiring.ladder != null && endpointsActive && Time.time < retiring.removeAt)
            {
                UpdateLadderPosition(retiring.lower, retiring.upper, retiring.ladder);
                continue;
            }

            if (retiring.ladder != null)
                DespawnLadder(retiring.ladder);
            _retiringLadders.RemoveAt(i);
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

    /// <summary>True when the current activation of both clouds is connected by a managed ladder.</summary>
    public bool HasLadderBetween(CloudPlatform a, CloudPlatform b)
    {
        if (a == null || b == null || a == b) return false;
        return IsCurrentBinding(OrderPair(a, b));
    }

    /// <summary>Gets the ladder for the current activation of both clouds.</summary>
    public bool TryGetLadderBetween(CloudPlatform a, CloudPlatform b, out GameObject ladder)
    {
        ladder = null;
        if (a == null || b == null || a == b) return false;
        var pair = OrderPair(a, b);
        return IsCurrentBinding(pair) && _ladders.TryGetValue(pair, out ladder) && ladder != null;
    }

    /// <summary>Checks the same physical placement and obstruction rules used by automatic pairing.</summary>
    public bool IsLadderGeometryValid(CloudPlatform a, CloudPlatform b)
    {
        if (a == null || b == null || a == b) return false;
        GetActiveCloudPlatforms();
        return TryGetLadderGeometry(a, b, out _, out _);
    }

    /// <summary>Describes the first placement rule that accepts or rejects a pair.</summary>
    public string GetLadderGeometryDiagnostic(CloudPlatform a, CloudPlatform b)
    {
        if (a == null || b == null || a == b) return "invalid endpoint";
        GetActiveCloudPlatforms();
        TryGetLadderGeometry(a, b, out _, out _, collectDiagnostic: true, out string diagnostic);
        return diagnostic;
    }

    /// <summary>Returns (lower, upper) by vertical position. Used by NetworkCloudLadderController for client ladder rebuild.</summary>
    public static (CloudPlatform, CloudPlatform) OrderPair(CloudPlatform a, CloudPlatform b)
    {
        Bounds ba = a.GetMainBounds();
        Bounds bb = b.GetMainBounds();
        return ba.min.y < bb.min.y ? (a, b) : (b, a);
    }

    bool TryGetLadderGeometry(CloudPlatform a, CloudPlatform b, out float surfaceGap, out float horizontalDistance)
    {
        return TryGetLadderGeometry(a, b, out surfaceGap, out horizontalDistance, collectDiagnostic: false, out _);
    }

    bool TryGetLadderGeometry(
        CloudPlatform a,
        CloudPlatform b,
        out float surfaceGap,
        out float horizontalDistance,
        bool collectDiagnostic,
        out string diagnostic)
    {
        surfaceGap = 0f;
        horizontalDistance = 0f;
        diagnostic = collectDiagnostic ? "valid" : null;
        Bounds ba = a.GetMainBounds();
        Bounds bb = b.GetMainBounds();

        horizontalDistance = Mathf.Abs(ba.center.x - bb.center.x);
        if (horizontalDistance > maxDistance)
        {
            if (collectDiagnostic)
                diagnostic = $"horizontal distance {horizontalDistance:F3} > {maxDistance:F3}";
            return false;
        }

        var (lower, upper) = OrderPair(a, b);
        if (!TryGetHorizontalOverlap(lower, upper, out _, out _))
        {
            if (collectDiagnostic)
                diagnostic = "no physical horizontal overlap";
            return false;
        }
        GetLadderPlacement(lower, upper, out float ladderX, out _, out _);

        float lowerTopY = GetEdgeYAtX(lower, ladderX, true);
        float upperBottomY = GetEdgeYAtX(upper, ladderX, false);
        surfaceGap = upperBottomY - lowerTopY;
        if (surfaceGap < minVerticalGap - 0.05f)
        {
            if (collectDiagnostic)
                diagnostic = $"surface gap {surfaceGap:F3} < {minVerticalGap - 0.05f:F3}";
            return false;
        }
        if (surfaceGap > maxVerticalGap)
        {
            if (collectDiagnostic)
                diagnostic = $"surface gap {surfaceGap:F3} > {maxVerticalGap:F3}";
            return false;
        }

        float obstacleHeight = upperBottomY - lowerTopY - HorizontalEdgeTolerance * 2f;
        if (obstacleHeight <= 0f)
        {
            if (collectDiagnostic)
                diagnostic = $"open span height {obstacleHeight:F3} <= 0";
            return false;
        }

        _ladderOverlapScratch.Clear();
        int overlapCount = Physics2D.OverlapBox(
            new Vector2(ladderX, (lowerTopY + upperBottomY) * 0.5f),
            new Vector2(ladderWidth, obstacleHeight),
            0f,
            ContactFilter2D.noFilter,
            _ladderOverlapScratch);
        for (int i = 0; i < overlapCount; i++)
        {
            Collider2D collider = _ladderOverlapScratch[i];
            if (collider == null || !collider.enabled || collider.isTrigger) continue;
            CloudPlatform other = collider.GetComponentInParent<CloudPlatform>();
            if (other == null || other == lower || other == upper) continue;
            if (_activeSetScratch.Contains(other.gameObject) || _cachedPlatformList.Contains(other))
            {
                if (collectDiagnostic)
                    diagnostic = $"blocked by active cloud '{other.name}' at x={ladderX:F3}, gap={surfaceGap:F3}";
                return false;
            }
        }

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

        var activeSet = _activeSetScratch;
        activeSet.Clear();
        foreach (var go in cloudManager.GetActiveClouds())
            if (go != null) activeSet.Add(go);
        if (!activeSet.Contains(a.gameObject) || !activeSet.Contains(b.gameObject)) return false;

        var pair = OrderPair(a, b);
        if (IsCurrentBinding(pair)) return true;
        if (a.IsDespawning || b.IsDespawning) return false;
        GetActiveCloudPlatforms();
        if (!TryGetLadderGeometry(a, b, out _, out _)) return false;
        if (WouldNewLadderOverlapPlayer(pair.Item1, pair.Item2)) return false;

        bool lowerHasAbove = false, upperHasBelow = false;
        foreach (var kvp in _ladders)
        {
            if (kvp.Key.Item1 == pair.Item1) lowerHasAbove = true;
            if (kvp.Key.Item2 == pair.Item2) upperHasBelow = true;
        }
        foreach (var retiring in _retiringLadders)
        {
            if (retiring.lower == pair.Item1) lowerHasAbove = true;
            if (retiring.upper == pair.Item2) upperHasBelow = true;
        }

        if (lowerHasAbove || upperHasBelow)
            return false;
        if (TotalManagedLadderCount >= maxLadders)
            return false;

        _forcedPairs.Add(pair);
        CreateLadder(pair.Item1, pair.Item2);
        if (!IsCurrentBinding(pair))
        {
            _forcedPairs.Remove(pair);
            return false;
        }
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
            rootRenderer.enabled = false;
        EnsureMovingPlatformLadder(newLadder);
        return newLadder;
    }

    static MovingPlatformLadder EnsureMovingPlatformLadder(GameObject ladder)
    {
        if (ladder == null) return null;
        MovingPlatformLadder moving = ladder.GetComponent<MovingPlatformLadder>();
        return moving != null ? moving : ladder.AddComponent<MovingPlatformLadder>();
    }

    static float GetSpriteWorldHeight(Sprite sprite)
    {
        return sprite != null ? sprite.bounds.size.y : 0f;
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
        _ladderEndpointVersions[(lower, upper)] = (lower.ActivationVersion, upper.ActivationVersion);
        _onLadderActivated?.Invoke(ladder, lower, upper);
    }

    /// <summary>Top or bottom Y of colliders intersecting a vertical line at worldX. Considers all non-trigger colliders.</summary>
    static float GetEdgeYAtX(CloudPlatform platform, float worldX, bool top)
    {
        float bestY = top ? float.MinValue : float.MaxValue;
        bool found = false;
        var colliders = platform.BoundsColliders;
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
        if (lower == null || upper == null || ladder == null) return;

        GetLadderPlacement(lower, upper, out float x, out float y, out float height);

        MovingPlatformLadder presentation = EnsureMovingPlatformLadder(ladder);
        if (presentation == null) return;
        presentation.SetRootPose(x, y);

#if UNITY_SERVER && !UNITY_EDITOR
        // Pure clients rebuild ladder presentation from NetworkLadder endpoints.
        // The dedicated server only needs the authoritative trigger collider; do
        // not create or maintain cap/middle SpriteRenderer children here.
        var serverCollider = presentation.RootCollider;
        if (serverCollider != null)
        {
            serverCollider.size = new Vector2(ladderWidth, height);
            serverCollider.offset = Vector2.zero;
        }
        return;
#else
        if (!presentation.NeedsGeometryRebuild(height, ladderWidth, middleOverlap,
            ladderBottomSprite, ladderMiddleSprite, ladderTopSprite))
            return;

        float topH = GetSpriteWorldHeight(ladderTopSprite);
        float bottomH = GetSpriteWorldHeight(ladderBottomSprite);
        float middleH = GetSpriteWorldHeight(ladderMiddleSprite);

        float middleTotal = height - topH - bottomH;
        float middleToMiddleOverlap = Mathf.Clamp(middleOverlap, 0f, Mathf.Max(0f, middleH - 0.001f));
        float step = middleH - middleToMiddleOverlap;
        int middleCount = 0;
        float firstMiddleY = 0f;
        float lastMiddleY = 0f;
        if (middleTotal > 0.001f && middleH > 0.001f && step > 0.001f)
        {
            float capJoinOverlap = Mathf.Min(
                middleOverlap,
                Mathf.Max(0f, bottomH * 0.5f + middleH * 0.5f - 0.001f),
                Mathf.Max(0f, topH * 0.5f + middleH * 0.5f - 0.001f));

            firstMiddleY = -height * 0.5f + bottomH + middleH * 0.5f - capJoinOverlap;
            lastMiddleY = height * 0.5f - topH - middleH * 0.5f + capJoinOverlap;

            if (lastMiddleY <= firstMiddleY)
            {
                middleCount = 1;
            }
            else
            {
                middleCount = 1 + Mathf.CeilToInt((lastMiddleY - firstMiddleY) / step);
            }
        }

        var bottomTr = presentation.GetBottom(ladderBottomSprite).transform;
        bottomTr.localPosition = new Vector3(0f, -height * 0.5f + bottomH * 0.5f, 0f);
        bottomTr.localScale = Vector3.one;

        for (int i = 0; i < middleCount; i++)
        {
            float localY = middleCount == 1
                ? (firstMiddleY + lastMiddleY) * 0.5f
                : Mathf.Lerp(firstMiddleY, lastMiddleY, i / (float)(middleCount - 1));
            var middleSr = presentation.GetMiddle(i, ladderMiddleSprite);
            if (middleSr == null) break;
            middleSr.transform.localPosition = new Vector3(0f, localY, 0f);
            middleSr.transform.localScale = Vector3.one;
            middleSr.gameObject.SetActive(true);
        }
        presentation.SetActiveMiddleCount(middleCount);

        var topTr = presentation.GetTop(ladderTopSprite).transform;
        topTr.localPosition = new Vector3(0f, height * 0.5f - topH * 0.5f, 0f);
        topTr.localScale = Vector3.one;

        var col = presentation.RootCollider;
        if (col != null)
        {
            col.size = new Vector2(ladderWidth, height);
            col.offset = Vector2.zero;
        }
        presentation.MarkGeometryRebuilt(height, ladderWidth, middleOverlap,
            ladderBottomSprite, ladderMiddleSprite, ladderTopSprite);
#endif
    }

    void GetLadderPlacement(CloudPlatform lower, CloudPlatform upper, out float x, out float y, out float height)
    {
        Bounds bl = lower.GetMainBounds();
        Bounds bu = upper.GetMainBounds();

        float overlapMin, overlapMax;
        bool hasOverlap = TryGetHorizontalOverlap(lower, upper, out overlapMin, out overlapMax);
        float centerX = (bl.center.x + bu.center.x) * 0.5f;
        x = hasOverlap ? Mathf.Clamp(centerX, overlapMin, overlapMax) : centerX;
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
        height = Mathf.Max(0.1f, yMax - yMin);
        y = (yMin + yMax) * 0.5f;
    }

    bool WouldNewLadderOverlapPlayer(CloudPlatform lower, CloudPlatform upper)
    {
        GetLadderPlacement(lower, upper, out float x, out float y, out float height);
        _ladderOverlapScratch.Clear();
        int overlapCount = Physics2D.OverlapBox(
            new Vector2(x, y),
            new Vector2(ladderWidth, height),
            0f,
            ContactFilter2D.noFilter,
            _ladderOverlapScratch);
        for (int i = 0; i < overlapCount; i++)
        {
            Collider2D overlap = _ladderOverlapScratch[i];
            if (overlap == null) continue;
            if (overlap.gameObject.CompareTag("Player")) return true;
            if (overlap.attachedRigidbody != null && overlap.attachedRigidbody.gameObject.CompareTag("Player"))
                return true;
            if (overlap.GetComponentInParent<PlayerControllerM>() != null)
                return true;
        }
        return false;
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
        var lowerCols = lower.BoundsColliders;
        var upperCols = upper.BoundsColliders;

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
