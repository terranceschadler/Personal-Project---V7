// Assets/Scripts/UI scripts/GoalPointer.cs
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using System.Collections;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
public class GoalPointer : MonoBehaviour
{
    public static GoalPointer Instance { get; private set; }

    [Header("Singleton")]
    public bool dontDestroyOnLoad = true;
    public bool autoCullOnSceneLoaded = true;
    public bool cleanupOrphanArrowsOnCull = true;
    public bool rebindOnSceneLoaded = true;

    private static bool _isCulling;

    // ---------- References ----------
    [Header("References")]
    public Transform player;
    public RectTransform arrowUI;
    public string arrowUITag = "GoalArrow";
    private Image arrowImage;

    [Header("Helicopter Target")]
    public Transform helicopter;
    public string helicopterTag = "Helicopter";

    [Header("Helicopter Parts (pre-complete priority)")]
    public string helicopterPartTag = "HelicopterPart";
    public float partRescanInterval = 0.25f;

    [Header("Boss Target (fallback when no parts are found)")]
    public string bossTag = "Boss";
    [Tooltip("If true, bosses will be auto-targeted only when within bossAutoAcquireRange (also used for proximity override).")]
    public bool allowBossAutoAcquireByRange = true;
    public float bossAutoAcquireRange = 35f;
    public float bossRescanInterval = 0.35f;

    [Header("AI Spawn (local override)")]
    public string aiSpawnTag = "AISpawn";
    public float aiSpawnEnterRadius = 25f;
    public float aiSpawnExitRadius = 27.5f;
    public float aiSpawnRescanInterval = 0.2f;

    [Header("Path / Navigation")]
    public bool useNavmeshPath = true;
    public float pathRecalcInterval = 0.15f;
    public float pathRecalcMoveThreshold = 0.75f;
    public float cornerMinDistance = 1.0f;
    public int cornerScanLimit = 6;
    public int navMeshAreaMask = NavMesh.AllAreas;

    [Header("Completion Source / Fallback")]
    public int targetCountIfNoUniqueList = 1;

    [Header("UI Settings")]
    [Range(0f, 1f)] public float arrowAlpha = 1f;
    public float hideDistance = 2f;
    public float rotationSpeed = 5f;
    public float onScreenPadding = 32f;

    [Header("Colors")]
    public Color normalColor = Color.white;
    public Color aiSpawnColor = Color.green;
    public Color bossColor = Color.red;

    [Header("Screen-Edge Indicator")]
    public Camera mainCamera;
    public Canvas canvas;
    public RectTransform canvasRect;
    public float edgePadding = 24f;
    public float arrowMoveSpeed = 12f;
    public bool snapInstantOnRebind = true;

    [Header("Timing")]
    [Tooltip("Use unscaled time so the pointer updates while the game is paused (map open).")]
    public bool useUnscaledTime = true;

    [Header("Debug")]
    public bool debugLogs = false;

    // ---------- Auto-clear (map-selected only) ----------
    [Header("Auto-Clear (Map-Selected Goal)")]
    [Tooltip("If true, clears the current map-selected goal once the player reaches it.")]
    public bool clearOnReach = true;
    [Tooltip("Distance at which a map-selected goal is considered reached and auto-cleared.")]
    [Min(0.1f)] public float reachRadius = 2.5f;

    /// <summary>Fires when a map-selected goal is cleared.</summary>
    public event System.Action GoalCleared;

    // ---------- Internals ----------
    private Transform goal;                 // baseline (part/boss/heli)
    private Transform _currentBoss;
    private Transform _currentPart;

    // Proximity overrides (always allowed)
    private Transform _overrideSpawn;       // friendly spawn proximity override
    private Transform _overrideBoss;        // boss proximity override

    private Transform _nearestVisibleSpawn;
    private Transform _proxBoss;

    // Map selection (overrides baseline, but not proximity)
    [Header("Map Selection Override")]
    public bool mapSelectionOverridesBaseline = true;
    private Transform _mapSelectedTarget;
    private MapKeyArea.AreaType _mapSelectedType = MapKeyArea.AreaType.Other;

