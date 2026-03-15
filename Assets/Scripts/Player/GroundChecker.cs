using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class GroundChecker : MonoBehaviour
{
    public bool isGrounded;
    public string platformTag = "Untagged";
}
