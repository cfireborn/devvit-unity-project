using UnityEngine;

[CreateAssetMenu(fileName = "GameState", menuName = "Scriptable Objects/GameState")]
public class GameState : ScriptableObject
{
	[Header("Level Positions")]
	public Vector3 startPosition;
	public Vector3 goalPosition;

	[Header("Progress")]
	public bool levelComplete = false;
	public int lettersCollected = 0;
}
