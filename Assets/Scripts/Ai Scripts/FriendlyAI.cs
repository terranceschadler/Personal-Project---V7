using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using UnityEngine.Serialization;

public class FriendlyAI : MonoBehaviour
{
    // ---------- GLOBAL COORDINATION ----------
    private static readonly HashSet<FriendlyAI> AllFriendlies = new HashSet<FriendlyAI>();
    private static readonly Dictionary<Transform, FriendlyAI> TargetClaims = new Dictionary<Transform, FriendlyAI>();

    private static bool EnemyClaimedByOther(Transform enemyRoot, FriendlyAI requester)
    {
        if (enemyRoot == null) return false;
        if (!TargetClaims.TryGetValue(enemyRoot, out var owner)) return false;
        return owner != null && owner != requester;
    }

    private void ClaimEnemy(Transform enemyRoot)
    {
        if (enemyRoot == null) return;
        TargetClaims[enemyRoot] = this;
        currentEnemy = enemyRoot;
    }

    private void ReleaseEnemy(Transform enemyRoot)
    {
        if (enemyRoot == null) return;
        if (TargetClaims.TryGetValue(enemyRoot, out var owner) && owner == this)
            TargetClaims.Remove(enemyRoot);
        if (currentEnemy == enemyRoot) currentEnemy = null;
    }

    // ---------- USER SETTINGS ----------
    [Header("References")]
    public Transform player;
    public GameObject bulletPrefab;
    public Transform firePoint;

    [Tooltip("Prefab root can be an empty with a world-space Canvas + Background + Fill (Image). 'Fill' child is required.")]
    public GameObject healthBarPrefab;

    [Header("Combat Settings")]
    public float followDistance = 10f;
    public float shootRange = 15f;
    [Tooltip("Shots per second.")]
    public float fireRate = 1f;
    public float bulletSpeed = 20f;

    [Header("Patrol Around Player")]
    [Tooltip("Average orbit radius around the player.")]
    public float patrolRadius = 7.5f;
    [Tooltip("Randomness added to each patrol point.")]
    public float patrolJitter = 2.0f;
    [Tooltip("How close is considered 'arrived' at a patrol point.")]
    public float patrolArrivalThreshold = 0.8f;
    [Tooltip("If we get stuck or time out, choose a new patrol point.")]
    public float patrolRetargetTime = 4f;

    [Header("Avoid Enemies (General)")]
    [Tooltip("Start steering away from enemies detected within this radius.")]
    public float avoidEnemyRadius = 6f;
    [Tooltip("Desired separation to push away from enemies.")]
    public float personalSpace = 4f;
    [Range(0f, 3f)] public float avoidWeight = 1.0f;
    [Tooltip("Cap the max distance we will move from the player while avoiding.")]
    public float leashMaxDistance = 14f;

    [Header("Boss Handling")]
    [Tooltip("Prefer to shoot bosses when visible/in range.")]
    public bool prioritizeBosses = true;
    [Tooltip("Score bonus when target selection considers bosses.")]
    public float bossPriorityBonus = 20f;
    [Tooltip("Larger avoidance radius against bosses.")]
    public float bossAvoidRadius = 9f;
    [Tooltip("Extra personal space vs. bosses.")]
    public float bossPersonalSpace = 6f;
    [Range(0f, 3f)] public float bossAvoidWeight = 1.5f;
    [Tooltip("How strongly we kite away from a boss if too close while shooting.")]
    public float bossKitePush = 2.0f;

    [Header("Targeting")]
    [Tooltip("Layers considered enemies (their root or collider should be on this mask).")]
    public LayerMask enemyMask = ~0;
    [Tooltip("Layers that can block line of sight (e.g., Default, Environment). Enemy layer is okay here too.")]
    public LayerMask losMask = ~0;
    [Tooltip("How many colliders to scan per frame (cap to reduce GC/CPU).")]
    public int scanBuffer = 32;

    [Header("Health Settings")]
    public float maxHealth = 100f;
    [Tooltip("Hide the health bar when at full health.")]
    public bool hideBarAtFull = true;

