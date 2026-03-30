using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Optional companion: renders a live status overlay in the Scene View as Gizmos,
/// showing test-relevant runtime values (cloud counts, ladder counts, active lanes, etc.)
/// so a developer or agent can read scene state without entering Play Mode logs.
/// Attach to the same TestRunner GameObject.
/// </summary>
public class TestSceneVisualizer : MonoBehaviour
{
    [Header("References (auto-found if null)")]
    public CloudManager cloudManager;
    public CloudLadderController ladderController;

    [Header("Gizmo Colors")]
    public Color cloudGizmoColor  = new Color(0.3f, 0.8f, 1f, 0.9f);
    public Color ladderGizmoColor = new Color(1f, 0.65f, 0.2f, 0.9f);
    public Color rangeGizmoColor  = new Color(0.4f, 1f, 0.4f, 0.35f);

    void Awake()
    {
        if (cloudManager   == null) cloudManager   = FindFirstObjectByType<CloudManager>(FindObjectsInactive.Include);
        if (ladderController == null) ladderController = FindFirstObjectByType<CloudLadderController>(FindObjectsInactive.Include);
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        DrawCloudGizmos();
        DrawLadderGizmos();
        DrawLadderRangeCircles();
    }

    void DrawCloudGizmos()
    {
        if (cloudManager == null) return;

        IReadOnlyList<GameObject> active = cloudManager.GetActiveClouds();
        Gizmos.color = cloudGizmoColor;
        foreach (var cloud in active)
        {
            if (cloud == null) continue;
            var platform = cloud.GetComponent<CloudPlatform>();
            if (platform == null) continue;
            Bounds b = platform.GetMainBounds();
            Gizmos.DrawWireCube(b.center, b.size);
        }
    }

    void DrawLadderGizmos()
    {
        var ladders = GameObject.FindGameObjectsWithTag("Ladder");
        Gizmos.color = ladderGizmoColor;
        foreach (var ladder in ladders)
        {
            if (ladder == null || !ladder.activeSelf) continue;
            var col = ladder.GetComponent<BoxCollider2D>();
            if (col != null)
                Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
            else
                Gizmos.DrawWireSphere(ladder.transform.position, 0.25f);
        }
    }

    void DrawLadderRangeCircles()
    {
        if (ladderController == null) return;

        float r = ladderController.maxDistance;
        if (r <= 0f) return;

        IReadOnlyList<GameObject> active = cloudManager != null ? cloudManager.GetActiveClouds() : null;
        if (active == null || active.Count == 0) return;

        Gizmos.color = rangeGizmoColor;
        foreach (var cloud in active)
        {
            if (cloud == null) continue;
            // Draw a rough radius circle at cloud position using wire sphere (2D approximation)
            Gizmos.DrawWireSphere(cloud.transform.position, r);
        }
    }
}
