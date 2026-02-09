using UnityEngine;

/// <summary>
/// Registry of shared game references. Holds only references and events; no game logic.
/// Components obtain references from here; GameManagerM orchestrates and pushes refs when available.
/// </summary>
public class GameServices : MonoBehaviour
{
    CameraManager _cameraManager;
    PlayerControllerM _player;
    CloudManager _cloudManager;

    public event System.Action onCameraManagerRegistered;
    public event System.Action onPlayerRegistered;

    public void RegisterCameraManager(CameraManager cm)
    {
        _cameraManager = cm;
        onCameraManagerRegistered?.Invoke();
    }

    public void RegisterPlayer(PlayerControllerM player)
    {
        _player = player;
        onPlayerRegistered?.Invoke();
    }

    public void RegisterCloudManager(CloudManager cm)
    {
        _cloudManager = cm;
    }

    public CameraManager GetCameraManager() => _cameraManager;
    public PlayerControllerM GetPlayer() => _player;
    public CloudManager GetCloudManager() => _cloudManager;
}