    // timers (unscaled if configured)
    private float _nextBossScanTime = 0f;
    private float _nextPartScanTime = 0f;
    private float _nextHeliScanTime = 0f;
    private float _nextSpawnScanTime = 0f;
    private float _nextProxBossScanTime = 0f;

    private bool _lockedToHelicopter = false;
    private GameManager gm;

#if UNITY_2019_4_OR_NEWER
    private NavMeshPath _path;
#endif
    private float _nextPathTime = 0f;
    private Transform _lastPathTarget;
    private Vector3 _lastPlayerPos;
    private bool _hasValidPath = false;

    private bool _doOneInstantSnap = false;

    private float Now => useUnscaledTime ? Time.unscaledTime : Time.time;
    private float Dt => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

    // ---------- Global duplicate cull hooks ----------
#if UNITY_2019_4_OR_NEWER
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void PreSceneLoadCull() => CullDuplicatesGlobal();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void PostSceneLoadCull() => CullDuplicatesGlobal();
#endif

    private void OnEnable()
    {
        CullDuplicatesGlobal();
        if (autoCullOnSceneLoaded || rebindOnSceneLoaded)
            SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        if (autoCullOnSceneLoaded || rebindOnSceneLoaded)
            SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (autoCullOnSceneLoaded) CullDuplicatesGlobal();
        if (rebindOnSceneLoaded)
            StartCoroutine(RebindAfterSceneLoad());
    }

    private static void CullDuplicatesGlobal()
    {
        if (_isCulling) return;
        _isCulling = true;

        var all = Resources.FindObjectsOfTypeAll<GoalPointer>();
        if (all == null || all.Length <= 1) { _isCulling = false; return; }

        GoalPointer survivor = Instance != null ? Instance : all[0];
        int bestId = survivor.GetInstanceID();
        for (int i = 0; i < all.Length; i++)
        {
            var gp = all[i]; if (gp == null) continue;
            int id = gp.GetInstanceID();
            if (id < bestId) { bestId = id; survivor = gp; }
        }

        if (Instance != survivor)
        {
            Instance = survivor;
            if (survivor.dontDestroyOnLoad)
                Object.DontDestroyOnLoad(survivor.gameObject);
        }

        for (int i = 0; i < all.Length; i++)
        {
            var gp = all[i]; if (gp == null || gp == survivor) continue;

            if (survivor.cleanupOrphanArrowsOnCull && gp.arrowUI != null && gp.arrowUI != survivor.arrowUI)
            {
                var go = gp.arrowUI.gameObject;
                if (go != null)
                {
#if UNITY_EDITOR
                    Object.DestroyImmediate(go);
#else
                    Object.Destroy(go);
#endif
                }
            }

#if UNITY_EDITOR
            if (gp.GetComponents<GoalPointer>().Length > 1) Object.DestroyImmediate(gp);
            else Object.DestroyImmediate(gp.gameObject);
#else
            if (gp.GetComponents<GoalPointer>().Length > 1) Object.Destroy(gp);
            else Object.Destroy(gp.gameObject);
#endif
        }

        _isCulling = false;
    }

    private void OnValidate()
    {
        if (aiSpawnExitRadius < aiSpawnEnterRadius) aiSpawnExitRadius = aiSpawnEnterRadius;
        if (aiSpawnEnterRadius < 0f) aiSpawnEnterRadius = 0f;
        if (aiSpawnExitRadius < 0f) aiSpawnExitRadius = 0f;
        if (rotationSpeed < 0f) rotationSpeed = 0f;
        arrowAlpha = Mathf.Clamp01(arrowAlpha);
        if (cornerMinDistance < 0f) cornerMinDistance = 0f;
        if (cornerScanLimit < 1) cornerScanLimit = 1;
        if (pathRecalcMoveThreshold < 0f) pathRecalcMoveThreshold = 0f;
        if (pathRecalcInterval < 0.02f) pathRecalcInterval = 0.02f;
        if (edgePadding < 0f) edgePadding = 0f;
        if (arrowMoveSpeed < 0f) arrowMoveSpeed = 0f;
        if (onScreenPadding < 0f) onScreenPadding = 0f;
        if (bossAutoAcquireRange < 0f) bossAutoAcquireRange = 0f;
        if (reachRadius < 0.1f) reachRadius = 0.1f;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            if (debugLogs) Debug.LogWarning($"[GoalPointer] Duplicate '{name}' destroyed; survivor = '{Instance.name}'.", this);
            Destroy(gameObject);
            return;
        }

