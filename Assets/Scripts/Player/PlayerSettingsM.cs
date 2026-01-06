using UnityEngine;

[CreateAssetMenu(fileName = "PlayerSettingsM", menuName = "Scriptable Objects/PlayerSettings")]
public class PlayerSettingsM : ScriptableObject
{
	[Header("Movement")]
	public float moveSpeed = 6f;
	[Range(0f, 1f)] public float airControlMultiplier = 0.7f;

	[Header("Jump")]
	public float jumpForce = 12f;
	[Tooltip("How many total jumps the player has (1 = single jump, 2 = double jump)")]
	public int maxJumps = 1;

	[Header("Glide")]
	[Tooltip("Gravity scale while gliding")]
	public float glideGravityScale = 0.6f;
	[Tooltip("Normal gravity scale applied when not gliding")]
	public float normalGravityScale = 3f;
	[Tooltip("Maximum time (seconds) player can glide continuously; 0 = unlimited")]
	public float maxGlideTime = 0f;

	[Header("Ladder")]
	public float ladderClimbSpeed = 4f;

	[Header("Ground Check")]
	public Vector2 groundCheckOffset = new Vector2(0f, -0.6f);
	public float groundCheckRadius = 0.15f;
	public LayerMask groundLayer;
}
