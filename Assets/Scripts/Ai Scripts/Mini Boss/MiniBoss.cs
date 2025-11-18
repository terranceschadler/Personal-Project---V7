// Assets/Scripts/Ai Scripts/Mini Boss/MiniBoss.cs
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

[DisallowMultipleComponent]
public class MiniBoss : MonoBehaviour
{
    [Header("Size")]
    [Min(0.25f)] public float minScale = 0.8f;
    [Min(0.25f)] public float maxScale = 1.6f;

    [Header("Health Scaling")]
    [Tooltip("Base HP at scale = 1.0")]
    [Min(1f)] public float baseHealth = 75f;
    [Tooltip("If true, health = baseHealth * scale. If false, use sizeToHealthCurve.")]
    public bool linearHealth = true;
    [Tooltip("If linearHealth=false, evaluated at x = chosen scale.")]
    public AnimationCurve sizeToHealthCurve = AnimationCurve.Linear(0.5f, 100f, 2f, 600f);
    [Tooltip("Cap the computed max HP (0 = no cap).")]
    [Min(0f)] public float maxHealthCap = 0f;
    [Tooltip("Also set current health = max after scaling.")]
    public bool refillCurrentHealth = true;

    [Header("Timing / Resilience")]
    [Tooltip("Frames to wait before first apply (lets other scripts Awake/Start).")]
    [Min(0)] public int applyDelayFrames = 1;
    [Tooltip("Additional frames after Start() to re-apply (defends against re-inits). 0=off")]
    [Min(0)] public int reapplyAfterStartFrames = 2;

    [Header("Aggressive Behavior")]
    [Tooltip("Make mini boss extremely aggressive - increases chase range and speed")]
    public bool aggressiveBehavior = true;
    [Tooltip("Chase range multiplier for mini bosses (2x = double range)")]
    [Min(1f)] public float chaseRangeMultiplier = 3f;
    [Tooltip("Movement speed multiplier for mini bosses (1.5x = 50% faster)")]
    [Min(1f)] public float speedMultiplier = 1.5f;
    [Tooltip("Angular speed multiplier for quick turning")]
    [Min(1f)] public float angularSpeedMultiplier = 2f;
    [Tooltip("Acceleration multiplier for rapid pursuit")]
    [Min(1f)] public float accelerationMultiplier = 2f;
    [Tooltip("Disable wandering - mini boss always pursues")]
    public bool disableWandering = true;

    [Header("Feet Alignment (auto)")]
    [Tooltip("If no MeshFeetAlignToAgentBase is present, add one automatically.")]
    public bool autoAddAligner = true;
    [Tooltip("Run alignment after scaling (recommended).")]
    public bool alignAfterScale = true;
    [Min(0)] public int alignWaitFrames = 1;
    [Tooltip("Keep aligned briefly to survive early IK/root-motion settling. 0=off")]
    [Min(0f)] public float tempKeepAlignedSeconds = 0.0f;
    [Tooltip("Override aligner.extraPadding each spawn (negative = leave as-is).")]
    public float alignerExtraPaddingOverride = 0.05f;

    // ------------------- LOOT -------------------
    [Header("Loot (MiniBoss bonus)")]
    [Tooltip("Scales existing loot systems (LootManager, LootDropper, etc.). 1 = normal enemy, 3 = triple.")]
    [Min(0f)] public float lootMultiplier = 3f;

    [Tooltip("Extra independent loot rolls on top of the base drop. Useful if your LootManager does per-roll selection.")]
    [Min(0)] public int extraLootRolls = 1;

    [Tooltip("Guaranteed coin bonus (added to whatever your normal system does).")]
    public Vector2Int guaranteedCoinBonusRange = new Vector2Int(20, 50);

    [Tooltip("Guaranteed material bonus (added to whatever your normal system does).")]
    public Vector2Int guaranteedMaterialBonusRange = new Vector2Int(10, 20);

    [Tooltip("Try to auto-subscribe to a death event on attached components (OnDeath/Died/onDeath). If not found, call NotifyKilled() from your death code.")]
    public bool autoHookDeathEvent = true;

    [Header("Fallback Loot (if no loot system found)")]
    [Tooltip("If true, spawns fallback items/currency when no LootManager/LootDropper is detected. ENSURES loot always drops.")]
    public bool useFallbackLoot = true;
    
    [Tooltip("Coin prefab to spawn (REQUIRED - mini bosses must drop coins)")]
    public GameObject coinPrefab;
    
    [Tooltip("Minimum coins to spawn (guaranteed)")]
    [Min(2)] public int minCoinsToSpawn = 3;
    
    [Tooltip("Maximum coins to spawn")]
    [Min(2)] public int maxCoinsToSpawn = 8;
    
    [Tooltip("Additional loot prefabs to spawn (health, items, etc.)")]
    public GameObject[] additionalLootPrefabs;
    
    [Tooltip("How many additional random items to spawn from additionalLootPrefabs array")]
    [Min(0)] public int additionalLootCount = 2;
    
    [Tooltip("Radius around mini boss to spawn fallback loot")]
    [Min(0f)] public float fallbackLootSpawnRadius = 2f;

    [Header("Health Bar")]
    [Tooltip("Health bar prefab to display above mini boss (uses same prefab as boss/player)")]
    public GameObject healthBarPrefab;
    [Tooltip("Local offset from mini boss pivot where the bar will appear")]
    public Vector3 healthBarLocalOffset = new Vector3(0f, 2.5f, 0f);
    [Tooltip("Show health bar only when taking damage")]
    public bool showOnlyWhenDamaged = true;
    [Tooltip("How long to keep health bar visible after last damage (seconds)")]
    [Min(0f)] public float healthBarVisibleDuration = 3f;
    [Tooltip("World width of the health bar canvas")]
    [Min(0.02f)] public float barWorldWidth = 0.5f;
    [Tooltip("World height of the health bar canvas")]
    [Min(0.02f)] public float barWorldHeight = 0.06f;
    [Tooltip("Scale multiplier for the health bar (smaller = smaller bar)")]
    public float healthBarScale = 0.003f;
    [Tooltip("Always face camera (billboard effect)")]
    public bool billboardHealthBar = true;

    [Header("Diagnostics")]
    public bool debugLogs = true;

    // internals
    private IHealthAdapter _health;
    private MeshFeetAlignToAgentBase _aligner;
    private float _chosenScale = 1f;
    private float _targetMax = 1f;

    // Loot internals
    private ILootAdapter _loot;
    private bool _lootGrantedThisLifecycle = false; // safety against double drops

    // Health bar internals
    private Transform _hbTransform;
    private UnityEngine.UI.Slider _hbSlider;
    private UnityEngine.UI.Image _hbFilledImage;
    private Canvas _hbCanvas;
    private RectTransform _hbCanvasRT;
    private Camera _mainCam;
    private float _lastDamageTime = -999f;
    private EnemyController _enemyController;