        Instance = this;
        if (dontDestroyOnLoad) DontDestroyOnLoad(gameObject);

        if (player == null) { var p = GameObject.FindGameObjectWithTag("Player"); if (p) player = p.transform; }
        if (helicopter == null && !string.IsNullOrEmpty(helicopterTag))
        { var h = GameObject.FindGameObjectWithTag(helicopterTag); if (h) helicopter = h.transform; }

#if UNITY_2019_4_OR_NEWER
        _path = new NavMeshPath();
#endif
        gm = GameManager.Instance;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        EnsureArrowImageBound();
        EnsureCanvasBound();
        BootstrapInitialGoalAndDraw();
    }

    private IEnumerator RebindAfterSceneLoad()
    {
        yield return null;

        if (player == null) { var p = GameObject.FindGameObjectWithTag("Player"); if (p) player = p.transform; }
        if ((helicopter == null) && !string.IsNullOrEmpty(helicopterTag))
        { var h = GameObject.FindGameObjectWithTag(helicopterTag); if (h) helicopter = h.transform; }

        if (arrowUI == null && !string.IsNullOrEmpty(arrowUITag))
        {
            var go = GameObject.FindGameObjectWithTag(arrowUITag);
            if (go) arrowUI = go.GetComponent<RectTransform>();
        }
        EnsureArrowImageBound();
        EnsureCanvasBound();

        gm = GameManager.Instance;

        _overrideSpawn = null;
        _overrideBoss = null;
        _nearestVisibleSpawn = null;
        _proxBoss = null;

        _currentPart = null;
        _currentBoss = null;

        _lastPathTarget = null;
        _hasValidPath = false;

        float now = Now;
        _nextPartScanTime = _nextBossScanTime = _nextSpawnScanTime = _nextHeliScanTime = now;
        _nextProxBossScanTime = now;
        _nextPathTime = now;

        _lockedToHelicopter = false;
        if (IsHelicopterComplete())
        {
            _lockedToHelicopter = true;
            goal = helicopter ?? TryFindHelicopterByTag();
            if (debugLogs) Debug.Log("[GoalPointer] Rebind: helicopter complete ? locking to heli.", this);
        }
        else
        {
            _currentPart = FindClosestPart();
            goal = _currentPart != null ? _currentPart : (_currentBoss = FindClosestBossInRangeOrNull());
            if (debugLogs)
            {
                if (goal == null) Debug.Log("[GoalPointer] Rebind: no parts or in-range bosses found.");
                else Debug.Log($"[GoalPointer] Rebind: baseline goal now {goal.name}");
            }
        }

        _lastPlayerPos = player ? player.position : Vector3.zero;

        _doOneInstantSnap = snapInstantOnRebind;
        ApplyVisibilityAndRotation(forceImmediate: true);
    }

    private Transform TryFindHelicopterByTag()
    {
        if (string.IsNullOrEmpty(helicopterTag)) return null;
        var go = GameObject.FindGameObjectWithTag(helicopterTag);
        return go ? go.transform : null;
    }

    private void EnsureArrowImageBound()
    {
        if (arrowUI != null)
        {
            arrowImage = arrowUI.GetComponent<Image>();
            if (arrowImage != null) arrowImage.color = normalColor.WithAlpha(arrowAlpha);
        }
    }

    private void EnsureCanvasBound()
    {
        if (arrowUI == null) return;

        if (canvas == null)
        {
            var canvases = arrowUI.GetComponentsInParent<Canvas>(true);
            if (canvases != null && canvases.Length > 0)
                canvas = canvases[0];
        }

        if (canvasRect == null && canvas != null)
            canvasRect = canvas.GetComponent<RectTransform>();

        if (mainCamera == null)
            mainCamera = Camera.main;
    }

    private void BootstrapInitialGoalAndDraw()
    {
        if (IsHelicopterComplete())
        {
            _lockedToHelicopter = true;
            goal = helicopter ?? TryFindHelicopterByTag();
            if (debugLogs) Debug.Log("[GoalPointer] Start: already complete ? locking to heli.", this);
        }
        else
        {
            _currentPart = FindClosestPart();
            goal = _currentPart != null ? _currentPart : (_currentBoss = FindClosestBossInRangeOrNull());
            if (debugLogs)
            {
                if (goal == null) Debug.Log("[GoalPointer] Start: no parts or in-range bosses found.");
                else Debug.Log($"[GoalPointer] Start: baseline goal {goal.name}");
            }
        }
        _lastPlayerPos = player ? player.position : Vector3.zero;
        _doOneInstantSnap = true;
        ApplyVisibilityAndRotation(forceImmediate: true);
    }

    // ---------- MAP SELECTION API ----------
    public void SelectFromMap(Transform target, MapKeyArea.AreaType type)
    {
        _mapSelectedTarget = target;
        _mapSelectedType = type;

        // force refresh
        _lastPathTarget = null;
        _nextPathTime = Now;
        _doOneInstantSnap = true;
        ApplyVisibilityAndRotation(forceImmediate: true);

        if (debugLogs) Debug.Log($"[GoalPointer] Map-selected: {(target ? target.name : "null")} ({type}).", this);
    }

    public void ClearMapSelection()
    {
        _mapSelectedTarget = null;
        GoalCleared?.Invoke();
    }

    private void Update()
    {
        if (player == null || arrowUI == null || arrowImage == null) return;

        // Drop invalid map selection
        if (_mapSelectedTarget != null)
        {
            var goSel = _mapSelectedTarget.gameObject;
            if (!goSel || !goSel.activeInHierarchy) _mapSelectedTarget = null;
        }

        // Completion lock
        if (!_lockedToHelicopter && IsHelicopterComplete())
        {
            _lockedToHelicopter = true;
            _currentBoss = null; _currentPart = null;
            goal = helicopter ?? TryFindHelicopterByTag();
            if (debugLogs) Debug.Log("[GoalPointer] Completion detected -> locking baseline to helicopter.");
        }

        float now = Now;

        // Baseline goal maintenance (independent of map selection)
        if (_lockedToHelicopter)
        {
            if (goal == null) goal = helicopter ?? TryFindHelicopterByTag();
            if (goal == null && now >= _nextHeliScanTime)
            {
                goal = TryFindHelicopterByTag();
                _nextHeliScanTime = now + 0.5f;
            }
        }
        else
        {
            if (now >= _nextPartScanTime)
            {
                _currentPart = FindClosestPart();
                _nextPartScanTime = now + partRescanInterval;
            }

            if (_currentPart == null && now >= _nextBossScanTime)
            {
                _currentBoss = allowBossAutoAcquireByRange ? FindClosestBossInRangeOrNull() : null;
                _nextBossScanTime = now + bossRescanInterval;
            }

            goal = _currentPart != null ? _currentPart : _currentBoss;
        }

        // --- Proximity overrides (ALWAYS allowed) ---

        // Friendly spawns
        if (now >= _nextSpawnScanTime)
        {
            _nearestVisibleSpawn = FindClosestVisibleAISpawn();
            _nextSpawnScanTime = now + aiSpawnRescanInterval;
        }

        // Clear stale override spawn (mesh hidden, inactive, or destroyed)
        if (_overrideSpawn != null)
        {
            var go = _overrideSpawn.gameObject;
            if (!go || !go.activeInHierarchy || !SpawnHasVisibleRenderer(go))
                _overrideSpawn = null;
        }

        if (_nearestVisibleSpawn != null)
        {
            float d = FlatDistance(player.position, _nearestVisibleSpawn.position);
            if (d <= aiSpawnEnterRadius) _overrideSpawn = _nearestVisibleSpawn;
            else if (_overrideSpawn == _nearestVisibleSpawn && d > aiSpawnExitRadius) _overrideSpawn = null;
        }
        else if (_overrideSpawn == _nearestVisibleSpawn)
        {
            _overrideSpawn = null;
        }

        // Boss proximity
        if (allowBossAutoAcquireByRange && now >= _nextProxBossScanTime)
        {
            _proxBoss = FindClosestBossInRangeOrNull();
            _nextProxBossScanTime = now + bossRescanInterval;
        }
        _overrideBoss = _proxBoss; // null if none in range

        // ---------- NEW: Auto-clear for MAP-SELECTED goal (logic layer) ----------
        // Do this BEFORE UI/rotation so the next frame uses the new active target.
        Transform activeBeforeClear = ComputeActiveTarget();
        if (clearOnReach && _mapSelectedTarget != null && activeBeforeClear == _mapSelectedTarget)
        {
            float dReach = Vector3.Distance(player.position, _mapSelectedTarget.position);
            if (dReach <= reachRadius)
            {
                if (debugLogs) Debug.Log($"[GoalPointer] Reached map-selected goal '{_mapSelectedTarget.name}' at d={dReach:F2} -> clearing.");
                ClearMapSelection();  // fires GoalCleared
                // No need to manually recompute here; ApplyVisibilityAndRotation will call ComputeActiveTarget() again.
            }
        }
        // -----------------------------------------------------------------------

        ApplyVisibilityAndRotation();
    }

    // ---------- Target selection with precedence ----------
    private Transform ComputeActiveTarget()
    {
        // 1) Friendly spawn proximity
        if (_overrideSpawn != null) return _overrideSpawn;

        // 2) Boss proximity
        if (_overrideBoss != null) return _overrideBoss;

        // 3) Map selection (overrides baseline only)
        if (mapSelectionOverridesBaseline && _mapSelectedTarget != null) return _mapSelectedTarget;

        // 4) Baseline (parts/heli or in-range boss if allowed)
        return goal;
    }

    private void ApplyVisibilityAndRotation(bool forceImmediate = false)
    {
        if (arrowImage == null || arrowUI == null || player == null)
            return;

        EnsureCanvasBound();
        if (canvasRect == null) return;

        Transform target = ComputeActiveTarget();

        if (target == null)
        {
            arrowImage.enabled = false;
            return;
        }

        arrowImage.enabled = true;

        // Visibility threshold
        float distance = Vector3.Distance(player.position, target.position);
        bool shouldShow = distance > hideDistance;

        // Color logic
        bool pointingAtSpawn = (target == _overrideSpawn);
        bool selectedBoss = (!pointingAtSpawn && _mapSelectedTarget != null && target == _mapSelectedTarget && _mapSelectedType == MapKeyArea.AreaType.Boss);
        bool tagBoss = (!pointingAtSpawn && !selectedBoss && !string.IsNullOrEmpty(bossTag) && target.CompareTag(bossTag));
        bool proximityBoss = (!pointingAtSpawn && target == _overrideBoss);

        Color baseCol =
            pointingAtSpawn ? aiSpawnColor :
            (selectedBoss || tagBoss || proximityBoss) ? bossColor :
            normalColor;

        arrowImage.color = baseCol.WithAlpha(shouldShow ? arrowAlpha : 0f);

        // Direction (NavMesh if enabled)
        Vector3 dir;
        if (useNavmeshPath && TryGetNavDirection(player.position, target.position, out dir, forceImmediate))
            dir.y = 0f;
        else
        {
            dir = target.position - player.position;
            dir.y = 0f;
        }

        if (dir.sqrMagnitude > 0.0001f)
        {
            float angle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            Quaternion targetRot = Quaternion.Euler(0f, 0f, -angle);
            arrowUI.rotation = forceImmediate ? targetRot : Quaternion.Lerp(arrowUI.rotation, targetRot, Dt * rotationSpeed);
        }

        if (!shouldShow) return;

        Camera cam = mainCamera != null ? mainCamera : Camera.main;
        if (cam == null) return;

        Vector3 vp = cam.WorldToViewportPoint(target.position);
        Vector2 screenSize = new Vector2(Screen.width, Screen.height);
        Vector2 screenCenter = screenSize * 0.5f;

        Vector2 desiredScreenPos;
        bool onScreen = (vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f);

        if (onScreen)
        {
            Vector2 sp = new Vector2(vp.x * screenSize.x, vp.y * screenSize.y);
            Vector2 dirFromCenter = (sp - screenCenter).normalized;
            if (dirFromCenter.sqrMagnitude > 1e-6f)
                sp -= dirFromCenter * onScreenPadding;
            desiredScreenPos = ClampToScreenWithPadding(sp, edgePadding);
        }
        else
        {
            Vector2 fromCenterNorm = new Vector2(vp.x - 0.5f, vp.y - 0.5f);
            if (vp.z < 0f) fromCenterNorm = -fromCenterNorm;
            if (fromCenterNorm.sqrMagnitude < 1e-6f) fromCenterNorm = Vector2.up;

            Vector2 dPixels = new Vector2(fromCenterNorm.x * screenSize.x, fromCenterNorm.y * screenSize.y);
            dPixels.Normalize();

            Vector2 half = screenSize * 0.5f - new Vector2(edgePadding, edgePadding);
            float tx = (dPixels.x != 0f) ? (half.x / Mathf.Abs(dPixels.x)) : float.PositiveInfinity;
            float ty = (dPixels.y != 0f) ? (half.y / Mathf.Abs(dPixels.y)) : float.PositiveInfinity;
            float t = Mathf.Min(tx, ty);

            desiredScreenPos = screenCenter + dPixels * t;
        }

        Vector2 desiredLocal;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, desiredScreenPos, GetUICameraForCanvas(canvas), out desiredLocal))
        {
            return;
        }

        if (_doOneInstantSnap || forceImmediate)
        {
            arrowUI.anchoredPosition = desiredLocal;
            _doOneInstantSnap = false;
        }
        else
        {
            float k = 1f - Mathf.Exp(-arrowMoveSpeed * Dt);
            arrowUI.anchoredPosition = Vector2.Lerp(arrowUI.anchoredPosition, desiredLocal, k);
        }
    }

    private static Vector2 ClampToScreenWithPadding(Vector2 screenPos, float padding)
    {
        float x = Mathf.Clamp(screenPos.x, padding, Screen.width - padding);
        float y = Mathf.Clamp(screenPos.y, padding, Screen.height - padding);
        return new Vector2(x, y);
    }

    private static Camera GetUICameraForCanvas(Canvas c)
    {
        if (c == null) return null;
        if (c.renderMode == RenderMode.ScreenSpaceOverlay) return null;
        return c.worldCamera != null ? c.worldCamera : Camera.main;
    }

    // ---------- Nav helpers ----------
    private bool TryGetNavDirection(Vector3 from, Vector3 to, out Vector3 dir, bool forceImmediate = false)
    {
        dir = Vector3.zero;

        Transform active = ComputeActiveTarget();

        bool needRecalc = forceImmediate
                          || Now >= _nextPathTime
                          || _lastPathTarget != active
                          || (from - _lastPlayerPos).sqrMagnitude >= (pathRecalcMoveThreshold * pathRecalcMoveThreshold);

        if (needRecalc)
        {
            _lastPathTarget = active;
            _lastPlayerPos = from;
            _nextPathTime = Now + pathRecalcInterval;
            _hasValidPath = CalculatePathOnNavMesh(from, to
#if UNITY_2019_4_OR_NEWER
                , _path
#endif
            );
        }

        if (!_hasValidPath) return false;

#if UNITY_2019_4_OR_NEWER
        if (_path == null || _path.corners == null || _path.corners.Length == 0) return false;

        Vector3 lookPoint = Vector3.zero;
        bool found = false;

        int limit = Mathf.Min(_path.corners.Length, cornerScanLimit);
        for (int i = 1; i < limit; i++)
        {
            Vector3 c = _path.corners[i];
            Vector3 flatC = new Vector3(c.x, from.y, c.z);
            if ((flatC - from).sqrMagnitude >= (cornerMinDistance * cornerMinDistance))
            {
                lookPoint = flatC;
                found = true;
                break;
            }
        }

        if (!found)
        {
            int idx = Mathf.Min(_path.corners.Length - 1, limit - 1);
            Vector3 c = _path.corners[idx];
            lookPoint = new Vector3(c.x, from.y, c.z);
        }

        dir = (lookPoint - from);
        return dir.sqrMagnitude > 1e-6f;
#else
        dir = (to - from);
        return dir.sqrMagnitude > 1e-6f;
#endif
    }

    private bool CalculatePathOnNavMesh(Vector3 from, Vector3 to
#if UNITY_2019_4_OR_NEWER
        , NavMeshPath pathOut
#endif
    )
    {
#if UNITY_2019_4_OR_NEWER
        NavMeshHit hStart, hEnd;
        bool haveStart = NavMesh.SamplePosition(from, out hStart, 2.0f, navMeshAreaMask);
        bool haveEnd = NavMesh.SamplePosition(to, out hEnd, 2.0f, navMeshAreaMask);

        if (!haveStart || !haveEnd) return false;

        bool ok = NavMesh.CalculatePath(hStart.position, hEnd.position, navMeshAreaMask, pathOut);
        if (!ok || pathOut.status == NavMeshPathStatus.PathInvalid) return false;

        return pathOut.corners != null && pathOut.corners.Length > 0;
#else
        return false;
#endif
    }

    // ---------- Find helpers ----------
    private bool IsHelicopterComplete()
    {
        if (gm == null) gm = GameManager.Instance;
        if (gm == null) return false;

        int have = gm.CollectedHelicopterParts != null ? gm.CollectedHelicopterParts.Count : 0;
        int need = (gm.helicopterPartPrefabs != null && gm.helicopterPartPrefabs.Length > 0)
            ? gm.helicopterPartPrefabs.Length
            : Mathf.Max(1, targetCountIfNoUniqueList);

        return have >= need;
    }

    private Transform FindClosestBossInRangeOrNull()
    {
        if (string.IsNullOrEmpty(bossTag)) return null;
        var bosses = GameObject.FindGameObjectsWithTag(bossTag);
        if (bosses == null || bosses.Length == 0) return null;

        Transform closest = null;
        float minDistSq = float.PositiveInfinity;
        Vector3 p = player ? player.position : Vector3.zero;
        float rangeSq = bossAutoAcquireRange * bossAutoAcquireRange;

        foreach (var b in bosses)
        {
            if (!b || !b.activeInHierarchy) continue;
            float dsq = FlatDistanceSq(p, b.transform.position);
            if (dsq <= rangeSq && dsq < minDistSq)
            {
                minDistSq = dsq;
                closest = b.transform;
            }
        }
        return closest;
    }

    private Transform FindClosestPart()
    {
        if (string.IsNullOrEmpty(helicopterPartTag)) return null;
        var parts = GameObject.FindGameObjectsWithTag(helicopterPartTag);
        if (parts == null || parts.Length == 0) return null;

        Transform closest = null;
        float minDistSq = float.PositiveInfinity;
        Vector3 p = player ? player.position : Vector3.zero;

        foreach (var part in parts)
        {
            if (!part || !part.activeInHierarchy) continue;
            float dsq = FlatDistanceSq(p, part.transform.position);
            if (dsq < minDistSq) { minDistSq = dsq; closest = part.transform; }
        }
        return closest;
    }

    private Transform FindClosestVisibleAISpawn()
    {
        if (string.IsNullOrEmpty(aiSpawnTag)) return null;

        var spawns = GameObject.FindGameObjectsWithTag(aiSpawnTag);
        if (spawns == null || spawns.Length == 0) return null;

        Transform closest = null;
        float minDistSq = float.PositiveInfinity;
        Vector3 p = player ? player.position : Vector3.zero;

        foreach (var s in spawns)
        {
            if (!s || !s.activeInHierarchy) continue;
            if (!SpawnHasVisibleRenderer(s)) continue; // require visible mesh

            float dsq = FlatDistanceSq(p, s.transform.position);
            if (dsq < minDistSq) { minDistSq = dsq; closest = s.transform; }
        }
        return closest;
    }

    // Visible if it has an enabled MeshRenderer or SkinnedMeshRenderer somewhere under it
    private bool SpawnHasVisibleRenderer(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (!r || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (r is MeshRenderer || r is SkinnedMeshRenderer) return true;
        }
        return false;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private static float FlatDistanceSq(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return (a - b).sqrMagnitude;
    }
}

// Small helper to apply alpha without touching RGB
public static class ColorExtensions
{
    public static Color WithAlpha(this Color c, float a) { c.a = a; return c; }
}