    [FormerlySerializedAs("barHeight")]
    [Tooltip("Back-compat height for the bar. If you set a non-zero healthBarOffset, that takes precedence.")]
    public float legacyBarHeight = 2f;

    [Tooltip("World-space offset for the health bar from the Friendly's pivot. If zero, legacyBarHeight is used.")]
    public Vector3 healthBarOffset = Vector3.zero;

    [Tooltip("Optional scale to apply to the spawned health bar root.")]
    public Vector3 healthBarScale = Vector3.one;

    [Tooltip("Optional: smooth the follow position for the health bar (seconds to reach ~63%). 0 = snap.")]
    [Min(0f)] public float healthBarFollowSmoothing = 0f;

    [Tooltip("Optional parent to keep health bars organized (e.g. a WorldSpace canvas root).")]
    public Transform healthBarParent;

    [Header("Movement")]
    [Tooltip("Repath interval while chasing a target or following patrol.")]
    public float repathInterval = 0.2f;
    [Tooltip("When sampling patrol points on the NavMesh.")]
    public float navmeshSampleRadius = 2f;

    // ---------- Audio / VFX ----------
    [Header("Audio / VFX")]
    public AudioClip shootClip;
    [Range(0f, 1f)] public float shootVolume = 0.8f;
    public AudioSource audioSource;
    public ParticleSystem muzzleFlash;
    public GameObject muzzleFlashPrefab;
    public float muzzleFlashLifetime = 0.25f;

    [Header("Muzzle Flash Light")]
    public Light muzzleFlashLight;
    public float flashDuration = 0.05f;
    private float flashTimer = 0f;

    // ---------- Pickup Collection ----------
    [Header("Pickups")]
    public bool enablePickupCollection = true;
    public LayerMask pickupMask = ~0;
    public float pickupScanRadius = 10f;
    public float pickupReach = 1.8f;
    public float pickupScanInterval = 0.35f;
    [Range(0f, 1f)] public float healthPickupThreshold = 0.9f;
    public bool collectCoins = true;
    public bool collectHealth = true;
    public bool collectOther = true;

    [Header("Debug")]
    public bool debugPickups = true;
    public Color debugPickupLine = Color.green;

    [Header("Individualization & Teamwork")]
    [Tooltip("Prefer enemies not already targeted by another Friendly.")]
    public bool coordinateWithFriendlies = true;
    [Tooltip("Slight per-AI randomization to fire rate (+/- this fraction). 0 disables.")]
    [Range(0f, 0.5f)] public float perAIFireRateVariance = 0.15f;
    [Tooltip("How strongly friendlies steer away from each other.")]
    [Range(0f, 2f)] public float friendlySeparationWeight = 0.35f;
    [Tooltip("Radius to consider other friendlies for separation.")]
    public float friendlySeparationRadius = 2.0f;

    // ----------------------------------

    private float currentHealth;
    private NavMeshAgent agent;
    private float nextFireTime = 0f;
    private float nextRepathTime = 0f;

    // health bar instance
    private Image healthFill;
    private Transform healthBarTransform;
    private Canvas barCanvas;

    private Camera mainCam;
    private Collider[] scanResults;

    // Patrol state
    private Vector3 currentPatrolPoint;
    private float patrolSetTime;

    // Pickup state
    private float nextPickupScanTime = 0f;
    private Transform pickupTarget;
    private Vector3 pickupTargetPos;
    private float lastPickupSeenTime = -999f;

    // Targeting state (ALWAYS store the enemy ROOT here)
    private Transform currentEnemy;

    // Per-AI randomization
    private float individualFireRate;
    private float patrolRadiusBias;

