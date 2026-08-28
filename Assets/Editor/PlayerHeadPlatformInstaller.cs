using FishNet.Component.Transforming;
using FishNet.Object;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PlayerHeadPlatformInstaller
{
    const string PlayerPrefabPath = "Assets/Player/NetworkPlayer.prefab";
    const string BasicSettingsPath = "Assets/Scene/Clouds/CloudManagerSettings_Basic.asset";
    const string SimpleLevelSettingsPath = "Assets/Scene/Clouds/CloudManagerSettings_SimpleLevel.asset";
    const string SimpleLevelPath = "Assets/Levels/SimpleLevel.unity";

    [MenuItem("Tools/Compersion/Install Head Platform And Lane Density")]
    public static void Install()
    {
        InstallHeadPlatform();
        InstallSimpleLevelDensity();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Player head platform and SimpleLevel lane density installed.");
    }

    static void InstallHeadPlatform()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            if (root.GetComponents<NetworkBehaviour>().Length != 2 ||
                root.GetComponent<NetworkTransform>() == null ||
                root.GetComponent<NetworkPlayerController>() == null)
                throw new System.InvalidOperationException("NetworkPlayer FishNet behaviour schema is not the expected [NetworkTransform, NetworkPlayerController].");

            if (root.GetComponent<PlayerHeadPlatform>() == null)
                root.AddComponent<PlayerHeadPlatform>();

            Transform head = root.transform.Find("HeadPlatform");
            if (head == null)
            {
                head = new GameObject("HeadPlatform").transform;
                head.SetParent(root.transform, false);
            }

            head.gameObject.tag = "Platform";
            head.localPosition = new Vector3(0f, 0.15f, 0f);
            head.localRotation = Quaternion.identity;
            head.localScale = Vector3.one;

            Rigidbody2D body = head.GetComponent<Rigidbody2D>();
            if (body == null)
                body = head.gameObject.AddComponent<Rigidbody2D>();
            Debug.Log($"Head platform install: Rigidbody2D added={body != null}.");
            body.bodyType = RigidbodyType2D.Kinematic;
            body.simulated = true;
            body.useFullKinematicContacts = false;
            body.gravityScale = 0f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;

            EdgeCollider2D edge = head.GetComponent<EdgeCollider2D>();
            if (edge == null)
                edge = head.gameObject.AddComponent<EdgeCollider2D>();
            Debug.Log($"Head platform install: EdgeCollider2D added={edge != null}.");
            edge.isTrigger = false;
            edge.points = new[] { new Vector2(-0.115f, 0f), new Vector2(0.115f, 0f) };
            edge.edgeRadius = 0.005f;
            edge.usedByEffector = true;

            PlatformEffector2D effector = head.GetComponent<PlatformEffector2D>();
            if (effector == null)
                effector = head.gameObject.AddComponent<PlatformEffector2D>();
            Debug.Log($"Head platform install: PlatformEffector2D added={effector != null}.");
            effector.useOneWay = true;
            effector.useOneWayGrouping = true;
            effector.surfaceArc = 180f;
            effector.useSideFriction = false;
            effector.useSideBounce = false;

            if (!PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath))
                throw new System.InvalidOperationException("Failed to save NetworkPlayer prefab.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void InstallSimpleLevelDensity()
    {
        CloudBehaviorSettings basic = AssetDatabase.LoadAssetAtPath<CloudBehaviorSettings>(BasicSettingsPath);
        if (basic == null)
            throw new System.InvalidOperationException("Basic cloud settings asset is missing.");

        CloudBehaviorSettings settings = AssetDatabase.LoadAssetAtPath<CloudBehaviorSettings>(SimpleLevelSettingsPath);
        if (settings == null)
        {
            settings = Object.Instantiate(basic);
            settings.name = "CloudManagerSettings_SimpleLevel";
            AssetDatabase.CreateAsset(settings, SimpleLevelSettingsPath);
        }

        settings.laneSpacing = basic.laneSpacing;
        settings.minCloudSpacing = basic.minCloudSpacing * 0.5f;
        settings.maxCloudSpacing = basic.maxCloudSpacing * 0.5f;
        EditorUtility.SetDirty(settings);

        Scene scene = EditorSceneManager.OpenScene(SimpleLevelPath, OpenSceneMode.Single);
        CloudManager[] managers = Object.FindObjectsByType<CloudManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (managers.Length != 1)
            throw new System.InvalidOperationException($"Expected one CloudManager in SimpleLevel, found {managers.Length}.");

        managers[0].settings = settings;
        EditorUtility.SetDirty(managers[0]);
        PrefabUtility.RecordPrefabInstancePropertyModifications(managers[0]);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new System.InvalidOperationException("Failed to save SimpleLevel.");
    }
}
