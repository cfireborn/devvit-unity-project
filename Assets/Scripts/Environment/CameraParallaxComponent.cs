using UnityEngine;

/// <summary>
/// Offsets an object's position each frame based on camera movement, scaled by depth.
/// Higher depth = further = slower; negative depth = closer = faster.
/// </summary>
public class CameraParallaxComponent : MonoBehaviour
{
    [Header("Parallax")]
    [Tooltip("Higher = further away, moves slower. Negative = closer than foreground, moves faster.")]
    public float depth = 0f;

    Transform _cameraTransform;
    Vector3 _lastCameraPosition;

    void Start()
    {
        var gameServices = FindFirstObjectByType<GameServices>();
        if (gameServices != null)
        {
            var cm = gameServices.GetCameraManager();
            if (cm != null)
            {
                _cameraTransform = cm.transform;
                _lastCameraPosition = _cameraTransform.position;
            }
            gameServices.onCameraManagerRegistered += OnCameraManagerRegistered;
        }
    }

    void OnDestroy()
    {
        var gameServices = FindFirstObjectByType<GameServices>();
        if (gameServices != null)
            gameServices.onCameraManagerRegistered -= OnCameraManagerRegistered;
    }

    void OnCameraManagerRegistered()
    {
        var gameServices = FindFirstObjectByType<GameServices>();
        if (gameServices != null)
        {
            var cm = gameServices.GetCameraManager();
            if (cm != null)
            {
                _cameraTransform = cm.transform;
                _lastCameraPosition = _cameraTransform.position;
            }
        }
    }

    void LateUpdate()
    {
        if (_cameraTransform == null) return;

        Vector3 currentCamPos = _cameraTransform.position;
        Vector3 cameraDelta = currentCamPos - _lastCameraPosition;
        float factor = 1f / Mathf.Max(0.01f, 1f + depth);
        transform.position += cameraDelta * factor;
        _lastCameraPosition = currentCamPos;
    }
}
