using UnityEngine;

[DisallowMultipleComponent]
public class PooledMiniBoss : MonoBehaviour
{
    [HideInInspector] public MiniBossPool pool;
    [HideInInspector] public int prefabKey;
    [HideInInspector] public bool inUse;

    public void MarkFromPool(MiniBossPool poolRef, int key)
    {
        pool = poolRef; prefabKey = key; inUse = false;
    }

    public void MarkSpawned(MiniBossPool poolRef, int key)
    {
        pool = poolRef; prefabKey = key; inUse = true;
    }

    public void MarkReturned() { inUse = false; }

    private void OnDisable()
    {
        if (!Application.isPlaying) return;
        if (inUse && pool != null) pool.Reclaim(this);
    }
}
