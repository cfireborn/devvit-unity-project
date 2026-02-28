using UnityEngine;

[CreateAssetMenu(menuName = "Compersion/Edgegap Settings", fileName = "EdgegapSettings")]
public class EdgegapSettings : ScriptableObject
{
    [Tooltip("Edgegap API token (from edgegap.com → API Tokens).")]
    public string apiKey;

    [Tooltip("Application name as configured in Edgegap dashboard.")]
    public string appName = "compersion";

    [Tooltip("Version name as configured in Edgegap dashboard.")]
    public string versionName = "latest";

    [Tooltip("The named port in Edgegap that maps to Bayou (7771 TCP).")]
    public string portName = "game-ws";
}
