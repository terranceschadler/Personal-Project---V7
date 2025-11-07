using UnityEngine;
using UnityEngine.AI;
using System;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(AudioSource))]
public class EnemyController : MonoBehaviour
{
    [Header("Stats")]
    public float maxHealth = 100f;
    public float chaseRange = 10f;
    public float damageAmount = 10f;      // per hit
    public float damageRate = 0.5f;       // hits per second
    public float wanderRadius = 15f;
    public float wanderInterval = 5f;

    [Header("Targeting")]
    [Tooltip("Layers considered valid targets when searching (optional). If empty, tags are used.")]
    public LayerMask targetLayers = ~0;
    [Tooltip("Stop a little short so the agent doesn't try to occupy the same space as the obstacle.")]
    public float stopBuffer = 0.8f;
    [Tooltip("How often (seconds) to refresh target search to avoid per-frame allocations.")]
    public float targetRefreshInterval = 0.35f;
    [Tooltip("Vertical ray start offset for line-of-sight checks (avoid floor hits).")]
    public float losRaycastHeight = 0.5f;

    [Header("Contact / Proximity Damage (fallback)")]
    [Tooltip("If true, enemies will also damage targets when within this radius, even if physics contacts don't fire.")]
    public bool useProximityAttacks = true;
    [Tooltip("Radius around the enemy for proximity hits.")]
    public float contactDamageRadius = 1.15f;

    [Header("Coin Drop Settings")]
    public GameObject coinPrefab;
    [Range(0f, 1f)] public float coinDropChance = 0.5f;
    public int minCoins = 1;
    public int maxCoins = 3;

    [Header("Health Drop Settings")]
    public GameObject healthLootPrefab;
    [Range(0f, 1f)] public float healthLootChance = 0.25f;
    public int minHealthDrops = 1;
    public int maxHealthDrops = 1;

    [Header("Scoring & Rewards")]
    [Tooltip("Score awarded when this enemy dies.")]
    public int killScore = 10;
    [Tooltip("Optional: coins awarded immediately on kill (separate from dropped coins).")]
    public int coinsOnKillAward = 0;
    [Tooltip("Optional: materials awarded immediately on kill.")]
    public int materialsOnKillAward = 0;

    // ---------- AUDIO ----------
    [Header("Audio")]
    [Tooltip("Played when damaging the Player.")]
    public AudioClip attackPlayerClip;
    [Tooltip("Played when damaging a FriendlyAI.")]
    public AudioClip attackFriendlyClip;
    [Tooltip("Played when damaging a Barricade.")]
    public AudioClip attackBarricadeClip;
    [Range(0f, 1f)] public float attackVolume = 0.7f;
    [Tooltip("Random pitch range for variety (x=min, y=max). Set both to 1 for no variance.")]
    public Vector2 attackPitchRange = new Vector2(0.97f, 1.03f);
    [Tooltip("If true, make audio 3D/spatial.")]
    public bool spatial3D = true;
    [Tooltip("Min time (sec) between SFX of the same hit type.")]
    public float minAttackSfxInterval = 0.12f;

    // ---------- UI / Health Bar ----------
    [Header("UI")]
    [Tooltip("Drag your existing healthbar script component here (same one used by Player/Friendly). Must expose Set(float,float) or Set01(float).")]
    [SerializeField] private MonoBehaviour healthBar; // e.g., HealthBarUI
    [Tooltip("RectTransform of the bar root (the object you want to resize). If empty, we'll try to get it from 'healthBar'.")]
    [SerializeField] private RectTransform healthBarRoot;

    [Tooltip("Boss-only size (Width, Height) in UI units.")]
    public Vector2 bossHealthBarSize = new Vector2(300f, 35f);
    [Tooltip("If true, anchors are set to center before applying sizeDelta (makes absolute sizing predictable).")]
    public bool bossBarForceAbsoluteSizing = true;
    [Tooltip("Optional scale multiplier applied to the bar AFTER sizing (handy for world-space canvas).")]
    [Range(0.25f, 4f)] public float bossBarScaleMultiplier = 1f;

    // ---------- Internals ----------
    protected float currentHealth;
    protected NavMeshAgent agent;
    private float nextDamageTime = 0f;
    private float lastWanderTime = 0f;
    private float lastTargetRefresh = -999f;

