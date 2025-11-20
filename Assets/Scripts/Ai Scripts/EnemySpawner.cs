using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

public class EnemySpawner : MonoBehaviour
{
    public enum DifficultyDriver { RealTime, PlayerScore, MinutesSurvived, WaveNumber }

    [System.Serializable]
    public class DriverConfig
    {
        public DifficultyDriver driver = DifficultyDriver.RealTime;
        public AnimationCurve curve = AnimationCurve.Linear(0, 0, 1, 1);

        [Header("Normalization (ignored by RealTime)")]
        public float scoreMin = 0f;
        public float scoreMax = 10000f;
        public float minutesMin = 0f;
        public float minutesMax = 20f;
        public int wavesMin = 1;
        public int wavesMax = 20;
    }

    [Header("Difficulty Mapping")]
    public DriverConfig waveSizeDriver = new DriverConfig { driver = DifficultyDriver.PlayerScore };  // used if Top-Up disabled
    public DriverConfig intervalDriver = new DriverConfig { driver = DifficultyDriver.MinutesSurvived };

    [Header("Wave Size Output (legacy 'wave' mode)")]
    public int startEnemiesPerWave = 2;
    public int maxEnemiesPerWave = 12;

    [Header("Top-Up Mode (recommended)")]
    [Tooltip("Keep at least N enemies alive (scales with difficulty).")]
    public bool maintainTargetActive = true;
    public int targetActiveStart = 12;
    public int targetActiveMax = 60;
    [Tooltip("Max number of new enemies spawned per tick while topping up.")]
    public int burstSpawnsPerTick = 20; // more per tick (was 12)

    [Header("Interval Output")]
    public float startSpawnInterval = 0.8f;
    public float minSpawnInterval = 0.20f;

    [Header("Global Limits")]
    [Tooltip("<= 0 = unlimited")]
    public int maxActiveEnemies = 0;

    [Header("Boss Awareness")]
    public string bossTag = "BossEnemy";
    public float bossAnchorWeight = 3f;
    public float withBossWaveMultiplier = 1.0f;
    public float noBossWaveMultiplier = 0.8f;

    [Header("Spawn Rings")]
    public Vector2 playerRing = new Vector2(12f, 28f);
    public Vector2 bossRing = new Vector2(6f, 16f);
    [Tooltip("Used if player is missing; spawns around this spawner.")]
    public Vector2 spawnerRing = new Vector2(8f, 18f);

    [Header("Player Safety")]
    [Tooltip("Absolute minimum horizontal (XZ) distance from the player for any spawn.")]
    public float minDistanceFromPlayer = 12f;

    [Header("References")]
    public Transform player;
    public GameObject enemyPrefab;

    [Header("Pooling")]
    public int poolSize = 60;
    private readonly List<GameObject> enemyPool = new List<GameObject>();

    [Header("NavMesh / Geometry")]
    [Tooltip("Search radius for NavMesh.SamplePosition")]
    public float navMeshCheckRadius = 6f;

    [Header("Grounding")]
    public LayerMask groundMask = ~0;
    public float groundProbeUp = 5f;
    public float groundProbeDown = 20f;
    public bool spherecastFallback = true;
    public float spherecastRadius = 0.25f;

    [Header("Height Filter")]
    [Tooltip("Absolute world Y limit. Spawns ABOVE this are forbidden (always enforced).")]
    public float maxSpawnHeight = 200f;
    [Tooltip("Optional: forbid spawns above player Y + delta. <= 0 disables this softer/legacy check.")]
    public float maxHeightDeltaFromPlayer = 0f; // <=0 disables

    [Header("Relative Height Gate (strict)")]
    [Tooltip("If true, reject any spawn whose Y is above the player's ground Y + allowance.")]
    public bool requireBelowOrEqualToPlayer = true;
    [Tooltip("Base allowance above player's ground Y when candidate is very close (meters).")]
    public float relativeCeiling = 0.5f;

    [Header("Distance-Aware Ceiling (anti-hill starvation)")]
    [Tooltip("Allowance near the player (<= this takes precedence over RelativeCeiling).")]
    public float ceilingAllowanceAtZero = 0.6f;
    [Tooltip("Allowance far from the player (lets hills pass but still blocks roofs).")]
    public float ceilingAllowanceAtMax = 3.0f;
    [Tooltip("Beyond this XZ distance from the player, the max allowance is used.")]
    public float ceilingAllowanceMaxRadius = 40f;

    [Header("Surface Filter (anti-rooftop)")]
    [Tooltip("Spawn is rejected if the ground collider below the candidate sits on any of these layers (e.g., 'Roof').")]
    public LayerMask forbiddenGroundLayers = 0; // set to your Roof/BuildingTop layer(s)
    [Tooltip("Optional: spawn is rejected if the ground collider tag matches any of these (e.g., 'Roof').")]
    public string[] forbiddenGroundTags;

    [Header("Spawn-Free Zones")]
    [Tooltip("Any collider on these layers marks a no-spawn zone (e.g., your SafeRoomTile layer).")]
    public LayerMask noSpawnZoneLayers = 0; // 0 = disabled
    [Tooltip("Optional: if the hit collider underfoot has any of these tags, it's a no-spawn zone.")]
    public string[] noSpawnZoneTags;
    [Tooltip("Extra radius to check around the candidate for no-spawn zone colliders/components.")]
    public float noSpawnZoneRadius = 0.75f;
    [Tooltip("If true, any collider with a NoSpawnZone component (or on parent) blocks the spawn.")]
    public bool checkNoSpawnZoneComponents = true;

    [Header("Player Ground Reference")]
    [Tooltip("Use a raycast to find the player's ground Y (better than transform.y for stairs/slopes).")]
    public bool useGroundedPlayerHeight = true;
    public float playerGroundSampleUp = 2f;
    public float playerGroundSampleDown = 6f;

