using UnityEngine;

public class TargetTrigger : MonoBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        // Check if the player entered
        if (other.CompareTag("Player"))
        {
            GameManager.Instance.OnTargetReached();
            gameObject.SetActive(false);
        }
    }
}