    // ---------- Helpers: enemy/boss recognition ----------
    bool IsEnemyTag(Transform t) => t.CompareTag("Enemy") || t.CompareTag("Boss");
    bool IsBoss(Transform t)
    {
        if (t == null) return false;
        if (t.CompareTag("Boss")) return true;
        return t.GetComponentInParent<BossEnemy>() != null;
    }
    Transform GetEnemyRoot(Transform t)
    {
        if (t == null) return null;
        var ec = t.GetComponentInParent<EnemyController>();
        if (ec != null) return ec.transform;
        var be = t.GetComponentInParent<BossEnemy>();
        if (be != null) return be.transform;
        return t.root;
    }
    bool RaycastHitsTarget(Transform targetRoot, Vector3 origin, Vector3 dir, float maxDist)
    {
        if (Physics.Raycast(origin, dir, out RaycastHit hit, maxDist, losMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform == targetRoot || hit.transform.IsChildOf(targetRoot))
                return true;
        }
        return false;
    }

    void Awake()
    {
        AllFriendlies.Add(this);

        mainCam = Camera.main;

        if (scanBuffer < 8) scanBuffer = 8;
        scanResults = new Collider[scanBuffer];

        agent = GetComponent<NavMeshAgent>();
        currentHealth = maxHealth;

        var rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.interpolation = RigidbodyInterpolation.None;
        rb.constraints = RigidbodyConstraints.FreezeRotation;

        agent.stoppingDistance = 0.05f;
        agent.autoBraking = true;

        if (player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null) player = playerObj.transform;
            else Debug.LogError("Player not found! Make sure the Player is tagged 'Player'.", this);
        }

        // ---- Health bar spawn (offset + optional parent + scale) ----
        if (healthBarPrefab != null)
        {
            GameObject hb = Instantiate(healthBarPrefab, transform.position + GetBarOffset(), Quaternion.identity);
            if (healthBarParent != null) hb.transform.SetParent(healthBarParent, true);

            healthBarTransform = hb.transform;

            if (healthBarScale != Vector3.one)
                healthBarTransform.localScale = healthBarScale;

            barCanvas = hb.GetComponentInChildren<Canvas>();
            if (barCanvas == null)
            {
                barCanvas = hb.AddComponent<Canvas>();
                barCanvas.renderMode = RenderMode.WorldSpace;
                hb.AddComponent<CanvasScaler>();
            }
            else
            {
                barCanvas.renderMode = RenderMode.WorldSpace;
            }

            // DEEP "Fill" lookup (grandchildren OK, includes inactive)
            healthFill = FindHealthFillDeep(hb.transform);
            if (healthFill == null)
            {
                Debug.LogWarning("HealthBar Prefab has no 'Fill' Image under any child. Health bar will not update.", hb);
            }
            else
            {
                EnsureImageIsFilledHorizontal(healthFill);
            }

            // Billboard
            if (hb.GetComponent<BillboardToCamera>() == null)
            {
                var bb = hb.AddComponent<BillboardToCamera>();
                bb.GetOrSetCamera(mainCam);
            }

            UpdateHealthBar();
        }

        if (muzzleFlashLight != null) muzzleFlashLight.enabled = false;

        float var = (perAIFireRateVariance <= 0f) ? 0f : Random.Range(-perAIFireRateVariance, perAIFireRateVariance);
        individualFireRate = Mathf.Max(0.05f, fireRate * (1f + var));
        nextFireTime = Time.time + Random.Range(0f, 0.6f);

        patrolRadiusBias = Random.Range(-0.75f, 0.75f);

