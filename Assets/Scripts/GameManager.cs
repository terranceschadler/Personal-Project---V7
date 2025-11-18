using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems; // UI selection clear

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem; // harmless if define exists (we don't read from it for pause)
#endif

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    // ---------- Currency ----------
    [Header("Currency")] public int coins = 0;

    // ---------- Materials ----------
    [Header("Materials")] public int materials = 0;

    // ---------- Run Stats ----------
    [Header("Run Stats")]
    [SerializeField] private int score = 0;
    public int Score { get { return score; } }

    [SerializeField] private int currentWave = 1;
    public int CurrentWave { get { return currentWave; } }

    // ---------- Combat / Kill Stats ----------
    [Header("Kill Stats")]
    [SerializeField] private int enemiesKilled = 0;      // total enemies killed (bosses included)
    public int EnemiesKilled { get { return enemiesKilled; } }

    // ---------- Boss Kills ----------
    [Header("Boss Kills")]
    [SerializeField] private int bossKillCount = 0;
    public int BossKillCount { get { return bossKillCount; } }

    // ---------- Friendlies Rescued ----------
    [Header("Friendly Rescues")]
    [SerializeField] private int friendliesRescued = 0;
    public int FriendliesRescued { get { return friendliesRescued; } }

    // ---------- Boss Weapon Drops ----------
    [Header("Boss Weapon Drop Prefabs (Array)")]
    [Tooltip("Assign any number of weapon drop prefabs here. A random one will be chosen on boss death when the drop roll passes.")]
    public GameObject[] bossWeaponDropPrefabs;

    // Legacy (optional; kept for backward compatibility)
    [Header("Boss Weapon Drop Prefabs (Legacy - optional)")]
    public GameObject shotgunDropPrefab;
    public GameObject machinegunDropPrefab;
    public GameObject rocketDropPrefab;

    // ---------- Boss Drop Randomization ----------
    [Header("Boss Drop Randomization")]
    [Range(0f, 1f)] public float weaponDropChance = 0.6f;
    [Range(0f, 1f)] public float helicopterPartDropChance = 0.75f;
    public Vector3 secondDropOffset = new Vector3(0.75f, 0f, 0.75f);

    // Max Health pickup
    [Header("Boss Max Health Pickup")]
    public GameObject maxHealthPickupPrefab;
    [Range(0f, 1f)] public float maxHealthPickupDropChance = 0.35f;
    public Vector3 maxHealthPickupOffset = new Vector3(-0.75f, 0f, -0.75f);

    // Weapon Upgrade pickups (tiered by quality)
    [Header("Boss Weapon Upgrade Pickups")]
    [Tooltip("Epic quality upgrade prefab - drops from full bosses (highest tier).")]
    public GameObject epicWeaponUpgradePrefab;
    [Tooltip("Rare quality upgrade prefab - for mini bosses (middle tier).")]
    public GameObject rareWeaponUpgradePrefab;
    [Range(0f, 1f)] public float weaponUpgradeDropChance = 0.7f;
    public Vector3 weaponUpgradeOffset = new Vector3(0f, 0f, -0.75f);

    // ---------- Helicopter Parts ----------
    [Header("Helicopter Parts Drops")]
    public GameObject helicopterPartPrefab;    // fallback
    public GameObject[] helicopterPartPrefabs; // unique sequence
    private int _helicopterPartIndex = 0;

    [Header("Helicopter Parts Inventory")]
    [SerializeField] private List<string> collectedHelicopterParts = new List<string>();
    public IReadOnlyList<string> CollectedHelicopterParts { get { return collectedHelicopterParts.AsReadOnly(); } }

    [Header("Helicopter Parts Requirement")]
    [Tooltip("How many unique parts are needed to complete the helicopter.")]
    [SerializeField] private int requiredHelicopterParts = 4;

    /// <summary>Raised when the (collected, required) parts numbers change.</summary>
    public event Action<int, int> OnHelicopterProgressChanged;
    public event Action<string> OnHelicopterPartCollected;

    [Header("Weapon Reset")]
    [Tooltip("Automatically reset player weapons when level restarts")]
    public bool resetWeaponsOnLevelRestart = true;

    // =========================
    // BOSS HEALTH SCALING
    // =========================
    public enum BossHealthScalingMode { Additive, Multiplicative, Curve }

    [Header("Boss Health Scaling")]
    public BossHealthScalingMode bossHealthScalingMode = BossHealthScalingMode.Multiplicative;
    public float bossBaseHealthOverride = 0f;
    public float bossHealthAddPerBoss = 150f;
    public float bossHealthMulPerBoss = 1.20f;
    public float bossHealthMaxMultiplierClamp = 0f;
    public AnimationCurve bossHealthCurve = AnimationCurve.Linear(0f, 1f, 5f, 2.5f);

    [Tooltip("If true, the very first boss of each run will be forced to 'firstBossHealth' regardless of template/override.")]
    public bool forceFirstBossHealthEachRun = true;

    [Tooltip("Default HP for the first boss in a run (used when 'forceFirstBossHealthEachRun' is true).")]
    public float firstBossHealth = 500f;

    public float GetBossHealthForSpawnOrder(int order1Based, float templateMaxHealth)
    {
        int n = Mathf.Max(1, order1Based);
        float baseH = (bossBaseHealthOverride > 0f) ? bossBaseHealthOverride : Mathf.Max(1f, templateMaxHealth);

        if (order1Based == 1 && forceFirstBossHealthEachRun && firstBossHealth > 0f)
            baseH = firstBossHealth;

        float finalHealth;
        switch (bossHealthScalingMode)
        {
            case BossHealthScalingMode.Additive:
                finalHealth = Mathf.Max(1f, baseH + bossHealthAddPerBoss * (n - 1));
                break;
            case BossHealthScalingMode.Multiplicative:
                float mul = Mathf.Max(0.01f, bossHealthMulPerBoss);
                float factor = Mathf.Pow(mul, n - 1);
                if (bossHealthMaxMultiplierClamp > 0f) factor = Mathf.Min(factor, bossHealthMaxMultiplierClamp);
                finalHealth = Mathf.Max(1f, baseH * factor);
                break;
            case BossHealthScalingMode.Curve:
                float y = bossHealthCurve.Evaluate(n - 1);
                if (y <= 0f) y = 0.01f;
                finalHealth = Mathf.Max(1f, baseH * y);
                break;
            default:
                finalHealth = Mathf.Max(1f, baseH);
                break;
        }

        // Full bosses have 20x the calculated health
        finalHealth *= 20f;

        return finalHealth;
    }

    public float GetNextBossHealth(float templateMaxHealth)
    {
        int nextOrder = bossKillCount + 1;
        return GetBossHealthForSpawnOrder(nextOrder, templateMaxHealth);
    }

    public void ApplyBossScalingTo(GameObject bossGO)
    {
        if (!bossGO) return;
        var ec = bossGO.GetComponent<EnemyController>();
        if (!ec) { Debug.LogWarning("[GameManager] ApplyBossScalingTo: No EnemyController on boss."); return; }
        float template = Mathf.Max(1f, ec.maxHealth);
        float scaled = GetBossHealthForSpawnOrder(bossKillCount + 1, template);
        ec.ForceSetMaxHealth(scaled, keepPercent: false);
        bossGO.SendMessage("OnBossHealthScaled", scaled, SendMessageOptions.DontRequireReceiver);
    }

    // ---------- Boss Engagement Order ----------
    private int _nextBossEngagementOrder = 1; // 1-based
    private readonly Dictionary<int, int> _bossOrderByInstance = new Dictionary<int, int>();
    private readonly HashSet<int> _bossScaledOnce = new HashSet<int>();

    public int GetBossEngagementOrder(GameObject bossGO)
    {
        if (!bossGO) return 0;
        int order;
        return _bossOrderByInstance.TryGetValue(bossGO.GetInstanceID(), out order) ? order : 0;
    }

    private int EnsureBossOrder(GameObject bossGO)
    {
        int id = bossGO.GetInstanceID();
        int existing;
        if (_bossOrderByInstance.TryGetValue(id, out existing)) return existing;
        int order = _nextBossEngagementOrder++;
        _bossOrderByInstance[id] = order;
        return order;
    }

    public void TryScaleBossOnFirstEngage(EnemyController ec)
    {
        if (ec == null) return;
        var bossGO = ec.gameObject;
        int id = bossGO.GetInstanceID();
        if (_bossScaledOnce.Contains(id)) return;

        int order = EnsureBossOrder(bossGO);
        float template = Mathf.Max(1f, ec.GetMaxHealth());
        float scaled = GetBossHealthForSpawnOrder(order, template);

        ec.ForceSetMaxHealth(scaled, keepPercent: true);
        _bossScaledOnce.Add(id);
        bossGO.SendMessage("OnBossHealthScaled", scaled, SendMessageOptions.DontRequireReceiver);
    }

    // ---------- Pause / Input ----------
    [Header("Pause (ESC toggle, no UI)")]
    public bool togglePauseOnEsc = true;
    public bool autoDisableEscIfPauseUI = true;
    public KeyCode pauseKey = KeyCode.Escape;
    public KeyCode panicUnpauseKey = KeyCode.F10;
    public bool gameplayCursorVisible = false;
    public CursorLockMode gameplayCursorLockMode = CursorLockMode.Locked;
    public bool pauseCursorVisible = true;
    public CursorLockMode pauseCursorLockMode = CursorLockMode.None;
    public bool enforcePauseWatchdog = true;
    public bool IsPaused { get; private set; }

    // ===== External Pause Locks =====
    [NonSerialized] public int ExternalPauseLocks = 0;
    public void PushExternalPause() { ExternalPauseLocks++; Debug.Log($"[GM] PushExternalPause -> {ExternalPauseLocks}"); }
    public void PopExternalPause() { ExternalPauseLocks = Mathf.Max(0, ExternalPauseLocks - 1); Debug.Log($"[GM] PopExternalPause -> {ExternalPauseLocks}"); }
    public bool IsActuallyPaused { get { return IsPaused || ExternalPauseLocks > 0; } }

    // ---------- Wave System ----------
    public enum WaveAdvanceMode { ByKills, ByTimer, Manual }
    [Header("Wave System")]
    public WaveAdvanceMode waveAdvanceMode = WaveAdvanceMode.ByKills;
    public bool autoAdvanceWaves = true;
    [Header("ByKills Settings")] public int baseKillsPerWave = 10; public int killsPerWaveGrowth = 5;
    [Header("ByTimer Settings")] public float baseWaveDuration = 60f; public float waveDurationGrowth = 10f;
    [Header("Inter-wave")] public float interWaveDelay = 6f; public bool requireClearBoardToEndWave = true;

    // >>>>>>>>>> Concurrent enemy ramp + batched requests <<<<<<<<<<
    [Header("Concurrent Enemy Ramp")]
    [Tooltip("Minimum concurrent enemies at start of a wave.")]
    public int minConcurrentEnemies = 12;

    [Tooltip("Maximum concurrent enemies by the end of a wave.")]
    public int maxConcurrentEnemies = 80;

    [Tooltip("Delay after wave start before ramping begins (seconds).")]
    [Min(0f)] public float rampStartDelay = 0f;

    [Tooltip("0..1 -> 0..1 curve controlling how quickly we move from min to max within a wave.")]
    public AnimationCurve concurrentRamp = AnimationCurve.Linear(0, 0, 1, 1);

    [Tooltip("How often we re-check and request top-ups (seconds).")]
    [Min(0.05f)] public float topUpCheckInterval = 0.25f;

    [Tooltip("If true, prints desired/alive/needed numbers when top-ups are requested.")]
    public bool logTopUps = false;

    [Header("Top-Up Batching")]
    [Tooltip("Max enemies requested per heartbeat top-up.")]
    public int topUpBatchSize = 5;

    [Header("Wave Start Burst")]
    [Tooltip("How many enemies to request immediately when a wave begins.")]
    public int initialSpawnBurst = 25;

    public int EnemiesAlive { get; private set; }
    public int EnemiesKilledThisWave { get; private set; }

    /// <summary>Current desired concurrent enemies for this wave tick (after ramp).</summary>
    public int DesiredConcurrentEnemies { get; private set; }

    /// <summary>Raised when the GM wants enemies spawned now. Args: requestCount, desiredConcurrent, currentlyAlive.</summary>
    public event Action<int, int, int> OnTopUpNeeded;

    private float _waveTimeRemaining = 0f;
    private float _waveElapsed = 0f;
    private float _topUpTimer = 0f;

    private bool _inInterWave = false;
    private Coroutine _interWaveRoutine;

    // ---------- Events ----------
    public event Action<int> OnCoinsChanged;
    public event Action<int> OnMaterialsChanged;
    public event Action<int> OnScoreChanged;
    public event Action<int> OnWaveChanged;
    public event Action<int> OnEnemiesKilledChanged;
    public event Action<int> OnBossKillsChanged;
    public event Action<int> OnFriendliesRescuedChanged;
    public event Action<int> OnWaveStarted;
    public event Action<int> OnWaveEnded;
    public event Action<float> OnInterWaveCountdown;

    // ---------- Internals ----------
    private float _runSeconds = 0f;
    private bool _runActive = true;
    private int _lastBossKillFrame = -1;

    private bool _pendingResetOnSceneLoad = false;

    // ---------- Diagnostics ----------
    [Header("Diagnostics")]
    [Tooltip("Enable verbose GameManager logs in Console (info-level). Warnings/errors always show.")]
    public bool debugLogs = true;

    // ---------- AXIS RESET MODES (Old Input Manager) ----------
    public enum AxisResetMode { Off, ManualKeyOnly, OnFocusIdleOnly }

    [Header("Legacy Input: Axis Reset")]
    public AxisResetMode axisResetMode = AxisResetMode.Off;

    [Tooltip("Manual axis reset hotkey (only when AxisResetMode = ManualKeyOnly).")]
    public KeyCode manualAxisResetKey = KeyCode.F9;

    [Tooltip("Idle time (seconds) after regaining app focus before resetting axes (AxisResetMode = OnFocusIdleOnly).")]
    [Range(0.25f, 5f)] public float focusIdleSeconds = 2f;

    [Tooltip("Minimum seconds between any two axis resets.")]
    public float axisResetCooldown = 10f;

    private float _lastAxisResetAt = -999f;

    // Helpers
    private void DLog(string msg) { if (debugLogs) Debug.Log(msg); }
    private void DLogFormat(string fmt, params object[] args) { if (debugLogs) Debug.LogFormat(fmt, args); }

    public float RunSeconds { get { return _runSeconds; } }
    public float MinutesSurvived { get { return _runSeconds / 60f; } }

    // ===== Safe emit helpers =====
    private void EmitInt(Action<int> evt, int value, string name)
    {
        if (evt == null) { if (debugLogs) Debug.Log("[GM] " + name + ": no listeners"); return; }
        if (debugLogs) Debug.Log("[GM] " + name + ": " + evt.GetInvocationList().Length + " listeners -> " + value);
        foreach (Delegate d in evt.GetInvocationList())
        {
            try { ((Action<int>)d)(value); }
            catch (Exception ex) { Debug.LogError("[GM] Listener threw in " + name + ". Target=" + d.Target + " Method=" + d.Method.Name + "\n" + ex); }
        }
    }
    private void EmitFloat(Action<float> evt, float value, string name)
    {
        if (evt == null) { if (debugLogs) Debug.Log("[GM] " + name + ": no listeners"); return; }
        if (debugLogs) Debug.Log("[GM] " + name + ": " + evt.GetInvocationList().Length + " listeners -> " + value);
        foreach (Delegate d in evt.GetInvocationList())
        {
            try { ((Action<float>)d)(value); }
            catch (Exception ex) { Debug.LogError("[GM] Listener threw in " + name + ". Target=" + d.Target + " Method=" + d.Method.Name + "\n" + ex); }
        }
    }
    private void Emit2(Action<int, int> evt, int a, int b, string name)
    {
        if (evt == null) { if (debugLogs) Debug.Log("[GM] " + name + ": no listeners"); return; }
        if (debugLogs) Debug.Log("[GM] " + name + ": " + evt.GetInvocationList().Length + " listeners -> (" + a + "," + b + ")");
        foreach (Delegate d in evt.GetInvocationList())
        {
            try { ((Action<int, int>)d)(a, b); }
            catch (Exception ex) { Debug.LogError("[GM] Listener threw in " + name + ". Target=" + d.Target + " Method=" + d.Method.Name + "\n" + ex); }
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (bossWeaponDropPrefabs == null || bossWeaponDropPrefabs.Length == 0)
        {
            List<GameObject> legacy = new List<GameObject>(3);
            if (shotgunDropPrefab) legacy.Add(shotgunDropPrefab);
            if (machinegunDropPrefab) legacy.Add(machinegunDropPrefab);
            if (rocketDropPrefab) legacy.Add(rocketDropPrefab);
            if (legacy.Count > 0) bossWeaponDropPrefabs = legacy.ToArray();
        }

        ValidateHelicopterDropConfig();
        FireHelicopterProgressChanged();
    }

    private void OnEnable() { SceneManager.sceneLoaded += OnSceneLoaded; }
    private void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }

    private void Start()
    {
        AutoDisableEscIfPauseUI();
        HardResumeGameplay();
        if (_runActive && currentWave < 1) currentWave = 1;
    }

    private void Update()
    {
        if (_runActive && !IsActuallyPaused) _runSeconds += Time.deltaTime;

        if (Input.GetKeyDown(panicUnpauseKey)) HardResumeGameplay();

        // Manual axis reset hotkey (debug / emergency)
        if (axisResetMode == AxisResetMode.ManualKeyOnly &&
            Input.GetKeyDown(manualAxisResetKey))
        {
            TryAxisReset("ManualKey");
        }

        if (togglePauseOnEsc && ShouldTogglePause())
        {
            if (IsPaused) ResumeGame(); else PauseGame();
        }

        if (enforcePauseWatchdog)
        {
            if (IsActuallyPaused)
            {
                if (Time.timeScale != 0f) Time.timeScale = 0f;
                if (!AudioListener.pause) AudioListener.pause = true;
                if (Cursor.lockState != pauseCursorLockMode) Cursor.lockState = pauseCursorLockMode;
                if (Cursor.visible != pauseCursorVisible) Cursor.visible = pauseCursorVisible;
            }
            else
            {
                if (Time.timeScale != 1f) Time.timeScale = 1f;
                if (AudioListener.pause) AudioListener.pause = false;
                if (Cursor.lockState != gameplayCursorLockMode) Cursor.lockState = gameplayCursorLockMode;
                if (Cursor.visible != gameplayCursorVisible) Cursor.visible = gameplayCursorVisible;
            }
        }

        // ----- Wave countdown (ByTimer end condition) -----
        if (!_inInterWave && autoAdvanceWaves && waveAdvanceMode == WaveAdvanceMode.ByTimer && _runActive && !IsActuallyPaused)
        {
            _waveTimeRemaining -= Time.unscaledDeltaTime;
            if (_waveTimeRemaining <= 0f) TryEndCurrentWave("timer");
        }

        // ----- ByKills end condition -----
        if (!_inInterWave && autoAdvanceWaves && waveAdvanceMode == WaveAdvanceMode.ByKills && _runActive && !IsActuallyPaused)
        {
            int target = GetKillsRequiredForWave(currentWave);
            bool killsMet = EnemiesKilledThisWave >= target;
            bool clearOk = !requireClearBoardToEndWave || EnemiesAlive == 0;
            if (killsMet && clearOk) TryEndCurrentWave("kills");
        }

        // ----- Concurrent enemy ramp + top-up heartbeat -----
        if (!_inInterWave && _runActive && !IsActuallyPaused)
        {
            // Advance clocks
            _waveElapsed += Time.unscaledDeltaTime;
            _topUpTimer += Time.unscaledDeltaTime;

            // Compute wave progress [0..1]
            float progress01 = 0f;
            if (waveAdvanceMode == WaveAdvanceMode.ByTimer)
            {
                float total = GetDurationForWave(currentWave);
                float elapsed = Mathf.Clamp(total - _waveTimeRemaining, 0f, total);
                progress01 = (total <= 0.0001f) ? 1f : Mathf.Clamp01(elapsed / total);
            }
            else if (waveAdvanceMode == WaveAdvanceMode.ByKills)
            {
                int targetKills = GetKillsRequiredForWave(currentWave);
                progress01 = (targetKills <= 0) ? 1f : Mathf.Clamp01((float)EnemiesKilledThisWave / targetKills);
            }
            else // Manual waves: use elapsed time proxy (2 minutes -> full)
            {
                progress01 = Mathf.InverseLerp(0f, 120f, _waveElapsed);
            }

            // Respect start delay (no ramp until delay passes)
            float rampT = (_waveElapsed < rampStartDelay) ? 0f : Mathf.Clamp01(progress01);

            // Update desired concurrent enemies via curve
            UpdateDesiredConcurrentEnemies(rampT);

            // Periodic top-up request (BATCHED)
            if (_topUpTimer >= topUpCheckInterval)
            {
                _topUpTimer = 0f;
                RequestTopUpBatched();
            }
        }
    }

    private void RequestTopUpBatched()
    {
        if (_inInterWave || IsActuallyPaused) return;

        int deficit = DesiredConcurrentEnemies - EnemiesAlive;
        if (deficit <= 0) return;

        int toRequest = Mathf.Clamp(deficit, 1, Mathf.Max(1, topUpBatchSize));
        if (logTopUps) DLog($"[GM] TopUpNeeded: request={toRequest} (deficit={deficit}) desired={DesiredConcurrentEnemies} alive={EnemiesAlive}");
        try { OnTopUpNeeded?.Invoke(toRequest, DesiredConcurrentEnemies, EnemiesAlive); }
        catch (Exception ex) { Debug.LogError("[GM] OnTopUpNeeded listener error: " + ex); }
    }

    // Old-input-only pause toggle (no Keyboard.current path)
    private bool ShouldTogglePause()
    {
        if (ExternalPauseLocks > 0) return false;
        return Input.GetKeyDown(pauseKey);
    }

    private void AutoDisableEscIfPauseUI()
    {
        if (!autoDisableEscIfPauseUI) return;

        EscPauseUI escUI = null;

#if UNITY_2023_1_OR_NEWER
        // 2023+: can include inactive objects explicitly
        escUI = UnityEngine.Object.FindFirstObjectByType<EscPauseUI>(FindObjectsInactive.Include);
#elif UNITY_2022_2_OR_NEWER
        // 2022.2+: FindFirstObjectByType exists (no includeInactive overload)
        escUI = UnityEngine.Object.FindFirstObjectByType<EscPauseUI>();
#else
        // Legacy API supports includeInactive bool
        escUI = UnityEngine.Object.FindObjectOfType<EscPauseUI>(true);
#endif

        if (escUI != null && togglePauseOnEsc)
            togglePauseOnEsc = false;
    }

    // ========================= Pause / Resume (no UI)
    public void PauseGame()
    {
        IsPaused = true;
        Time.timeScale = 0f;
        AudioListener.pause = true;
        Cursor.lockState = pauseCursorLockMode;
        Cursor.visible = pauseCursorVisible;
    }
    public void ResumeGame()
    {
        IsPaused = false;
        try { if (WinUIController.Instance != null) WinUIController.Instance.Resume(); } catch { }
        Time.timeScale = 1f;
        AudioListener.pause = false;
        Cursor.lockState = gameplayCursorLockMode;
        Cursor.visible = gameplayCursorVisible;
    }
    public void HardResumeGameplay()
    {
        IsPaused = false;
        try { if (WinUIController.Instance != null) WinUIController.Instance.Resume(); } catch { }
        Time.timeScale = 1f;
        AudioListener.pause = false;
        Cursor.lockState = gameplayCursorLockMode;
        Cursor.visible = gameplayCursorVisible;
    }

    // ========================= Restart / Quit
    public void RestartLevel()
    {
        _pendingResetOnSceneLoad = true;
        try { if (WinUIController.Instance != null) WinUIController.Instance.Resume(); } catch { }
        HardResumeGameplay();

        // Clean up scene before reload
        StartCoroutine(RestartLevelCoroutine());
    }

    private IEnumerator RestartLevelCoroutine()
    {
        DLog("[GameManager] Starting level restart...");
        
        // Clean up any lingering objects
        CleanupSceneObjects();
        
        // Wait a frame for cleanup
        yield return null;

        var scene = SceneManager.GetActiveScene();
        DLog("[GameManager] Restarting '" + scene.name + "' (#" + scene.buildIndex + ").");
        SceneManager.LoadScene(scene.buildIndex);
    }

    private void CleanupSceneObjects()
    {
        DLog("[GameManager] Cleaning up scene objects...");
        
        // Clean up any upgrade pickups
        try
        {
            GameObject[] pickups = GameObject.FindGameObjectsWithTag("Pickup");
            foreach (GameObject pickup in pickups)
            {
                if (pickup != null) Destroy(pickup);
            }
        }
        catch { }
        
        // Clean up any lingering enemies
        try
        {
            GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");
            foreach (GameObject enemy in enemies)
            {
                if (enemy != null) Destroy(enemy);
            }
        }
        catch { }
        
        // Clean up any bullets
        try
        {
            GameObject[] bullets = GameObject.FindGameObjectsWithTag("Bullet");
            foreach (GameObject bullet in bullets)
            {
                if (bullet != null) Destroy(bullet);
            }
        }
        catch { }
    }
    public void ConfirmQuitFromUI() { QuitGame(); }
    public void QuitGame()
    {
        EndRun();
        DLog("[GameManager] Quit requested.");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ========================= Coins / Materials
    public void AddCoins(int amount) { if (amount <= 0) return; coins += amount; EmitInt(OnCoinsChanged, coins, nameof(OnCoinsChanged)); }
    public bool SpendCoins(int amount)
    {
        if (amount <= 0) return true;
        if (coins >= amount) { coins -= amount; EmitInt(OnCoinsChanged, coins, nameof(OnCoinsChanged)); return true; }
        return false;
    }
    public void AddMaterials(int amount) { if (amount <= 0) return; materials += amount; EmitInt(OnMaterialsChanged, materials, nameof(OnMaterialsChanged)); }
    public bool SpendMaterials(int amount)
    {
        if (amount <= 0) return true;
        if (materials >= amount) { materials -= amount; EmitInt(OnMaterialsChanged, materials, nameof(OnMaterialsChanged)); return true; }
        return false;
    }

    // ========================= Score / Waves / Run
    public void AddScore(int amount) { if (amount <= 0) return; score += amount; EmitInt(OnScoreChanged, score, nameof(OnScoreChanged)); }
    public void SetScore(int newScore) { score = Mathf.Max(0, newScore); EmitInt(OnScoreChanged, score, nameof(OnScoreChanged)); }
    public void AdvanceWave() { currentWave = Mathf.Max(1, currentWave + 1); EmitInt(OnWaveChanged, currentWave, nameof(OnWaveChanged)); }
    public void SetWave(int waveIndex) { currentWave = Mathf.Max(1, waveIndex); EmitInt(OnWaveChanged, currentWave, nameof(OnWaveChanged)); }

    public void StartRun(bool resetWaveToOne = true, bool resetScore = true)
    {
        _runSeconds = 0f; _runActive = true;
        if (resetWaveToOne) { currentWave = 1; EmitInt(OnWaveChanged, currentWave, nameof(OnWaveChanged)); }
        if (resetScore) { score = 0; EmitInt(OnScoreChanged, score, nameof(OnScoreChanged)); }
        enemiesKilled = 0; EmitInt(OnEnemiesKilledChanged, enemiesKilled, nameof(OnEnemiesKilledChanged));
        bossKillCount = 0; EmitInt(OnBossKillsChanged, bossKillCount, nameof(OnBossKillsChanged));
        friendliesRescued = 0; EmitInt(OnFriendliesRescuedChanged, friendliesRescued, nameof(OnFriendliesRescuedChanged));

        _helicopterPartIndex = 0;
        collectedHelicopterParts.Clear();

        ResetBossEngagementOrder();

        EnemiesAlive = 0;
        EnemiesKilledThisWave = 0;
        _inInterWave = false;
        if (_interWaveRoutine != null) { StopCoroutine(_interWaveRoutine); _interWaveRoutine = null; }

        ValidateHelicopterDropConfig();
        FireHelicopterProgressChanged();

        BeginWave(currentWave);

        RebroadcastAllStats();
    }

    public void EndRun() { _runActive = false; }

    public void ResetRun(bool resetCurrencies = false)
    {
        _runSeconds = 0f; _runActive = true;
        score = 0; EmitInt(OnScoreChanged, score, nameof(OnScoreChanged));
        currentWave = 1; EmitInt(OnWaveChanged, currentWave, nameof(OnWaveChanged));
        enemiesKilled = 0; EmitInt(OnEnemiesKilledChanged, enemiesKilled, nameof(OnEnemiesKilledChanged));
        bossKillCount = 0; EmitInt(OnBossKillsChanged, bossKillCount, nameof(OnBossKillsChanged));
        friendliesRescued = 0; EmitInt(OnFriendliesRescuedChanged, friendliesRescued, nameof(OnFriendliesRescuedChanged));

        _helicopterPartIndex = 0;
        collectedHelicopterParts.Clear();

        if (resetCurrencies)
        {
            coins = 0;
            materials = 0;
            EmitInt(OnCoinsChanged, coins, nameof(OnCoinsChanged));
            EmitInt(OnMaterialsChanged, materials, nameof(OnMaterialsChanged));
        }

        ResetBossEngagementOrder();

        EnemiesAlive = 0;
        EnemiesKilledThisWave = 0;
        _inInterWave = false;
        if (_interWaveRoutine != null) { StopCoroutine(_interWaveRoutine); _interWaveRoutine = null; }

        ValidateHelicopterDropConfig();
        FireHelicopterProgressChanged();
        ResetPlayerWeapons();
        // Intentionally NOT starting a wave here; caller decides (e.g., after scene load).
    }

    private void ResetPlayerWeapons()
    {
        if (!resetWeaponsOnLevelRestart) return;

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject == null)
        {
            PlayerWeaponController pwc = FindObjectOfType<PlayerWeaponController>();
            if (pwc != null) playerObject = pwc.gameObject;
        }

        if (playerObject == null)
        {
            if (debugLogs) Debug.LogWarning("[GameManager] Cannot reset weapons - player not found");
            return;
        }

        WeaponResetSystem resetSystem = playerObject.GetComponent<WeaponResetSystem>();
        if (resetSystem == null)
        {
            resetSystem = playerObject.AddComponent<WeaponResetSystem>();
            if (debugLogs) Debug.Log("[GameManager] Added WeaponResetSystem to player");
        }

        resetSystem.ResetWeapon();
        DLog("[GameManager] Player weapons reset complete");
    }

    private int GetKillsRequiredForWave(int waveIndex) { return Mathf.Max(1, baseKillsPerWave + killsPerWaveGrowth * (waveIndex - 1)); }
    private float GetDurationForWave(int waveIndex) { return Mathf.Max(5f, baseWaveDuration + waveDurationGrowth * (waveIndex - 1)); }

    private void BeginWave(int waveIndex)
    {
        EnemiesKilledThisWave = 0;
        _waveElapsed = 0f;
        _topUpTimer = 0f;

        if (waveAdvanceMode == WaveAdvanceMode.ByTimer)
            _waveTimeRemaining = GetDurationForWave(waveIndex);

        _inInterWave = false;
        EmitInt(OnWaveStarted, waveIndex, nameof(OnWaveStarted));

        // Initialize desired concurrent enemies at wave start
        UpdateDesiredConcurrentEnemies(0f);

        DLog("[GameManager] Wave " + waveIndex + " started. Mode=" + waveAdvanceMode +
             (waveAdvanceMode == WaveAdvanceMode.ByKills ? " targetKills=" + GetKillsRequiredForWave(waveIndex) :
              waveAdvanceMode == WaveAdvanceMode.ByTimer ? " duration=" + _waveTimeRemaining.ToString("0.0") + "s" : " (manual)") +
             $" | desired now={DesiredConcurrentEnemies}");

        // Start-of-wave burst (e.g., 25)
        int burst = Mathf.Max(0, initialSpawnBurst);
        if (burst > 0)
        {
            try { OnTopUpNeeded?.Invoke(burst, DesiredConcurrentEnemies, EnemiesAlive); }
            catch (Exception ex) { Debug.LogError("[GM] OnTopUpNeeded (start burst) error: " + ex); }
        }

        // Kick the normal batched loop too so ramp catches up quickly
        RequestTopUpBatched();
    }

    private void TryEndCurrentWave(string reason)
    {
        if (_inInterWave) return;
        _inInterWave = true;
        EmitInt(OnWaveEnded, currentWave, nameof(OnWaveEnded));
        DLog("[GameManager] Wave " + currentWave + " ended via " + reason + ". Inter-wave for " + interWaveDelay.ToString("0.0") + "s.");
        if (_interWaveRoutine != null) StopCoroutine(_interWaveRoutine);
        _interWaveRoutine = StartCoroutine(InterWaveThenAdvance());
    }

    private IEnumerator InterWaveThenAdvance()
    {
        float t = interWaveDelay;
        while (t > 0f)
        {
            EmitFloat(OnInterWaveCountdown, t, nameof(OnInterWaveCountdown));
            yield return null;
            if (!IsActuallyPaused) t -= Time.unscaledDeltaTime;
        }

        _interWaveRoutine = null;

        if (!autoAdvanceWaves || waveAdvanceMode == WaveAdvanceMode.Manual)
        {
            _inInterWave = false;
            yield break;
        }

        AdvanceWave();
        BeginWave(currentWave);
    }

    // ========================= Enemy / Boss Kills & Drops
    public void RegisterEnemySpawned() { EnemiesAlive = Mathf.Max(0, EnemiesAlive + 1); }

    public void RegisterEnemyDeath(Vector3 deathPosition, bool isBoss = false)
    {
        EnemiesAlive = Mathf.Max(0, EnemiesAlive - 1);
        RegisterEnemyKill(1);
        EnemiesKilledThisWave = Mathf.Max(0, EnemiesKilledThisWave + 1);

        if (isBoss) RegisterBossKill(deathPosition);

        // If we fell below desired, ask for a batched top-up *now*
        if (!_inInterWave && _runActive && !IsActuallyPaused && EnemiesAlive < DesiredConcurrentEnemies)
            RequestTopUpBatched();

        if (!_inInterWave && autoAdvanceWaves && waveAdvanceMode == WaveAdvanceMode.ByKills && _runActive && !IsActuallyPaused)
        {
            int target = GetKillsRequiredForWave(currentWave);
            bool killsMet = EnemiesKilledThisWave >= target;
            bool clearOk = !requireClearBoardToEndWave || EnemiesAlive == 0;
            if (killsMet && clearOk) TryEndCurrentWave("kills+clear");
        }
    }

    public void RegisterEnemyDespawned() { EnemiesAlive = Mathf.Max(0, EnemiesAlive - 1); }

    public void RegisterEnemyKill(int amount = 1)
    {
        if (amount <= 0) return;
        enemiesKilled = Mathf.Max(0, enemiesKilled + amount);
        EmitInt(OnEnemiesKilledChanged, enemiesKilled, nameof(OnEnemiesKilledChanged));
    }

    public void SetEnemiesKilled(int value)
    {
        enemiesKilled = Mathf.Max(0, value);
        EmitInt(OnEnemiesKilledChanged, enemiesKilled, nameof(OnEnemiesKilledChanged));
    }

    public void RegisterFriendlyRescue(int amount = 1)
    {
        if (amount <= 0) return;
        friendliesRescued = Mathf.Max(0, friendliesRescued + amount);
        EmitInt(OnFriendliesRescuedChanged, friendliesRescued, nameof(OnFriendliesRescuedChanged));
        DLog("[GameManager] Friendly rescued! Total=" + friendliesRescued);
    }

    public void SetFriendliesRescued(int value)
    {
        friendliesRescued = Mathf.Max(0, value);
        EmitInt(OnFriendliesRescuedChanged, friendliesRescued, nameof(OnFriendliesRescuedChanged));
    }

    public void RegisterBossKill(Vector3 dropPosition)
    {
        if (_lastBossKillFrame == Time.frameCount) return;
        _lastBossKillFrame = Time.frameCount;

        bossKillCount = Mathf.Max(0, bossKillCount) + 1;
        EmitInt(OnBossKillsChanged, bossKillCount, nameof(OnBossKillsChanged));
        const int bossKillScoreReward = 10000;
        AddScore(bossKillScoreReward);

        DLog("[GameManager] Boss kill #" + bossKillCount + " at " + dropPosition + ". weaponChance=" + weaponDropChance.ToString("0.##") +
             " partChance=" + helicopterPartDropChance.ToString("0.##") + " maxHPchance=" + maxHealthPickupDropChance.ToString("0.##") +
             " upgradeChance=" + weaponUpgradeDropChance.ToString("0.##"));

        if (UnityEngine.Random.value <= weaponDropChance)
        {
            var weaponDrop = GetRandomWeaponDropPrefab();
            if (weaponDrop != null) UnityEngine.Object.Instantiate(weaponDrop, dropPosition, Quaternion.identity);
        }
        if (UnityEngine.Random.value <= helicopterPartDropChance)
        {
            var partPrefab = GetNextHelicopterPartPrefab();
            if (partPrefab != null) UnityEngine.Object.Instantiate(partPrefab, dropPosition + secondDropOffset, Quaternion.identity);
        }
        if (maxHealthPickupPrefab != null && UnityEngine.Random.value <= maxHealthPickupDropChance)
        {
            UnityEngine.Object.Instantiate(maxHealthPickupPrefab, dropPosition + maxHealthPickupOffset, Quaternion.identity);
        }
        if (epicWeaponUpgradePrefab != null && UnityEngine.Random.value <= weaponUpgradeDropChance)
        {
            UnityEngine.Object.Instantiate(epicWeaponUpgradePrefab, dropPosition + weaponUpgradeOffset, Quaternion.identity);
        }
    }

    public bool SpawnNextHelicopterPart(Vector3 position)
    {
        var part = GetNextHelicopterPartPrefab();
        if (part == null) { Debug.LogWarning("[GameManager] No part available."); return false; }
        UnityEngine.Object.Instantiate(part, position, Quaternion.identity);
        return true;
    }

    /// <summary>
    /// Spawns a rare weapon upgrade at the given position (for mini bosses).
    /// Returns true if upgrade was spawned.
    /// </summary>
    public bool SpawnRareWeaponUpgrade(Vector3 position)
    {
        if (rareWeaponUpgradePrefab == null)
        {
            Debug.LogWarning("[GameManager] No rare weapon upgrade prefab assigned!");
            return false;
        }
        UnityEngine.Object.Instantiate(rareWeaponUpgradePrefab, position, Quaternion.identity);
        DLog("[GameManager] Spawned rare weapon upgrade at " + position);
        return true;
    }

    private GameObject GetRandomWeaponDropPrefab()
    {
        if (bossWeaponDropPrefabs != null && bossWeaponDropPrefabs.Length > 0)
        {
            int nonNullCount = 0;
            for (int i = 0; i < bossWeaponDropPrefabs.Length; i++) if (bossWeaponDropPrefabs[i] != null) nonNullCount++;
            if (nonNullCount > 0)
            {
                int pickIndex = UnityEngine.Random.Range(0, nonNullCount);
                for (int i = 0, seen = 0; i < bossWeaponDropPrefabs.Length; i++)
                {
                    var go = bossWeaponDropPrefabs[i];
                    if (go == null) continue;
                    if (seen == pickIndex) return go;
                    seen++;
                }
            }
        }
        if (shotgunDropPrefab || machinegunDropPrefab || rocketDropPrefab)
        {
            List<GameObject> legacy = new List<GameObject>(3);
            if (shotgunDropPrefab) legacy.Add(shotgunDropPrefab);
            if (machinegunDropPrefab) legacy.Add(machinegunDropPrefab);
            if (rocketDropPrefab) legacy.Add(rocketDropPrefab);
            int idx = UnityEngine.Random.Range(0, legacy.Count);
            return legacy[idx];
        }
        return null;
    }

    private GameObject GetNextHelicopterPartPrefab()
    {
        if (helicopterPartPrefabs != null && helicopterPartPrefabs.Length > 0)
        {
            if (_helicopterPartIndex < helicopterPartPrefabs.Length)
                return helicopterPartPrefabs[_helicopterPartIndex++];
            return null;
        }
        return helicopterPartPrefab ? helicopterPartPrefab : null;
    }

    public void AddHelicopterPart(string partName)
    {
        if (string.IsNullOrEmpty(partName)) return;
        if (!collectedHelicopterParts.Contains(partName))
        {
            collectedHelicopterParts.Add(partName);
            if (OnHelicopterPartCollected != null) OnHelicopterPartCollected(partName);
            FireHelicopterProgressChanged();
        }
        else
        {
            FireHelicopterProgressChanged();
        }
    }

    // ===== Helicopter progress API =====
    public (int collected, int required) GetHelicopterProgress()
    {
        return (collectedHelicopterParts.Count, requiredHelicopterParts);
    }
    public void GetHelicopterProgress(out int collected, out int required)
    {
        collected = collectedHelicopterParts.Count;
        required = requiredHelicopterParts;
    }
    public void SetRequiredHelicopterParts(int value)
    {
        requiredHelicopterParts = Mathf.Max(0, value);
        FireHelicopterProgressChanged();
    }
    public void RebroadcastHelicopterProgress() { FireHelicopterProgressChanged(); }
    private void FireHelicopterProgressChanged()
    {
        Emit2(OnHelicopterProgressChanged, collectedHelicopterParts.Count, requiredHelicopterParts, nameof(OnHelicopterProgressChanged));
    }

    // ========================= Scene load handling =========================
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        HardResumeGameplay();
        try { if (WinUIController.Instance != null) WinUIController.Instance.Resume(); } catch { }
        AutoDisableEscIfPauseUI();
        DLog("[GameManager] Scene loaded: '" + scene.name + "'.");

        if (_pendingResetOnSceneLoad)
        {
            _pendingResetOnSceneLoad = false;
            StartCoroutine(InitializeAfterSceneLoad());
        }
        else
        {
            if (_runActive && !_inInterWave) BeginWave(currentWave);
            StartCoroutine(DeferredRebroadcastAllStats());
        }
    }

    private IEnumerator InitializeAfterSceneLoad()
    {
        // Wait a bit for scene to settle
        yield return new WaitForSeconds(0.2f);
        
        // CRITICAL: Trigger NavMesh baking using your existing NavMeshRuntimeBaker
        bool navMeshBaked = false;
        NavMeshRuntimeBaker navBaker = FindObjectOfType<NavMeshRuntimeBaker>();
        
        if (navBaker != null)
        {
            DLog("[GameManager] Found NavMeshRuntimeBaker, requesting bake...");
            
            // Request bake from the runtime baker
            navBaker.RequestBake(this);
            
            // Wait for bake to complete (max 5 seconds)
            float waitTime = 0f;
            while (!navBaker.BakeCompleted && waitTime < 5f)
            {
                yield return new WaitForSeconds(0.2f);
                waitTime += 0.2f;
            }
            
            if (navBaker.BakeCompleted)
            {
                DLog($"[GameManager] NavMesh bake completed in {waitTime:F1}s");
                navMeshBaked = true;
            }
            else
            {
                Debug.LogWarning($"[GameManager] NavMesh bake timeout after {waitTime:F1}s");
            }
        }
        else
        {
            Debug.LogWarning("[GameManager] NavMeshRuntimeBaker not found! NavMesh may not be available.");
            // Fallback: wait a bit and hope for the best
            yield return new WaitForSeconds(1.0f);
        }
        
        // Reset the run
        ResetRun(true);
        
        // Wait another frame for weapon reset to complete
        yield return null;
        
        // Wait a bit more to ensure UI components are ready
        yield return new WaitForSeconds(0.2f);
        
        // Start the wave
        BeginWave(currentWave);
        
        // Rebroadcast all stats
        StartCoroutine(DeferredRebroadcastAllStats());
    }
    
    private void TriggerNavMeshBake()
    {
        // This method is now handled in InitializeAfterSceneLoad
        // Keeping it for backward compatibility
        NavMeshRuntimeBaker navBaker = FindObjectOfType<NavMeshRuntimeBaker>();
        if (navBaker != null)
        {
            DLog("[GameManager] Triggering NavMeshRuntimeBaker...");
            navBaker.RequestBake(this);
        }
    }

    public void ResetBossEngagementOrder()
    {
        _nextBossEngagementOrder = 1;
        _bossOrderByInstance.Clear();
        _bossScaledOnce.Clear();
    }

    private IEnumerator DeferredRebroadcastAllStats()
    {
        yield return null;
        RebroadcastAllStats();
    }

    public void RebroadcastAllStats()
    {
        EmitInt(OnCoinsChanged, coins, nameof(OnCoinsChanged));
        EmitInt(OnMaterialsChanged, materials, nameof(OnMaterialsChanged));
        EmitInt(OnScoreChanged, score, nameof(OnScoreChanged));
        EmitInt(OnWaveChanged, currentWave, nameof(OnWaveChanged));
        EmitInt(OnEnemiesKilledChanged, enemiesKilled, nameof(OnEnemiesKilledChanged));
        EmitInt(OnBossKillsChanged, bossKillCount, nameof(OnBossKillsChanged));
        EmitInt(OnFriendliesRescuedChanged, friendliesRescued, nameof(OnFriendliesRescuedChanged));
        FireHelicopterProgressChanged();
    }

    // ========================= NEW helpers =========================
    private void UpdateDesiredConcurrentEnemies(float rampT01)
    {
        float curved = Mathf.Clamp01(concurrentRamp.Evaluate(Mathf.Clamp01(rampT01)));
        int desired = Mathf.RoundToInt(Mathf.Lerp(minConcurrentEnemies, maxConcurrentEnemies, curved));
        if (desired < 0) desired = 0;
        DesiredConcurrentEnemies = desired;
    }

    // ========================= Config validation =========================
    private void ValidateHelicopterDropConfig()
    {
        bool hasSequence = helicopterPartPrefabs != null && helicopterPartPrefabs.Length > 0;
        bool hasFallback = helicopterPartPrefab != null;

        if (!hasSequence && !hasFallback)
            Debug.LogWarning("[GameManager] No helicopter part prefabs assigned!");

        if (helicopterPartDropChance <= 0f)
            Debug.LogWarning("[GameManager] HelicopterPartDropChance is 0 � parts will NEVER drop.");

        bool anyWeapon =
            (bossWeaponDropPrefabs != null && bossWeaponDropPrefabs.Length > 0) ||
            shotgunDropPrefab != null || machinegunDropPrefab != null || rocketDropPrefab != null;

        if (!anyWeapon && weaponDropChance > 0f)
            Debug.LogWarning("[GameManager] weaponDropChance > 0, but no weapon drop prefabs are assigned.");
    }

    // ========================= Axis Reset helpers =========================
    private void TryAxisReset(string reason)
    {
        if (Time.unscaledTime - _lastAxisResetAt < axisResetCooldown) return;

        // Only reset if we're not paused AND there's no live input right now.
        if (IsActuallyPaused) return;
        if (IsAnyLiveInput()) { DLog($"[GM] AxisReset ABORT ({reason}) due to live input."); return; }

        Input.ResetInputAxes();
        _lastAxisResetAt = Time.unscaledTime;
        DLog($"[GM] AxisReset OK ({reason}) at t={_lastAxisResetAt:0.00}");
    }

    private bool IsAnyLiveInput()
    {
        // Held keyboard movement keys
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D)) return true;
        if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.RightArrow)) return true;
        if (Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) return true;

        // Any fresh key or mouse press held
        if (Input.anyKeyDown) return true;
        if (Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2)) return true;

        // Axes
        if (Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.01f) return true;
        if (Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.01f) return true;

        float mx = 0f, my = 0f;
        try { mx = Input.GetAxisRaw("Mouse X"); my = Input.GetAxisRaw("Mouse Y"); } catch { }
        if (Mathf.Abs(mx) > 0.01f || Mathf.Abs(my) > 0.01f) return true;

        // Gamepad sticks (if defined in Input Manager)
        float lx = 0f, ly = 0f;
        try { lx = Input.GetAxisRaw("Joystick X"); ly = Input.GetAxisRaw("Joystick Y"); } catch { }
        if (Mathf.Abs(lx) > 0.01f || Mathf.Abs(ly) > 0.01f) return true;

        return false;
    }

    private void OnApplicationFocus(bool focus)
    {
        if (axisResetMode != AxisResetMode.OnFocusIdleOnly) return;

        if (focus) StartCoroutine(FocusIdleResetRoutine());
    }

    private IEnumerator FocusIdleResetRoutine()
    {
        float end = Time.unscaledTime + Mathf.Max(0.25f, focusIdleSeconds);

        // Wait until idle for the whole window; abort if any input shows up.
        while (Time.unscaledTime < end)
        {
            if (IsActuallyPaused) yield break; // don't touch axes while paused
            if (IsAnyLiveInput()) yield break; // player is active: do not reset
            yield return null;
        }

        TryAxisReset("FocusIdle");
    }
}