    void Awake()
    {
        _health = HealthAdapter.TryCreate(gameObject, debugLogs);
        _aligner = GetComponent<MeshFeetAlignToAgentBase>();
        if (!_aligner && autoAddAligner) _aligner = gameObject.AddComponent<MeshFeetAlignToAgentBase>();

        _loot = LootAdapter.TryCreate(gameObject, debugLogs);

        // Get reference to EnemyController to hook into damage events
        _enemyController = GetComponent<EnemyController>();
        if (_enemyController == null && debugLogs)
        {
            Debug.LogWarning($"[MiniBoss] No EnemyController found on '{gameObject.name}' - health bar won't update on damage!");
        }

        // Try to auto-load health bar prefab if not assigned
        if (healthBarPrefab == null)
        {
            // Try to find health bar prefab from existing BossEnemy in scene
            var existingBoss = FindObjectOfType<BossEnemy>();
            if (existingBoss != null && existingBoss.bossHealthBarPrefab != null)
            {
                healthBarPrefab = existingBoss.bossHealthBarPrefab;
                if (debugLogs)
                    Debug.Log($"[MiniBoss] Borrowed health bar prefab from BossEnemy for '{gameObject.name}'");
            }

            // Fallback: try Resources folder
            if (healthBarPrefab == null)
            {
                healthBarPrefab = Resources.Load<GameObject>("Healthbar");
                if (healthBarPrefab == null)
                    healthBarPrefab = Resources.Load<GameObject>("Prefabs/Healthbar");

                if (healthBarPrefab != null && debugLogs)
                    Debug.Log($"[MiniBoss] Auto-loaded health bar prefab from Resources for '{gameObject.name}'");
            }
        }

        // Always add health bar handler so it's ready when prefab is assigned
        if (GetComponent<MiniBossHealthBarHandler>() == null)
        {
            gameObject.AddComponent<MiniBossHealthBarHandler>();
            if (debugLogs)
                Debug.Log($"[MiniBoss] Auto-added MiniBossHealthBarHandler to '{gameObject.name}'");
        }

        // Warn if health bar prefab is still not assigned
        if (healthBarPrefab == null)
        {
            Debug.LogWarning($"[MiniBoss] healthBarPrefab not assigned on '{gameObject.name}'. " +
                           "Assign a health bar prefab in the inspector or place 'Healthbar.prefab' in a Resources folder.");
        }

        if (autoHookDeathEvent)
        {
            if (debugLogs)
                Debug.Log($"[MiniBoss] Attempting to auto-hook death event on '{gameObject.name}'...", gameObject);
            TryAutoHookDeathEvent();
        }

        // Apply aggressive behavior immediately
        if (aggressiveBehavior)
        {
            ApplyAggressiveBehavior();
        }
    }

    void OnEnable()
    {
        _lootGrantedThisLifecycle = false; // reset for pooled spawns
        _lastDamageTime = -999f; // reset damage timer

        if (_mainCam == null) _mainCam = Camera.main;

        // Create health bar when mini boss spawns
        EnsureHealthBarExists();
        UpdateHealthBarValue();

        StartCoroutine(CoApplyAll());
    }

    void OnDisable()
    {
        SafeDestroyHealthBar();
    }

    void Update()
    {
        UpdateHealthBarVisibility();
    }

    void LateUpdate()
    {
        // Update health bar position and rotation
        if (_hbTransform != null)
        {
            _hbTransform.localPosition = healthBarLocalOffset;

            // Billboard effect - always face camera
            if (billboardHealthBar)
            {
                if (_mainCam == null) _mainCam = Camera.main;
                if (_mainCam != null)
                {
                    _hbTransform.rotation = Quaternion.LookRotation(-_mainCam.transform.forward, Vector3.up);
                }
            }

            ApplyHealthBarScale();
        }

        UpdateHealthBarValue();
    }

    // ---------------- Back-compat for existing spawners ----------------
    /// <summary>
    /// Kept for backward compatibility with older mixers that call this.
    /// Randomizes size and applies health immediately (with alignment).
    /// </summary>
    public void ApplyRandomSizeAndHealth()
    {
        ComputeScaleAndHealth();     // sets _chosenScale + _targetMax
        ApplyScale();                // scales transform
        // Apply health once right away
        if (_health != null && _health.IsValid)
        {
            _health.SetMaxHealth(_targetMax, refillCurrentHealth, debugLogs);
            _health.NotifyUI(debugLogs);
        }
        // Align quickly; also run the coroutine alignment for safety
        if (alignAfterScale && _aligner != null) _aligner.AlignNow();
        StartCoroutine(CoApplyAll()); // still run the resilient flow
    }
    // -------------------------------------------------------------------

    private IEnumerator CoApplyAll()
    {
        for (int i = 0; i < applyDelayFrames; i++) yield return null;

        // If back-compat path already computed, skip recomputing
        if (_chosenScale <= 0.001f) ComputeScaleAndHealth();
        ApplyScale();

        yield return CoApplyHealth();

        if (alignAfterScale) yield return CoAlign();

        for (int i = 0; i < reapplyAfterStartFrames; i++) yield return null;
        yield return CoApplyHealth();
    }

    private void ComputeScaleAndHealth()
    {
        _chosenScale = UnityEngine.Random.Range(minScale, maxScale);

        float hp = linearHealth ? (baseHealth * _chosenScale)
                                : Mathf.Max(1f, sizeToHealthCurve.Evaluate(_chosenScale));
        if (maxHealthCap > 0f) hp = Mathf.Min(hp, maxHealthCap);
        _targetMax = Mathf.Max(1f, hp);

        if (debugLogs)
            Debug.Log($"[MiniBoss] Computed scale={_chosenScale:0.00} targetMaxHP={_targetMax:0}");
    }

    private void ApplyScale()
    {
        transform.localScale = new Vector3(_chosenScale, _chosenScale, _chosenScale);
    }

    private IEnumerator CoApplyHealth()
    {
        if (_health != null && _health.IsValid)
        {
            _health.SetMaxHealth(_targetMax, refillCurrentHealth, debugLogs);
            _health.NotifyUI(debugLogs);
        }
        else if (debugLogs)
        {
            Debug.LogWarning("[MiniBoss] No compatible health script found; consider mapping names or add a wrapper.");
        }
        yield break;
    }

    private IEnumerator CoAlign()
    {
        if (_aligner == null) yield break;

        if (alignerExtraPaddingOverride >= 0f)
            _aligner.extraPadding = alignerExtraPaddingOverride;

        for (int i = 0; i < Mathf.Max(0, alignWaitFrames); i++) yield return null;

        _aligner.AlignNow();

        if (tempKeepAlignedSeconds > 0f)
        {
            bool prev = _aligner.keepAligned;
            _aligner.keepAligned = true;
            float t = 0f;
            while (t < tempKeepAlignedSeconds) { t += Time.deltaTime; yield return null; }
            _aligner.keepAligned = prev;
        }
    }

    // ---------- Loot trigger API ----------
    /// <summary>
    /// Call this from your death code if auto-hooking doesn't catch it.
    /// Safe against multiple calls per lifecycle.
    /// </summary>
    public void NotifyKilled()
    {
        // ALWAYS log when this is called, regardless of debugLogs setting
        Debug.Log($"[MiniBoss] ★★★ NotifyKilled() CALLED on '{gameObject.name}' ★★★", gameObject);
        TryGrantMiniBossLoot();
    }

    /// <summary>
    /// TEST METHOD: Call this to manually test loot drops without killing the mini boss.
    /// Can be called from Inspector or during gameplay for testing.
    /// </summary>
    [ContextMenu("TEST: Force Loot Drop Now")]
    public void TestForceLootDrop()
    {
        Debug.Log($"[MiniBoss] ===== TESTING LOOT DROP =====", gameObject);
        _lootGrantedThisLifecycle = false; // Reset so it can drop again
        NotifyKilled();
    }

