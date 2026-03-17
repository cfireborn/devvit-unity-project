using UnityEngine;

/// <summary>
/// ScriptableObject that controls all cloud spawning and lane behavior.
/// Create via Assets > Create > Scriptable Objects > CloudBehaviorSettings.
/// Assign to CloudManager.settings in the Inspector.
/// Variation values of 0 mean no variation (uniform spacing/size/Y per lane).
/// </summary>
[CreateAssetMenu(fileName = "CloudBehaviorSettings", menuName = "Scriptable Objects/CloudBehaviorSettings")]
public class CloudBehaviorSettings : ScriptableObject
{
    [Header("Lane Layout")]
    [Tooltip("Vertical distance between adjacent lane centers in world units.")]
    public float laneSpacing = 0.5f;
    [Tooltip("Y offset applied to all lane positions (world units). Lanes are at baseY + laneYOffset + i * laneSpacing.")]
    public float laneYOffset = 0f;
    [Tooltip("A lane activates when any player's Y is within this vertical distance of the lane center.")]
    public float laneActivationDistance = 10f;
    [Tooltip("Random Y offset applied to each cloud within its lane at spawn (±this value). 0 = exact lane center.")]
    [Min(0f)]
    public float laneHeightVariation = 0.2f;

    [Header("Viewport")]
    [Tooltip("Visible X extent per player: viewport = union of (player.x ± viewportHalfWidth). Clouds outside viewport are pooled.")]
    public float viewportHalfWidth = 15f;
    [Tooltip("Spawn at viewport edge ± this margin so clouds are off-screen when created and travel in.")]
    public float spawnMargin = 5f;

    [Header("Cloud Density (per active lane)")]
    [Tooltip("Per-lane base gap is chosen from this range. Gap = space between cloud boundary edges (world units).")]
    [Min(0.1f)]
    public float minCloudSpacing = 4f;
    [Tooltip("Maximum edge-to-edge gap between clouds (world units).")]
    [Min(0.1f)]
    public float maxCloudSpacing = 8f;
    [Tooltip("Variation in edge-to-edge gap. 0 = all gaps equal lane base spacing.")]
    [Min(0f)]
    public float spacingVariation = 0f;

    [Header("Cloud Size (radius = half-height)")]
    [Tooltip("Cloud prefab is scaled so its Y bounds fit inside this radius (world units). Per-lane radius is chosen from this range.")]
    [Min(0.1f)]
    public float minCloudRadius = 0.5f;
    [Tooltip("Maximum cloud radius (world units).")]
    [Min(0.1f)]
    public float maxCloudRadius = 1.5f;
    [Tooltip("Per-cloud radius variation. 0 = one radius per lane for all clouds.")]
    [Min(0f)]
    public float radiusVariation = 0f;

    [Header("Cloud Movement")]
    [Tooltip("Speed magnitude range. A random magnitude is chosen from this range; direction (sign) is randomized per lane on each activation.")]
    public Vector2 speedRange = new Vector2(1f, 3f);

    [Header("Dynamic Cloud Cap")]
    [Tooltip("Maximum number of dynamically spawned clouds that can be active at once across all lanes. 0 = unlimited.")]
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
        if (maxCloudRadius < minCloudRadius)
            maxCloudRadius = minCloudRadius;
        if (speedRange.y < speedRange.x)
            speedRange.y = speedRange.x;
        UnityEditor.SceneView.RepaintAll();
    }
#endif
}
