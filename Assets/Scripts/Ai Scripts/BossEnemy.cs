using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

public class BossEnemy : EnemyController
{
    [Header("Boss Settings")]
    public GameObject[] zombiePrefabs;
    public float specialAttackCooldown = 10f;
    public float spawnRadius = 5f;
    public float bossScale = 2.5f;
    public float bossHealth = 500f;
    public float bossDamage = 25f;

    [Header("Patrol Settings")]
    public float patrolRadius = 10f;
    public float patrolInterval = 5f;
    public float returnThreshold = 2f;

    [Header("Drops")]
    public GameObject healthPickupPrefab;
    [Tooltip("Kept for backward-compatibility but not used anymore (ordered drops come from GameManager).")]
    public GameObject weaponPickupPrefab;

    [Header("Spawner Reference")]
    public BossSpawner spawner; // assigned by BossSpawner at runtime

    // ---------- HEALTH BAR ----------
    [Header("Health Bar (World-Space)")]
    [Tooltip("Shared prefab used by player/friendly/boss. May contain a Slider OR a Filled Image.")]
    public GameObject bossHealthBarPrefab;
    [Tooltip("Local offset from the boss pivot where the bar will appear.")]
    public Vector3 healthBarLocalOffset = new Vector3(0f, 3.0f, 0f);

    [Header("Health Bar Size / Facing")]
    [Tooltip("Keeps bar size independent of boss scaling.")]
    public bool maintainScaleIndependentOfBoss = true;
    [Tooltip("If true, the bar tilts with the camera (full billboard). If false, it stays upright (Y-only).")]
    public bool tiltWithCamera = true;

    [Header("Health Bar Absolute World Size (meters)")]
    [Tooltip("World width to FORCE on the bar's root Canvas RectTransform.")]
    public float barWorldWidth = 0.9f;
    [Tooltip("World height to FORCE on the bar's root Canvas RectTransform.")]
    public float barWorldHeight = 0.12f;
    [Tooltip("Additional uniform multiplier applied AFTER size & parent-scale cancel.")]
    public float healthBarWorldScale = 1f;
    [Tooltip("Normalize all RectTransform localScale in the bar hierarchy to (1,1,1).")]
    public bool normalizeHierarchyScales = true;

    // Internals
    private Transform _hbTransform;
    private Slider _hbSlider;                 // optional
    private Image _hbFilledImage;             // optional (Image.type == Filled)
    private Canvas _hbCanvas;                 // root/child canvas if present
    private RectTransform _hbCanvasRT;
    private Camera _mainCam;

    // --- Damage gates ---
    [Header("Damage Gates")]
    [SerializeField] private float spawnInvulnSeconds = 0.75f;
    [SerializeField] private bool restrictToAllowedInstigators = true;
    [SerializeField] private string[] allowedInstigatorTags = { "Player", "FriendlyAI" };
    private float _damageEnabledAt;

    private float nextSpecialAttackTime;
    private float nextPatrolTime;
    private Vector3 spawnPoint;
    private Transform player;
    private bool returningToSpawn;

    // Robust spawner cleanup path
    private bool _markedDead;

    // ----------------- Lifecycle -----------------
    protected override void OnEnable()
    {
        maxHealth = bossHealth;
        currentHealth = Mathf.Clamp(currentHealth <= 0 ? bossHealth : currentHealth, 0, bossHealth);
        damageAmount = bossDamage;

        base.OnEnable();

        _damageEnabledAt = Time.time + Mathf.Max(0f, spawnInvulnSeconds);
        if (_mainCam == null) _mainCam = Camera.main;

        EnsureHealthBarExists();
        SyncHealthBarImmediate();
    }

    private void OnDisable()
    {
        SafeDestroyHealthBar();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (bossHealth > 0f) maxHealth = bossHealth;
        if (bossDamage >= 0f) damageAmount = bossDamage;
        barWorldWidth = Mathf.Max(0.02f, barWorldWidth);
        barWorldHeight = Mathf.Max(0.02f, barWorldHeight);
        healthBarWorldScale = Mathf.Clamp(healthBarWorldScale, 0.01f, 5f);
    }
#endif

