using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns ladders between clouds when they are within range and one is above the other (with a gap).
/// Does not spawn when clouds are touching.
/// </summary>
public class CloudLadderController : MonoBehaviour
{
    [Header("References")]
    public CloudManager cloudManager;
    [Tooltip("Prefab for the ladder. Must have tag 'Ladder', BoxCollider2D (trigger), and optionally SpriteRenderer. Will be scaled vertically to fit between clouds.")]
    public GameObject ladderPrefab;

    [Header("Params")]
    [Tooltip("Ladder appears when clouds are within this horizontal distance.")]
    public float maxDistance = 4f;
    [Tooltip("Minimum vertical gap between clouds. No ladder if they overlap or touch.")]
    public float minVerticalGap = 0.5f;
    [Tooltip("Maximum vertical gap. Clouds too far apart don't get a ladder.")]
    public float maxVerticalGap = 8f;
    [Tooltip("Maximum number of ladders that can be active. Pool prevents spawning beyond this.")]
    public int maxLadders = 10;

    readonly Dictionary<(CloudPlatform, CloudPlatform), GameObject> _ladders = new Dictionary<(CloudPlatform, CloudPlatform), GameObject>();
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

        for (int i = 0; i < platformList.Count; i++)
        {
            for (int j = i + 1; j < platformList.Count; j++)
            {
                var a = platformList[i];
                var b = platformList[j];
                if (ShouldHaveLadder(a, b))
                {
                    var pair = OrderPair(a, b);
                    validPairs.Add(pair);
                    if (!_ladders.ContainsKey(pair) && _ladders.Count < maxLadders)
                    {
                        CreateLadder(pair.Item1, pair.Item2);
                    }
                }
            }
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
            {
                ReturnLadderToPool(ladder);
            }
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
        Bounds ba = a.GetBounds();
        Bounds bb = b.GetBounds();
        return ba.min.y < bb.min.y ? (a, b) : (b, a);
    }

    bool ShouldHaveLadder(CloudPlatform a, CloudPlatform b)
    {
        Bounds ba = a.GetBounds();
        Bounds bb = b.GetBounds();

        float dx = Mathf.Abs(ba.center.x - bb.center.x);
        if (dx > maxDistance) return false;

        float overlapMin = Mathf.Max(ba.min.x, bb.min.x);
        float overlapMax = Mathf.Min(ba.max.x, bb.max.x);
        if (overlapMin >= overlapMax) return false;

        CloudPlatform lower, upper;
        if (ba.min.y < bb.min.y) { lower = a; upper = b; } else { lower = b; upper = a; }
        Bounds bl = lower.GetBounds();
        Bounds bu = upper.GetBounds();

        float gap = bu.min.y - bl.max.y;
        if (gap < minVerticalGap) return false;
        if (gap > maxVerticalGap) return false;

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
        return newLadder;
    }

    void ReturnLadderToPool(GameObject ladder)
    {
        ladder.SetActive(false);
        ladder.transform.SetParent(_ladderParent);
        _pool.Enqueue(ladder);
    }

    void CreateLadder(CloudPlatform lower, CloudPlatform upper)
    {
        var ladder = GetLadderFromPool();
        _ladders[(lower, upper)] = ladder;
        UpdateLadderPosition(lower, upper, ladder);
    }

    void UpdateLadderPosition(CloudPlatform lower, CloudPlatform upper, GameObject ladder)
    {
        Bounds bl = lower.GetBounds();
        Bounds bu = upper.GetBounds();

        float x = (bl.center.x + bu.center.x) * 0.5f;
        float yMin = bl.max.y;
        float yMax = bu.min.y;
        float y = (yMin + yMax) * 0.5f;
        float height = yMax - yMin;

        ladder.transform.position = new Vector3(x, y, ladder.transform.position.z);
        ladder.transform.localScale = new Vector3(1f, Mathf.Max(0.1f, height), 1f);

        var col = ladder.GetComponent<BoxCollider2D>();
        if (col != null)
        {
            var size = col.size;
            size.y = 1f;
            col.size = size;
            col.offset = Vector2.zero;
        }
    }
}