    [Header("Near-Player Salvage")]
    [Tooltip("If a candidate is inside the safety donut, push it out to the rim and resample nav/ground instead of rejecting.")]
    public bool pushOutOfSafetyDonut = true;
    [Tooltip("Extra padding beyond the donut rim when pushing out.")]
    public float pushOutExtra = 0.5f;
    [Tooltip("NavMesh.SamplePosition search radius used after push-out.")]
    public float resampleNavAfterPushRadius = 2.0f;

    [Header("NavMesh Area Filter")]
    [Tooltip("Leave empty to allow all.")]
    public string[] allowedAreas;
    private int allowedAreaMask = NavMesh.AllAreas;

    [Header("Visibility / Pop-in Control")]
    public bool requireOffscreenSpawn = false;
    public float offscreenBuffer = 0.2f;

    [Header("Collision Safety")]
    public LayerMask noSpawnOverlapLayers = 0; // 0 = disabled
    public float noSpawnOverlapRadius = 0.6f;

    [Header("Recycling")]
    public float recycleDistance = 60f;

    [Header("Attempts")]
    public int maxAttemptsPerEnemy = 32; // more stubborn (was 24)

    [Header("NavMesh Timing")]
    public bool waitForNavMesh = true;

    [Header("Diagnostics")]
    public bool debugLogs = true;

    [Header("Debug Rescue")]
    [Tooltip("Bypass offscreen/overlap/legacy relative-to-player filters when true. NOTE: Absolute height and strict/distance-aware ceilings are ALWAYS enforced.")]
    public bool debugBypassFilters = false;   // default false so filters run by default
    [Tooltip("Guarantee at least 1 attempt per tick if pool/cap allow.")]
    public bool ensureAtLeastOne = false;     // disabled; we�ll batch force instead
    [Tooltip("Max emergency spawns when normal produced 0 or not enough.")]
    public int forcedSpawnCount = 25;         // bigger emergency batches
    [Tooltip("If true, anchor to this spawner when player is missing.")]
    public bool spawnIfNoPlayer = true;

    private Camera mainCam;
    private float sessionStartTime;
    private GameManager gm;

    // rejections (per tick)
    private int rejOffscreen, rejHeightAbs, rejHeightDelta, rejOverlap, rejNoNav, rejNoGround, rejPoolFull, rejActiveCap, rejNearPlayer;
    private int rejAbovePlayerStrict, rejForbiddenSurface, rejAnchorTooClose, rejNoSpawnZone;

    // nav gate
    private bool navReady = true;
    private NavMeshRuntimeBaker baker;

    // new: deferred start control
    [SerializeField] private float navmeshReadyTimeout = 20f;
    private bool _spawnLoopStarted;

    // ---------------------- Unity lifecycle ----------------------