        if (player != null)
        {
            currentPatrolPoint = GetPatrolPointAroundPlayer();
            patrolSetTime = Time.time;
        }
    }

    void OnDisable()
    {
        ReleaseEnemy(currentEnemy);
        AllFriendlies.Remove(this);
    }

    void OnDestroy()
    {
        ReleaseEnemy(currentEnemy);
        AllFriendlies.Remove(this);

        if (healthBarTransform != null) Destroy(healthBarTransform.gameObject);
    }

    void Update()
    {
        if (player == null) return;
        if (agent == null || !agent.isOnNavMesh) return;

        // ---- Health bar follow + billboard (with optional smoothing) ----
        if (healthBarTransform != null)
        {
            Vector3 targetPos = transform.position + GetBarOffset();
            if (healthBarFollowSmoothing > 0f)
            {
                float t = 1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(0.0001f, healthBarFollowSmoothing));
                healthBarTransform.position = Vector3.Lerp(healthBarTransform.position, targetPos, t);
            }
            else
            {
                healthBarTransform.position = targetPos;
            }

            if (mainCam == null) mainCam = Camera.main;
        }

        if (muzzleFlashLight != null && muzzleFlashLight.enabled)
        {
            flashTimer -= Time.deltaTime;
            if (flashTimer <= 0f) muzzleFlashLight.enabled = false;
        }

        // --- PICKUP SCAN / SELECTION ---
        if (enablePickupCollection && Time.time >= nextPickupScanTime)
        {
            nextPickupScanTime = Time.time + pickupScanInterval;
            pickupTarget = FindBestPickup(out pickupTargetPos);
            if (pickupTarget != null) lastPickupSeenTime = Time.time;
        }

        // --- ENEMY SCAN ---
        int scanned = Physics.OverlapSphereNonAlloc(
            transform.position,
            Mathf.Max(shootRange, Mathf.Max(avoidEnemyRadius, bossAvoidRadius)),
            scanResults,
            enemyMask,
            QueryTriggerInteraction.Ignore
        );

        // Keep/choose target (stored as ROOT)
        Transform chosenEnemy = ValidateOrPickEnemy(scanned);

        // --- Desired destination (pickups first) ---
        Vector3 desired = transform.position;
        bool havePickup = enablePickupCollection && pickupTarget != null;

        if (havePickup)
        {
            desired = pickupTargetPos;

            Vector3 toDesired = desired - player.position;
            float distDesired = toDesired.magnitude;
            if (distDesired > leashMaxDistance)
                desired = player.position + toDesired.normalized * leashMaxDistance;
        }
        else
        {
            bool arrived = (transform.position - currentPatrolPoint).sqrMagnitude <= (patrolArrivalThreshold * patrolArrivalThreshold);
            bool timedOut = (Time.time - patrolSetTime) >= patrolRetargetTime;
            if (arrived || timedOut)
            {
                currentPatrolPoint = GetPatrolPointAroundPlayer();
                patrolSetTime = Time.time;
            }
            desired = currentPatrolPoint;
        }

        // Keep within leash
        Vector3 offsetFromPlayer = transform.position - player.position;
        float distFromPlayer = offsetFromPlayer.magnitude;
        if (distFromPlayer > leashMaxDistance * 1.05f)
        {
            desired = player.position + offsetFromPlayer.normalized * Mathf.Min(leashMaxDistance * 0.9f, patrolRadius);
        }

        // --- Enemy avoidance (general + boss-specific) ---
        if (scanned > 0)
        {
            Vector3 avoidance = ComputeAvoidanceVector(scanned);
            if (avoidance.sqrMagnitude > 0.0001f)
            {
                desired += avoidance * avoidWeight;
                Vector3 toDesired2 = desired - player.position;
                if (toDesired2.magnitude > leashMaxDistance)
                    desired = player.position + toDesired2.normalized * leashMaxDistance;
            }

            // Extra kiting if our current target is a boss
            if (chosenEnemy != null && IsBoss(chosenEnemy))
            {
                Vector3 away = (transform.position - chosenEnemy.position);
                away.y = 0f;
                float d = away.magnitude;
                float want = Mathf.Max(bossPersonalSpace, personalSpace);
                if (d < want)
                {
                    Vector3 push = away.normalized * (1f - (d / Mathf.Max(0.01f, want)));
                    desired += push * bossKitePush;

                    // Keep kiting within leash
                    Vector3 toDesired3 = desired - player.position;
                    if (toDesired3.magnitude > leashMaxDistance)
                        desired = player.position + toDesired3.normalized * leashMaxDistance;
                }
            }
        }

        // --- Friendly separation ---
        if (friendlySeparationWeight > 0f)
        {
            Vector3 sep = ComputeFriendlySeparation();
            if (sep.sqrMagnitude > 0.0001f)
                desired += sep * friendlySeparationWeight;
        }

        // --- Repath ---
        float interval = havePickup ? Mathf.Min(0.08f, repathInterval * 0.5f) : repathInterval;
        if (Time.time >= nextRepathTime)
        {
            Vector3 navTarget = ProjectToNav(desired, navmeshSampleRadius, transform.position);
            agent.SetDestination(navTarget);
            nextRepathTime = Time.time + interval;
        }

        // --- Face enemy & shoot if LOS ---
        if (chosenEnemy != null && firePoint != null)
        {
            Vector3 dirFlat = chosenEnemy.position - transform.position; dirFlat.y = 0f;
            if (dirFlat.sqrMagnitude > 0.0001f)
            {
                Quaternion lookRotation = Quaternion.LookRotation(dirFlat.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * 5f);
            }

            Vector3 shotDir = (chosenEnemy.position - firePoint.position).normalized;
            float maxDist = shootRange;

            if (RaycastHitsTarget(chosenEnemy, firePoint.position, shotDir, maxDist))
            {
                if (Time.time >= nextFireTime)
                {
                    ShootAtTarget(chosenEnemy);
                    nextFireTime = Time.time + 1f / individualFireRate;
                }
            }
        }

        if (debugPickups && pickupTarget != null)
            Debug.DrawLine(transform.position + Vector3.up * 0.2f, pickupTarget.position + Vector3.up * 0.2f, debugPickupLine);
    }

    // Helper to compute the final bar offset (new vector offset wins; otherwise legacy height).
    Vector3 GetBarOffset()
    {
        if (healthBarOffset != Vector3.zero) return healthBarOffset;
        return new Vector3(0f, legacyBarHeight, 0f);
    }

    // ---------- Patrol helpers ----------
    Vector3 GetPatrolPointAroundPlayer()
    {
        if (player == null) return transform.position;

        float angle = Random.value * Mathf.PI * 2f;
        float radius = Mathf.Max(0.5f, (patrolRadius + patrolRadiusBias) + Random.Range(-patrolJitter, patrolJitter));
        Vector3 raw = player.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;

        int count = Physics.OverlapSphereNonAlloc(raw, personalSpace, scanResults, enemyMask, QueryTriggerInteraction.Ignore);
        if (count > 0)
        {
            Vector3 push = Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                Collider c = scanResults[i];
                if (c == null || !IsEnemyTag(c.transform)) continue;
                Vector3 away = (raw - c.transform.position);
                float d = away.magnitude + 0.001f;
                push += away / d;
            }
            if (push.sqrMagnitude > 0.0001f)
                raw += push.normalized * personalSpace;
        }

        Vector3 toRaw = raw - player.position;
        if (toRaw.magnitude > leashMaxDistance)
            raw = player.position + toRaw.normalized * leashMaxDistance;

        return ProjectToNav(raw, navmeshSampleRadius, player.position);
    }

    Vector3 ComputeAvoidanceVector(int enemyCount)
    {
        Vector3 steer = Vector3.zero;
        int contributors = 0;

        float rEnemySq = avoidEnemyRadius * avoidEnemyRadius;
        float rBossSq = bossAvoidRadius * bossAvoidRadius;

        for (int i = 0; i < enemyCount; i++)
        {
            Collider c = scanResults[i];
            if (c == null || !IsEnemyTag(c.transform)) continue;

            bool boss = IsBoss(c.transform);
            float useRadiusSq = boss ? rBossSq : rEnemySq;
            float usePersonal = boss ? bossPersonalSpace : personalSpace;
            float useWeight = boss ? bossAvoidWeight : 1f;

            Vector3 toMe = transform.position - c.transform.position;
            float sq = toMe.sqrMagnitude;
            if (sq <= useRadiusSq && sq > 0.0001f)
            {
                float d = Mathf.Sqrt(sq);
                float push = Mathf.Clamp01((usePersonal - d) / Mathf.Max(0.01f, usePersonal));
                if (push > 0f)
                {
                    steer += toMe.normalized * (push * useWeight);
                    contributors++;
                }
            }
        }

        if (contributors > 0)
        {
            steer /= contributors;
            steer.y = 0f;
        }

        return steer;
    }

    Vector3 ComputeFriendlySeparation()
    {
        Vector3 steer = Vector3.zero;
        int contributors = 0;

        foreach (var f in AllFriendlies)
        {
            if (f == null || f == this) continue;
            float d = Vector3.Distance(transform.position, f.transform.position);
            if (d < friendlySeparationRadius && d > 0.001f)
            {
                steer += (transform.position - f.transform.position).normalized * (1f - (d / friendlySeparationRadius));
                contributors++;
            }
        }

        if (contributors > 0)
        {
            steer /= contributors;
            steer.y = 0f;
        }

        return steer;
    }

    Vector3 ProjectToNav(Vector3 target, float sampleRadius, Vector3 fallback)
    {
        if (NavMesh.SamplePosition(target, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
            return hit.position;

        for (int i = 0; i < 6; i++)
        {
            float a = (i / 6f) * Mathf.PI * 2f;
            Vector3 p = target + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (sampleRadius * 0.75f);
            if (NavMesh.SamplePosition(p, out hit, sampleRadius, NavMesh.AllAreas))
                return hit.position;
        }

        if (NavMesh.SamplePosition(fallback, out hit, sampleRadius, NavMesh.AllAreas))
            return hit.position;

        return transform.position;
    }

    // ---------- Targeting ----------
    Transform ValidateOrPickEnemy(int count)
    {
        // Keep current if still valid and LOS okay
        if (currentEnemy != null)
        {
            if (!currentEnemy.gameObject.activeInHierarchy)
            {
                ReleaseEnemy(currentEnemy);
            }
            else
            {
                float d = Vector3.Distance(transform.position, currentEnemy.position);
                if (d > shootRange * 1.25f)
                {
                    ReleaseEnemy(currentEnemy);
                }
                else
                {
                    Vector3 dir = (currentEnemy.position - firePoint.position).normalized;
                    if (RaycastHitsTarget(currentEnemy, firePoint.position, dir, shootRange * 1.4f))
                        return currentEnemy; // still good
                    ReleaseEnemy(currentEnemy);
                }
            }
        }

        // Pick best target; prefer bosses if enabled
        float bestScore = float.NegativeInfinity;
        Transform bestRoot = null;

        for (int i = 0; i < count; i++)
        {
            Collider c = scanResults[i];
            if (c == null || !IsEnemyTag(c.transform)) continue;

            Transform enemyRoot = GetEnemyRoot(c.transform);

            float d = Vector3.Distance(transform.position, enemyRoot.position);
            if (d > shootRange * 1.3f) continue;

            float score = 100f / (1f + d);

            // LOS bonus
            Vector3 dir = (enemyRoot.position - firePoint.position).normalized;
            bool hasLOS = RaycastHitsTarget(enemyRoot, firePoint.position, dir, shootRange * 1.4f);
            if (hasLOS) score += 12f; else score -= 8f;

            // Boss priority
            if (prioritizeBosses && IsBoss(enemyRoot)) score += bossPriorityBonus;

            // Coordination penalty
            if (coordinateWithFriendlies && EnemyClaimedByOther(enemyRoot, this))
                score -= 10f;

            if (score > bestScore)
            {
                bestScore = score;
                bestRoot = enemyRoot;
            }
        }

        if (bestRoot != null)
            ClaimEnemy(bestRoot);

        return bestRoot;
    }

    // ---------- Shooting ----------
    void ShootAtTarget(Transform targetRoot)
    {
        if (bulletPrefab == null || firePoint == null) return;

        if (targetRoot != currentEnemy) ClaimEnemy(targetRoot);

        TriggerShotEffects();

        Vector3 dir = (targetRoot.position - firePoint.position).normalized;
        Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
        GameObject bullet = Instantiate(bulletPrefab, firePoint.position, rot);

        // FIXED: Ensure the bullet's Rigidbody is NOT kinematic before setting velocity
        if (bullet.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.isKinematic = false;  // Make sure it's non-kinematic so we can use velocity
            rb.linearVelocity = rot * Vector3.forward * bulletSpeed;
        }

        if (bullet.TryGetComponent<Bullet>(out var bulletScript))
        {
            bulletScript.Initialize(gameObject);
            bulletScript.speed = bulletSpeed;
        }
        else
        {
            Debug.LogWarning("[FriendlyAI] Bullet prefab missing Bullet component. Boss may block damage.", bullet);
        }
    }

    // ---------- SFX / VFX helper ----------
    void TriggerShotEffects()
    {
        if (shootClip != null)
        {
            if (audioSource != null) audioSource.PlayOneShot(shootClip, shootVolume);
            else AudioSource.PlayClipAtPoint(shootClip, firePoint != null ? firePoint.position : transform.position, shootVolume);
        }

        if (muzzleFlash != null && firePoint != null)
        {
            muzzleFlash.transform.position = firePoint.position;
            muzzleFlash.transform.rotation = firePoint.rotation;
            muzzleFlash.Play(true);
        }
        else if (muzzleFlashPrefab != null && firePoint != null)
        {
            var fx = Instantiate(muzzleFlashPrefab, firePoint.position, firePoint.rotation, firePoint);
            Destroy(fx, muzzleFlashLifetime);
        }

        if (muzzleFlashLight != null)
        {
            muzzleFlashLight.enabled = true;
            flashTimer = flashDuration;
        }
    }

    // ---------- HEALTH ----------
    public void Heal(float amount)
    {
        if (amount <= 0f) return;
        currentHealth = Mathf.Min(currentHealth + amount, maxHealth);
        UpdateHealthBar();
    }

    public void TakeDamage(float damage)
    {
        if (damage <= 0f) return;
        currentHealth -= damage;
        UpdateHealthBar();

        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    void UpdateHealthBar()
    {
        if (healthFill != null)
        {
            float t = Mathf.Clamp01(currentHealth / maxHealth);
            healthFill.fillAmount = t;

            if (hideBarAtFull && barCanvas != null)
                barCanvas.enabled = t < 0.999f;
        }
    }

    public void SetPlayer(Transform newPlayer)
    {
        player = newPlayer;
        currentPatrolPoint = GetPatrolPointAroundPlayer();
        patrolSetTime = Time.time;
    }

    void Die()
    {
        Debug.Log($"{gameObject.name} has died!");
        ReleaseEnemy(currentEnemy);
        if (healthBarTransform != null) Destroy(healthBarTransform.gameObject);
        Destroy(gameObject);
    }

    // ---------- PICKUP LOGIC ----------
    Transform FindBestPickup(out Vector3 navPos)
    {
        navPos = Vector3.zero;
        if (!enablePickupCollection) return null;

        int count = Physics.OverlapSphereNonAlloc(transform.position, pickupScanRadius, scanResults, pickupMask, QueryTriggerInteraction.Collide);
        if (count == 0) return null;

        float healthFrac = Mathf.Clamp01(currentHealth / Mathf.Max(0.0001f, maxHealth));
        bool wantHealth = collectHealth && healthFrac < healthPickupThreshold;

        Transform best = null;
        float bestScore = float.NegativeInfinity;
        Vector3 bestWorld = Vector3.zero;

        for (int i = 0; i < count; i++)
        {
            var c = scanResults[i];
            if (c == null || !c.gameObject.activeInHierarchy) continue;

            bool isCoin = collectCoins && c.GetComponent<CoinPickup>() != null;
            bool isHealth = collectHealth && c.GetComponent<HealthPickup>() != null;
            bool isOther = collectOther && !isCoin && !isHealth;

            if (!(isCoin || isHealth || isOther)) continue;

            Vector3 wpos = c.transform.position;
            float d = Vector3.Distance(transform.position, wpos);
            if (d > pickupScanRadius * 1.25f) continue;

            float score = 10f / (1f + d);
            if (isHealth && wantHealth) score += 8f;

            float dToPlayer = Vector3.Distance(wpos, player.position);
            if (dToPlayer > leashMaxDistance) score -= 6f;

            int nearbyEnemies = Physics.OverlapSphereNonAlloc(wpos, 3.0f, scanResults, enemyMask, QueryTriggerInteraction.Ignore);
            if (nearbyEnemies > 0) score -= 2.5f;

            if (score > bestScore)
            {
                bestScore = score;
                best = c.transform;
                bestWorld = wpos;
            }
        }

        if (best != null)
            navPos = ProjectToNav(bestWorld, navmeshSampleRadius, transform.position);

        return best;
    }

    void LateUpdate()
    {
        if (pickupTarget == null)
        {
            if (Time.time - lastPickupSeenTime > 1.5f)
                pickupTarget = null;
            return;
        }

        if (Vector3.Distance(transform.position, pickupTarget.position) <= pickupReach + 0.2f)
        {
            TryCollectPickupNow(pickupTarget);
            pickupTarget = null;
        }
    }

    void TryCollectPickupNow(Transform pickup)
    {
        if (pickup == null) return;

        if (pickup.TryGetComponent<CoinPickup>(out var coin))
        {
            GameManager.Instance?.AddCoins(coin.coinValue);
            Destroy(coin.gameObject);
            return;
        }

        if (pickup.TryGetComponent<HealthPickup>(out var hp))
        {
            hp.SendMessage("OnCollected", this, SendMessageOptions.DontRequireReceiver);

            if (currentHealth < maxHealth * 0.99f)
                Heal(Mathf.Max(10f, Mathf.Min(hp.healAmount, 40f)));
            if (hp != null) Destroy(hp.gameObject);
            return;
        }

        pickup.gameObject.SendMessage("OnCollected", this, SendMessageOptions.DontRequireReceiver);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!enablePickupCollection || other == null) return;

        if (other.TryGetComponent<CoinPickup>(out var coin))
        {
            GameManager.Instance?.AddCoins(coin.coinValue);
            Destroy(coin.gameObject);
            return;
        }

        if (other.TryGetComponent<HealthPickup>(out var hp))
        {
            hp.SendMessage("OnCollected", this, SendMessageOptions.DontRequireReceiver);

            if (currentHealth < maxHealth * 0.99f)
                Heal(Mathf.Max(10f, Mathf.Min(hp.healAmount, 40f)));
            if (hp != null) Destroy(hp.gameObject);
            return;
        }

        other.gameObject.SendMessage("OnCollected", this, SendMessageOptions.DontRequireReceiver);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan; Gizmos.DrawWireSphere(transform.position, shootRange);
        Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(transform.position, avoidEnemyRadius);
        Gizmos.color = Color.red; Gizmos.DrawWireSphere(transform.position, bossAvoidRadius);
        Gizmos.color = Color.green; Gizmos.DrawWireSphere(transform.position, pickupScanRadius);
        Gizmos.color = new Color(1f, 0.4f, 0.4f, 1f); Gizmos.DrawWireSphere(transform.position, friendlySeparationRadius);
    }

    // ---------- HEALTH BAR HELPERS (deep search) ----------
    // Finds an Image named exactly "Fill" (case-insensitive) anywhere under root.
    // If not found, returns the first Image whose name contains "fill".
    Image FindHealthFillDeep(Transform root)
    {
        if (root == null) return null;
        var imgs = root.GetComponentsInChildren<Image>(true); // include inactive
        // Prefer exact name match
        for (int i = 0; i < imgs.Length; i++)
        {
            if (string.Equals(imgs[i].name, "Fill", System.StringComparison.OrdinalIgnoreCase))
                return imgs[i];
        }
        // Fallback: name contains "fill"
        for (int i = 0; i < imgs.Length; i++)
        {
            if (imgs[i].name.ToLower().Contains("fill"))
                return imgs[i];
        }
        return null;
    }

    void EnsureImageIsFilledHorizontal(Image img)
    {
        if (img == null) return;
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = (int)Image.OriginHorizontal.Left;
        img.fillAmount = Mathf.Clamp01(currentHealth / Mathf.Max(1f, maxHealth));
    }
}

/// <summary>Keeps a transform facing the main camera.</summary>
public class BillboardToCamera : MonoBehaviour
{
    private Camera cam;
    public void GetOrSetCamera(Camera fallback) { cam = Camera.main != null ? Camera.main : fallback; }
    void LateUpdate()
    {
        if (cam == null) { cam = Camera.main; if (cam == null) return; }
        transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, Vector3.up);
    }
}
