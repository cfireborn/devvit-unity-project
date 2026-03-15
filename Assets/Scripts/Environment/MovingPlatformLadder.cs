using UnityEngine;

/// <summary>
/// Add to ladder root GameObjects (tag "Ladder") so they implement IMovingPlatform.
/// Allows the player to move with the ladder when it is repositioned (e.g. by CloudLadderController).
/// </summary>
public class MovingPlatformLadder : MonoBehaviour, IMovingPlatform
{
    public Vector2 GetPosition() => (Vector2)transform.position;
}
