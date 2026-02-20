using UnityEngine;

public class Goal : MonoBehaviour
{
    [Header("Goal Data")]
    [Tooltip("When set, location is read from this transform's position at runtime. Assign in scene (e.g. from ItemDeliveryTrigger).")]
    public Transform locationSource;
    [Tooltip("Fallback position when locationSource is null.")]
    public Vector3 location;
    public Sprite sprite;
    public string type = "delivery";

    /// <summary>Current world position: locationSource.position when set, otherwise location.</summary>
    public Vector3 Location => locationSource != null ? locationSource.position : location;
}
