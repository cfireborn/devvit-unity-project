using UnityEngine;

/// <summary>
/// Implemented by objects that move (e.g. clouds, ladders) so that the player can
/// apply the platform's delta position each frame and move with it.
/// The consumer tracks last position and adds (GetPosition() - lastPosition) to the player.
/// </summary>
public interface IMovingPlatform
{
    /// <summary>Current world position of the platform. Consumer uses this to compute delta and apply to player.</summary>
    Vector2 GetPosition();
}
