using UnityEngine;

/// <summary>
/// Bridge between an enemy instance and the wave/spawn system + GameManager.
/// Works with pooled or non-pooled enemies. All methods are idempotent.
/// </summary>
public class EnemyWaveHooks : MonoBehaviour
{
    // Guards
    private bool _spawnReported = false;  // we've told GM the enemy is alive
    private bool _killReported = false;  // we've told GM the enemy was killed

    // Optional: store a reference to the spawner if you want to update its local counters too.
    private MonoBehaviour _spawnerRef;

    /// <summary>Optionally let a spawner attach itself for its own bookkeeping.</summary>
    public void SetSpawner(MonoBehaviour spawner) => _spawnerRef = spawner;

    // ----------------------------
    // Auto mode (pool-friendly)
    // ----------------------------
    private void OnEnable()
    {
        // Treat enable as a (re)spawn for pooled enemies.
        NotifySpawned();
        _killReported = false; // allow this instance to be killed again after reuse
    }

    private void OnDisable()
    {
        // If we get disabled without a kill, count it as a despawn.
        if (_spawnReported && !_killReported)
            GameManager.Instance?.RegisterEnemyDespawned();

        _spawnReported = false; // ready for next enable/spawn
    }

    // ----------------------------
    // Explicit calls (from spawner)
    // ----------------------------

    /// <summary>
    /// Mark this enemy as spawned/alive. Safe to call multiple times.
    /// Returns true if it actually notified the GameManager this call.
    /// </summary>
    public bool NotifySpawned()
    {
        if (_spawnReported) return false;
        GameManager.Instance?.RegisterEnemySpawned();
        _spawnReported = true;
        return true;
    }

    /// <summary>
    /// Mark this enemy as despawned (not killed). Safe to call multiple times.
    /// Returns true if it actually notified the GameManager this call.
    /// </summary>
    public bool NotifyDespawned()
    {
        if (!_spawnReported || _killReported) return false; // either never spawned, or already killed
        GameManager.Instance?.RegisterEnemyDespawned();
        _spawnReported = false;
        return true;
    }

    /// <summary>
    /// Report a kill exactly once and decrement alive count.
    /// Safe to call multiple times; only the first will notify.
    /// </summary>
    /// <param name="isBoss">If true, also increments boss kills & triggers boss-drop logic.</param>
    /// <returns>True if GameManager was notified on this call.</returns>
    public bool NotifyKilled(bool isBoss)
    {
        if (_killReported) return false;       // already reported a kill for this instance

        _killReported = true;

        // If we previously reported a spawn, EnemyDeath will internally decrement
        // alive count and increment kill stats. (Your GameManager.RegisterEnemyDeath does both.)
        GameManager.Instance?.RegisterEnemyDeath(transform.position, isBoss);

        // We leave _spawnReported as-is; OnDisable will not double-decrement since _killReported is now true.

        // If you track counts in your spawner, update via _spawnerRef here.

        return true;
    }

    /// <summary>
    /// Optional helper if you need to forcibly clear internal guards (rare).
    /// </summary>
    public void ResetGuardsForPool()
    {
        _spawnReported = false;
        _killReported = false;
    }
}
