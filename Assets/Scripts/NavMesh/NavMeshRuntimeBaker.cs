using UnityEngine;
using UnityEngine.AI;
using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Crash-safe runtime NavMesh baker (no NavMeshSurface required).
/// Key stability points:
/// - Builds into a fresh NavMeshData each time, then swaps it in ONLY after success.
/// - Never registers a NavMeshData in OnEnable; registration happens post-build.
/// - Optional synchronous build (debug).
/// - Waits a few frames after scene start or generator completion before baking.
/// </summary>
[DisallowMultipleComponent]
public class NavMeshRuntimeBaker : MonoBehaviour
{
    public static NavMeshRuntimeBaker Instance { get; private set; }

    [Header("Bake Scope")]
    public LayerMask includedLayers = 0;
    public int agentTypeId = 0;
    public bool autoBoundsFromIncludedLayers = true;
    public Bounds worldBounds = new Bounds(Vector3.zero, new Vector3(300, 80, 300));

    [Header("Collection Mode")]
    public NavMeshCollectGeometry collectGeometry = NavMeshCollectGeometry.PhysicsColliders;

    [Header("Safety / Performance")]
    public float buildDelaySeconds = 0.25f;
    public int maxCollidersToScan = 5000;
    public int maxSourcesToBuild = 10000;
    public float maxBoundsAxis = 2000f;
    public bool verifyMeshesAreReadable = true;
    public bool preventConcurrentBakes = true;

    [Header("Advanced")]
    public int defaultArea = 0;
    public List<NavMeshBuildMarkup> extraMarkups = new List<NavMeshBuildMarkup>();

    [Header("Diagnostics")]
    public bool logSummary = true;
    public bool logDetails = false;

    [Header("Orchestration")]
    public bool waitForGenerator = true;
    public RandomMapGenerator mapGenerator;

    [Header("Debug")]
    [Tooltip("Blocking, single-threaded build — helpful to rule out race conditions.")]
    public bool debugBuildSynchronously = false;

    [Header("Stability")]
    [Tooltip("Minimum frames to wait after entering Play or finishing generation before starting a bake.")]
    public int minFramesBeforeBake = 2;

    // State
    public bool BakeCompleted { get; private set; }
    public bool IsBaking => _baking;
    public event Action OnBakeCompleted;

    private NavMeshData _currentNavData;          // last successfully built data (kept for cleanup/replace)
    private NavMeshDataInstance _instance;        // live, registered instance
    private bool _baking;
    private AsyncOperation _activeOp;
    private int _playStartFrame;

