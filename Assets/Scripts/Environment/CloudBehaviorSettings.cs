using UnityEngine;

/// <summary>
/// ScriptableObject that controls all cloud spawning and lane behavior.
/// Create via Assets > Create > Scriptable Objects > CloudBehaviorSettings.
/// Assign to CloudManager.settings in the Inspector.
/// </summary>
[CreateAssetMenu(fileName = "CloudBehaviorSettings", menuName = "Scriptable Objects/CloudBehaviorSettings")]
public class CloudBehaviorSettings : ScriptableObject
{
    [Header("Lane Layout")]
    [Tooltip("Vertical distance between adjacent lane centers in world units.")]
    public float laneSpacing = 0.5f;
    [Tooltip("Total number of world-absolute lanes, starting at Y = 0 going upward.")]
    public int laneCount = 40;
    [Tooltip("A lane activates when any player's Y is within this vertical distance of the lane center.")]
    public float laneActivationDistance = 10f;
    [Tooltip("Random Y offset applied to each cloud within its lane at spawn/recycle (±this value). 0 = exact lane center.")]
    [Min(0f)]
    public float laneHeightVariation = 0.2f;

    [Header("Cloud Density (per active lane)")]
    [Tooltip("Minimum horizontal spacing between cloud centers within a lane (world units).")]
    [Min(0.1f)]
    public float minCloudSpacing = 4f;
    [Tooltip("Maximum horizontal spacing between cloud centers within a lane (world units).")]
    [Min(0.1f)]
    public float maxCloudSpacing = 8f;

    [Header("Cloud Movement")]
    [Tooltip("Speed magnitude range. A random magnitude is chosen from this range; direction (sign) is randomized per lane on each activation.")]
    public Vector2 speedRange = new Vector2(1f, 3f);

    [Header("Cloud Scale")]
    public Vector2 scaleRange = new Vector2(0.8f, 1.2f);

    [Header("Active Window")]
    [Tooltip("Clouds within this horizontal half-distance of the nearest player are kept alive. The window spans from (minPlayerX - this) to (maxPlayerX + this).")]
    public float activeWindowHalfWidth = 20f;
    [Tooltip("Extra buffer past activeWindowHalfWidth before a cloud is recycled back to the entry side.")]
    public float recycleMargin = 2f;
    [Tooltip("Maximum extra distance behind the entry edge at which a recycled cloud re-enters (adds random stagger so clouds don't all reappear at once).")]
    public float recycleReentryMaxGap = 4f;

    [Header("Update Threshold")]
    [Tooltip("Lane activation is only re-evaluated when any player has moved at least this far vertically.")]
    public float distanceThresholdForUpdate = 0.5f;

    [Header("Dynamic Cloud Cap")]
    [Tooltip("Maximum number of dynamically spawned clouds that can be active at once across all lanes. Does not count manually placed scene clouds. 0 = unlimited.")]
    [Min(0)]
    public int maxDynamicClouds = 50;

    [Header("Spawn Zones")]
    [Tooltip("Maximum retries when a spawn position overlaps a CloudNoSpawnZone.")]
    public int maxSpawnRetries = 10;

#if UNITY_EDITOR
    void OnValidate()
    {
        if (maxCloudSpacing < minCloudSpacing)
            maxCloudSpacing = minCloudSpacing;
        if (speedRange.y < speedRange.x)
            speedRange.y = speedRange.x;
        if (scaleRange.y < scaleRange.x)
            scaleRange.y = scaleRange.x;
    }
#endif
}