    protected override void Start()
    {
        base.Start();

        transform.localScale = Vector3.one * bossScale;
        spawnPoint = transform.position;

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) player = playerObj.transform;

        nextSpecialAttackTime = Time.time + Random.Range(0.4f, 1.2f);
        nextPatrolTime = Time.time + patrolInterval;

        if (_mainCam == null) _mainCam = Camera.main;
        EnsureHealthBarExists();
        SyncHealthBarImmediate();
    }

    protected override void Update()
    {
        base.Update();

        // Follow + billboard + scale
        if (_hbTransform != null)
        {
            _hbTransform.localPosition = healthBarLocalOffset;

            if (_mainCam == null) _mainCam = Camera.main;
            if (_mainCam != null)
            {
                if (tiltWithCamera)
                {
                    _hbTransform.rotation = Quaternion.LookRotation(-_mainCam.transform.forward, Vector3.up);
                }
                else
                {
                    Vector3 toCam = _mainCam.transform.position - _hbTransform.position;
                    toCam.y = 0f;
                    if (toCam.sqrMagnitude > 1e-6f)
                        _hbTransform.rotation = Quaternion.LookRotation(toCam.normalized, Vector3.up);
                }
            }

            ApplyHealthBarScale();
        }

        if (player == null) return;

        float distToPlayer = Vector3.Distance(player.position, transform.position);
        float distToSpawn = Vector3.Distance(spawnPoint, transform.position);

        if (distToPlayer > chaseRange)
        {
            if (distToSpawn > returnThreshold)
            {
                returningToSpawn = true;
                agent.SetDestination(spawnPoint);
            }
            else
            {
                if (returningToSpawn)
                {
                    returningToSpawn = false;
                    nextPatrolTime = Time.time + patrolInterval;
                }
                if (Time.time >= nextPatrolTime)
                {
                    Patrol();
                    nextPatrolTime = Time.time + patrolInterval;
                }
            }
        }
        else
        {
            returningToSpawn = false;
        }

        if (Time.time >= nextSpecialAttackTime)
        {
            SpecialAttack();
            nextSpecialAttackTime = Time.time + specialAttackCooldown;
        }
    }

    private void LateUpdate()
    {
        UpdateHealthBarValue();
    }

    // ----------------- AI helpers -----------------
    private void Patrol()
    {
        Vector3 randomDir = Random.insideUnitSphere * patrolRadius;
        randomDir += spawnPoint;
        randomDir.y = spawnPoint.y;

        if (NavMesh.SamplePosition(randomDir, out NavMeshHit hit, patrolRadius, NavMesh.AllAreas))
        {
            agent.SetDestination(hit.position);
        }
    }

    protected virtual void SpecialAttack()
    {
        if (zombiePrefabs == null || zombiePrefabs.Length == 0) return;

        int zombieCount = 5;
        float step = 360f / zombieCount;
        for (int i = 0; i < zombieCount; i++)
        {
            float ang = i * step * Mathf.Deg2Rad;
            Vector3 spawnPos = transform.position + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * spawnRadius;

            Vector3 finalPos = spawnPos;
            if (NavMesh.SamplePosition(spawnPos, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                finalPos = hit.position;

            GameObject prefab = zombiePrefabs[Random.Range(0, zombiePrefabs.Length)];
            GameObject minion = Instantiate(prefab, finalPos, Quaternion.identity);

            if (minion.TryGetComponent<NavMeshAgent>(out var mAgent))
            {
                if (!mAgent.enabled) mAgent.enabled = true;
                if (mAgent.isOnNavMesh) mAgent.Warp(finalPos);
                else minion.transform.position = finalPos;
            }
            else
            {
                minion.transform.position = finalPos;
            }
        }
    }

    // ----------------- Damage -----------------
    public override void TakeDamage(float damage)
    {
        if (Time.time < _damageEnabledAt) return;

        if (restrictToAllowedInstigators)
        {
            // Use ApplyDamageFrom to enforce allowed instigators
            return;
        }

        base.TakeDamage(damage);
    }

    public void ApplyDamageFrom(object payload)
    {
        if (Time.time < _damageEnabledAt) return;

        float amt = 0f;
        GameObject instigator = null;

        switch (payload)
        {
            case DamageEnvelope env:
                amt = env.amount; instigator = env.instigator; break;
            case System.ValueTuple<float, GameObject> tup:
                amt = tup.Item1; instigator = tup.Item2; break;
            default:
                if (!restrictToAllowedInstigators && payload is float fAmt)
                {
                    base.TakeDamage(fAmt);
                }
                return;
        }

        if (restrictToAllowedInstigators && !IsAllowedInstigator(instigator))
            return;

        base.TakeDamage(amt);
    }

    private bool IsAllowedInstigator(GameObject instigator)
    {
        if (instigator == null) return false;
        for (int i = 0; i < allowedInstigatorTags.Length; i++)
            if (instigator.CompareTag(allowedInstigatorTags[i])) return true;
        return false;
    }

    // ----------------- Death & Cleanup -----------------
    protected override void Die()
    {
        _markedDead = true; // mark real death path

        Vector3 dropBase = transform.position;
       // Debug.Log("[BossEnemy] Boss died at " + dropBase + ". Requesting boss-kill handling & drops.");

        if (healthPickupPrefab != null)
        {
            Instantiate(healthPickupPrefab, dropBase + Vector3.right * 1.5f, Quaternion.identity);
         //   Debug.Log("[BossEnemy] Spawned health pickup.");
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.RegisterBossKill(dropBase);
        }
        else
        {
            Debug.LogWarning("[BossEnemy] GameManager.Instance is null — boss drops will NOT occur.");
        }

        // Inform spawner (preferred path)
        if (spawner != null)
        {
            spawner.NotifyBossDied(this); // ✅ pass this BossEnemy
        }

        SafeDestroyHealthBar();
        base.Die();
    }

    private void OnDestroy()
    {
        // If object is being destroyed after a real death and the spawner still exists,
        // ensure the spawner is notified as a fallback (won't double-notify if spawner handles state).
        if (_markedDead && spawner != null)
        {
            spawner.NotifyBossDied(this); // ✅ pass this BossEnemy
        }
    }

    // ----------------- Health bar: spawn/sync/size -----------------
    private void EnsureHealthBarExists()
    {
        if (_hbTransform != null) return;

        if (bossHealthBarPrefab == null)
        {
            Debug.LogWarning("[BossEnemy] bossHealthBarPrefab is NULL on " + name + " — no health bar will spawn.");
            return;
        }

        GameObject hb = Instantiate(bossHealthBarPrefab);
        hb.name = $"{name}_BossHealthBar";
        hb.transform.SetParent(transform, worldPositionStays: false);
        hb.transform.localPosition = healthBarLocalOffset;
        hb.transform.localRotation = Quaternion.identity;

        // Normalize scales BEFORE sizing the canvas (prevents gigantic bars)
        if (normalizeHierarchyScales) NormalizeLocalScalesRecursive(hb.transform);

        // Force Canvas to World Space and set a real world size
        _hbCanvas = hb.GetComponentInChildren<Canvas>(true);
        if (_hbCanvas != null)
        {
            _hbCanvas.renderMode = RenderMode.WorldSpace;
            _hbCanvasRT = _hbCanvas.GetComponent<RectTransform>();
            if (_hbCanvasRT != null)
            {
                _hbCanvasRT.sizeDelta = new Vector2(barWorldWidth, barWorldHeight);
            }
        }

        // Keep on same layer as Boss (avoid culling mismatches)
        hb.layer = gameObject.layer;

        _hbTransform = hb.transform;

        _hbSlider = hb.GetComponentInChildren<Slider>(true);
        _hbFilledImage = FindFirstFilledImage(hb);

        SyncHealthBarImmediate();
        ApplyHealthBarScale(); // cancel parent scaling + apply world scale

        if (_hbSlider == null && _hbFilledImage == null)
            Debug.LogWarning("[BossEnemy] Healthbar prefab has neither Slider nor a Filled Image.", hb);
    }

    private static void NormalizeLocalScalesRecursive(Transform root)
    {
        root.localScale = Vector3.one;
        for (int i = 0; i < root.childCount; i++)
            NormalizeLocalScalesRecursive(root.GetChild(i));
    }

    private Image FindFirstFilledImage(GameObject root)
    {
        var imgs = root.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < imgs.Length; i++)
        {
            if (imgs[i] != null && imgs[i].type == Image.Type.Filled)
                return imgs[i];
        }
        return null;
    }

    private void SyncHealthBarImmediate()
    {
        if (_hbSlider == null && _hbFilledImage == null) return;

        float maxHp = Mathf.Max(1f, maxHealth);
        float hp = Mathf.Clamp(currentHealth, 0f, maxHp);
        float ratio = hp / maxHp;

        if (_hbSlider != null)
        {
            _hbSlider.minValue = 0f;
            _hbSlider.maxValue = maxHp;
            _hbSlider.value = hp;
        }

        if (_hbFilledImage != null)
        {
            _hbFilledImage.fillAmount = ratio;
        }
    }

    private void UpdateHealthBarValue()
    {
        if (_hbSlider == null && _hbFilledImage == null) return;

        float maxHp = Mathf.Max(1f, GetMaxHealth());
        float hp = Mathf.Clamp(GetHealth(), 0f, maxHp);
        float ratio = hp / maxHp;

        if (_hbSlider != null)
        {
            if (!Mathf.Approximately(_hbSlider.maxValue, maxHp)) _hbSlider.maxValue = maxHp;
            if (!Mathf.Approximately(_hbSlider.value, hp)) _hbSlider.value = hp;
        }

        if (_hbFilledImage != null)
        {
            if (!Mathf.Approximately(_hbFilledImage.fillAmount, ratio))
                _hbFilledImage.fillAmount = ratio;
        }
    }

    private void ApplyHealthBarScale()
    {
        if (_hbTransform == null) return;

        // 1) Cancel parent (boss) scale so bar remains constant
        Vector3 parent = transform.lossyScale;
        Vector3 invParent = new Vector3(
            parent.x != 0 ? 1f / parent.x : 1f,
            parent.y != 0 ? 1f / parent.y : 1f,
            parent.z != 0 ? 1f / parent.z : 1f
        );

        // 2) Apply uniform world scale multiplier
        Vector3 targetLocalScale = maintainScaleIndependentOfBoss
            ? Vector3.Scale(Vector3.one * healthBarWorldScale, invParent)
            : Vector3.one * healthBarWorldScale;

        // Safe clamps
        float minS = 0.0001f, maxS = 10f;
        targetLocalScale.x = Mathf.Clamp(targetLocalScale.x, minS, maxS);
        targetLocalScale.y = Mathf.Clamp(targetLocalScale.y, minS, maxS);
        targetLocalScale.z = Mathf.Clamp(targetLocalScale.z, minS, maxS);

        _hbTransform.localScale = targetLocalScale;
    }

    private void SafeDestroyHealthBar()
    {
        if (_hbTransform != null)
        {
            Destroy(_hbTransform.gameObject);
            _hbTransform = null;
            _hbSlider = null;
            _hbFilledImage = null;
            _hbCanvas = null;
            _hbCanvasRT = null;
        }
    }

    protected override void OnHealthChanged()
    {
        UpdateHealthBarValue();
    }

    // ----------------- Damage envelope struct (keep here for callers) -----------------
    public struct DamageEnvelope
    {
        public float amount;
        public GameObject instigator;
        public DamageEnvelope(float amount, GameObject instigator)
        { this.amount = amount; this.instigator = instigator; }
    }
}