    // ---------- Aggressive Behavior ----------
    private void ApplyAggressiveBehavior()
    {
        var enemyController = GetComponent<EnemyController>();
        var agent = GetComponent<UnityEngine.AI.NavMeshAgent>();

        if (enemyController == null && agent == null)
        {
            if (debugLogs)
                Debug.LogWarning($"[MiniBoss] No EnemyController or NavMeshAgent found on '{gameObject.name}' - cannot apply aggressive behavior!");
            return;
        }

        // Boost EnemyController settings
        if (enemyController != null)
        {
            // Massively increase chase range so mini boss pursues from far away
            float originalChaseRange = enemyController.chaseRange;
            enemyController.chaseRange *= chaseRangeMultiplier;

            // Disable wandering if requested - mini boss only chases
            if (disableWandering)
            {
                enemyController.wanderRadius = 0f;
                enemyController.wanderInterval = 999999f; // Effectively never wander
            }

            if (debugLogs)
                Debug.Log($"[MiniBoss] Aggressive behavior applied to '{gameObject.name}': " +
                         $"Chase range {originalChaseRange:F1} → {enemyController.chaseRange:F1}, " +
                         $"Wandering: {!disableWandering}");
        }

        // Boost NavMeshAgent settings for speed and responsiveness
        if (agent != null)
        {
            float originalSpeed = agent.speed;
            float originalAngular = agent.angularSpeed;
            float originalAccel = agent.acceleration;

            agent.speed *= speedMultiplier;
            agent.angularSpeed *= angularSpeedMultiplier;
            agent.acceleration *= accelerationMultiplier;

            // Make mini boss more aggressive in pursuit
            agent.stoppingDistance = Mathf.Min(agent.stoppingDistance, 1.5f); // Get closer
            agent.autoBraking = false; // Don't slow down when approaching

            if (debugLogs)
                Debug.Log($"[MiniBoss] NavMeshAgent boosted on '{gameObject.name}': " +
                         $"Speed {originalSpeed:F1} → {agent.speed:F1}, " +
                         $"Angular {originalAngular:F0} → {agent.angularSpeed:F0}, " +
                         $"Accel {originalAccel:F1} → {agent.acceleration:F1}");
        }
    }

    private void TryGrantMiniBossLoot()
    {
        if (_lootGrantedThisLifecycle) return;
        _lootGrantedThisLifecycle = true;

        bool lootSystemWorked = false;

        if (_loot != null && _loot.IsValid)
        {
            // 1) Scale existing loot (spawns physical pickups via LootManager/LootDropper)
            if (lootMultiplier > 0f)
                _loot.SpawnScaledLoot(transform.position, lootMultiplier, debugLogs);

            // 2) Extra independent rolls (for table-based systems)
            for (int i = 0; i < Mathf.Max(0, extraLootRolls); i++)
                _loot.SpawnScaledLoot(transform.position, 1f, debugLogs);

            // NOTE: We DO NOT use GrantCurrencyBonuses here because that only adds to stats
            // Instead, we rely on SpawnGuaranteedCoins() below to spawn physical coin pickups

            if (debugLogs)
                Debug.Log($"[MiniBoss] Loot system called with mult={lootMultiplier}, extraRolls={extraLootRolls}.");
            
            lootSystemWorked = true;
        }

        // ALWAYS spawn guaranteed physical coin/health drops (in addition to any loot system)
        if (useFallbackLoot)
        {
            SpawnGuaranteedCoins();
            SpawnAdditionalLoot();
            
            if (debugLogs)
                Debug.Log($"[MiniBoss] Spawned guaranteed physical loot drops (coins + health/items).");
        }

        // Only show error if BOTH the loot system AND fallback are disabled/missing
        if (!lootSystemWorked && !useFallbackLoot)
        {
            Debug.LogError($"[MiniBoss] CRITICAL ERROR: No valid loot system found on '{gameObject.name}'!\n" +
                           "Mini bosses are configured to ALWAYS drop loot, but the loot adapter could not find a compatible system.\n" +
                           "Required: GameObject or scene must have one of:\n" +
                           "  - LootManager, DropManager, or ItemDropManager (with SpawnLoot method)\n" +
                           "  - LootDropper, Dropper, or ItemDropper (with DropLoot method)\n" +
                           "  - GameManager (with coins/materials fields for currency bonuses)\n" +
                           "OR enable 'useFallbackLoot' and assign coinPrefab (required) + additionalLootPrefabs (optional) to guarantee drops.",
                           gameObject);
        }
    }

    private void SpawnGuaranteedCoins()
    {
        if (coinPrefab == null)
        {
            Debug.LogWarning($"[MiniBoss] coinPrefab not assigned on '{gameObject.name}'! Mini bosses should always drop coins.");
            return;
        }

        Vector3 spawnCenter = transform.position;
        int coinCount = UnityEngine.Random.Range(minCoinsToSpawn, maxCoinsToSpawn + 1);
        
        if (debugLogs)
            Debug.Log($"[MiniBoss] SPAWNING {coinCount} PHYSICAL COIN PICKUPS at position {spawnCenter}");
        
        for (int i = 0; i < coinCount; i++)
        {
            // Random position around mini boss in a circle
            Vector2 randomCircle = UnityEngine.Random.insideUnitCircle * fallbackLootSpawnRadius;
            Vector3 spawnPos = spawnCenter + new Vector3(randomCircle.x, 1f, randomCircle.y); // Raised to 1f for visibility

            // Instantiate the physical coin pickup
            GameObject coin = Instantiate(coinPrefab, spawnPos, Quaternion.identity);
            
            if (coin == null)
            {
                Debug.LogError($"[MiniBoss] Failed to instantiate coinPrefab! Check that the prefab is valid.");
                continue;
            }
            
            if (debugLogs)
                Debug.Log($"[MiniBoss] Spawned coin {i+1}/{coinCount}: {coin.name} at {spawnPos}");
            
            // Optional: Add upward force if coin has rigidbody for dramatic effect
            Rigidbody rb = coin.GetComponent<Rigidbody>();
            if (rb != null)
            {
                Vector3 randomForce = new Vector3(randomCircle.x, 3f, randomCircle.y);
                rb.AddForce(randomForce, ForceMode.Impulse);
            }
        }

        Debug.Log($"[MiniBoss] ✓ Successfully spawned {coinCount} physical coin pickups!");
    }

