using UnityEngine;

public class CameraManager : MonoBehaviour
{

    public Transform target;
    public float followDistance;

    void Update()
    {
        transform.position = target.position - transform.forward * followDistance;
    }
}
