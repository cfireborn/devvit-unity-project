#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Draws active pooled + tracked cloud count at the top-left of the Scene view when a CloudManager exists.
/// </summary>
[InitializeOnLoad]
static class CloudManagerSceneOverlay
{
    static CloudManagerSceneOverlay()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    static void OnSceneGUI(SceneView sceneView)
    {
        var cm = Object.FindFirstObjectByType<CloudManager>(FindObjectsInactive.Include);
        if (cm == null) return;

        int count = cm.GetActiveClouds().Count;

        Handles.BeginGUI();
        try
        {
            var r = new Rect(8f, 8f, 320f, 24f);
            GUI.Label(r, $"Active clouds: {count}", EditorStyles.boldLabel);
        }
        finally
        {
            Handles.EndGUI();
        }
    }
}
#endif
