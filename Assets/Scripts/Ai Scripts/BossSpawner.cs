using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public class BossSpawner : MonoBehaviour
{
    [Header("Boss Prefab(s)")]
    [Tooltip("One of these will be chosen at spawn time. Must include BossEnemy; NavMeshAgent recommended.")]
    public GameObject[] bossPrefabs;

    [Header("Global Randomization")]
    [Tooltip("If true, this spawner participates in a global pool shared by all spawners with the same Group Id.")]
    public bool useGlobalPool = true;

    [Tooltip("Spawners with the same Group Id share a global pool / history.")]
    public string globalGroupId = "Bosses";

    public enum GlobalMode { NoImmediateRepeat, ShuffleBag }
    [Tooltip("NoImmediateRepeat = avoid repeating the last global pick.\nShuffleBag = cycle all prefabs in random order before repeating.")]
    public GlobalMode globalMode = GlobalMode.ShuffleBag;

    [Header("Local Randomization (fallback)")]
    [Tooltip("Only used if global is disabled or the global pool degenerates to a single valid entry.")]
    public bool avoidRepeatingLastLocal = true;

    [Header("Spawn Timing")]
    [Tooltip("Spawn once automatically when this spawner becomes active.")]
    public bool spawnOnStart = true;

    [Tooltip("Wait for a NavMesh to exist before trying to spawn (good for runtime baking).")]
    public bool waitForNavMesh = true;

    [Tooltip("Extra safety delay after NavMesh is detected (seconds).")]
    [Min(0f)] public float postNavmeshDelay = 0.1f;

    [Header("Proximity Gating")]
    [Tooltip("Require the player to be near this spawner before any spawn is allowed (including respawns).")]
    public bool requirePlayerProximity = true;

    [Tooltip("Player must be within this distance of THIS spawner (center) to allow a spawn.")]
    [Min(0f)] public float spawnActivationRadius = 20f;

    [Tooltip("If true, ignore vertical distance when checking player proximity.")]
    public bool use2DDistanceForActivation = true;

    [Tooltip("How often to re-check player proximity while waiting.")]
    [Min(0.02f)] public float proximityCheckInterval = 0.2f;

    [Header("Placement")]
    [Min(0f)] public float searchRadiusFromSpawner = 12f;
    [Min(0f)] public float fallbackRadiusFromPlayer = 18f;
    [Min(0.1f)] public float sampleMaxDistance = 4f;
    [Min(0f)] public float minDistanceFromPlayer = 10f;
    [Min(1)] public int maxAttempts = 20;

    [Header("Agent Init")]
    [Tooltip("If the prefab has a NavMeshAgent, temporarily disable then Warp, then enable.")]
    public bool safeAgentInit = true;

    [Header("On Boss Death")]
    [Tooltip("If true and respawn is disabled, destroy this spawner the first time the boss dies.")]
    public bool destroySpawnerOnFirstBossDeath = true;

    [Tooltip("If true, spawner will respawn a new boss after the old one dies.")]
    public bool allowRespawn = false;

    [Tooltip("Seconds before respawning a new boss (if allowed).")]
    [Min(0f)] public float respawnDelay = 10f;

    [Tooltip("Maximum number of respawns allowed (-1 for unlimited).")]
    public int maxRespawns = -1;

    [Header("Debug")]
    public bool debugLogs = false;

    // Runtime/state
    private Transform player;
    private GameObject currentBoss;  // track instance
    private bool _isSpawning;
    private int _respawnsSoFar;
    private bool _deathHandledOnce; // prevents double-destroy / double-respawn
    private int _lastPickedIndexLocal = -1;

    private void Awake()
    {
        if (bossPrefabs == null || bossPrefabs.Length == 0)
        {
            Debug.LogError("[BossSpawner] No bossPrefabs assigned.", this);
        }

        // Register this spawner's prefabs into the global pool (if enabled)
        if (useGlobalPool)
        {
            BossGlobalRandomizer.Register(globalGroupId, bossPrefabs);
            if (debugLogs)
                Debug.Log($"[BossSpawner] Registered {ValidCount(bossPrefabs)} prefab(s) to global group '{globalGroupId}'.", this);
        }
    }

    private int ValidCount(GameObject[] arr)
    {
        if (arr == null) return 0;
        int c = 0; foreach (var x in arr) if (x != null) c++;
        return c;
    }

    private void Start()
    {
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p) player = p.transform;

        if (spawnOnStart)
            StartCoroutine(SpawnFlow());
    }

    /// <summary>
    /// External trigger (e.g., from a volume or script) to attempt spawn now.
    /// Still respects proximity gating if enabled.
    /// </summary>
    public void TriggerSpawnNow()
    {
        StartCoroutine(SpawnFlow());
    }

    private IEnumerator SpawnFlow()
    {
        // No prefabs -> abort
        if (bossPrefabs == null || bossPrefabs.Length == 0)
            yield break;

        // Only one boss at a time by default
        if (currentBoss != null)
        {
            if (debugLogs) Debug.Log("[BossSpawner] Aborting: current boss still alive.", this);
            yield break;
        }

        if (_isSpawning) yield break;
        _isSpawning = true;

        // Optionally wait for NavMesh to be available
        if (waitForNavMesh)
        {
            while (!NavMeshUtils.NavMeshExists())
                yield return null;

            if (postNavmeshDelay > 0f)
                yield return new WaitForSeconds(postNavmeshDelay);
        }

        // ----------- Proximity gate -----------
        if (requirePlayerProximity)
        {
            // make sure we have/refresh a player reference
            if (player == null)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p) player = p.transform;
            }

            if (debugLogs) Debug.Log("[BossSpawner] Waiting for player to be within activation radius...", this);

            // Wait until player is inside radius of THIS spawner
            while (!IsPlayerWithinActivationRadius())
                yield return new WaitForSeconds(proximityCheckInterval);

            if (debugLogs) Debug.Log("[BossSpawner] Player within activation radius — proceeding to spawn.", this);
        }
        // -------------------------------------

        // Choose which boss to spawn (GLOBAL first, then LOCAL fallback)
        GameObject chosenPrefab = null;

        if (useGlobalPool)
        {
            chosenPrefab = BossGlobalRandomizer.Pick(globalGroupId,
                                                     globalMode,
                                                     debugLogs ? this : null);
            if (chosenPrefab == null && debugLogs)
                Debug.LogWarning($"[BossSpawner] Global pick returned null for group '{globalGroupId}'. Falling back to local.", this);
        }

        if (chosenPrefab == null)
        {
            chosenPrefab = PickBossPrefabLocal();
        }

        if (chosenPrefab == null)
        {
            Debug.LogError("[BossSpawner] No valid boss prefab could be selected. Aborting spawn.", this);
            _isSpawning = false;
            yield break;
        }

        // Find placement
        Vector3 spawnPos;
        if (!TryFindValidPointNear(transform.position, searchRadiusFromSpawner, out spawnPos))
        {
            if (debugLogs) Debug.LogWarning("[BossSpawner] No valid NavMesh near spawner; trying near player.", this);

            if (player != null)
            {
                if (!TryFindValidPointNear(player.position, fallbackRadiusFromPlayer, out spawnPos))
                {
                    Debug.LogError("[BossSpawner] Failed to find ANY NavMesh placement (even near player). Aborting spawn.", this);
                    _isSpawning = false;
                    yield break;
                }
            }
            else
            {
                Debug.LogError("[BossSpawner] No player found and no valid NavMesh near spawner. Aborting spawn.", this);
                _isSpawning = false;
                yield break;
            }
        }

        if (debugLogs) Debug.Log($"[BossSpawner] Spawning '{chosenPrefab.name}' at {spawnPos}", this);
        currentBoss = SpawnBossAt(chosenPrefab, spawnPos);
        _deathHandledOnce = false; // reset for new boss
        _isSpawning = false;
    }

    /// <summary>
    /// Local-only picker (used if global is disabled or unavailable).
    /// Avoids repeating the last local pick if configured.
    /// </summary>
    private GameObject PickBossPrefabLocal()
    {
        if (bossPrefabs == null || bossPrefabs.Length == 0) return null;

        // Collect valid indices
        var valid = new List<int>();
        for (int i = 0; i < bossPrefabs.Length; i++)
            if (bossPrefabs[i] != null) valid.Add(i);

        if (valid.Count == 0) return null;
        if (valid.Count == 1)
        {
            _lastPickedIndexLocal = valid[0];
            return bossPrefabs[_lastPickedIndexLocal];
        }

        const int maxRerolls = 8;
        int pick = -1;
        int rerolls = 0;
        do
        {
            pick = valid[Random.Range(0, valid.Count)];
            if (!avoidRepeatingLastLocal || pick != _lastPickedIndexLocal) break;
            rerolls++;
        }
        while (rerolls < maxRerolls);

        _lastPickedIndexLocal = pick;
        return bossPrefabs[pick];
    }

    /// <summary>
    /// Called by BossEnemy when it dies/destroys. We ensure this only processes once.
    /// </summary>
    public void NotifyBossDied(BossEnemy boss)
    {
        if (_deathHandledOnce) return; // prevent duplicate handling from multiple callbacks
        _deathHandledOnce = true;

        if (debugLogs)
            Debug.Log($"[BossSpawner] Boss '{boss?.name}' reported dead.", this);

        currentBoss = null;

        // If respawn is enabled, do that; otherwise optionally destroy the spawner.
        if (allowRespawn)
        {
            if (maxRespawns >= 0 && _respawnsSoFar >= maxRespawns)
            {
                if (debugLogs) Debug.Log("[BossSpawner] Max respawns reached; not respawning.", this);
                if (destroySpawnerOnFirstBossDeath)
                    DestroySpawnerSafely();
                return;
            }

            _respawnsSoFar++;
            StartCoroutine(RespawnAfterDelay(respawnDelay));
        }
        else
        {
            if (destroySpawnerOnFirstBossDeath)
                DestroySpawnerSafely();
        }
    }

    private void DestroySpawnerSafely()
    {
        if (debugLogs) Debug.Log($"[BossSpawner] Destroying spawner '{name}' after boss death.", this);
        Destroy(gameObject);
    }

    private IEnumerator RespawnAfterDelay(float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        yield return SpawnFlow(); // Proximity gating applies here too (and new random pick)
    }

    // ----------------- Helpers -----------------

    private bool IsPlayerWithinActivationRadius()
    {
        if (!requirePlayerProximity) return true;
        if (player == null) return false;

        Vector3 sp = transform.position;
        Vector3 pp = player.position;

        if (use2DDistanceForActivation)
        {
            sp.y = 0f;
            pp.y = 0f;
        }

        float d = Vector3.Distance(sp, pp);
        return d <= spawnActivationRadius;
    }

    private bool TryFindValidPointNear(Vector3 center, float radius, out Vector3 hitPos)
    {
        hitPos = center;

        if (NavMesh.SamplePosition(center, out var hitCenter, sampleMaxDistance, NavMesh.AllAreas))
        {
            if (PassesPlayerDistance(hitCenter.position))
            {
                hitPos = hitCenter.position;
                return true;
            }
        }

        for (int i = 0; i < maxAttempts; i++)
        {
            var r = Random.insideUnitCircle * radius;
            var candidate = new Vector3(center.x + r.x, center.y, center.z + r.y);

            if (NavMesh.SamplePosition(candidate, out var hit, sampleMaxDistance, NavMesh.AllAreas))
            {
                if (PassesPlayerDistance(hit.position))
                {
                    hitPos = hit.position;
                    return true;
                }
            }
        }

        return false;
    }

    private bool PassesPlayerDistance(Vector3 pos)
    {
        if (player == null) return true;
        Vector3 a = pos; a.y = 0;
        Vector3 b = player.position; b.y = 0;
        return Vector3.Distance(a, b) >= minDistanceFromPlayer;
    }

    private GameObject SpawnBossAt(GameObject prefab, Vector3 pos)
    {
        if (prefab == null) return null;

        var go = Instantiate(prefab, pos, Quaternion.identity);

        var agent = go.GetComponent<NavMeshAgent>();
        if (agent != null && safeAgentInit)
        {
            bool wasEnabled = agent.enabled;
            if (wasEnabled) agent.enabled = false;

            if (NavMesh.SamplePosition(pos, out var hit, sampleMaxDistance, NavMesh.AllAreas))
            {
                go.transform.position = hit.position;
                agent.enabled = true;
                if (!agent.Warp(hit.position))
                    Debug.LogWarning("[BossSpawner] Agent.Warp returned false; agent may still be off-mesh.", go);
            }
            else
            {
                Debug.LogError("[BossSpawner] Spawned position not on NavMesh (post-sample failed). Destroying boss instance.", go);
                Destroy(go);
                return null;
            }
        }

        // Optional: face player
        if (player != null)
        {
            Vector3 dir = player.position - go.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
                go.transform.rotation = Quaternion.LookRotation(dir);
        }

        // Wire back-reference so the boss can call NotifyBossDied(this)
        var bossComp = go.GetComponent<BossEnemy>();
        if (bossComp != null)
            bossComp.spawner = this;

        if (debugLogs) Debug.Log($"[BossSpawner] Boss '{prefab.name}' spawned successfully at {go.transform.position}.", this);
        return go;
    }

    private void OnDrawGizmosSelected()
    {
        if (!requirePlayerProximity) return;
        Gizmos.color = Color.yellow;
        Vector3 center = transform.position;
        if (use2DDistanceForActivation) center.y = (Camera.main ? Camera.main.transform.position.y : center.y);
        Gizmos.DrawWireSphere(center, Mathf.Max(0f, spawnActivationRadius));
    }
}

