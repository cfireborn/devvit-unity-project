using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class GroundChecker : MonoBehaviour
{
    public bool isGrounded;
    public string platformTag = "Untagged";
    private List<GameObject> currentPlatforms = new List<GameObject>();

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.gameObject.tag == platformTag)
        {
            isGrounded = true;
            currentPlatforms.Add(other.gameObject);
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.gameObject.tag == platformTag)
        {
            currentPlatforms.Remove(other.gameObject);
            if (currentPlatforms.Count == 0)
            {
                isGrounded = false;
            }
        }
    }
}