    protected Transform currentTarget;
    protected PlayerController playerController;
    protected FriendlyAI friendlyAI;

    // Cached lookups
    private GameObject cachedPlayerGO;
    private GameObject[] cachedFriendlies = Array.Empty<GameObject>();

    // LoS state
    private bool priorityTargetsInRange = false;  // any Player/Friendly within chaseRange
    private Transform blockingBarricade = null;   // barricade found by LoS ray (if any)

    private AudioSource audioSource;
    private EnemyWaveHooks waveHooks;             // bridge to GameManager/spawner

    public event Action<EnemyController> OnEnemyDeath;

    private enum HitKind { Player = 0, Friendly = 1, Barricade = 2 }
    private float[] lastSfxTimes = new float[3];   // per HitKind
    private int lastSfxVictimId = -1;              // instance ID of last victim that triggered SFX this frame
    private int lastSfxFrame = -1;                 // frame we last played for that victim

    // Exactly-once kill guard (covers pooling & multiple Die() entry)
    private bool _deadReported = false;

    void Reset()
    {
        var a = GetComponent<NavMeshAgent>();
        if (a != null)
        {
            a.stoppingDistance = Mathf.Max(a.stoppingDistance, 0.8f);
            a.autoBraking = true;
            a.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        }
    }

    // ----------------------------
    // SCALING-SAFE HEALTH SETTERS
    // ----------------------------
    public void ForceSetMaxHealth(float newMax, bool keepPercent = false)
    {
        newMax = Mathf.Max(1f, newMax);

        float pct = (maxHealth > 0f) ? Mathf.Clamp01(currentHealth / maxHealth) : 1f;

        maxHealth = newMax;
        currentHealth = keepPercent ? (newMax * pct) : newMax;

        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        OnHealthChanged();
    }

    /// <summary>Intentionally empty: scaling is handled by GameManager on first engage.</summary>
    public void OnBossHealthScaled(float scaledMax) { }

    protected virtual void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        audioSource = GetComponent<AudioSource>();

        // ensure hooks exist
        waveHooks = GetComponent<EnemyWaveHooks>();
        if (!waveHooks) waveHooks = gameObject.AddComponent<EnemyWaveHooks>();
        waveHooks.SetSpawner(null); // optional

        if (agent == null)
        {
            Debug.LogError("NavMeshAgent component missing.");
            enabled = false;
            return;
        }

        if (agent.stoppingDistance < stopBuffer)
            agent.stoppingDistance = stopBuffer;

        agent.autoBraking = true;

        // Audio config
        if (audioSource != null)
        {
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = spatial3D ? 1f : 0f;
        }

        if (currentHealth <= 0.001f || currentHealth > maxHealth)
            currentHealth = maxHealth;

        // Boss-only: resize health bar
        if (IsBoss())
            ApplyBossHealthBarSizing();