public static class NavMeshUtils
{
    public static bool NavMeshExists()
    {
        var tri = NavMesh.CalculateTriangulation();
        return tri.indices != null && tri.indices.Length > 0;
    }
}

/// <summary>
/// Global boss randomizer shared across spawners with the same Group Id.
/// - Maintains a union pool of prefabs per group (auto-registered by spawners in Awake).
/// - Two modes:
///     * NoImmediateRepeat: avoid picking the same prefab twice in a row globally.
///     * ShuffleBag: exhaustively cycle through randomized order before repeating.
/// </summary>
public static class BossGlobalRandomizer
{
    private class Group
    {
        public readonly HashSet<GameObject> pool = new HashSet<GameObject>(); // union of all registered prefabs
        public List<GameObject> bag = null; // for ShuffleBag mode
        public int bagCursor = 0;
        public GameObject lastPicked = null;
    }

    private static readonly Dictionary<string, Group> groups = new Dictionary<string, Group>();

    public static void Register(string groupId, IEnumerable<GameObject> prefabs)
    {
        if (string.IsNullOrEmpty(groupId) || prefabs == null) return;

        if (!groups.TryGetValue(groupId, out var g))
        {
            g = new Group();
            groups[groupId] = g;
        }

        bool addedAny = false;
        foreach (var p in prefabs)
        {
            if (p == null) continue;
            if (g.pool.Add(p)) addedAny = true;
        }

        // If pool changed, invalidate the bag so it rebuilds with the new union.
        if (addedAny)
        {
            g.bag = null;
            g.bagCursor = 0;
        }
    }