    private void SpawnAdditionalLoot()
    {
        if (additionalLootPrefabs == null || additionalLootPrefabs.Length == 0)
        {
            Debug.LogWarning($"[MiniBoss] No additionalLootPrefabs assigned on '{gameObject.name}'! " +
                           "Mini bosses should drop health pickups. " +
                           "Assign health pickup prefabs in the 'Fallback Loot' section.", gameObject);
            return;
        }

        Vector3 spawnCenter = transform.position;
        int itemsSpawned = 0;

        if (debugLogs)
            Debug.Log($"[MiniBoss] SPAWNING {additionalLootCount} PHYSICAL HEALTH/ITEM PICKUPS from {additionalLootPrefabs.Length} prefabs");

        // Find or create a persistent tracker object
        GameObject tracker = GameObject.Find("MiniBossLootTracker");
        if (tracker == null)
        {
            tracker = new GameObject("MiniBossLootTracker");
            tracker.AddComponent<MiniBossLootTracker>();
            DontDestroyOnLoad(tracker);
            Debug.Log("[MiniBoss] Created persistent MiniBossLootTracker object");
        }
        var trackerScript = tracker.GetComponent<MiniBossLootTracker>();

        for (int i = 0; i < additionalLootCount; i++)
        {
            // Pick random prefab
            GameObject prefab = additionalLootPrefabs[UnityEngine.Random.Range(0, additionalLootPrefabs.Length)];
            if (prefab == null)
            {
                Debug.LogWarning($"[MiniBoss] additionalLootPrefabs[{i}] is null! Skipping...");
                continue;
            }

            // Random position around mini boss - HIGHER spawn for visibility
            Vector2 randomCircle = UnityEngine.Random.insideUnitCircle * fallbackLootSpawnRadius;
            Vector3 spawnPos = spawnCenter + new Vector3(randomCircle.x, 2.5f, randomCircle.y);

            // Instantiate the physical pickup
            GameObject item = Instantiate(prefab, spawnPos, Quaternion.identity);
            
            if (item == null)
            {
                Debug.LogError($"[MiniBoss] Failed to instantiate {prefab.name}! Check that the prefab is valid.");
                continue;
            }
            
            // CRITICAL FIX: Disable any lifetime timers that would destroy the pickup
            var pickupBase = item.GetComponent<MonoBehaviour>();
            if (pickupBase != null)
            {
                var lifetimeField = pickupBase.GetType().GetField("lifetimeSeconds");
                if (lifetimeField != null)
                {
                    float currentLifetime = (float)lifetimeField.GetValue(pickupBase);
                    if (currentLifetime > 0f && currentLifetime < 10f)
                    {
                        // Disable short lifetime timers (mini boss loot should persist)
                        lifetimeField.SetValue(pickupBase, 0f);
                        Debug.Log($"[MiniBoss] Disabled lifetime timer on health pickup (was {currentLifetime}s, now 0s - will persist)");
                    }
                }
                
                // CRITICAL: Disable snapToGroundOnEnable to prevent instant destruction
                var snapField = pickupBase.GetType().GetField("snapToGroundOnEnable");
                if (snapField != null)
                {
                    bool currentSnap = (bool)snapField.GetValue(pickupBase);
                    if (currentSnap)
                    {
                        snapField.SetValue(pickupBase, false);
                        Debug.Log($"[MiniBoss] Disabled snapToGroundOnEnable on health pickup (was causing instant destruction)");
                    }
                }
            }
            
            itemsSpawned++;
            
            // ALWAYS log health pickup spawns for debugging
            Debug.Log($"[MiniBoss] ✓ Spawned HEALTH PICKUP #{itemsSpawned}: '{item.name}' at position {spawnPos}", item);
            Debug.Log($"[MiniBoss] Health pickup active: {item.activeInHierarchy}, instance ID: {item.GetInstanceID()}");
            
            // Check for common components
            Collider col = item.GetComponent<Collider>();
            if (col == null)
            {
                Debug.LogError($"[MiniBoss] ⚠️ Health pickup '{item.name}' has NO COLLIDER - player can't pick it up!", item);
            }
            else
            {
                Debug.Log($"[MiniBoss] Health pickup has collider: {col.GetType().Name}, isTrigger={col.isTrigger}");
            }
            
            // Draw a debug sphere in the Scene view to show spawn location
            Debug.DrawRay(spawnPos, Vector3.up * 5f, Color.green, 10f);
            Debug.DrawRay(spawnPos, Vector3.down * 5f, Color.green, 10f);
            
            // Check all components on the health pickup
            var components = item.GetComponents<Component>();
            Debug.Log($"[MiniBoss] Health pickup has {components.Length} components: {string.Join(", ", System.Array.ConvertAll(components, c => c.GetType().Name))}");
            
            // Optional: Add upward force if item has rigidbody - DELAYED to prevent instant destruction
            Rigidbody rb = item.GetComponent<Rigidbody>();
            if (rb != null)
            {
                // Disable gravity initially to prevent falling through floor
                rb.useGravity = false;
                
                Vector3 randomForce = new Vector3(randomCircle.x * 0.5f, 2f, randomCircle.y * 0.5f);
                
                // Use the persistent tracker to apply delayed physics
                if (trackerScript != null)
                {
                    trackerScript.StartCoroutine(trackerScript.ApplyDelayedPhysics(rb, randomForce));
                }
                
                Debug.Log($"[MiniBoss] Health pickup has Rigidbody - will apply delayed physics via persistent tracker");
            }
            else
            {
                Debug.LogWarning($"[MiniBoss] Health pickup '{item.name}' has no Rigidbody - it won't fall naturally!", item);
            }
            
            // Check if it's still alive after frames - use persistent tracker
            if (trackerScript != null)
            {
                trackerScript.StartCoroutine(trackerScript.CheckHealthPickupExists(item, spawnPos));
            }
        }

        if (itemsSpawned > 0)
        {
            Debug.Log($"[MiniBoss] ✓ Successfully spawned {itemsSpawned} physical health/item pickups!");
        }
        else
        {
            Debug.LogWarning($"[MiniBoss] Failed to spawn any health/item pickups! Check that prefabs are valid.");
        }
    }
    // Try to auto-subscribe to a likely death event on attached scripts
    private void TryAutoHookDeathEvent()
    {
        try
        {
            // Common death event names we�ll probe for (UnityEvent or C# event or Action)
            string[] deathNames = { "OnDeath", "onDeath", "Died", "OnDied", "Death", "Killed", "OnKilled" };

            foreach (var c in GetComponents<Component>())
            {
                if (c == null) continue;
                var t = c.GetType();

                // 1) UnityEvent fields/properties (no params)
                foreach (var name in deathNames)
                {
                    // Field
                    var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (f != null && typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(f.FieldType))
                    {
                        var ue = f.GetValue(c) as UnityEngine.Events.UnityEventBase;
                        if (ue is UnityEngine.Events.UnityEvent uevt)
                        {
                            uevt.AddListener(OnDeathDetected);
                            if (debugLogs) Debug.Log($"[MiniBoss] Auto-hooked UnityEvent field {t.Name}.{name}");
                            return;
                        }
                    }
                    // Property
                    var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (p != null && typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(p.PropertyType))
                    {
                        var ue = p.GetValue(c, null) as UnityEngine.Events.UnityEventBase;
                        if (ue is UnityEngine.Events.UnityEvent uevt)
                        {
                            uevt.AddListener(OnDeathDetected);
                            if (debugLogs) Debug.Log($"[MiniBoss] Auto-hooked UnityEvent prop {t.Name}.{name}");
                            return;
                        }
                    }
                }

                // 2) C# events/delegates: look for Action or Action<object> etc.
                foreach (var e in t.GetEvents(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    foreach (var name in deathNames)
                    {
                        if (!e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;

                        var handler = Delegate.CreateDelegate(e.EventHandlerType, this,
                            typeof(MiniBoss).GetMethod(nameof(OnDeathDetected), BindingFlags.Instance | BindingFlags.NonPublic));
                        e.AddEventHandler(c, handler);
                        if (debugLogs) Debug.Log($"[MiniBoss] Auto-hooked C# event {t.Name}.{e.Name}");
                        return;
                    }
                }

                // 3) Action fields/properties named like death events
                foreach (var name in deathNames)
                {
                    var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (f != null && typeof(Delegate).IsAssignableFrom(f.FieldType))
                    {
                        // Append our handler if possible (only works for multicast delegate)
                        var del = f.GetValue(c) as Delegate;
                        var add = Delegate.CreateDelegate(f.FieldType, this,
                            typeof(MiniBoss).GetMethod(nameof(OnDeathDetected), BindingFlags.Instance | BindingFlags.NonPublic));
                        del = Delegate.Combine(del, add);
                        f.SetValue(c, del);
                        if (debugLogs) Debug.Log($"[MiniBoss] Auto-hooked delegate field {t.Name}.{name}");
                        return;
                    }

                    var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (p != null && typeof(Delegate).IsAssignableFrom(p.PropertyType) && p.CanRead && p.CanWrite)
                    {
                        var del = p.GetValue(c, null) as Delegate;
                        var add = Delegate.CreateDelegate(p.PropertyType, this,
                            typeof(MiniBoss).GetMethod(nameof(OnDeathDetected), BindingFlags.Instance | BindingFlags.NonPublic));
                        del = Delegate.Combine(del, add);
                        p.SetValue(c, del, null);
                        if (debugLogs) Debug.Log($"[MiniBoss] Auto-hooked delegate prop {t.Name}.{name}");
                        return;
                    }
                }
            }
        }
        catch (Exception e)
        {
            if (debugLogs)
                Debug.Log($"[MiniBoss] Auto-hook death event attempt finished on '{gameObject.name}'. " +
                         "Using EnemyController.Die() integration instead.", gameObject);
        }
    }

    // Invoked by auto-hook or can be called from other scripts if needed
    private void OnDeathDetected()
    {
        Debug.Log($"[MiniBoss] OnDeathDetected() triggered on '{gameObject.name}' - calling NotifyKilled()", gameObject);
        NotifyKilled();
    }

    // ---------- Health Bar System ----------

    /// <summary>
    /// Call this when mini boss takes damage to show health bar
    /// </summary>
    public void OnMiniBossDamaged()
    {
        _lastDamageTime = Time.time;

        // Make health bar visible when taking damage
        if (_hbTransform != null && showOnlyWhenDamaged)
        {
            _hbTransform.gameObject.SetActive(true);
        }

        UpdateHealthBarValue();
    }

    private void EnsureHealthBarExists()
    {
        if (_hbTransform != null) return;

        if (healthBarPrefab == null)
        {
            if (debugLogs)
                Debug.LogWarning($"[MiniBoss] healthBarPrefab is NULL on '{name}' - no health bar will spawn.");
            return;
        }

        GameObject hb = Instantiate(healthBarPrefab);
        hb.name = $"{name}_MiniBossHealthBar";
        hb.transform.SetParent(transform, worldPositionStays: false);
        hb.transform.localPosition = healthBarLocalOffset;
        hb.transform.localRotation = Quaternion.identity;

        // Normalize scales to prevent gigantic bars
        NormalizeLocalScalesRecursive(hb.transform);

        // Force Canvas to World Space and set real world size
        _hbCanvas = hb.GetComponentInChildren<Canvas>(true);
        if (_hbCanvas != null)
        {
            _hbCanvas.renderMode = RenderMode.WorldSpace;

            // Disable CanvasScaler if present - it causes sizing issues in world space
            var scaler = _hbCanvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            if (scaler != null)
            {
                scaler.enabled = false;
            }

            _hbCanvasRT = _hbCanvas.GetComponent<RectTransform>();
            if (_hbCanvasRT != null)
            {
                _hbCanvasRT.sizeDelta = new Vector2(barWorldWidth, barWorldHeight);
            }
        }

        // Keep on same layer as mini boss
        hb.layer = gameObject.layer;

        _hbTransform = hb.transform;
        _hbSlider = hb.GetComponentInChildren<UnityEngine.UI.Slider>(true);
        _hbFilledImage = FindFirstFilledImage(hb);

        // Initially hide if configured to show only when damaged
        if (showOnlyWhenDamaged)
        {
            hb.SetActive(false);
        }

        UpdateHealthBarValue();
        ApplyHealthBarScale();

        if (_hbSlider == null && _hbFilledImage == null && debugLogs)
            Debug.LogWarning($"[MiniBoss] Health bar prefab has neither Slider nor Filled Image.", hb);

        if (debugLogs)
            Debug.Log($"[MiniBoss] Health bar created for '{name}'");
    }

    private static void NormalizeLocalScalesRecursive(Transform root)
    {
        root.localScale = Vector3.one;
        for (int i = 0; i < root.childCount; i++)
            NormalizeLocalScalesRecursive(root.GetChild(i));
    }

    private UnityEngine.UI.Image FindFirstFilledImage(GameObject root)
    {
        var imgs = root.GetComponentsInChildren<UnityEngine.UI.Image>(true);
        for (int i = 0; i < imgs.Length; i++)
        {
            if (imgs[i] != null && imgs[i].type == UnityEngine.UI.Image.Type.Filled)
                return imgs[i];
        }
        return null;
    }

    private void UpdateHealthBarValue()
    {
        if (_hbSlider == null && _hbFilledImage == null) return;

        float maxHp = Mathf.Max(1f, GetMaxHealth());
        float hp = Mathf.Clamp(GetCurrentHealth(), 0f, maxHp);
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

    private void UpdateHealthBarVisibility()
    {
        if (!showOnlyWhenDamaged || _hbTransform == null) return;

        // Hide health bar after duration expires
        float timeSinceLastDamage = Time.time - _lastDamageTime;
        if (timeSinceLastDamage > healthBarVisibleDuration && _hbTransform.gameObject.activeSelf)
        {
            _hbTransform.gameObject.SetActive(false);
        }
    }

    private void ApplyHealthBarScale()
    {
        if (_hbTransform == null) return;

        // Cancel parent (mini boss) scale so bar remains constant size
        Vector3 parent = transform.lossyScale;
        Vector3 invParent = new Vector3(
            parent.x != 0 ? 1f / parent.x : 1f,
            parent.y != 0 ? 1f / parent.y : 1f,
            parent.z != 0 ? 1f / parent.z : 1f
        );

        // Apply the healthBarScale multiplier to get correct world size
        Vector3 targetScale = invParent * healthBarScale;

        // Safe clamps
        float minS = 0.0001f, maxS = 10f;
        targetScale.x = Mathf.Clamp(targetScale.x, minS, maxS);
        targetScale.y = Mathf.Clamp(targetScale.y, minS, maxS);
        targetScale.z = Mathf.Clamp(targetScale.z, minS, maxS);

        _hbTransform.localScale = targetScale;
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

    private float GetMaxHealth()
    {
        if (_enemyController != null)
            return _enemyController.maxHealth;
        return _targetMax;
    }

    private float GetCurrentHealth()
    {
        // Use reflection to get current health from EnemyController
        if (_enemyController != null)
        {
            var field = typeof(EnemyController).GetField("currentHealth",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);
            if (field != null)
                return (float)field.GetValue(_enemyController);
        }
        return 0f;
    }

    // ----------------- Health Adapter -----------------
    public interface IHealthAdapter
    {
        bool IsValid { get; }
        void SetMaxHealth(float value, bool fillCurrent, bool log);
        void NotifyUI(bool log);
    }

    class HealthAdapter : IHealthAdapter
    {
        private readonly Component host;

        // Fields/props (int OR float)
        private readonly FieldInfo fMax, fCur;
        private readonly PropertyInfo pMax, pCur;
        private readonly Type maxType, curType;

        // Methods
        private readonly MethodInfo mForceSetMax_2;        // ForceSetMaxHealth(value,bool keepPercent)
        private readonly MethodInfo mSetMax_1, mSetMax_2;  // SetMaxHealth(value) / SetMaxHealth(value,bool)
        private readonly MethodInfo mSetCur_1;             // SetHealth/SetHP(value)
        private readonly MethodInfo mResetToMax_0;         // ResetToMax()/Refill()

        // Optional UI hooks (no params)
        private readonly MethodInfo mOnHealthChanged_0, mRefreshUI_0, mUpdateUI_0, mSyncUI_0;

        public bool IsValid => host != null;

        private HealthAdapter(Component h,
                              FieldInfo _fMax, FieldInfo _fCur, PropertyInfo _pMax, PropertyInfo _pCur,
                              MethodInfo force2, MethodInfo set1, MethodInfo set2, MethodInfo setCur1, MethodInfo reset0,
                              MethodInfo onChanged, MethodInfo refresh, MethodInfo update, MethodInfo sync,
                              Type _maxType, Type _curType)
        {
            host = h;
            fMax = _fMax; fCur = _fCur; pMax = _pMax; pCur = _pCur;
            mForceSetMax_2 = force2; mSetMax_1 = set1; mSetMax_2 = set2; mSetCur_1 = setCur1; mResetToMax_0 = reset0;
            mOnHealthChanged_0 = onChanged; mRefreshUI_0 = refresh; mUpdateUI_0 = update; mSyncUI_0 = sync;
            maxType = _maxType; curType = _curType;
        }

        public static IHealthAdapter TryCreate(GameObject go, bool logs)
        {
            // Prefer likely components first
            string[] typeHints = { "EnemyController", "Health", "EnemyHealth", "BossEnemy", "CharacterHealth", "HitPoints", "Stats", "Boss" };

            Component chosen = null;
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                var n = c.GetType().Name;
                for (int i = 0; i < typeHints.Length; i++)
                    if (n.IndexOf(typeHints[i], StringComparison.OrdinalIgnoreCase) >= 0) { chosen = c; break; }
                if (chosen != null) break;
            }
            // If nothing obvious, try the first non-null component
            if (chosen == null)
            {
                foreach (var c in go.GetComponents<Component>()) { if (c != null) { chosen = c; break; } }
            }
            if (chosen == null)
            {
                return new HealthAdapter(null, null, null, null, null, null, null, null, null, null,
                                         null, null, null, null, null, null);
            }

            var t = chosen.GetType();

            // Field/prop names to probe
            string[] maxNames = { "maxHealth", "MaxHealth", "maxHP", "MaxHP", "hpMax", "HPMax", "HealthMax", "TotalHealth", "Max" };
            string[] curNames = { "currentHealth", "CurrentHealth", "health", "Health", "hp", "HP", "Current", "Value" };

            FieldInfo _fMax = null, _fCur = null;
            PropertyInfo _pMax = null, _pCur = null;
            Type _maxType = null, _curType = null;

            foreach (var n in maxNames)
            {
                _pMax ??= t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _fMax ??= t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            foreach (var n in curNames)
            {
                _pCur ??= t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _fCur ??= t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            if (_pMax != null) _maxType = _pMax.PropertyType; else if (_fMax != null) _maxType = _fMax.FieldType;
            if (_pCur != null) _curType = _pCur.PropertyType; else if (_fCur != null) _curType = _fCur.FieldType;

            // Methods
            MethodInfo force2 = null, set1 = null, set2 = null, setCur1 = null, reset0 = null;

            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var pars = m.GetParameters();

                // EnemyController-specific: ForceSetMaxHealth(value,bool keepPercent)
                if (m.Name.Equals("ForceSetMaxHealth", StringComparison.OrdinalIgnoreCase) &&
                    pars.Length == 2 &&
                    (pars[0].ParameterType == typeof(float) || pars[0].ParameterType == typeof(int)) &&
                    pars[1].ParameterType == typeof(bool))
                {
                    force2 ??= m;
                    continue;
                }

                // Common SetMaxHealth overloads
                if (m.Name.Equals("SetMaxHealth", StringComparison.OrdinalIgnoreCase))
                {
                    if (pars.Length == 1 && (pars[0].ParameterType == typeof(float) || pars[0].ParameterType == typeof(int)))
                        set1 ??= m;
                    if (pars.Length == 2 &&
                        (pars[0].ParameterType == typeof(float) || pars[0].ParameterType == typeof(int)) &&
                        pars[1].ParameterType == typeof(bool))
                        set2 ??= m;
                    continue;
                }

                // Set current helpers
                if ((m.Name.Equals("SetHealth", StringComparison.OrdinalIgnoreCase) ||
                     m.Name.Equals("SetHP", StringComparison.OrdinalIgnoreCase) ||
                     m.Name.Equals("SetCurrentHealth", StringComparison.OrdinalIgnoreCase) ||
                     m.Name.Equals("SetCurrentHP", StringComparison.OrdinalIgnoreCase)) &&
                     pars.Length == 1 &&
                     (pars[0].ParameterType == typeof(float) || pars[0].ParameterType == typeof(int)))
                {
                    setCur1 ??= m;
                    continue;
                }

                // Refill helpers (no params)
                if ((m.Name.Equals("ResetToMax", StringComparison.OrdinalIgnoreCase) ||
                     m.Name.Equals("Refill", StringComparison.OrdinalIgnoreCase) ||
                     m.Name.Equals("RestoreToFull", StringComparison.OrdinalIgnoreCase) ||
                     m.Name.Equals("RegenToFull", StringComparison.OrdinalIgnoreCase) ||
                     m.Name.Equals("FillToMax", StringComparison.OrdinalIgnoreCase) ||
                     m.Name.Equals("HealFull", StringComparison.OrdinalIgnoreCase)) &&
                    pars.Length == 0)
                {
                    reset0 ??= m;
                    continue;
                }
            }

            // UI hooks (no params)
            MethodInfo onChanged = t.GetMethod("OnHealthChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            MethodInfo refresh = t.GetMethod("RefreshUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            MethodInfo update = t.GetMethod("UpdateUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            MethodInfo sync = t.GetMethod("SyncUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);

            return new HealthAdapter(chosen, _fMax, _fCur, _pMax, _pCur, force2, set1, set2, setCur1, reset0,
                                     onChanged, refresh, update, sync, _maxType, _curType);
        }

        public void SetMaxHealth(float value, bool fillCurrent, bool log)
        {
            if (host == null) return;

            // 1) Prefer EnemyController's ForceSetMaxHealth(value, keepPercent)
            if (mForceSetMax_2 != null)
            {
                object v = Cast(value, mForceSetMax_2.GetParameters()[0].ParameterType);
                // keepPercent = !fillCurrent  (refill -> false)
                mForceSetMax_2.Invoke(host, new object[] { v, !fillCurrent });
                if (log) Debug.Log($"[MiniBoss] HealthAdapter: ForceSetMaxHealth({v}, keepPercent={!fillCurrent}) on {host.GetType().Name}");
                return;
            }

            // 2) Generic SetMaxHealth overloads
            if (mSetMax_2 != null)
            {
                object v = Cast(value, mSetMax_2.GetParameters()[0].ParameterType);
                mSetMax_2.Invoke(host, new object[] { v, fillCurrent });
                if (log) Debug.Log($"[MiniBoss] HealthAdapter: SetMaxHealth({v},{fillCurrent}) on {host.GetType().Name}");
                return;
            }
            if (mSetMax_1 != null)
            {
                object v = Cast(value, mSetMax_1.GetParameters()[0].ParameterType);
                mSetMax_1.Invoke(host, new object[] { v });
                if (fillCurrent)
                {
                    if (mSetCur_1 != null)
                    {
                        object cv = Cast(value, mSetCur_1.GetParameters()[0].ParameterType);
                        mSetCur_1.Invoke(host, new object[] { cv });
                    }
                    else if (!TryWrite(pCur, fCur, value, curType) && mResetToMax_0 != null)
                    {
                        mResetToMax_0.Invoke(host, null);
                    }
                }
                if (log) Debug.Log($"[MiniBoss] HealthAdapter: SetMaxHealth({v}) + fillCurrent={fillCurrent}");
                return;
            }

            // 3) Fields/Properties fallback
            bool wroteMax = TryWrite(pMax, fMax, value, maxType);
            if (fillCurrent)
            {
                if (!TryWrite(pCur, fCur, value, curType) && mResetToMax_0 != null)
                    mResetToMax_0.Invoke(host, null);
            }
            if (log && wroteMax) Debug.Log($"[MiniBoss] HealthAdapter: wrote max via {(pMax != null ? "property" : "field")} on {host.GetType().Name}");
        }

        public void NotifyUI(bool log)
        {
            try
            {
                if (mOnHealthChanged_0 != null) { mOnHealthChanged_0.Invoke(host, null); return; }
                if (mRefreshUI_0 != null) { mRefreshUI_0.Invoke(host, null); return; }
                if (mUpdateUI_0 != null) { mUpdateUI_0.Invoke(host, null); return; }
                if (mSyncUI_0 != null) { mSyncUI_0.Invoke(host, null); return; }
            }
            catch (Exception e)
            {
                if (log) Debug.LogWarning($"[MiniBoss] UI notify failed on {host.GetType().Name}: {e.Message}");
            }
        }

        // ---- helpers ----
        private static object Cast(float value, Type t)
        {
            return (t == typeof(int)) ? (object)Mathf.RoundToInt(value) : value;
        }

        private bool TryWrite(PropertyInfo p, FieldInfo f, float value, Type t)
        {
            try
            {
                if (p != null && p.CanWrite)
                {
                    object v = (t == typeof(int)) ? (object)Mathf.RoundToInt(value) : value;
                    p.SetValue(host, v, null);
                    return true;
                }
                if (f != null)
                {
                    object v = (t == typeof(int)) ? (object)Mathf.RoundToInt(value) : value;
                    f.SetValue(host, v);
                    return true;
                }
            }
            catch { /* swallow and report false */ }
            return false;
        }
    }

    // ----------------- Loot Adapter -----------------
    public interface ILootAdapter
    {
        bool IsValid { get; }
        void SpawnScaledLoot(Vector3 pos, float multiplier, bool log);
        void GrantCurrencyBonuses(int coins, int materials, bool log);
    }

    class LootAdapter : ILootAdapter
    {
        private readonly Component lootManager;     // optional singleton style
        private readonly Component lootDropper;     // optional component on this object
        private readonly Component gameManager;     // for currency bonuses

        // Cached method infos
        private readonly MethodInfo mLM_Spawn_1F;   // SpawnLoot(Vector3, float)
        private readonly MethodInfo mLM_Spawn_1;    // SpawnLoot(Vector3)
        private readonly MethodInfo mLD_Drop_1F;    // DropLoot(float)
        private readonly MethodInfo mLD_Drop_0;     // DropLoot()

        private readonly FieldInfo fGM_Coins, fGM_Materials;
        private readonly PropertyInfo pGM_Coins, pGM_Materials;

        public bool IsValid =>
            lootManager != null || lootDropper != null || gameManager != null;

        private LootAdapter(Component lm, Component ld, Component gm,
                            MethodInfo lmSpawn1F, MethodInfo lmSpawn1,
                            MethodInfo ldDrop1F, MethodInfo ldDrop0,
                            FieldInfo coinsF, FieldInfo matsF, PropertyInfo coinsP, PropertyInfo matsP)
        {
            lootManager = lm; lootDropper = ld; gameManager = gm;
            mLM_Spawn_1F = lmSpawn1F; mLM_Spawn_1 = lmSpawn1;
            mLD_Drop_1F = ldDrop1F; mLD_Drop_0 = ldDrop0;
            fGM_Coins = coinsF; fGM_Materials = matsF;
            pGM_Coins = coinsP; pGM_Materials = matsP;
        }

        public static ILootAdapter TryCreate(GameObject go, bool logs)
        {
            Component lm = null, ld = null, gm = null;
            MethodInfo lmSpawn1F = null, lmSpawn1 = null, ldDrop1F = null, ldDrop0 = null;
            FieldInfo coinsF = null, matsF = null; PropertyInfo coinsP = null, matsP = null;

            // Find a LootManager-like Component type (must derive from Component)
            var lootManagerType = FindComponentTypeByPartialName(new[] { "LootManager", "DropManager", "ItemDropManager" });
            if (lootManagerType != null)
            {
                // Try static Instance
                var propInst = lootManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                if (propInst != null) lm = propInst.GetValue(null, null) as Component;

                if (lm == null)
                {
                    // fallback: find any component of this type in scene
                    lm = UnityEngine.Object.FindObjectOfType(lootManagerType) as Component;
                }

                if (lm != null)
                {
                    foreach (var m in lootManagerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        var pars = m.GetParameters();
                        if (m.Name.Equals("SpawnLoot", StringComparison.OrdinalIgnoreCase))
                        {
                            if (pars.Length == 2 && pars[0].ParameterType == typeof(Vector3) &&
                                (pars[1].ParameterType == typeof(float) || pars[1].ParameterType == typeof(int)))
                                lmSpawn1F ??= m;

                            if (pars.Length == 1 && pars[0].ParameterType == typeof(Vector3))
                                lmSpawn1 ??= m;
                        }
                    }
                }
            }

            // Find a LootDropper-like component on this object (must derive from Component)
            var lootDropperType = FindComponentTypeByPartialName(new[] { "LootDropper", "Dropper", "ItemDropper" });
            if (lootDropperType != null)
            {
                ld = go.GetComponent(lootDropperType);
                if (ld != null)
                {
                    var t = ld.GetType();
                    foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        var pars = m.GetParameters();
                        if (m.Name.Equals("DropLoot", StringComparison.OrdinalIgnoreCase))
                        {
                            if (pars.Length == 1 && (pars[0].ParameterType == typeof(float) || pars[0].ParameterType == typeof(int)))
                                ldDrop1F ??= m;
                            if (pars.Length == 0)
                                ldDrop0 ??= m;
                        }
                    }
                }
            }

            // GameManager for coins/materials (Component only)
            var gmType = FindComponentTypeByPartialName(new[] { "GameManager" });
            if (gmType != null)
            {
                var gmInstProp = gmType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                if (gmInstProp != null) gm = gmInstProp.GetValue(null, null) as Component;
                if (gm == null) gm = UnityEngine.Object.FindObjectOfType(gmType) as Component;

                if (gm != null)
                {
                    coinsP = gmType.GetProperty("coins", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    matsP = gmType.GetProperty("materials", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    coinsF = gmType.GetField("coins", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    matsF = gmType.GetField("materials", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
            }

            return new LootAdapter(lm, ld, gm, lmSpawn1F, lmSpawn1, ldDrop1F, ldDrop0, coinsF, matsF, coinsP, matsP);
        }

        public void SpawnScaledLoot(Vector3 pos, float multiplier, bool log)
        {
            // 1) LootManager route
            if (lootManager != null)
            {
                if (mLM_Spawn_1F != null)
                {
                    object v = (mLM_Spawn_1F.GetParameters()[1].ParameterType == typeof(int))
                        ? (object)Mathf.RoundToInt(multiplier)
                        : multiplier;
                    mLM_Spawn_1F.Invoke(lootManager, new object[] { pos, v });
                    if (log) Debug.Log($"[MiniBoss] LootAdapter: LootManager.SpawnLoot(pos, {v})");
                    return;
                }
                if (mLM_Spawn_1 != null)
                {
                    // Call once per multiplier round to simulate scaling with multiple rolls
                    int rolls = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(1f, multiplier)));
                    for (int i = 0; i < rolls; i++) mLM_Spawn_1.Invoke(lootManager, new object[] { pos });
                    if (log) Debug.Log($"[MiniBoss] LootAdapter: LootManager.SpawnLoot(pos) x{rolls}");
                    return;
                }
            }

            // 2) LootDropper route
            if (lootDropper != null)
            {
                if (mLD_Drop_1F != null)
                {
                    object v = (mLD_Drop_1F.GetParameters()[0].ParameterType == typeof(int))
                        ? (object)Mathf.RoundToInt(multiplier)
                        : multiplier;
                    mLD_Drop_1F.Invoke(lootDropper, new object[] { v });
                    if (log) Debug.Log($"[MiniBoss] LootAdapter: LootDropper.DropLoot({v})");
                    return;
                }
                if (mLD_Drop_0 != null)
                {
                    int rolls = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(1f, multiplier)));
                    for (int i = 0; i < rolls; i++) mLD_Drop_0.Invoke(lootDropper, null);
                    if (log) Debug.Log($"[MiniBoss] LootAdapter: LootDropper.DropLoot() x{rolls}");
                    return;
                }
            }

            // 3) No structured loot system found -> no-op here
        }

        public void GrantCurrencyBonuses(int coins, int materials, bool log)
        {
            if (gameManager == null) return;

            try
            {
                if (coins != 0)
                {
                    if (pGM_Coins != null && pGM_Coins.CanWrite)
                    {
                        int cur = (int)(pGM_Coins.GetValue(gameManager, null) ?? 0);
                        pGM_Coins.SetValue(gameManager, cur + coins, null);
                    }
                    else if (fGM_Coins != null)
                    {
                        int cur = (int)(fGM_Coins.GetValue(gameManager) ?? 0);
                        fGM_Coins.SetValue(gameManager, cur + coins);
                    }
                }
                if (materials != 0)
                {
                    if (pGM_Materials != null && pGM_Materials.CanWrite)
                    {
                        int cur = (int)(pGM_Materials.GetValue(gameManager, null) ?? 0);
                        pGM_Materials.SetValue(gameManager, cur + materials, null);
                    }
                    else if (fGM_Materials != null)
                    {
                        int cur = (int)(fGM_Materials.GetValue(gameManager) ?? 0);
                        fGM_Materials.SetValue(gameManager, cur + materials);
                    }
                }

                if (log) Debug.Log($"[MiniBoss] LootAdapter: +coins={coins}, +materials={materials}");
            }
            catch (Exception e)
            {
                if (log) Debug.LogWarning($"[MiniBoss] LootAdapter currency bonus failed: {e.Message}");
            }
        }

        // Return a type whose name contains any of the tokens AND derives from UnityEngine.Component.
        private static Type FindComponentTypeByPartialName(string[] names)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types = null;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types)
                {
                    if (!typeof(Component).IsAssignableFrom(t)) continue; // important: must be a Component
                    var tn = t.Name;
                    for (int i = 0; i < names.Length; i++)
                    {
                        if (tn.IndexOf(names[i], StringComparison.OrdinalIgnoreCase) >= 0)
                            return t;
                    }
                }
            }
            return null;
        }
    }
}

// Persistent tracker for health pickups that survives mini boss destruction
public class MiniBossLootTracker : MonoBehaviour
{
    public System.Collections.IEnumerator ApplyDelayedPhysics(Rigidbody rb, Vector3 force)
    {
        if (rb == null) yield break;
        
        // Wait 3 frames to let everything initialize
        yield return null;
        yield return null;
        yield return null;
        
        if (rb != null && rb.gameObject != null)
        {
            rb.useGravity = true;
            rb.AddForce(force, ForceMode.Impulse);
            Debug.Log($"[MiniBossTracker] Applied delayed physics to {rb.gameObject.name}");
        }
    }
    
    public System.Collections.IEnumerator CheckHealthPickupExists(GameObject item, Vector3 spawnPos)
    {
        if (item == null)
        {
            Debug.LogError($"[MiniBossTracker] ⚠️ Health pickup was NULL immediately after spawn! Position: {spawnPos}");
            yield break;
        }
        
        Debug.Log($"[MiniBossTracker] CheckHealthPickupExists coroutine STARTED for {item.name}", item);
        
        int frame = 0;
        while (frame < 180) // Check for 3 seconds (180 frames at 60fps)
        {
            yield return null;
            frame++;
            
            if (item == null)
            {
                Debug.LogError($"[MiniBossTracker] ⚠️⚠️⚠️ HEALTH PICKUP DESTROYED at frame {frame}! " +
                             $"Original spawn position: {spawnPos}. " +
                             $"Something is destroying health pickups after {frame} frames!");
                yield break;
            }
            
            if (!item.activeInHierarchy)
            {
                Debug.LogError($"[MiniBossTracker] ⚠️⚠️⚠️ HEALTH PICKUP DEACTIVATED at frame {frame}! " +
                             $"Position: {item.transform.position}. " +
                             $"It exists but was set inactive!");
                yield break;
            }
            
            if (frame == 1)
            {
                Debug.Log($"[MiniBossTracker] ✓ Frame 1: Health pickup still exists: {item.name} at {item.transform.position}, active={item.activeInHierarchy}", item);
            }
            else if (frame == 3)
            {
                Debug.Log($"[MiniBossTracker] ✓ Frame 3: Health pickup still exists: {item.name} at {item.transform.position}, active={item.activeInHierarchy}", item);
            }
            else if (frame == 10)
            {
                Debug.Log($"[MiniBossTracker] ✓ Frame 10: Health pickup still exists: {item.name} at {item.transform.position}, active={item.activeInHierarchy}", item);
            }
            else if (frame == 30)
            {
                Debug.Log($"[MiniBossTracker] ✓ Frame 30 (0.5s): Health pickup still exists: {item.name} at {item.transform.position}, active={item.activeInHierarchy}", item);
            }
            else if (frame == 60)
            {
                Debug.Log($"[MiniBossTracker] ✓ Frame 60 (1s): Health pickup still exists and visible! {item.name} at {item.transform.position}", item);
            }
            else if (frame == 120)
            {
                Debug.Log($"[MiniBossTracker] ✓ Frame 120 (2s): Health pickup still exists! {item.name} at {item.transform.position}", item);
            }
        }
        
        if (item != null)
        {
            Debug.Log($"[MiniBossTracker] ✓✓✓ SUCCESS! Health pickup survived 3 FULL SECONDS! {item.name} at {item.transform.position}", item);
            
            // Check if it's visible
            var renderer = item.GetComponent<Renderer>();
            if (renderer != null)
            {
                Debug.Log($"[MiniBossTracker] Renderer status: enabled={renderer.enabled}, visible={renderer.isVisible}");
            }
            else
            {
                Debug.LogWarning($"[MiniBossTracker] Health pickup has NO RENDERER - that's why you can't see it!");
            }
        }
    }
}
