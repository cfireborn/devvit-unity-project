using UnityEngine;

/// <summary>
/// ScriptableObject that controls cloud spawning and lane behavior.
/// Assign to CloudManager.settings in the Inspector.
/// </summary>
[CreateAssetMenu(fileName = "CloudBehaviorSettings", menuName = "Scriptable Objects/CloudBehaviorSettings")]
public class CloudBehaviorSettings : ScriptableObject
{
    [Header("Lane Layout")]
    [Tooltip("Vertical distance between adjacent lane centers in world units.")]
    public float laneSpacing = 0.5f;
    [Tooltip("Y offset applied to all lane positions (world units). Lanes are at baseY + laneYOffset + i * laneSpacing.")]
    public float laneYOffset = 0f;
    [Tooltip("Per-lane fixed Y offset: on lane activation, rolled once in [-laneHeightVariation, +laneHeightVariation]. 0 = lane uses baseline worldY only.")]
    [Min(0f)]
    public float laneHeightVariation = 0f;
    [Tooltip("Per-cloud random Y jitter in [-cloudHeightVariation, +cloudHeightVariation] around the lane line.")]
    [Min(0f)]
    public float cloudHeightVariation = 0f;

    [Header("Viewport")]
    [Tooltip("Extra world units added to each side of the camera frustum for spawn/despawn and lane activation.")]
    [Min(0f)]
    public float viewportMargin = 2f;
    [Tooltip("When camera/viewport is unavailable (e.g. headless server before client sync), use this half-width in world units.")]
    [Min(0.1f)]
    public float fallbackViewportHalfWidth = 15f;
    [Tooltip("When camera/viewport is unavailable, use this half-height in world units.")]
    [Min(0.1f)]
    public float fallbackViewportHalfHeight = 8.5f;

    [Header("Cloud Density (per active lane)")]
    [Tooltip("When a lane activates, fixed edge-to-edge gap between clouds (main collider X) is chosen uniformly from [minCloudSpacing, maxCloudSpacing].")]
    [Min(0.1f)]
    public float minCloudSpacing = 4f;
    [Tooltip("Upper end of the range for the lane gap (must be >= minCloudSpacing).")]
    [Min(0.1f)]
    public float maxCloudSpacing = 8f;

    [Header("Cloud Size (main collider bounds after scale)")]
    [Tooltip("Minimum world width of main collider bounds.")]
    [Min(0.01f)]
    public float minCloudMainBoundsWidth = 0.5f;
    [Tooltip("Maximum world width of main collider bounds.")]
    [Min(0.01f)]
    public float maxCloudMainBoundsWidth = 2f;
    [Tooltip("Minimum world height of main collider bounds.")]
    [Min(0.01f)]
    public float minCloudMainBoundsHeight = 0.5f;
    [Tooltip("Maximum world height of main collider bounds.")]
    [Min(0.01f)]
    public float maxCloudMainBoundsHeight = 2f;

    [Header("Cloud Movement")]
    [Tooltip("Speed magnitude range. Direction (sign) is randomized per lane on activation.")]
    public Vector2 speedRange = new Vector2(1f, 3f);

    [Header("Dynamic Cloud Cap")]
    [Tooltip("Maximum number of dynamically spawned clouds that can be active at once across all lanes. 0 = unlimited.")]
    [Min(0)]
    public int maxDynamicClouds = 50;

#if UNITY_EDITOR
    void OnValidate()
    {
        if (maxCloudSpacing < minCloudSpacing)
            maxCloudSpacing = minCloudSpacing;
        if (maxCloudMainBoundsWidth < minCloudMainBoundsWidth)
            maxCloudMainBoundsWidth = minCloudMainBoundsWidth;
        if (maxCloudMainBoundsHeight < minCloudMainBoundsHeight)
            maxCloudMainBoundsHeight = minCloudMainBoundsHeight;
        if (speedRange.y < speedRange.x)
            speedRange.y = speedRange.x;
        UnityEditor.SceneView.RepaintAll();
    }
#endif
}
