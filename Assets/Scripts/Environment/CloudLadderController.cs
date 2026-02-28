using System.Collections.Generic;
using FishNet;
using FishNet.Object;
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

        var clouds = cloudManager.GetActiveClouds();
        var platformList = new List<CloudPlatform>();
        foreach (var go in clouds)
        {
            if (go != null)
            {
                var p = go.GetComponent<CloudPlatform>();
                if (p != null) platformList.Add(p);
            }
        }

        var validPairs = new HashSet<(CloudPlatform, CloudPlatform)>();
        var usedClouds = new HashSet<CloudPlatform>();

        for (int i = 0; i < platformList.Count; i++)
        {
            for (int j = i + 1; j < platformList.Count; j++)
            {
                var a = platformList[i];
                var b = platformList[j];
                if (usedClouds.Contains(a) || usedClouds.Contains(b) || !a.canBuildLadder || !b.canBuildLadder)
                    continue;
                if (ShouldHaveLadder(a, b))
                {
                    var pair = OrderPair(a, b);
                    validPairs.Add(pair);
                    usedClouds.Add(pair.Item1);
                    usedClouds.Add(pair.Item2);
                    if (!_ladders.ContainsKey(pair) && _ladders.Count < maxLadders)
                    {
                        CreateLadder(pair.Item1, pair.Item2);
                    }
                }
            }
        }

        foreach (var pair in _forcedPairs)
            validPairs.Add(pair);

        var activeSet = new HashSet<GameObject>(clouds);
        var forcedToRemove = new List<(CloudPlatform, CloudPlatform)>();
        foreach (var pair in _forcedPairs)
        {
            if (pair.Item1 == null || pair.Item2 == null ||
                !activeSet.Contains(pair.Item1.gameObject) || !activeSet.Contains(pair.Item2.gameObject))
            {
                forcedToRemove.Add(pair);
            }
        }
        foreach (var pair in forcedToRemove)
        {
            _forcedPairs.Remove(pair);
            if (_ladders.TryGetValue(pair, out var ladder) && ladder != null)
                DespawnLadder(ladder);
            _ladders.Remove(pair);
        }

        var toRemove = new List<(CloudPlatform, CloudPlatform)>();
        foreach (var kvp in _ladders)
        {
            if (!validPairs.Contains(kvp.Key))
                toRemove.Add(kvp.Key);
        }
        foreach (var pair in toRemove)
        {
            if (_ladders.TryGetValue(pair, out var ladder) && ladder != null)
                DespawnLadder(ladder);
            _ladders.Remove(pair);
        }

        foreach (var kvp in _ladders)
        {
            if (kvp.Value != null)
                UpdateLadderPosition(kvp.Key.Item1, kvp.Key.Item2, kvp.Value);
        }
    }

    (CloudPlatform, CloudPlatform) OrderPair(CloudPlatform a, CloudPlatform b)
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
    /// Forcibly try to build a ladder between two clouds. Bypasses the one-ladder-per-cloud rule.
    /// Returns true if a ladder exists or was created; false if invalid (same cloud, null, over max, or geometry not valid).
    /// </summary>
    public bool TryBuildLadder(CloudPlatform a, CloudPlatform b)
    {
        if (a == null || b == null || a == b) return false;
        if (cloudManager == null || ladderPrefab == null) return false;

        var pair = OrderPair(a, b);
        if (_ladders.ContainsKey(pair)) return true;
        if (!ShouldHaveLadder(a, b)) return false;

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
            return ladder;
        }
        var newLadder = Instantiate(ladderPrefab, _ladderParent);
        newLadder.tag = "Ladder";
        var col = newLadder.GetComponent<BoxCollider2D>();
        if (col != null) col.isTrigger = true;
        var rootRenderer = newLadder.GetComponent<SpriteRenderer>();
        if (rootRenderer != null)
            Destroy(rootRenderer);
        return newLadder;
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

        if (InstanceFinder.IsServerStarted)
        {
            // Networked server: FishNet despawns on server and destroys on all clients
            var nob = ladder.GetComponent<NetworkObject>();
            if (nob != null && nob.IsSpawned)
                InstanceFinder.ServerManager.Despawn(nob);
            else
                Destroy(ladder);
        }
        else
        {
            ReturnLadderToPool(ladder);
        }
    }

    void CreateLadder(CloudPlatform lower, CloudPlatform upper)
    {
        GameObject ladder;

        if (InstanceFinder.IsServerStarted)
        {
            // Networked server: Instantiate and let FishNet Spawn replicate to all clients.
            // NetworkLadder.SyncCloudIds() tells clients which clouds this ladder bridges.
            ladder = Instantiate(ladderPrefab);
            ladder.tag = "Ladder";
            var col = ladder.GetComponent<BoxCollider2D>();
            if (col != null) col.isTrigger = true;
            var rootRenderer = ladder.GetComponent<SpriteRenderer>();
            if (rootRenderer != null) Destroy(rootRenderer);

            _ladders[(lower, upper)] = ladder;
            UpdateLadderPosition(lower, upper, ladder);

            var nob = ladder.GetComponent<NetworkObject>();
            if (nob != null)
            {
                InstanceFinder.ServerManager.Spawn(nob);

                // Sync cloud IDs so clients know which clouds to derive geometry from
                var nobA = lower.gameObject.GetComponent<NetworkObject>();
                var nobB = upper.gameObject.GetComponent<NetworkObject>();
                int cloudAId = (nobA != null && nobA.IsSpawned) ? nobA.ObjectId : -1;
                int cloudBId = (nobB != null && nobB.IsSpawned) ? nobB.ObjectId : -1;

                var nl = ladder.GetComponent<NetworkLadder>();
                if (nl != null) nl.SyncCloudIds(cloudAId, cloudBId);
            }
        }
        else
        {
            // Offline / non-networked: use pool
            ladder = GetLadderFromPool();
            _ladders[(lower, upper)] = ladder;
            UpdateLadderPosition(lower, upper, ladder);
        }
    }

    /// <summary>Rebuilds ladder visuals and collider between two cloud platforms.
    /// Public so NetworkCloudLadderController can call it on clients.</summary>
    public void UpdateLadderPosition(CloudPlatform lower, CloudPlatform upper, GameObject ladder)
    {
        Bounds bl = lower.GetMainBounds();
        Bounds bu = upper.GetMainBounds();

        float x = (bl.center.x + bu.center.x) * 0.5f;
        float yMin = bl.max.y;
        float yMax = bu.min.y;
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
        for (int i = middleCount; i < 64; i++)
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
}
