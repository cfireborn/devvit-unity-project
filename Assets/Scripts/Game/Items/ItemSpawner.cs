using UnityEngine;

/// <summary>
/// Spawns item prefabs at a position. Can be used by NPCController or other systems.
/// Calls Item.OnSpawned() on spawned instances that have an Item component.
/// </summary>
public class ItemSpawner : MonoBehaviour
{
    [Header("Items")]
    [Tooltip("Item prefabs to spawn. If SpawnItem() is called without index, uses first prefab.")]
    public GameObject[] itemPrefabs;

    [Header("Spawn")]
    [Tooltip("Local offset from this transform for spawn position.")]
    public Vector3 spawnOffset = Vector3.zero;
    [Tooltip("Parent for spawned items. If null, uses this transform.")]
    public Transform spawnParent;

    /// <summary>Spawn the first item prefab. Returns the instantiated GameObject or null.</summary>
    public GameObject SpawnItem()
    {
        return SpawnItem(0);
    }

    /// <summary>Spawn item at index. Returns the instantiated GameObject or null.</summary>
    public GameObject SpawnItem(int prefabIndex)
    {
        if (itemPrefabs == null || itemPrefabs.Length == 0) return null;
        int idx = Mathf.Clamp(prefabIndex, 0, itemPrefabs.Length - 1);
        var prefab = itemPrefabs[idx];
        if (prefab == null) return null;

        var parent = spawnParent != null ? spawnParent : transform;
        Vector3 pos = transform.position + spawnOffset;
        var instance = Instantiate(prefab, pos, Quaternion.identity, parent);
        NotifySpawned(instance);
        return instance;
    }

    /// <summary>Spawn a specific prefab by reference. Returns the instantiated GameObject or null.</summary>
    public GameObject SpawnItem(GameObject prefab)
    {
        if (prefab == null) return null;
        var parent = spawnParent != null ? spawnParent : transform;
        Vector3 pos = transform.position + spawnOffset;
        var instance = Instantiate(prefab, pos, Quaternion.identity, parent);
        NotifySpawned(instance);
        return instance;
    }

    void NotifySpawned(GameObject instance)
    {
        var item = instance.GetComponent<Item>();
        if (item != null)
            item.OnSpawned();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position + spawnOffset, 0.2f);
    }
}