        OnHealthChanged(); // push initial to UI
    }

    protected virtual void OnEnable()
    {
        _deadReported = false;

        if (currentHealth <= 0.001f || currentHealth > maxHealth)
            currentHealth = maxHealth;

        nextDamageTime = 0f;
        lastWanderTime = 0f;
        lastTargetRefresh = -999f;
        currentTarget = null;
        playerController = null;
        friendlyAI = null;
        priorityTargetsInRange = false;
        blockingBarricade = null;

        for (int i = 0; i < lastSfxTimes.Length; i++) lastSfxTimes[i] = -999f;
        lastSfxVictimId = -1;
        lastSfxFrame = -1;

        if (!waveHooks) waveHooks = GetComponent<EnemyWaveHooks>();
    }

    protected virtual void Update()
    {
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh)
            return;

        if (Time.time - lastTargetRefresh >= targetRefreshInterval)
        {
            RefreshTargetingState();
            lastTargetRefresh = Time.time;
        }

        if (currentTarget != null)
        {
            float distanceToTarget = Vector3.Distance(transform.position, currentTarget.position);

            if (distanceToTarget <= chaseRange)
            {
                Vector3 seekPoint = GetReachableEdgePoint(currentTarget);
                if (agent.isOnNavMesh)
                    agent.SetDestination(seekPoint);
            }
            else
            {
                Wander();
            }

            if (agent.velocity.sqrMagnitude > 0.01f)
            {
                Quaternion lookRotation = Quaternion.LookRotation(agent.velocity.normalized);
                transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * 5f);
            }
        }
        else
        {
            Wander();
        }

        if (useProximityAttacks)
        {
            TryProximityDamage();
        }
    }

    // --- Boss UI helpers ---
    private bool IsBoss()
    {
        return CompareTag("Boss") || GetComponent<BossEnemy>() != null;
    }

    private void ApplyBossHealthBarSizing()
    {
        RectTransform rt = healthBarRoot;
        if (rt == null && healthBar != null)
        {
            var comp = healthBar as Component;
            if (comp != null) rt = comp.GetComponent<RectTransform>();
        }
        if (rt == null) return;

        if (bossBarForceAbsoluteSizing)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
        }

        rt.sizeDelta = bossHealthBarSize;

        if (Mathf.Abs(bossBarScaleMultiplier - 1f) > 0.001f)
        {
            rt.localScale = new Vector3(bossBarScaleMultiplier, bossBarScaleMultiplier, 1f);
        }
    }

    // --- Targeting ---
    private void RefreshTargetingState()
    {
        cachedPlayerGO = GameObject.FindGameObjectWithTag("Player");
        cachedFriendlies = GameObject.FindGameObjectsWithTag("FriendlyAI");

        Transform nearestPriority = null;
        float nearestPriorityDist = Mathf.Infinity;

        if (cachedPlayerGO != null)
        {
            float d = Vector3.Distance(transform.position, cachedPlayerGO.transform.position);
            if (d <= chaseRange && d < nearestPriorityDist)
            {
                nearestPriorityDist = d;
                nearestPriority = cachedPlayerGO.transform;
                playerController = cachedPlayerGO.GetComponent<PlayerController>();
                friendlyAI = null;
            }
        }

        for (int i = 0; i < cachedFriendlies.Length; i++)
        {
            var f = cachedFriendlies[i];
            if (f == null) continue;
            float d = Vector3.Distance(transform.position, f.transform.position);
            if (d <= chaseRange && d < nearestPriorityDist)
            {
                nearestPriorityDist = d;
                nearestPriority = f.transform;
                friendlyAI = f.GetComponent<FriendlyAI>();
                playerController = null;
            }
        }

        priorityTargetsInRange = (nearestPriority != null);
        blockingBarricade = null;

        if (priorityTargetsInRange)
        {
            Vector3 origin = transform.position + Vector3.up * losRaycastHeight;
            Vector3 dir = (nearestPriority.position - origin).normalized;
            float dist = Vector3.Distance(origin, nearestPriority.position);

            if (Physics.Raycast(origin, dir, out RaycastHit hit, dist))
            {
                if (hit.collider != null && hit.collider.CompareTag("Barricade"))
                {
                    blockingBarricade = hit.collider.transform;
                    currentTarget = blockingBarricade;
                    playerController = null;
                    friendlyAI = null;
                    return;
                }
            }

            currentTarget = nearestPriority;
            return;
        }

        GameObject[] barricades = GameObject.FindGameObjectsWithTag("Barricade");
        Transform nearestBarricade = null;
        float nearestBarricadeDist = Mathf.Infinity;

        for (int i = 0; i < barricades.Length; i++)
        {
            var b = barricades[i];
            if (b == null) continue;
            float d = Vector3.Distance(transform.position, b.transform.position);
            if (d < nearestBarricadeDist)
            {
                nearestBarricadeDist = d;
                nearestBarricade = b.transform;
            }
        }

        currentTarget = nearestBarricade;
        playerController = null;
        friendlyAI = null;
    }

    private Vector3 GetReachableEdgePoint(Transform target)
    {
        Vector3 targetPos = target.position;

        Collider col = target.GetComponentInChildren<Collider>();
        if (col != null)
        {
            Vector3 from = transform.position;
            Vector3 closest = col.ClosestPoint(from);

            Vector3 dir = (closest - from);
            float mag = dir.magnitude;
            if (mag > 0.001f)
            {
                dir /= mag;
                targetPos = closest - dir * Mathf.Max(stopBuffer, 0.1f);
            }
            else
            {
                targetPos = closest;
            }
        }

        if (NavMesh.SamplePosition(targetPos, out NavMeshHit hitA, 1.5f, NavMesh.AllAreas))
            return hitA.position;

        if (NavMesh.SamplePosition(target.position, out NavMeshHit hitB, 2.5f, NavMesh.AllAreas))
            return hitB.position;

        return transform.position;
    }

    private void Wander()
    {
        if (Time.time - lastWanderTime > wanderInterval || agent.remainingDistance < 0.5f)
        {
            Vector2 randomDirection = UnityEngine.Random.insideUnitCircle * wanderRadius;
            Vector3 candidate = new Vector3(
                transform.position.x + randomDirection.x,
                transform.position.y,
                transform.position.z + randomDirection.y
            );

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, wanderRadius, NavMesh.AllAreas))
            {
                if (agent.isActiveAndEnabled && agent.isOnNavMesh)
                    agent.SetDestination(hit.position);

                lastWanderTime = Time.time;
            }
        }
    }

    // --- Proximity fallback ---
    private void TryProximityDamage()
    {
        if (Time.time < nextDamageTime) return;

        GameObject p = cachedPlayerGO != null ? cachedPlayerGO : GameObject.FindGameObjectWithTag("Player");
        if (p != null)
        {
            float dp = Vector3.Distance(transform.position, p.transform.position);
            if (dp <= contactDamageRadius)
            {
                var pc = p.GetComponent<PlayerController>();
                if (pc != null)
                {
                    pc.TakeDamage(damageAmount);
                    int victimId = p.transform.root.gameObject.GetInstanceID();
                    MaybePlayAttackSound(HitKind.Player, victimId);
                    nextDamageTime = Time.time + 1f / damageRate;
                    return;
                }
            }
        }

        if (cachedFriendlies == null || cachedFriendlies.Length == 0)
            cachedFriendlies = GameObject.FindGameObjectsWithTag("FriendlyAI");

        for (int i = 0; i < cachedFriendlies.Length; i++)
        {
            var f = cachedFriendlies[i];
            if (f == null) continue;
            float df = Vector3.Distance(transform.position, f.transform.position);
            if (df <= contactDamageRadius)
            {
                var fa = f.GetComponent<FriendlyAI>();
                if (fa != null)
                {
                    fa.TakeDamage(damageAmount);
                    int victimId = f.transform.root.gameObject.GetInstanceID();
                    MaybePlayAttackSound(HitKind.Friendly, victimId);
                    nextDamageTime = Time.time + 1f / damageRate;
                    return;
                }
            }
        }

        if (currentTarget != null && currentTarget.CompareTag("Barricade"))
        {
            float db = Vector3.Distance(transform.position, currentTarget.position);
            if (db <= contactDamageRadius)
            {
                var barricade = currentTarget.GetComponentInParent<BarricadeHealth>() ?? currentTarget.GetComponent<BarricadeHealth>();
                if (barricade != null)
                {
                    int dmgInt = Mathf.Max(1, Mathf.RoundToInt(damageAmount));
                    barricade.TakeDamage(dmgInt);

                    Transform rootT = barricade.transform.root != null ? barricade.transform.root : currentTarget.root;
                    int victimId = rootT.gameObject.GetInstanceID();
                    MaybePlayAttackSound(HitKind.Barricade, victimId);
                    nextDamageTime = Time.time + 1f / damageRate;
                }
            }
        }
    }

    // --- Damage + Per-target / Per-type SFX Throttle ---
    private void TryDealDamage(GameObject otherGO)
    {
        if (Time.time < nextDamageTime) return;

        if (otherGO.CompareTag("Player"))
        {
            PlayerController player = otherGO.GetComponent<PlayerController>();
            if (player != null)
            {
                player.TakeDamage(damageAmount);

                int victimId = otherGO.transform.root.gameObject.GetInstanceID();
                MaybePlayAttackSound(HitKind.Player, victimId);

                nextDamageTime = Time.time + 1f / damageRate;
            }
            return;
        }

        if (otherGO.CompareTag("FriendlyAI"))
        {
            FriendlyAI ally = otherGO.GetComponent<FriendlyAI>();
            if (ally != null)
            {
                ally.TakeDamage(damageAmount);

                int victimId = otherGO.transform.root.gameObject.GetInstanceID();
                MaybePlayAttackSound(HitKind.Friendly, victimId);

                nextDamageTime = Time.time + 1f / damageRate;
            }
            return;
        }

        if (otherGO.CompareTag("Barricade"))
        {
            if (currentTarget == null || !currentTarget.CompareTag("Barricade"))
                return;

            var barricade = otherGO.GetComponentInParent<BarricadeHealth>() ?? otherGO.GetComponent<BarricadeHealth>();
            if (barricade != null)
            {
                int dmgInt = Mathf.Max(1, Mathf.RoundToInt(damageAmount));
                barricade.TakeDamage(dmgInt);

                Transform rootT = (barricade.transform.root != null ? barricade.transform.root : otherGO.transform.root);
                int victimId = rootT.gameObject.GetInstanceID();
                MaybePlayAttackSound(HitKind.Barricade, victimId);

                nextDamageTime = Time.time + 1f / damageRate;
            }
        }
    }

    private void MaybePlayAttackSound(HitKind kind, int victimInstanceId)
    {
        if (lastSfxFrame == Time.frameCount && lastSfxVictimId == victimInstanceId)
            return;

        int idx = (int)kind;

        if (Time.time - lastSfxTimes[idx] < minAttackSfxInterval)
            return;

        PlayAttackSound(kind);

        lastSfxTimes[idx] = Time.time;
        lastSfxVictimId = victimInstanceId;
        lastSfxFrame = Time.frameCount;
    }

    private void PlayAttackSound(HitKind kind)
    {
        if (audioSource == null) return;

        AudioClip clip = null;
        switch (kind)
        {
            case HitKind.Player: clip = attackPlayerClip; break;
            case HitKind.Friendly: clip = attackFriendlyClip; break;
            case HitKind.Barricade: clip = attackBarricadeClip; break;
        }
        if (clip == null) return;

        float originalPitch = audioSource.pitch;
        if (attackPitchRange.x != 1f || attackPitchRange.y != 1f)
        {
            audioSource.pitch = UnityEngine.Random.Range(attackPitchRange.x, attackPitchRange.y);
        }

        audioSource.PlayOneShot(clip, attackVolume);

        audioSource.pitch = originalPitch;
    }

    private void OnTriggerStay(Collider other) => TryDealDamage(other.gameObject);
    private void OnCollisionStay(Collision collision) => TryDealDamage(collision.collider.gameObject);

    // ------------------------------------------------------------------------------------------

    public virtual void TakeDamage(float damage)
    {
        currentHealth -= damage;
        if (currentHealth < 0f) currentHealth = 0f;

        bool isBoss = IsBoss();
        if (isBoss && GameManager.Instance != null)
        {
            // Ensure scaling is applied once the boss is first engaged
            GameManager.Instance.TryScaleBossOnFirstEngage(this); // keeps % HP
        }

        OnHealthChanged();

        if (currentHealth <= 0f) Die();
    }

    public virtual void Heal(float amount)
    {
        if (amount <= 0f) return;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        OnHealthChanged();
    }

    protected virtual void OnHealthChanged()
    {
        if (healthBar != null)
        {
            var t = healthBar.GetType();
            var mSet = t.GetMethod("Set", new Type[] { typeof(float), typeof(float) });
            if (mSet != null)
            {
                mSet.Invoke(healthBar, new object[] { currentHealth, maxHealth });
            }
            else
            {
                var m01 = t.GetMethod("Set01", new Type[] { typeof(float) });
                if (m01 != null)
                {
                    float frac = (maxHealth > 0f) ? currentHealth / maxHealth : 0f;
                    m01.Invoke(healthBar, new object[] { frac });
                }
            }
        }
    }

    public float GetHealth() => currentHealth;
    public float GetMaxHealth() => maxHealth;

    protected virtual void Die()
    {
        if (_deadReported) return; // exactly-once guard
        _deadReported = true;

        var gm = GameManager.Instance;
        bool isBoss = IsBoss();

        // 1) Award immediate rewards first (score/coins/materials)
        if (gm != null)
        {
            if (killScore > 0) gm.AddScore(killScore);
            if (coinsOnKillAward > 0) gm.AddCoins(coinsOnKillAward);
            if (materialsOnKillAward > 0) gm.AddMaterials(materialsOnKillAward);
        }

        // 2) REPORT THE DEATH TO GAME MANAGER EARLY (so stats always move)
        bool notified = false;
        try
        {
            if (waveHooks != null)
                notified = waveHooks.NotifyKilled(isBoss); // should internally call GM.RegisterEnemyDeath(...)
        }
        catch (Exception ex)
        {
            Debug.LogError($"[EnemyController] waveHooks.NotifyKilled threw: {ex}");
            notified = false; // fall back to direct GM call
        }

        if (!notified && gm != null)
        {
            try
            {
                gm.RegisterEnemyDeath(transform.position, isBoss);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EnemyController] GameManager.RegisterEnemyDeath threw: {ex}");
            }
        }

        // 3) Local loot drops (okay if these fail—they no longer block stats)
        try { DropCoins(); } catch (Exception ex) { Debug.LogError($"[EnemyController] DropCoins threw: {ex}"); }
        try { DropHealth(); } catch (Exception ex) { Debug.LogError($"[EnemyController] DropHealth threw: {ex}"); }

        // 3.25) NEW: Upgrade drops (integrated upgrade system)
        try
        {
            EnemyUpgradeDropper upgradeDropper = GetComponent<EnemyUpgradeDropper>();
            if (upgradeDropper != null)
            {
                upgradeDropper.OnEnemyDeath();
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[EnemyController] EnemyUpgradeDropper.OnEnemyDeath threw: {ex}");
        }

        // 3.5) Mini Boss special loot (if this is a mini boss)
        try
        {
            var miniBoss = GetComponent<MiniBoss>();
            if (miniBoss != null)
            {
                miniBoss.NotifyKilled();
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[EnemyController] MiniBoss.NotifyKilled threw: {ex}");
        }

        // 4) Notify external listeners LAST and SAFELY
        try
        {
            OnEnemyDeath?.Invoke(this);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[EnemyController] OnEnemyDeath listener threw: {ex}");
        }

        // 5) Pool-friendly deactivate
        gameObject.SetActive(false);
    }


    // ---------- Loot Drops ----------
    private void DropCoins()
    {
        if (coinPrefab == null) return;
        if (UnityEngine.Random.value > coinDropChance) return;

        int coinCount = UnityEngine.Random.Range(minCoins, maxCoins + 1);
        for (int i = 0; i < coinCount; i++)
        {
            Vector3 dropPos = transform.position + new Vector3(
                UnityEngine.Random.Range(-1f, 1f),
                0f,
                UnityEngine.Random.Range(-1f, 1f)
            );

            if (NavMesh.SamplePosition(dropPos, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
                dropPos = navHit.position;

            GameObject coin = Instantiate(coinPrefab, dropPos, Quaternion.identity);

            // Nudge to ground if needed
            if (Physics.Raycast(coin.transform.position + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 5f))
                coin.transform.position = hit.point + Vector3.up * 0.02f;

            Rigidbody rb = coin.GetComponent<Rigidbody>();
            if (rb != null)
                rb.AddForce(Vector3.up * 1f, ForceMode.Impulse);
        }
    }

    private void DropHealth()
    {
        if (healthLootPrefab == null) return;
        if (UnityEngine.Random.value > healthLootChance) return;

        int packCount = UnityEngine.Random.Range(minHealthDrops, maxHealthDrops + 1);
        for (int i = 0; i < packCount; i++)
        {
            Vector3 dropPos = transform.position + new Vector3(
                UnityEngine.Random.Range(-1f, 1f),
                0f,
                UnityEngine.Random.Range(-1f, 1f)
            );

            if (NavMesh.SamplePosition(dropPos, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
                dropPos = navHit.position;

            GameObject pack = Instantiate(healthLootPrefab, dropPos, Quaternion.identity);

            // Nudge to ground if needed
            if (Physics.Raycast(pack.transform.position + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 5f))
                pack.transform.position = hit.point + Vector3.up * 0.02f;

            Rigidbody rb = pack.GetComponent<Rigidbody>();
            if (rb != null)
                rb.AddForce(Vector3.up * 1f, ForceMode.Impulse);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, contactDamageRadius);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, chaseRange);
    }
#endif
}
