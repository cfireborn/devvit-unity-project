using UnityEngine;

/// <summary>Exposes a player's network-smoothed root position to riders without coupling their physics bodies.</summary>
public sealed class PlayerHeadPlatform : MonoBehaviour, IMovingPlatform
{
    public Vector2 GetPosition() => transform.position;
}