    public static GameObject Pick(string groupId, BossSpawner.GlobalMode mode, Component debugCtxOrNull)
    {
        if (string.IsNullOrEmpty(groupId)) return null;
        if (!groups.TryGetValue(groupId, out var g)) return null;

        // Prepare a stable list of valid entries
        var list = g.pool.Where(x => x != null).Distinct().ToList();
        if (list.Count == 0) return null;

        if (mode == BossSpawner.GlobalMode.NoImmediateRepeat)
        {
            if (list.Count == 1) // degenerate
                return g.lastPicked = list[0];

            // Avoid last picked globally
            const int maxRerolls = 12;
            GameObject pick = null;
            int rerolls = 0;
            do
            {
                pick = list[Random.Range(0, list.Count)];
                if (pick != g.lastPicked) break;
                rerolls++;
            }
            while (rerolls < maxRerolls);

            g.lastPicked = pick;
            if (debugCtxOrNull)
                Debug.Log($"[BossGlobalRandomizer] (NoRepeat) Picked '{pick?.name}' in group '{groupId}'.", debugCtxOrNull);
            return pick;
        }
        else // ShuffleBag
        {
            // If bag is missing or exhausted or pool changed size, rebuild
            if (g.bag == null || g.bagCursor >= g.bag.Count || g.bag.Count != list.Count)
            {
                g.bag = new List<GameObject>(list);
                // Fisher-Yates shuffle
                for (int i = g.bag.Count - 1; i > 0; i--)
                {
                    int k = Random.Range(0, i + 1);
                    (g.bag[i], g.bag[k]) = (g.bag[k], g.bag[i]);
                }
                // Avoid starting the new bag with the same as lastPicked if possible
                if (g.lastPicked != null && g.bag.Count > 1 && g.bag[0] == g.lastPicked)
                {
                    // swap with next
                    (g.bag[0], g.bag[1]) = (g.bag[1], g.bag[0]);
                }
                g.bagCursor = 0;

                if (debugCtxOrNull)
                    Debug.Log($"[BossGlobalRandomizer] Rebuilt shuffle bag for group '{groupId}' with {g.bag.Count} entries.", debugCtxOrNull);
            }

            var pick = g.bag[g.bagCursor++];
            g.lastPicked = pick;

            if (debugCtxOrNull)
                Debug.Log($"[BossGlobalRandomizer] (ShuffleBag) Picked '{pick?.name}' in group '{groupId}' (slot {g.bagCursor}/{g.bag.Count}).", debugCtxOrNull);

            return pick;
        }
    }
}
