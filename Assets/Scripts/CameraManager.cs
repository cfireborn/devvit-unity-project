using UnityEngine;

/// <summary>
/// 2D camera controller that smoothly follows a target (e.g. the player).
/// </summary>
public class CameraManager : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Follow")]
    [Tooltip("Smooth damp time. Lower = snappier follow.")]
    public float smoothTime = 0.2f;
    [Tooltip("Optional: only move camera when target exceeds this distance from camera center. 0 = always follow.")]
    public float deadZoneRadius = 0f;

    Vector3 _velocity = Vector3.zero;

    void Start()
    {
        var gameServices = FindFirstObjectByType<GameServices>();
        if (gameServices != null)
        {
            gameServices.RegisterCameraManager(this);
            var p = gameServices.GetPlayer();
            if (p != null) target = p.transform;
            gameServices.onPlayerRegistered += OnPlayerRegistered;
        }
    }

    void OnDestroy()
    {
        var gameServices = FindFirstObjectByType<GameServices>();
        if (gameServices != null)
            gameServices.onPlayerRegistered -= OnPlayerRegistered;
    }

    void OnPlayerRegistered()
    {
        var gameServices = FindFirstObjectByType<GameServices>();
        if (gameServices != null)
        {
            var p = gameServices.GetPlayer();
            if (p != null) target = p.transform;
        }
    }

    void LateUpdate()
    {
        if (target == null) return;

        Vector3 targetPos = target.position;
        Vector3 camPos = transform.position;

        float desiredZ = camPos.z;
        Vector3 desired = new Vector3(targetPos.x, targetPos.y, desiredZ);

        if (deadZoneRadius > 0f)
        {
            Vector2 delta = new Vector2(targetPos.x - camPos.x, targetPos.y - camPos.y);
            if (delta.sqrMagnitude <= deadZoneRadius * deadZoneRadius)
            {
                return;
            }
        }

        transform.position = Vector3.SmoothDamp(camPos, desired, ref _velocity, smoothTime);
    }
}
