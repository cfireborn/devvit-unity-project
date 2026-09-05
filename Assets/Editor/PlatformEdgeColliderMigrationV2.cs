using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PlatformEdgeColliderMigrationV2
{
    readonly struct PlatformShape
    {
        public readonly Vector2[] Points;
        public readonly bool UseGroundMaterial;

        public PlatformShape(Vector2[] points, bool useGroundMaterial = false)
        {
            Points = points;
            UseGroundMaterial = useGroundMaterial;
        }
    }

    const string GroundMaterialPath = "Assets/Scene/Balloons/PhyMat_GroundMaterial.physicsMaterial2D";

    static PlatformShape Line(float minX, float maxX, float y, bool useGroundMaterial = false) => new PlatformShape(new[]
    {
        new Vector2(minX, y),
        new Vector2(maxX, y)
    }, useGroundMaterial);

    static PlatformShape HorizontalCapsule(float centerX, float centerY, float width, float height,
        bool useGroundMaterial = false)
    {
        float radius = height * 0.5f;
        float straightHalfWidth = width * 0.5f - radius;
        float diagonal = radius * 0.70710678118f;
        return new PlatformShape(new[]
        {
            new Vector2(centerX - width * 0.5f, centerY),
            new Vector2(centerX - straightHalfWidth - diagonal, centerY + diagonal),
            new Vector2(centerX - straightHalfWidth, centerY + radius),
            new Vector2(centerX + straightHalfWidth, centerY + radius),
            new Vector2(centerX + straightHalfWidth + diagonal, centerY + diagonal),
            new Vector2(centerX + width * 0.5f, centerY)
        }, useGroundMaterial);
    }

    static PlatformShape UpperSemicircle(float centerX, float centerY, float radius)
    {
        float diagonal = radius * 0.70710678118f;
        return new PlatformShape(new[]
        {
            new Vector2(centerX - radius, centerY),
            new Vector2(centerX - diagonal, centerY + diagonal),
            new Vector2(centerX, centerY + radius),
            new Vector2(centerX + diagonal, centerY + diagonal),
            new Vector2(centerX + radius, centerY)
        });
    }

    static readonly Dictionary<string, PlatformShape[]> PrefabShapes = new Dictionary<string, PlatformShape[]>
    {
        ["Assets/Scene/Clouds/Cloud_Base.prefab"] = new[] { Line(-1.0187238f, 1.3187238f, 0.07f) },
        ["Assets/Scene/Clouds/Cloud_1.prefab"] = new[] { Line(-1.0187238f, 1.3187238f, 0.07f) },
        ["Assets/Scene/Clouds/Cloud_2.prefab"] = new[] { Line(-1.5095005f, 1.4504995f, -0.36394736f) },
        ["Assets/Scene/Clouds/Cloud_3.prefab"] = new[] { Line(-1.1187238f, 1.2187238f, -0.02f) },
        ["Assets/Scene/Clouds/Cloud_4.prefab"] = new[] { Line(-1.285f, 1.325f, 0.03f) },
        ["Assets/Scene/Clouds/Cloud_5.prefab"] = new[] { Line(-1.475f, 1.435f, 0.03f) },
        ["Assets/Scene/Clouds/Cloud_6_2.prefab"] = new[] { Line(-1.0187238f, 1.3187238f, 0.07f) },
        ["Assets/Scene/Clouds/Cloud_7.prefab"] = new[] { Line(-1.41f, 1.29f, 0.07f) },
        ["Assets/Scene/Clouds/DeliveryCloud_Base.prefab"] = new[]
        {
            Line(-0.93006355f, 1.4073842f, -0.07549465f),
            Line(-0.9932002f, 0.20512067f, -0.40505755f),
            Line(-0.34755278f, 0.9997566f, 0.37758732f)
        },
        ["Assets/Scene/Clouds/PostBoxCloud_Base.prefab"] = new[]
        {
            Line(-0.93006355f, 1.4073842f, -0.07549465f),
            Line(-0.9932002f, 0.20512067f, -0.40505755f),
            Line(-0.34755278f, 0.9997566f, 0.37758732f)
        },
        ["Assets/Scene/Balloons/Balloon_Base.prefab"] = new[]
        {
            Line(-0.445f, 0.565f, -0.41f, true),
            HorizontalCapsule(0f, -1.57f, 2.14f, 0.5f, true)
        },
        ["Assets/Scene/Balloons/Balloon_Koi.prefab"] = new[]
        {
            Line(-0.41417694f, 0.51112795f, -0.40418553f, true),
            HorizontalCapsule(0f, -1.57f, 2.14f, 0.21f, true)
        },
        ["Assets/Scene/Balloons/Balloon_Pirate.prefab"] = new[]
        {
            Line(0.2049999f, 1.2149999f, -0.14857674f, true),
            HorizontalCapsule(-0.12f, -1.27f, 2.6f, 0.08f, true),
            HorizontalCapsule(-0.14f, -1.49f, 0.46f, 0.2f, true),
            HorizontalCapsule(0.71f, -1.53f, 0.53f, 0.26f, true),
            HorizontalCapsule(0.24f, 1.23f, 2.95f, 0.89f, true),
            HorizontalCapsule(0.43f, -1.67f, 0.4f, 0.1f, true),
            UpperSemicircle(-1.84f, -0.09f, 0.38f),
            Line(0.4240141f, 1.2149999f, 0.12196922f, true)
        }
    };

    static readonly string[] MigrationOrder =
    {
        "Assets/Scene/Clouds/Cloud_Base.prefab",
        "Assets/Scene/Clouds/Cloud_1.prefab",
        "Assets/Scene/Clouds/Cloud_2.prefab",
        "Assets/Scene/Clouds/Cloud_3.prefab",
        "Assets/Scene/Clouds/Cloud_4.prefab",
        "Assets/Scene/Clouds/Cloud_5.prefab",
        "Assets/Scene/Clouds/Cloud_6_2.prefab",
        "Assets/Scene/Clouds/Cloud_7.prefab",
        "Assets/Scene/Clouds/DeliveryCloud_Base.prefab",
        "Assets/Scene/Clouds/PostBoxCloud_Base.prefab",
        "Assets/Scene/Balloons/Balloon_Base.prefab",
        "Assets/Scene/Balloons/Balloon_Koi.prefab",
        "Assets/Scene/Balloons/Balloon_Pirate.prefab"
    };

    static readonly string[] ScenePaths =
    {
        "Assets/Levels/SimpleLevel.unity",
        "Assets/Levels/CloudManagerTest.unity",
        "Assets/Levels/GoalTestScene.unity",
        "Assets/Levels/LadderManagerTest.unity"
    };

    [MenuItem("Tools/Codex/Migrate Platform Colliders V2")]
    public static void Migrate()
    {
        foreach (string path in MigrationOrder)
            MigratePrefab(path, PrefabShapes[path]);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        foreach (string scenePath in ScenePaths)
            MigrateScene(scenePath);

        ValidateAll();
        Debug.Log("PLATFORM_EDGE_MIGRATION_V2_OK");
    }

    static void MigratePrefab(string path, PlatformShape[] shapes)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var reusableEdges = new List<EdgeCollider2D>(root.GetComponents<EdgeCollider2D>());
            Collider2D[] rootColliders = root.GetComponents<Collider2D>();
            for (int i = rootColliders.Length - 1; i >= 0; i--)
            {
                Collider2D collider = rootColliders[i];
                if (!collider.isTrigger && collider is not EdgeCollider2D)
                    UnityEngine.Object.DestroyImmediate(collider, true);
            }

            while (reusableEdges.Count > shapes.Length)
            {
                int last = reusableEdges.Count - 1;
                UnityEngine.Object.DestroyImmediate(reusableEdges[last], true);
                reusableEdges.RemoveAt(last);
            }
            while (reusableEdges.Count < shapes.Length)
                reusableEdges.Add(root.AddComponent<EdgeCollider2D>());

            for (int i = 0; i < shapes.Length; i++)
                ConfigureEdge(reusableEdges[i], shapes[i]);

            int platformLayer = LayerMask.NameToLayer("Platform");
            root.tag = "Platform";
            if (platformLayer >= 0) root.layer = platformLayer;

            PlatformEffector2D effector = root.GetComponent<PlatformEffector2D>();
            if (effector == null) effector = root.AddComponent<PlatformEffector2D>();
            effector.useOneWay = true;
            effector.useOneWayGrouping = false;
            effector.useSideFriction = false;
            effector.useSideBounce = false;
            effector.surfaceArc = 178f;
            effector.sideArc = 0f;
            effector.rotationalOffset = 0f;

            CloudPlatform platform = root.GetComponent<CloudPlatform>();
            if (platform != null) platform.mainCollider = reusableEdges[0];

            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void ConfigureEdge(EdgeCollider2D edge, PlatformShape shape)
    {
        edge.enabled = true;
        edge.isTrigger = false;
        edge.usedByEffector = true;
        edge.offset = Vector2.zero;
        edge.edgeRadius = 0f;
        edge.useAdjacentStartPoint = false;
        edge.useAdjacentEndPoint = false;
        edge.sharedMaterial = shape.UseGroundMaterial
            ? AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(GroundMaterialPath)
            : null;
        edge.points = shape.Points;
        EditorUtility.SetDirty(edge);
        PrefabUtility.RecordPrefabInstancePropertyModifications(edge);
    }

    static void MigrateScene(string scenePath)
    {
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        bool changed = false;
        foreach (GameObject instanceRoot in FindPrefabRoots(scene))
        {
            string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot);
            if (prefabPath == "Assets/Scene/Balloons/Balloon_Pirate.prefab")
            {
                Collider2D[] colliders = instanceRoot.GetComponents<Collider2D>();
                for (int i = colliders.Length - 1; i >= 0; i--)
                {
                    if (!colliders[i].isTrigger && colliders[i] is not EdgeCollider2D)
                    {
                        UnityEngine.Object.DestroyImmediate(colliders[i], true);
                        changed = true;
                    }
                }
            }
            else if (scenePath == "Assets/Levels/SimpleLevel.unity" &&
                     prefabPath == "Assets/Scene/Balloons/Balloon_Koi.prefab")
            {
                EdgeCollider2D lower = FindLowestEdge(instanceRoot.GetComponents<EdgeCollider2D>());
                if (lower == null) throw new InvalidOperationException("SimpleLevel Koi lower EdgeCollider2D was not found.");
                PlatformShape simpleLevelShape = HorizontalCapsule(0f, -1.44f, 2.14f, 0.21f, true);
                if (!PointsMatch(lower.points, simpleLevelShape.Points))
                {
                    ConfigureEdge(lower, simpleLevelShape);
                    changed = true;
                }
            }
        }
        if (changed)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }

    static bool PointsMatch(Vector2[] actual, Vector2[] expected)
    {
        if (actual == null || actual.Length != expected.Length) return false;
        for (int i = 0; i < actual.Length; i++)
            if ((actual[i] - expected[i]).sqrMagnitude > 0.00000001f) return false;
        return true;
    }

    static IEnumerable<GameObject> FindPrefabRoots(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                GameObject candidate = transforms[i].gameObject;
                if (PrefabUtility.GetOutermostPrefabInstanceRoot(candidate) == candidate)
                    yield return candidate;
            }
        }
    }

    static EdgeCollider2D FindLowestEdge(EdgeCollider2D[] edges)
    {
        EdgeCollider2D best = null;
        float bestY = float.PositiveInfinity;
        var points = new List<Vector2>();
        for (int i = 0; i < edges.Length; i++)
        {
            points.Clear();
            edges[i].GetPoints(points);
            if (points.Count == 0) continue;
            float y = 0f;
            for (int p = 0; p < points.Count; p++) y += points[p].y;
            y /= points.Count;
            if (y < bestY)
            {
                bestY = y;
                best = edges[i];
            }
        }
        return best;
    }

    static void ValidateAll()
    {
        foreach (string path in MigrationOrder)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                EdgeCollider2D[] edges = root.GetComponents<EdgeCollider2D>();
                if (edges.Length != PrefabShapes[path].Length)
                    throw new InvalidOperationException($"{path}: expected {PrefabShapes[path].Length} root edges, found {edges.Length}.");
                foreach (Collider2D collider in root.GetComponents<Collider2D>())
                    if (!collider.isTrigger && collider is not EdgeCollider2D)
                        throw new InvalidOperationException($"{path}: still has root platform collider {collider.GetType().Name}.");
                for (int i = 0; i < edges.Length; i++)
                {
                    EdgeCollider2D edge = edges[i];
                    if (!edge.enabled || edge.isTrigger || !edge.usedByEffector ||
                        edge.useAdjacentStartPoint || edge.useAdjacentEndPoint)
                        throw new InvalidOperationException($"{path}: edge {i} one-way/adjacency configuration is invalid.");
                    PhysicsMaterial2D expectedMaterial = PrefabShapes[path][i].UseGroundMaterial
                        ? AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(GroundMaterialPath)
                        : null;
                    if (edge.sharedMaterial != expectedMaterial)
                        throw new InvalidOperationException($"{path}: edge {i} physics material changed.");
                    Vector2[] actual = edge.points;
                    Vector2[] expected = PrefabShapes[path][i].Points;
                    if (actual.Length != expected.Length)
                        throw new InvalidOperationException($"{path}: edge {i} point count changed.");
                    for (int p = 0; p < actual.Length; p++)
                        if ((actual[p] - expected[p]).sqrMagnitude > 0.00000001f)
                            throw new InvalidOperationException($"{path}: edge {i} point {p} changed from the authored shape.");
                }
                PlatformEffector2D effector = root.GetComponent<PlatformEffector2D>();
                if (effector == null || !effector.useOneWay || effector.useOneWayGrouping ||
                    Mathf.Abs(effector.surfaceArc - 178f) > 0.01f || Mathf.Abs(effector.sideArc) > 0.01f)
                    throw new InvalidOperationException($"{path}: PlatformEffector2D is not configured for independent top-only edges.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