    private void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _playStartFrame = Time.frameCount;
    }

    private void OnEnable()
    {
        if (!Application.isPlaying) return;
        BakeCompleted = false;
        // IMPORTANT: do NOT AddNavMeshData here.
    }

    private void OnDisable()
    {
        if (!Application.isPlaying) return;
        if (_instance.valid) { _instance.Remove(); _instance = default; }
        _activeOp = null;
        _baking = false;
        BakeCompleted = false;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public Coroutine RequestBake(MonoBehaviour host) => host.StartCoroutine(BakeAsyncSafely());

    public IEnumerator BakeAsyncSafely()
    {
        if (!Application.isPlaying) yield break; // keep consistent casing if you already fixed elsewhere

        // optional: wait for generator
        if (waitForGenerator && mapGenerator != null)
        {
            while (!mapGenerator.MapCompleted) { yield return null; }
            // extra settle frames
            for (int i = 0; i < minFramesBeforeBake; i++) yield return null;
        }
        else
        {
            // entering play settle frames
            int framesWaited = 0;
            while (Time.frameCount - _playStartFrame < minFramesBeforeBake)
            {
                framesWaited++;
                yield return null;
                if (framesWaited > 10) break; // safety cap
            }
        }

        if (preventConcurrentBakes && _baking)
        {
            if (logSummary) Debug.Log("[NavBake] Bake already in progress; waiting…");
            while (_baking) yield return null;
            yield break;
        }

        _baking = true;
        BakeCompleted = false;

        if (buildDelaySeconds > 0f) yield return new WaitForSeconds(buildDelaySeconds);

        if (includedLayers.value == 0)
        {
            _baking = false;
            Debug.LogError("[NavBake] 'includedLayers' is empty. Assign a dedicated Ground/NavStatic layer.");
            yield break;
        }

        var settings = NavMesh.GetSettingsByID(agentTypeId);
        if (settings.agentTypeID == -1)
        {
            if (logSummary) Debug.LogWarning("[NavBake] AgentTypeID not found; falling back to index 0.");
            settings = NavMesh.GetSettingsByIndex(0);
        }

        // compute scan bounds
        Bounds scanBounds = worldBounds;
        if (autoBoundsFromIncludedLayers)
        {
            if (!TryComputeSceneBounds(includedLayers, maxCollidersToScan, out scanBounds, out int colCount))
            {
                _baking = false; yield break;
            }
            if (logSummary) Debug.Log($"[NavBake] Auto-bounds from {colCount} collider(s): center={scanBounds.center:F2}, size={scanBounds.size:F2}");
        }
        if (ExceedsAxis(scanBounds.size, maxBoundsAxis))
        {
            _baking = false;
            Debug.LogError($"[NavBake] Bounds too large ({scanBounds.size}). Limit layers or scene size.");
            yield break;
        }

        // collect sources
        var sources = new List<NavMeshBuildSource>(1024);
        var markups = new List<NavMeshBuildMarkup>();
        if (extraMarkups != null && extraMarkups.Count > 0) markups.AddRange(extraMarkups);

        var swCollect = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            NavMeshBuilder.CollectSources(scanBounds, includedLayers, collectGeometry, defaultArea, markups, sources);
        }
        catch (Exception e)
        {
            _baking = false;
            Debug.LogError("[NavBake] CollectSources threw: " + e);
            yield break;
        }
        swCollect.Stop();

        if (sources.Count == 0)
        {
            _baking = false;
            BakeCompleted = true;
            if (logSummary) Debug.LogWarning("[NavBake] No sources found. NavMesh will be empty.");
            OnBakeCompleted?.Invoke();
            yield break;
        }
        if (sources.Count > maxSourcesToBuild)
        {
            _baking = false;
            Debug.LogError($"[NavBake] Too many sources ({sources.Count} > {maxSourcesToBuild}). Restrict layers/geometry.");
            yield break;
        }
        if (logSummary) Debug.Log($"[NavBake] Collected {sources.Count} sources in {swCollect.ElapsedMilliseconds} ms.");

        if (collectGeometry == NavMeshCollectGeometry.RenderMeshes && verifyMeshesAreReadable)
        {
            if (!AllMeshesReadable(out Mesh offender))
            {
                _baking = false;
                Debug.LogError($"[NavBake] Abort: Non-readable mesh: {offender?.name ?? "(unknown)"} (enable Read/Write).");
                yield break;
            }
        }

        // compute build bounds
        Bounds buildBounds = CalculateWorldBounds(sources);
        if (ExceedsAxis(buildBounds.size, maxBoundsAxis))
        {
            _baking = false;
            Debug.LogError($"[NavBake] Build bounds too large ({buildBounds.size}). Check layers/geometry.");
            yield break;
        }

        if (logSummary) Debug.Log($"[NavBake] Building {(debugBuildSynchronously ? "sync" : "async")}… bounds={buildBounds.size:F2}");

        // IMPORTANT: allocate a FRESH NavMeshData for this build (prevents native instability)
        var newData = new NavMeshData(agentTypeId);

        var swBuild = System.Diagnostics.Stopwatch.StartNew();
        if (debugBuildSynchronously)
        {
            try { NavMeshBuilder.UpdateNavMeshData(newData, settings, sources, buildBounds); }
            catch (Exception e)
            {
                _baking = false;
                Debug.LogError("[NavBake] UpdateNavMeshData threw: " + e);
                yield break;
            }
        }
        else
        {
            try { _activeOp = NavMeshBuilder.UpdateNavMeshDataAsync(newData, settings, sources, buildBounds); }
            catch (Exception e)
            {
                _baking = false;
                Debug.LogError("[NavBake] UpdateNavMeshDataAsync threw: " + e);
                yield break;
            }

            while (_activeOp != null && !_activeOp.isDone)
            {
                if (!Application.isPlaying) { _activeOp = null; _baking = false; yield break; }
                yield return null;
            }
            _activeOp = null;
        }
        swBuild.Stop();

        if (!Application.isPlaying) { _baking = false; yield break; }

        // Swap instance atomically
        if (_instance.valid) { _instance.Remove(); _instance = default; }
        _instance = NavMesh.AddNavMeshData(newData, Vector3.zero, Quaternion.identity);
        if (!_instance.valid)
        {
            _baking = false;
            Debug.LogError("[NavBake] Failed to register NavMeshData instance after build.");
            yield break;
        }

        // Keep reference to the current (for completeness; Unity cleans up data with scene)
        _currentNavData = newData;

        _baking = false;
        BakeCompleted = true;

        if (logSummary) Debug.Log($"[NavBake] Runtime bake complete in {swBuild.ElapsedMilliseconds} ms.");
        OnBakeCompleted?.Invoke();
    }

    // ---------------- Helpers ----------------
    private static bool ExceedsAxis(Vector3 size, float maxAxis) =>
        size.x > maxAxis || size.y > maxAxis || size.z > maxAxis;

    private static bool TryComputeSceneBounds(LayerMask layers, int maxColliders, out Bounds result, out int count)
    {
        result = default; count = 0;

        // Include inactive so freshly spawned-but-disabled tiles still count for bounds
#if UNITY_2023_1_OR_NEWER
        var cols = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        var cols = UnityEngine.Object.FindObjectsOfType<Collider>(true);
#endif

        bool init = false;

        for (int i = 0; i < cols.Length; i++)
        {
            var c = cols[i];
            if (!c || c.isTrigger) continue;
            if ((layers.value & (1 << c.gameObject.layer)) == 0) continue;

            var b = c.bounds;
            if (!init) { result = b; init = true; }
            else result.Encapsulate(b);

            count++;
            if (count > maxColliders)
            {
                Debug.LogError($"[NavBake] Too many colliders in includedLayers (> {maxColliders}). Limit to a dedicated Ground layer.");
                return false;
            }
        }

        if (!init)
        {
            Debug.LogError("[NavBake] No colliders found on 'includedLayers'. Assign proper Ground/NavStatic layers.");
            return false;
        }

        result.Expand(1.0f);
        return true;
    }

    private static Bounds CalculateWorldBounds(List<NavMeshBuildSource> sources)
    {
        if (sources == null || sources.Count == 0)
            return new Bounds(Vector3.zero, new Vector3(50, 20, 50));

        Bounds bounds = new Bounds();
        bool init = false;

        for (int i = 0; i < sources.Count; i++)
        {
            var src = sources[i];
            var mat = src.transform;
            Bounds srcBounds;

            switch (src.shape)
            {
                case NavMeshBuildSourceShape.Mesh:
                    var mesh = src.sourceObject as Mesh;
                    if (mesh == null) continue;
                    srcBounds = GetWorldBounds(mat, mesh.bounds);
                    break;

                case NavMeshBuildSourceShape.Terrain:
                    var td = src.sourceObject as TerrainData;
                    if (td == null) continue;
                    Vector3 size = td.size;
                    Vector3 pos = mat.MultiplyPoint(Vector3.zero);
                    srcBounds = new Bounds(pos + size * 0.5f, size);
                    break;

                default:
                    srcBounds = GetWorldBounds(mat, new Bounds(Vector3.zero, Vector3.one));
                    break;
            }

            if (!init) { bounds = srcBounds; init = true; }
            else { bounds.Encapsulate(srcBounds); }
        }

        bounds.Expand(1.0f);
        return bounds;
    }

    private static Bounds GetWorldBounds(Matrix4x4 mat, Bounds localBounds)
    {
        var ctr = localBounds.center;
        var ext = localBounds.extents;

        Vector3[] corners = new Vector3[8];
        int k = 0;
        for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                    corners[k++] = ctr + Vector3.Scale(ext, new Vector3(x, y, z));

        Bounds w = new Bounds(mat.MultiplyPoint3x4(corners[0]), Vector3.zero);
        for (int i = 1; i < corners.Length; i++)
            w.Encapsulate(mat.MultiplyPoint3x4(corners[i]));
        return w;
    }

    private static bool AllMeshesReadable(out Mesh offender)
    {
        offender = null;

#if UNITY_2023_1_OR_NEWER
        var filters = UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        var filters = UnityEngine.Object.FindObjectsOfType<MeshFilter>(true);
#endif

        for (int i = 0; i < filters.Length; i++)
        {
            var m = filters[i].sharedMesh;
            if (!m) continue;
            if (!m.isReadable) { offender = m; return false; }
        }
        return true;
    }
}
