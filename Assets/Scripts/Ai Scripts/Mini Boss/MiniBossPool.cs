// Assets/Scripts/Ai Scripts/Mini Boss/MiniBossPool.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public class MiniBossPool : MonoBehaviour
{
    [System.Serializable]
    public class PrefabEntry
    {
        public GameObject prefab;
        [Min(0)] public int preload = 3;
        [Min(1)] public int maxSize = 24;
        public Transform container; // optional override parent for pooled instances

        [HideInInspector] public int key;                   // prefab.GetInstanceID()
        [HideInInspector] public readonly Queue<GameObject> free = new Queue<GameObject>();
        [HideInInspector] public readonly List<GameObject> all = new List<GameObject>();
    }

    [Header("Configured Prefabs (optional)")]
    public List<PrefabEntry> prefabs = new List<PrefabEntry>();

    [Header("Defaults for auto-registered prefabs")]
    [Min(0)] public int defaultPreload = 2;
    [Min(1)] public int defaultMaxSize = 16;

    private readonly Dictionary<int, PrefabEntry> _byKey = new Dictionary<int, PrefabEntry>();

    void Awake()
    {
        // Index configured entries and warm them up
        for (int i = 0; i < prefabs.Count; i++)
        {
            var e = prefabs[i];
            if (!e.prefab) continue;
            e.key = e.prefab.GetInstanceID();
            if (!_byKey.ContainsKey(e.key)) _byKey.Add(e.key, e);
        }

        foreach (var kv in _byKey)
        {
            Warm(kv.Value);
        }
    }

    // Ensure a prefab has an entry (used by Mixer if you didn't preconfigure)
    public void EnsureEntry(GameObject prefab)
    {
        if (!prefab) return;
        int key = prefab.GetInstanceID();
        if (_byKey.ContainsKey(key)) return;

        var e = new PrefabEntry
        {
            prefab = prefab,
            preload = defaultPreload,
            maxSize = defaultMaxSize,
            container = null,
            key = key
        };
        prefabs.Add(e);
        _byKey.Add(key, e);
        Warm(e);
    }

    private void Warm(PrefabEntry e)
    {
        int toCreate = Mathf.Max(0, e.preload - e.all.Count);
        for (int i = 0; i < toCreate; i++)
        {
            var go = CreateInstanceForPool(e);
            Store(e, go);
        }
    }

    private GameObject CreateInstanceForPool(PrefabEntry e)
    {
        var parent = e.container ? e.container : this.transform;
        var go = Instantiate(e.prefab, parent);
        go.name = e.prefab.name + " (Pooled)";
        if (!go.TryGetComponent<PooledMiniBoss>(out var pooled))
            pooled = go.AddComponent<PooledMiniBoss>();
        pooled.MarkFromPool(this, e.key);
        go.SetActive(false);
        e.all.Add(go);
        return go;
    }

    private void Store(PrefabEntry e, GameObject go)
    {
        // Reset lightweight runtime state for safe reuse
        if (go.TryGetComponent<NavMeshAgent>(out var agent))
        {
            if (agent.isOnNavMesh) agent.ResetPath();
            agent.isStopped = true;
        }
        if (go.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.Sleep();
        }

        // park under container
        var parent = e.container ? e.container : this.transform;
        if (go.transform.parent != parent) go.transform.SetParent(parent, false);

        if (go.activeSelf) go.SetActive(false);
        e.free.Enqueue(go);
    }

    public GameObject SpawnFromPrefab(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (!prefab) return null;
        EnsureEntry(prefab);
        var e = _byKey[prefab.GetInstanceID()];

        GameObject go = null;
        // Reuse if available
        while (e.free.Count > 0 && go == null)
        {
            go = e.free.Dequeue();
            if (go == null) { /* stale slot */ }
        }

        // Create new if pool empty and under cap
        if (go == null)
        {
            if (e.all.Count < e.maxSize)
            {
                go = CreateInstanceForPool(e);
            }
            else
            {
                // Hard cap reached: pick an inactive object from 'all' if any
                for (int i = 0; i < e.all.Count && go == null; i++)
                {
                    var candidate = e.all[i];
                    if (candidate != null && !candidate.activeInHierarchy)
                        go = candidate;
                }
                if (go == null)
                {
                    Debug.LogWarning($"[MiniBossPool] Max size reached for '{prefab.name}' ({e.maxSize}). Consider raising the cap.");
                    return null;
                }
            }
        }

        // Activate + place
        if (!go.TryGetComponent<PooledMiniBoss>(out var pooled))
            pooled = go.AddComponent<PooledMiniBoss>();
        pooled.MarkSpawned(this, e.key);

        go.transform.SetPositionAndRotation(position, rotation);

        // Unpark from container to world (optional)
        go.transform.SetParent(null, true);

        // Re-enable agent cleanly
        if (go.TryGetComponent<NavMeshAgent>(out var agent2))
        {
            agent2.isStopped = false;
        }

        go.SetActive(true);
        return go;
    }

    internal void Reclaim(PooledMiniBoss pooled)
    {
        if (pooled == null) return;
        int key = pooled.prefabKey;
        if (!_byKey.TryGetValue(key, out var e)) return; // unknown (maybe destroyed)

        var go = pooled.gameObject;
        Store(e, go);
        pooled.MarkReturned();
    }
}
