using UnityEngine;

[CreateAssetMenu(fileName = "Goal", menuName = "Scriptable Objects/Goal")]
public class Goal : ScriptableObject
{
    [Header("Goal Data")]
    public Vector3 location;
    public Sprite sprite;
    public string type = "delivery";
}