    private void Awake()
    {
        if (!player)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p) player = p.transform;
        }
        mainCam = Camera.main;
        gm = GameManager.Instance;

        // Build area mask if names provided
        if (allowedAreas != null && allowedAreas.Length > 0)
        {
            int mask = 0;
            for (int i = 0; i < allowedAreas.Length; i++)
            {
                string nm = allowedAreas[i];
                if (string.IsNullOrEmpty(nm)) continue;
                int id = NavMesh.GetAreaFromName(nm);
                if (id >= 0) mask |= 1 << id;
                else Debug.LogWarning("[EnemySpawner] Unknown NavMesh area '" + nm + "'.");
            }
            if (mask != 0) allowedAreaMask = mask; // otherwise AllAreas
        }
    }

    // >>> UPDATED <<<
    private void Start()
    {
        if (!enemyPrefab)
        {
            Debug.LogError("[EnemySpawner] Missing enemyPrefab. Assign it in the Inspector.");
            enabled = false;
            return;
        }

        // Nav readiness
        navReady = true;
        if (waitForNavMesh)
        {
            // CHANGED: use new API to avoid CS0618
            baker = NavMeshRuntimeBaker.Instance ?? FindFirstObjectByType<NavMeshRuntimeBaker>();
            if (baker != null)
            {
                navReady = baker.BakeCompleted;
                if (!navReady) baker.OnBakeCompleted += HandleBakeCompleted;
            }
            else
            {
                // No runtime baker found; assume pre-baked navmesh exists
                navReady = true;
            }
        }

        // Clamp rings
        playerRing = new Vector2(Mathf.Max(0.5f, Mathf.Min(playerRing.x, playerRing.y)), Mathf.Max(playerRing.x, playerRing.y));
        bossRing = new Vector2(Mathf.Max(0.5f, Mathf.Min(bossRing.x, bossRing.y)), Mathf.Max(bossRing.x, bossRing.y));
        spawnerRing = new Vector2(Mathf.Max(0.5f, Mathf.Min(spawnerRing.x, spawnerRing.y)), Mathf.Max(spawnerRing.x, spawnerRing.y));

        CreatePool();
        sessionStartTime = Time.time;

        if (waitForNavMesh)
        {
            // Defer triangulation check + start until ready or timeout.
            StartCoroutine(WaitForNavMeshThenStart());
        }
        else
        {
            // Not waiting: warn only if navmesh is actually absent, then start.
            var tri = NavMesh.CalculateTriangulation();
            if (tri.vertices == null || tri.vertices.Length == 0)
                Debug.LogWarning("[EnemySpawner] No NavMesh data found. Ensure the scene is baked or runtime bake finished.");

            StartSpawnLoopIfNeeded();
        }
    }

    private void OnDestroy()
    {
        if (baker != null) baker.OnBakeCompleted -= HandleBakeCompleted;
    }

    // >>> UPDATED <<<
    private void HandleBakeCompleted()
    {
        navReady = true;
        if (debugLogs) Debug.Log("[EnemySpawner] NavMesh bake completed�spawning enabled.");
        StartSpawnLoopIfNeeded();
    }

    private void OnValidate()
    {
        // Keep sensible values in the editor
        if (playerRing.y < playerRing.x) playerRing.y = playerRing.x + 0.5f;
        if (bossRing.y < bossRing.x) bossRing.y = bossRing.x + 0.5f;
        if (spawnerRing.y < spawnerRing.x) spawnerRing.y = spawnerRing.x + 0.5f;

        navMeshCheckRadius = Mathf.Max(0.05f, navMeshCheckRadius);
        groundProbeUp = Mathf.Max(0.1f, groundProbeUp);
        groundProbeDown = Mathf.Max(0.1f, groundProbeDown);
        noSpawnOverlapRadius = Mathf.Max(0f, noSpawnOverlapRadius);
        recycleDistance = Mathf.Max(1f, recycleDistance);
        maxSpawnHeight = Mathf.Clamp(maxSpawnHeight, -10000f, 10000f);

        relativeCeiling = Mathf.Max(0f, relativeCeiling);
        ceilingAllowanceAtZero = Mathf.Max(0f, ceilingAllowanceAtZero);
        ceilingAllowanceAtMax = Mathf.Max(ceilingAllowanceAtZero, ceilingAllowanceAtMax);
        ceilingAllowanceMaxRadius = Mathf.Max(1f, ceilingAllowanceMaxRadius);

        playerGroundSampleUp = Mathf.Max(0.01f, playerGroundSampleUp);
        playerGroundSampleDown = Mathf.Max(0.01f, playerGroundSampleDown);

        resampleNavAfterPushRadius = Mathf.Max(0.25f, resampleNavAfterPushRadius);
        pushOutExtra = Mathf.Max(0f, pushOutExtra);

        noSpawnZoneRadius = Mathf.Max(0f, noSpawnZoneRadius);
    }

    // ---------------------- Core loop ----------------------

    private void CreatePool()
    {
        for (int i = 0; i < poolSize; i++)
        {
            GameObject enemy = Instantiate(enemyPrefab);
            EnemyWaveHooks hooks = enemy.GetComponent<EnemyWaveHooks>();
            if (!hooks) hooks = enemy.AddComponent<EnemyWaveHooks>();
            hooks.SetSpawner(this);
            enemy.SetActive(false);
            enemyPool.Add(enemy);
        }
    }

    private IEnumerator SpawnLoop()
    {
        yield return new WaitForSeconds(0.25f);

        while (true)
        {
            // Player may be missing � we can still spawn around this spawner
            if (!player && spawnIfNoPlayer)
            {
                GameObject p = GameObject.FindGameObjectWithTag("Player");
                if (p) player = p.transform;
            }

            if (waitForNavMesh && !navReady)
            {
                yield return new WaitForSeconds(0.2f);
                continue;
            }

            // reset counters
            rejOffscreen = rejHeightAbs = rejHeightDelta = rejOverlap = rejNoNav = rejNoGround =
            rejPoolFull = rejActiveCap = rejNearPlayer = rejAbovePlayerStrict = rejForbiddenSurface = rejAnchorTooClose = rejNoSpawnZone = 0;

            // Evaluate difficulty
            float waveEval = EvaluateDriver(waveSizeDriver); // 0..1
            float intEval = EvaluateDriver(intervalDriver); // 0..1

            int activeNow = GetActiveEnemyCount();
            int poolAvailable = GetAvailablePoolCount();
            int freeCapacity = (maxActiveEnemies > 0 ? Mathf.Max(0, maxActiveEnemies - activeNow) : int.MaxValue);
            if (freeCapacity == 0) rejActiveCap++;
            if (poolAvailable == 0) rejPoolFull++;

            // Decide how many to spawn this tick
            int spawnThisTick = 0;

            List<Transform> bosses = GetActiveBosses();
            if (maintainTargetActive)
            {
                // top-up mode: compute desired active count
                int desiredActive = Mathf.RoundToInt(Mathf.Lerp(targetActiveStart, targetActiveMax, waveEval));
                float mult = (bosses.Count > 0) ? withBossWaveMultiplier : noBossWaveMultiplier;
                desiredActive = Mathf.RoundToInt(desiredActive * Mathf.Max(0.1f, mult));

                int need = Mathf.Max(0, desiredActive - activeNow);
                spawnThisTick = Mathf.Min(need, burstSpawnsPerTick, poolAvailable, freeCapacity);

                // don't throttle to 1; let aggressive fallbacks handle starvation cases
            }
            else
            {
                // legacy 'wave' mode
                int targetWave = Mathf.RoundToInt(Mathf.Lerp(startEnemiesPerWave, maxEnemiesPerWave, waveEval));
                float waveMult = (bosses.Count > 0) ? withBossWaveMultiplier : noBossWaveMultiplier;
                targetWave = Mathf.RoundToInt(targetWave * Mathf.Max(0f, waveMult));
                spawnThisTick = Mathf.Min(targetWave, poolAvailable, freeCapacity);
            }

            int spawned = 0;
            if (spawnThisTick > 0)
                spawned = SpawnEnemiesBossAware(spawnThisTick, bosses);

            // --- Aggressive fallbacks ---

            // 1) Normal pass produced zero: force a real batch (not just 1)
            if (spawnThisTick > 0 && spawned == 0 && forcedSpawnCount > 0)
            {
                int forced = ForceSpawnAroundBestAnchor(
                    Mathf.Min(
                        Mathf.Max(spawnThisTick, 10),
                        forcedSpawnCount,
                        poolAvailable,
                        freeCapacity
                    )
                );
                if (debugLogs && forced > 0)
                    Debug.LogWarning($"[EnemySpawner] Forced batch (0 normal). Forced={forced}");
            }

            // 2) Normal pass partially succeeded: fill the rest this tick
            if (spawnThisTick > 0 && spawned > 0 && spawned < spawnThisTick && forcedSpawnCount > 0)
            {
                int remaining = spawnThisTick - spawned;
                int forced = ForceSpawnAroundBestAnchor(
                    Mathf.Min(
                        Mathf.Max(remaining, 5), // at least 5 to avoid trickle
                        forcedSpawnCount,
                        poolAvailable,
                        freeCapacity
                    )
                );
                if (debugLogs && forced > 0)
                    Debug.LogWarning($"[EnemySpawner] Forced remainder. Remaining={remaining} Forced={forced}");
            }

            // 3) If we wanted to spawn but calculation yielded 0 (need existed but capped by pool/cap), try a small forced burst
            if (spawnThisTick == 0 && maintainTargetActive && poolAvailable > 0 && freeCapacity > 0)
            {
                int desiredActive = Mathf.RoundToInt(Mathf.Lerp(targetActiveStart, targetActiveMax, waveEval));
                float mult = (bosses.Count > 0) ? withBossWaveMultiplier : noBossWaveMultiplier;
                desiredActive = Mathf.RoundToInt(desiredActive * Mathf.Max(0.1f, mult));

                int deficit = Mathf.Max(0, desiredActive - activeNow);
                if (deficit > 0 && forcedSpawnCount > 0)
                {
                    int tryForce = Mathf.Min(Mathf.Max(5, deficit / 2), forcedSpawnCount, poolAvailable, freeCapacity);
                    int forced = (tryForce > 0) ? ForceSpawnAroundBestAnchor(tryForce) : 0;
                    if (debugLogs && forced > 0)
                        Debug.LogWarning($"[EnemySpawner] Forced due to deficit with 0 planned. Deficit={deficit} Forced={forced}");
                }
            }

            RecycleEnemies();

            // Compute the base interval from difficulty driver
            float currentInterval = Mathf.Lerp(startSpawnInterval, minSpawnInterval, intEval);
            currentInterval = Mathf.Max(0.05f, currentInterval);

            // If we're behind the desired count, shrink the interval even more
            if (maintainTargetActive)
            {
                int desiredActive = Mathf.RoundToInt(Mathf.Lerp(targetActiveStart, targetActiveMax, waveEval));
                float mult = (bosses.Count > 0) ? withBossWaveMultiplier : noBossWaveMultiplier;
                desiredActive = Mathf.RoundToInt(desiredActive * Mathf.Max(0.1f, mult));

                int deficit = Mathf.Max(0, desiredActive - activeNow);
                if (deficit > 0)
                {
                    // 0..1 how far behind we are
                    float pressure = Mathf.Clamp01((float)deficit / Mathf.Max(1, desiredActive));
                    // up to ~5x faster when very behind
                    float speedup = Mathf.Lerp(1f, 0.2f, pressure);
                    currentInterval = Mathf.Max(0.03f, currentInterval * speedup);
                }
            }

            yield return new WaitForSeconds(currentInterval);
        }
    }

    // ---------------------- Spawning ----------------------

    private int SpawnEnemiesBossAware(int count, List<Transform> bosses)
    {
        // Build anchors (bosses + player or spawner) with distance-weighting away from the player
        List<Transform> anchors = new List<Transform>();
        List<float> weights = new List<float>();

        if (bosses != null && bosses.Count > 0)
        {
            for (int i = 0; i < bosses.Count; i++)
            {
                Transform b = bosses[i];
                if (!b || !b.gameObject.activeInHierarchy) continue;
                anchors.Add(b);
                weights.Add(ComputeAnchorWeight(b, Mathf.Max(0f, bossAnchorWeight)));
            }
        }

        Transform playerOrSpawner = (player != null) ? player : this.transform;
        anchors.Add(playerOrSpawner);
        weights.Add(ComputeAnchorWeight(playerOrSpawner, 1f));

        int spawned = 0, safety = 0;
        while (spawned < count && safety < count * maxAttemptsPerEnemy)
        {
            safety++;

            GameObject enemy = GetPooledEnemy();
            if (!enemy) { rejPoolFull++; break; }

            Transform anchor = WeightedPick(anchors, weights);
            bool anchorIsBoss = (bosses != null && anchor != null && bosses.Contains(anchor));
            Vector2 ring = anchorIsBoss ? bossRing : ((anchor == player) ? playerRing : spawnerRing);

            // If anchoring to player, bump min radius to at least the safety radius
            if (anchor == player)
                ring = new Vector2(Mathf.Max(ring.x, minDistanceFromPlayer + 0.5f), ring.y);

            // If anchoring to something NEAR the player, shrink the ring so candidates cannot fall into the near-player dead zone.
            if (player != null && anchor != player)
            {
                float distAP = DistanceXZ(anchor.position, player.position);
                float maxAllowed = distAP - (minDistanceFromPlayer + 0.5f);
                if (maxAllowed <= ring.x + 0.25f)
                {
                    // Anchor too close to player for this ring; try another anchor
                    rejAnchorTooClose++;
                    continue;
                }
                ring.y = Mathf.Min(ring.y, maxAllowed);
            }

            Vector3 pos;
            if (TryFindValidSpawnNearAnchor(anchor.position, ring.x, ring.y, out pos))
            {
                // Salvage if inside the safety donut: push outward then re-ground
                if (player != null && DistanceXZ(pos, player.position) < minDistanceFromPlayer)
                {
                    if (!TryPushOutOfSafetyDonut(ref pos))
                    {
                        rejNearPlayer++;
                        continue;
                    }
                }

                if (!PassesFilters(pos, checkNearPlayer: true))
                    continue;

                NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
                if (agent != null)
                {
                    if (!agent.enabled) agent.enabled = true;
                    agent.Warp(pos);
                }
                enemy.transform.position = pos;
                enemy.SetActive(true);

                // Apply wave-based health scaling
                if (GameManager.Instance != null)
                {
                    EnemyController ec = enemy.GetComponent<EnemyController>();
                    if (ec != null)
                    {
                        ec.SetSpawnWave(GameManager.Instance.CurrentWave);
                    }
                }

                AgentAutoBaseOffset auto = enemy.GetComponent<AgentAutoBaseOffset>();
                if (auto) auto.RecomputeNow();
                StartCoroutine(FinalSnapToGround(enemy));

                EnemyWaveHooks hooks = enemy.GetComponent<EnemyWaveHooks>();
                if (hooks) hooks.NotifySpawned();
                spawned++;
            }
        }

        if (debugLogs && spawned == 0)
        {
            Debug.LogWarning("[EnemySpawner] Tick produced 0 spawns. Rej: NoNav=" + rejNoNav +
                             " NoGround=" + rejNoGround + " HeightAbs=" + rejHeightAbs +
                             " StrictAbovePlayer=" + rejAbovePlayerStrict +
                             " LegacyHeightDelta=" + rejHeightDelta + " Offscreen=" + rejOffscreen +
                             " ForbiddenSurface=" + rejForbiddenSurface +
                             " NoSpawnZone=" + rejNoSpawnZone +
                             " Overlap=" + rejOverlap + " PoolFull=" + rejPoolFull + " Cap=" + rejActiveCap +
                             " NearPlayer=" + rejNearPlayer + " AnchorTooClose=" + rejAnchorTooClose);
        }
        return spawned;
    }

    private int ForceSpawnAroundBestAnchor(int count)
    {
        Vector3 center;
        Vector2 ring;

        if (player != null) { center = player.position; ring = playerRing; }
        else { center = transform.position; ring = spawnerRing; }

        // Respect safety radius when using player center
        if (player != null)
            ring = new Vector2(Mathf.Max(ring.x, minDistanceFromPlayer + 0.5f), ring.y);

        int spawned = 0;
        for (int i = 0; i < count; i++)
        {
            GameObject enemy = GetPooledEnemy();
            if (!enemy) { rejPoolFull++; break; }

            bool spawnedThis = false;

            for (int tries = 0; tries < 16; tries++)
            {
                Vector2 rv = Random.insideUnitCircle.normalized * Random.Range(Mathf.Max(1f, ring.x), ring.y);
                Vector3 guess = new Vector3(center.x + rv.x, center.y + groundProbeUp, center.z + rv.y);

                NavMeshHit hit;
                if (!NavMesh.SamplePosition(guess, out hit, navMeshCheckRadius * 2f, allowedAreaMask)) { rejNoNav++; continue; }

                Vector3 top = new Vector3(hit.position.x, Mathf.Max(hit.position.y, center.y) + groundProbeUp, hit.position.z);

                Vector3 pos;
                RaycastHit gh;
                if (Physics.Raycast(top, Vector3.down, out gh, groundProbeDown + groundProbeUp, groundMask, QueryTriggerInteraction.Ignore))
                    pos = gh.point;
                else if (spherecastFallback &&
                         Physics.SphereCast(top, spherecastRadius, Vector3.down, out gh, groundProbeDown + groundProbeUp, groundMask, QueryTriggerInteraction.Ignore))
                    pos = gh.point;
                else pos = hit.position;

                // Salvage if inside the safety donut
                if (player != null && DistanceXZ(pos, player.position) < minDistanceFromPlayer)
                {
                    if (!TryPushOutOfSafetyDonut(ref pos))
                    {
                        rejNearPlayer++;
                        continue;
                    }
                }

                // Apply filters (absolute + strict/distance-aware ceiling always enforced)
                if (!PassesFilters(pos, checkNearPlayer: true))
                    continue;

                NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
                if (agent) { if (!agent.enabled) agent.enabled = true; agent.Warp(pos); }
                enemy.transform.position = pos;
                enemy.SetActive(true);

                // Apply wave-based health scaling
                if (GameManager.Instance != null)
                {
                    EnemyController ec = enemy.GetComponent<EnemyController>();
                    if (ec != null)
                    {
                        ec.SetSpawnWave(GameManager.Instance.CurrentWave);
                    }
                }

                AgentAutoBaseOffset auto = enemy.GetComponent<AgentAutoBaseOffset>();
                if (auto) auto.RecomputeNow();

                StartCoroutine(FinalSnapToGround(enemy));
                EnemyWaveHooks hooks = enemy.GetComponent<EnemyWaveHooks>();
                if (hooks) hooks.NotifySpawned();
                spawned++;
                spawnedThis = true;
                break;
            }

            if (!spawnedThis) { /* fall-through; try next */ }
        }
        return spawned;
    }

    private IEnumerator FinalSnapToGround(GameObject enemy)
    {
        yield return null; // wait a frame so agent/anim updates don't fight us
        if (!enemy || !enemy.activeInHierarchy) yield break;

        Vector3 start = enemy.transform.position + Vector3.up * 3f;
        RaycastHit hit;
        if (Physics.Raycast(start, Vector3.down, out hit, 12f, groundMask, QueryTriggerInteraction.Ignore))
        {
            Vector3 grounded = hit.point;
            NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
            if (agent) agent.Warp(grounded);
            else enemy.transform.position = grounded;

            AgentAutoBaseOffset auto = enemy.GetComponent<AgentAutoBaseOffset>();
            if (auto) auto.RecomputeNow();
        }

        // Post-snap validation: if we still ended up invalid (e.g., roof), despawn it.
        if (!PassesFilters(enemy.transform.position, checkNearPlayer: false))
        {
            EnemyWaveHooks hooks = enemy.GetComponent<EnemyWaveHooks>();
            if (hooks) hooks.NotifyDespawned();
            enemy.SetActive(false);
        }
    }

    // ---------------------- Filters & Helpers ----------------------

    /// <summary>
    /// Applies spawn acceptance rules at a candidate position.
    /// - Absolute height is ALWAYS enforced.
    /// - Strict/distance-aware relative-to-player ceiling is ALWAYS enforced when enabled.
    /// - Offscreen/overlap/legacy relative height can be bypassed by debugBypassFilters.
    /// - Surface (roof layer/tag) is always checked (not bypassed).
    /// - Spawn-free zones (component/layer/tag) are always checked (not bypassed).
    /// </summary>
    private bool PassesFilters(Vector3 candidate, bool checkNearPlayer)
    {
        // Absolute world-height cap (always enforced)
        if (candidate.y > maxSpawnHeight)
        {
            rejHeightAbs++;
            return false;
        }

        // Strict + distance-aware relative-to-player ceiling
        if (requireBelowOrEqualToPlayer && player != null)
        {
            float refY = GetPlayerReferenceY();
            float allowance = GetDynamicCeilingAllowance(candidate); // depends on distance from player
            if (candidate.y > refY + allowance)
            {
                rejAbovePlayerStrict++;
                return false;
            }
        }

        // Optional legacy relative-to-player height cap (softer)
        if (!debugBypassFilters && player && maxHeightDeltaFromPlayer > 0f)
        {
            float refY = GetPlayerReferenceY();
            if (candidate.y > refY + maxHeightDeltaFromPlayer)
            {
                rejHeightDelta++;
                return false;
            }
        }

        // Surface + spawn-free-zone check using the collider directly underfoot
        RaycastHit surfHit;
        Vector3 probeStart = candidate + Vector3.up * 0.25f;
        if (Physics.Raycast(probeStart, Vector3.down, out surfHit, 1.0f, groundMask, QueryTriggerInteraction.Ignore))
        {
            // Layer gate (forbidden surfaces like Roofs)
            if ((forbiddenGroundLayers.value != 0) &&
                ((forbiddenGroundLayers.value & (1 << surfHit.collider.gameObject.layer)) != 0))
            {
                rejForbiddenSurface++;
                return false;
            }
            // Tag gate (forbidden surfaces like "Roof")
            if (forbiddenGroundTags != null && forbiddenGroundTags.Length > 0)
            {
                string tag = surfHit.collider.tag;
                for (int i = 0; i < forbiddenGroundTags.Length; i++)
                {
                    if (!string.IsNullOrEmpty(forbiddenGroundTags[i]) && tag == forbiddenGroundTags[i])
                    {
                        rejForbiddenSurface++;
                        return false;
                    }
                }
            }

            // --- Spawn-free-zone gates (ALWAYS enforced) ---
            if (IsInNoSpawnZone(candidate, surfHit.collider))
            {
                rejNoSpawnZone++;
                return false;
            }
        }
        else
        {
            // Even if we didn't find ground directly, still check radius-based no-spawn zones
            if (IsInNoSpawnZone(candidate, null))
            {
                rejNoSpawnZone++;
                return false;
            }
        }

        // Offscreen requirement
        if (!debugBypassFilters && requireOffscreenSpawn && !IsOffscreen(candidate))
        {
            rejOffscreen++;
            return false;
        }

        // Overlap clearance
        if (!debugBypassFilters && (noSpawnOverlapLayers.value != 0) &&
            Physics.CheckSphere(candidate, noSpawnOverlapRadius, noSpawnOverlapLayers, QueryTriggerInteraction.Ignore))
        {
            rejOverlap++;
            return false;
        }

        // Safety radius vs player (after push-out, this should rarely trigger)
        if (checkNearPlayer && player != null && DistanceXZ(candidate, player.position) < minDistanceFromPlayer)
        {
            rejNearPlayer++;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns true if candidate sits inside a spawn-free zone.
    /// Priority:
    /// 1) Collider or any parent carries NoSpawnZone component (when enabled)
    /// 2) Collider (or nearby colliders within radius) is on noSpawnZoneLayers
    /// 3) Collider (or nearby) has any of noSpawnZoneTags
    /// </summary>
    private bool IsInNoSpawnZone(Vector3 candidate, Collider groundCol)
    {
        // 1) Component check on ground collider or its parents
        if (checkNoSpawnZoneComponents)
        {
            if (groundCol)
            {
                if (groundCol.GetComponentInParent<NoSpawnZone>() != null)
                    return true;
            }

            // radius component check
            Collider[] comps = Physics.OverlapSphere(candidate, Mathf.Max(0.01f, noSpawnZoneRadius), ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < comps.Length; i++)
            {
                if (!comps[i]) continue;
                if (comps[i].GetComponentInParent<NoSpawnZone>() != null)
                    return true;
            }
        }

        // 2) Layer-based check (ground collider layer or any nearby collider on that layer)
        if (noSpawnZoneLayers.value != 0)
        {
            if (groundCol && ((noSpawnZoneLayers.value & (1 << groundCol.gameObject.layer)) != 0))
                return true;

            Collider[] hits = Physics.OverlapSphere(candidate, Mathf.Max(0.01f, noSpawnZoneRadius), noSpawnZoneLayers, QueryTriggerInteraction.Collide);
            if (hits != null && hits.Length > 0)
                return true;
        }

        // 3) Tag-based check
        if (noSpawnZoneTags != null && noSpawnZoneTags.Length > 0)
        {
            if (groundCol)
            {
                string tag = groundCol.tag;
                for (int i = 0; i < noSpawnZoneTags.Length; i++)
                {
                    if (!string.IsNullOrEmpty(noSpawnZoneTags[i]) && tag == noSpawnZoneTags[i])
                        return true;
                }
            }

            Collider[] hitsAll = Physics.OverlapSphere(candidate, Mathf.Max(0.01f, noSpawnZoneRadius), ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < hitsAll.Length; i++)
            {
                if (!hitsAll[i]) continue;
                string tag = hitsAll[i].tag;
                for (int j = 0; j < noSpawnZoneTags.Length; j++)
                {
                    if (!string.IsNullOrEmpty(noSpawnZoneTags[j]) && tag == noSpawnZoneTags[j])
                        return true;
                }
            }
        }

        return false;
    }

    private float GetPlayerReferenceY()
    {
        if (!player) return float.NegativeInfinity;
        if (!useGroundedPlayerHeight) return player.position.y;

        RaycastHit ph;
        if (Physics.Raycast(player.position + Vector3.up * playerGroundSampleUp,
                            Vector3.down,
                            out ph,
                            playerGroundSampleUp + playerGroundSampleDown,
                            groundMask,
                            QueryTriggerInteraction.Ignore))
        {
            return ph.point.y;
        }
        return player.position.y;
    }

    private float GetDynamicCeilingAllowance(Vector3 candidate)
    {
        if (!player) return relativeCeiling;
        float d = DistanceXZ(candidate, player.position);
        float t = Mathf.Clamp01(d / ceilingAllowanceMaxRadius);
        // start with the larger of relativeCeiling and "zero-distance" allowance
        float a0 = Mathf.Max(relativeCeiling, ceilingAllowanceAtZero);
        return Mathf.Lerp(a0, ceilingAllowanceAtMax, t);
    }

    private bool TryPushOutOfSafetyDonut(ref Vector3 pos)
    {
        if (!pushOutOfSafetyDonut || !player) return false;

        // radial direction on XZ away from player
        Vector3 dir = pos - player.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
        {
            // degenerate: choose a random outward direction
            Vector2 rv = Random.insideUnitCircle.normalized;
            dir = new Vector3(rv.x, 0f, rv.y);
        }
        dir.Normalize();

        Vector3 target = player.position + dir * (minDistanceFromPlayer + pushOutExtra);

        // sample navmesh near the pushed-out target
        NavMeshHit hit;
        if (!NavMesh.SamplePosition(target + Vector3.up * groundProbeUp, out hit, Mathf.Max(navMeshCheckRadius, resampleNavAfterPushRadius), allowedAreaMask))
            return false;

        // ground it
        Vector3 top = new Vector3(hit.position.x, Mathf.Max(hit.position.y, target.y) + groundProbeUp, hit.position.z);
        RaycastHit gh;
        Vector3 grounded;
        if (Physics.Raycast(top, Vector3.down, out gh, groundProbeDown + groundProbeUp, groundMask, QueryTriggerInteraction.Ignore))
            grounded = gh.point;
        else if (spherecastFallback &&
                 Physics.SphereCast(top, spherecastRadius, Vector3.down, out gh, groundProbeDown + groundProbeUp, groundMask, QueryTriggerInteraction.Ignore))
            grounded = gh.point;
        else
            grounded = hit.position;

        pos = grounded;
        return true;
    }

    private float ComputeAnchorWeight(Transform anchor, float baseWeight)
    {
        if (!player) return baseWeight;
        if (anchor == player) return baseWeight; // keep player's anchor viable

        float d = DistanceXZ(anchor.position, player.position);
        // Prefer anchors further from player; keep a nonzero floor so we don't starve options.
        float norm = Mathf.Max(1f, minDistanceFromPlayer + playerRing.y);
        float factor = Mathf.Clamp01((d - (minDistanceFromPlayer * 0.75f)) / norm);
        return baseWeight * (0.25f + 0.75f * factor);
    }

    private Transform WeightedPick(List<Transform> anchors, List<float> weights)
    {
        if (anchors == null || anchors.Count == 0) return player ? player : transform;
        if (weights == null || weights.Count != anchors.Count) return anchors[anchors.Count - 1];

        float total = 0f;
        for (int i = 0; i < weights.Count; i++) total += Mathf.Max(0f, weights[i]);
        if (total <= 0f) return anchors[anchors.Count - 1];

        float r = Random.value * total, acc = 0f;
        for (int i = 0; i < anchors.Count; i++)
        {
            acc += Mathf.Max(0f, weights[i]);
            if (r <= acc) return anchors[i];
        }
        return anchors[anchors.Count - 1];
    }

    private float EvaluateDriver(DriverConfig cfg)
    {
        float tNorm = 0f;
        GameManager gmLoc = gm ?? GameManager.Instance;
        switch (cfg.driver)
        {
            case DifficultyDriver.RealTime:
                tNorm = Mathf.Clamp01((Time.time - sessionStartTime) / 300f); break;
            case DifficultyDriver.PlayerScore:
                {
                    float s = gmLoc ? gmLoc.Score : 0f;
                    float span = Mathf.Max(0.0001f, cfg.scoreMax - cfg.scoreMin);
                    tNorm = Mathf.Clamp01((s - cfg.scoreMin) / span); break;
                }
            case DifficultyDriver.MinutesSurvived:
                {
                    float m = gmLoc ? gmLoc.MinutesSurvived : ((Time.time - sessionStartTime) / 60f);
                    float span = Mathf.Max(0.0001f, cfg.minutesMax - cfg.minutesMin);
                    tNorm = Mathf.Clamp01((m - cfg.minutesMin) / span); break;
                }
            case DifficultyDriver.WaveNumber:
                {
                    int w = gmLoc ? gmLoc.CurrentWave : 1;
                    float span = Mathf.Max(1f, cfg.wavesMax - cfg.wavesMin);
                    tNorm = Mathf.Clamp01((w - cfg.wavesMin) / span); break;
                }
        }
        return Mathf.Clamp01(cfg.curve.Evaluate(tNorm));
    }

    private List<Transform> GetActiveBosses()
    {
        List<Transform> list = new List<Transform>();
        if (string.IsNullOrEmpty(bossTag)) return list;
        GameObject[] objs = GameObject.FindGameObjectsWithTag(bossTag);
        for (int i = 0; i < objs.Length; i++)
            if (objs[i] && objs[i].activeInHierarchy) list.Add(objs[i].transform);
        return list;
    }

    private int GetActiveEnemyCount()
    {
        int c = 0;
        for (int i = 0; i < enemyPool.Count; i++) if (enemyPool[i].activeInHierarchy) c++;
        return c;
    }

    private int GetAvailablePoolCount()
    {
        int c = 0;
        for (int i = 0; i < enemyPool.Count; i++) if (!enemyPool[i].activeInHierarchy) c++;
        return c;
    }

    private GameObject GetPooledEnemy()
    {
        for (int i = 0; i < enemyPool.Count; i++) if (!enemyPool[i].activeInHierarchy) return enemyPool[i];
        return null;
    }

    private void RecycleEnemies()
    {
        Transform anchor = (player != null) ? player : this.transform;
        for (int i = 0; i < enemyPool.Count; i++)
        {
            GameObject e = enemyPool[i];
            if (!e.activeInHierarchy) continue;
            float dist = Vector3.Distance(anchor.position, e.transform.position);
            if (dist > recycleDistance && IsOffscreen(e.transform.position))
            {
                EnemyWaveHooks hooks = e.GetComponent<EnemyWaveHooks>();
                if (hooks) hooks.NotifyDespawned();
                e.SetActive(false);
            }
        }
    }

    private bool TryFindValidSpawnNearAnchor(Vector3 center, float minDist, float maxDist, out Vector3 result)
    {
        result = Vector3.zero;

        // widen search progressively
        float baseMin = Mathf.Max(0.5f, minDist);
        float baseMax = Mathf.Max(baseMin + 0.5f, maxDist);

        for (int attempt = 0; attempt < maxAttemptsPerEnemy; attempt++)
        {
            float t = (attempt / (float)Mathf.Max(1, maxAttemptsPerEnemy - 1));
            float radiusMin = Mathf.Lerp(baseMin, Mathf.Max(1f, baseMin * 0.5f), t);
            float radiusMax = Mathf.Lerp(baseMax, baseMax * 1.5f, t);

            Vector2 ring = Random.insideUnitCircle.normalized * Random.Range(radiusMin, radiusMax);
            Vector3 guess = new Vector3(center.x + ring.x, center.y + groundProbeUp, center.z + ring.y);

            NavMeshHit navHit;
            if (!NavMesh.SamplePosition(guess, out navHit, navMeshCheckRadius, allowedAreaMask))
            {
                // also try directly at center with a larger radius
                if (!NavMesh.SamplePosition(center, out navHit, Mathf.Max(navMeshCheckRadius * 2f, radiusMax), allowedAreaMask))
                { rejNoNav++; continue; }
            }

            Vector3 top = new Vector3(navHit.position.x, Mathf.Max(navHit.position.y, center.y) + groundProbeUp, navHit.position.z);

            Vector3 candidate;
            RaycastHit gh;
            if (Physics.Raycast(top, Vector3.down, out gh, groundProbeDown + groundProbeUp, groundMask, QueryTriggerInteraction.Ignore))
                candidate = gh.point;
            else if (spherecastFallback &&
                     Physics.SphereCast(top, spherecastRadius, Vector3.down, out gh, groundProbeDown + groundProbeUp, groundMask, QueryTriggerInteraction.Ignore))
                candidate = gh.point;
            else { candidate = navHit.position; rejNoGround++; }

            // Defer detailed checks to PassesFilters() at call site
            result = candidate;
            return true;
        }
        return false;
    }

    private bool IsOffscreen(Vector3 worldPos)
    {
        if (!mainCam) mainCam = Camera.main;
        if (!mainCam) return true;

        Vector3 vp = mainCam.WorldToViewportPoint(worldPos);
        if (vp.z <= 0f) return true; // behind camera counts as offscreen

        bool inside = (vp.x > -offscreenBuffer && vp.x < 1f + offscreenBuffer &&
                       vp.y > -offscreenBuffer && vp.y < 1f + offscreenBuffer);
        return !inside;
    }

    private static float DistanceXZ(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Transform anchor = (player != null) ? player : this.transform;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(anchor.position, playerRing.x);
        Gizmos.color = new Color(1f, 1f, 0f, 0.35f);
        Gizmos.DrawWireSphere(anchor.position, playerRing.y);

        // Safety radius viz (around player only)
        if (player != null)
        {
            Gizmos.color = new Color(0f, 1f, 0f, 0.25f);
            Gizmos.DrawWireSphere(player.position, minDistanceFromPlayer);
        }

        GameObject[] bosses = GameObject.FindGameObjectsWithTag(bossTag);
        Gizmos.color = Color.red;
        for (int i = 0; i < bosses.Length; i++)
        {
            GameObject bObj = bosses[i]; if (!bObj) continue;
            Transform b = bObj.transform;
            Gizmos.DrawWireSphere(b.position, bossRing.x);
            Gizmos.color = new Color(1f, 0f, 0f, 0.35f);
            Gizmos.DrawWireSphere(b.position, bossRing.y);
            Gizmos.color = Color.red;
        }
    }
#endif

    // ---------------------- NEW HELPERS ----------------------

    private IEnumerator WaitForNavMeshThenStart()
    {
        float deadline = Time.realtimeSinceStartup + Mathf.Max(1f, navmeshReadyTimeout);

        while (!navReady)
        {
            if (Time.realtimeSinceStartup > deadline)
            {
                Debug.LogWarning("[EnemySpawner] NavMesh did not become ready before timeout. Starting anyway (spawns may be limited).");
                break;
            }
            yield return null;
        }

        // Only warn now if navmesh remains empty.
        var tri = NavMesh.CalculateTriangulation();
        if (tri.vertices == null || tri.vertices.Length == 0)
            Debug.LogWarning("[EnemySpawner] No NavMesh data found after wait. Spawns may fail until baking completes.");

        StartSpawnLoopIfNeeded();
    }

    private void StartSpawnLoopIfNeeded()
    {
        if (_spawnLoopStarted) return;
        _spawnLoopStarted = true;
        StartCoroutine(SpawnLoop());
    }
}
