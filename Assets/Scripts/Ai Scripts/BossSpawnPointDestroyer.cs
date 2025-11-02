using UnityEngine;

/// <summary>
/// Attach this to the spawned Boss instance at spawn-time.
/// When the Boss GameObject is destroyed, this will also destroy the spawn point GameObject.
/// </summary>
public class BossSpawnPointDestroyer : MonoBehaviour
{
    [Tooltip("The spawn point object (usually the BossSpawner GameObject) that created this boss.")]
    public GameObject spawnPoint;

    [Tooltip("Set true to log what happens (safe to disable in production).")]
    public bool verboseLogs = true;

    private bool _didDestroy;

    private void OnDestroy()
    {
        // If we're quitting playmode or already did it, bail.
        if (_didDestroy || spawnPoint == null) return;

        // Safety: don't try to destroy ourselves through a circular ref.
        if (spawnPoint == gameObject || spawnPoint.transform.IsChildOf(transform))
            return;

        if (verboseLogs)
            Debug.Log($"[BossSpawnPointDestroyer] Boss '{name}' died -> destroying spawn point '{spawnPoint.name}'.", this);

        _didDestroy = true;
        Destroy(spawnPoint);
    }
}
