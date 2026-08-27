#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using FishNet.Component.Transforming;
using FishNet.Object;
using UnityEditor;
using UnityEngine;

public static class CloudPrefabRepairTool
{
    const string BasePath = "Assets/Scene/Clouds/Cloud_Base.prefab";
    const string VariantPath = "Assets/Scene/Clouds/Cloud_2.prefab";
    const string SmootherTypeName = "FishNet.Component.Transforming.Beta.NetworkTickSmoother";

    [MenuItem("Tools/Compersion/Repair Cloud Network Prefabs")]
    public static void Repair()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[CloudPrefabRepair] Exit Play Mode before repairing cloud prefabs.");
            return;
        }

        RepairBase();
        RepairVariant();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        FishNet.Editing.RefreshDefaultPrefabsMenu.RebuildDefaultPrefabs();
        AssetDatabase.SaveAssets();

        Validate(BasePath);
        Validate("Assets/Scene/Clouds/Cloud_1.prefab");
        Validate(VariantPath);
        Debug.Log("[CloudPrefabRepair] COMPLETE");
    }

    static void RepairBase()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(BasePath);
        try
        {
            foreach (Component component in root.GetComponents<Component>())
                if (component != null && component.GetType().FullName == SmootherTypeName)
                    Object.DestroyImmediate(component, true);

            ConfigureSingleNetworkStack(root, BasePath);
            PrefabUtility.SaveAsPrefabAsset(root, BasePath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void RepairVariant()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(VariantPath);
        try
        {
            KeepInheritedOnly(root.GetComponents<NetworkTransform>(), VariantPath);
            KeepInheritedOnly(root.GetComponents<NetworkCloud>(), VariantPath);
            KeepInheritedOnly(root.GetComponents<NetworkObject>(), VariantPath);
            ConfigureSingleNetworkStack(root, VariantPath);
            PrefabUtility.SaveAsPrefabAsset(root, VariantPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void KeepInheritedOnly<T>(T[] components, string path) where T : Component
    {
        if (components.Length < 1)
            throw new System.InvalidOperationException($"[CloudPrefabRepair] {path} has no {typeof(T).Name}.");

        T keep = components.FirstOrDefault(component =>
            PrefabUtility.GetCorrespondingObjectFromSource(component) != null) ?? components[0];
        foreach (T component in components)
            if (component != keep)
                Object.DestroyImmediate(component, true);
    }

    static void ConfigureSingleNetworkStack(GameObject root, string path)
    {
        NetworkObject[] networkObjects = root.GetComponents<NetworkObject>();
        NetworkTransform[] networkTransforms = root.GetComponents<NetworkTransform>();
        NetworkCloud[] networkClouds = root.GetComponents<NetworkCloud>();
        if (networkObjects.Length != 1 || networkTransforms.Length != 1 || networkClouds.Length != 1)
            throw new System.InvalidOperationException(
                $"[CloudPrefabRepair] {path} expected one NetworkObject/NetworkTransform/NetworkCloud, got " +
                $"{networkObjects.Length}/{networkTransforms.Length}/{networkClouds.Length}.");

        NetworkObject networkObject = networkObjects[0];
        NetworkTransform networkTransform = networkTransforms[0];
        NetworkCloud networkCloud = networkClouds[0];

        SetObjectReference(networkTransform, "_addedNetworkObject", networkObject);
        SetObjectReference(networkTransform, "_networkObjectCache", networkObject);
        SetInteger(networkTransform, "_componentIndexCache", 0);
        SetBoolean(networkTransform, "_clientAuthoritative", false);
        SetBoolean(networkTransform, "_synchronizeRotation", false);
        SetBoolean(networkTransform, "_synchronizeScale", false);

        SetObjectReference(networkCloud, "_addedNetworkObject", networkObject);
        SetObjectReference(networkCloud, "_networkObjectCache", networkObject);
        SetInteger(networkCloud, "_componentIndexCache", 1);

        SerializedObject networkObjectSerialized = new SerializedObject(networkObject);
        networkObjectSerialized.FindProperty("_enablePrediction").boolValue = false;
        networkObjectSerialized.FindProperty("_predictionType").intValue = 0;
        networkObjectSerialized.FindProperty("_networkTransform").objectReferenceValue = networkTransform;
        networkObjectSerialized.ApplyModifiedPropertiesWithoutUndo();
        networkObject.NetworkBehaviours = new List<NetworkBehaviour> { networkTransform, networkCloud };

        EditorUtility.SetDirty(networkObject);
        EditorUtility.SetDirty(networkTransform);
        EditorUtility.SetDirty(networkCloud);
    }

    static void SetBoolean(Object target, string propertyName, bool value)
    {
        SerializedObject serialized = new SerializedObject(target);
        serialized.FindProperty(propertyName).boolValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetInteger(Object target, string propertyName, int value)
    {
        SerializedObject serialized = new SerializedObject(target);
        serialized.FindProperty(propertyName).intValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetObjectReference(Object target, string propertyName, Object value)
    {
        SerializedObject serialized = new SerializedObject(target);
        serialized.FindProperty(propertyName).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static void Validate(string path)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        int smoothers = prefab.GetComponents<Component>().Count(component =>
            component != null && component.GetType().FullName == SmootherTypeName);
        int networkObjects = prefab.GetComponents<NetworkObject>().Length;
        int networkTransforms = prefab.GetComponents<NetworkTransform>().Length;
        int networkClouds = prefab.GetComponents<NetworkCloud>().Length;
        if (networkObjects != 1 || networkTransforms != 1 || networkClouds != 1 || smoothers != 0)
            throw new System.InvalidOperationException(
                $"[CloudPrefabRepair] Validation failed for {path}: NO/NT/NC/smoother = " +
                $"{networkObjects}/{networkTransforms}/{networkClouds}/{smoothers}.");

        Debug.Log($"[CloudPrefabRepair] PASS {path}: one server-authoritative network stack, no invalid smoother.");
    }
}
#endif
