// Assets/Scripts/Fog of War/FogOfWarManager.cs
using UnityEngine;

[DisallowMultipleComponent]
public class FogOfWarManager : MonoBehaviour
{
    public static FogOfWarManager Instance { get; private set; }

    [Header("Mask Texture")]
    public int textureSize = 1024;
    [Range(0f, 1f)] public float initialFogValue = 1f;
    public bool forceFullFogOnStart = true;
    public bool clearOnAwake = true;

    [Header("World Mapping (XZ)")]
    [Tooltip("Lower-left (min.x, min.z) of the playable area in world space.")]
    public Vector2 worldOriginXZ;
    [Tooltip("Size (width in X, depth in Z) of the playable area in world space.")]
    public Vector2 worldSizeXZ;

    [Header("Brush")]
    [Tooltip("World-space brush radius (meters). Overridden by binder sizing.")]
    public float brushWorldRadius = 3f;
    [Tooltip("Edge softness as a FRACTION of the brush radius. 0.0 = hard edge, 0.35 = soft rim.")]
    [Range(0f, 0.5f)] public float brushFeatherPercent = 0.30f;
    [Tooltip("Curve shaping (0 = very soft, 1 = sharper falloff).")]
    [Range(0f, 1f)] public float brushHardness = 0.7f;
    [Tooltip("Max world distance per segment when sweeping along movement.")]
    public float maxWorldStep = 0.8f;

    [Header("Safety / Diagnostics")]
    [Tooltip("Clamp computed UVs to [0,1] before painting. Prevents mirroring artifacts.")]
    public bool clampUVTo01 = true;
    [Tooltip("Show mapping warnings after a small startup grace period.")]
    public bool mappingWarnings = true;
    [Tooltip("Verbose logs for mapping and paint operations.")]
    public bool diagLogs = false;
    [Tooltip("Seconds to wait before emitting mapping warnings at startup.")]
    public float warningGraceSeconds = 0.5f;

    [Header("Materials / Shaders")]
    public Shader paintShader;  // Hidden/FOW/Paint
    private Material _paintMat;

    private RenderTexture _rtA, _rtB;
    private RenderTexture _currentRT, _otherRT;
    private Vector2 _lastUV = new Vector2(-9999f, -9999f);
    private bool _lastUVValid, _clearedOnce;

    private float _enabledAt;
    private bool _warnedMapping;

    public RenderTexture FogMask => _currentRT;

    // Calibration readiness
    const float MAP_EPS = 0.001f;
    private bool _isCalibrated;
    public bool IsCalibrated => _isCalibrated && worldSizeXZ.x > MAP_EPS && worldSizeXZ.y > MAP_EPS;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (paintShader == null) paintShader = Shader.Find("Hidden/FOW/Paint");
        _paintMat = new Material(paintShader) { hideFlags = HideFlags.HideAndDontSave };

        AllocateRTs();
        _enabledAt = Time.realtimeSinceStartup;
        _warnedMapping = false;

        if (forceFullFogOnStart) ClearMask(1f);
        else if (clearOnAwake) ClearMask(initialFogValue);
    }

    void Start()
    {
        if (!_clearedOnce) ClearMask(forceFullFogOnStart ? 1f : initialFogValue);
        // Don’t warn immediately; binder/mapgen may calibrate shortly.
        Invoke(nameof(SanityCheckMapping), warningGraceSeconds);
    }

    void OnDestroy()
    {
        if (_paintMat) DestroyImmediate(_paintMat);
        ReleaseRT(ref _rtA); ReleaseRT(ref _rtB);
        _currentRT = _otherRT = null;
        if (Instance == this) Instance = null;
    }

    void OnValidate()
    {
        textureSize = Mathf.Max(16, textureSize);
        worldSizeXZ.x = Mathf.Max(0f, worldSizeXZ.x);
        worldSizeXZ.y = Mathf.Max(0f, worldSizeXZ.y);
        warningGraceSeconds = Mathf.Max(0f, warningGraceSeconds);
    }

    void AllocateRTs()
    {
        CreateRT(ref _rtA, textureSize);
        CreateRT(ref _rtB, textureSize);
        _currentRT = _rtA; _otherRT = _rtB;
        _clearedOnce = false;
    }

    void CreateRT(ref RenderTexture rt, int size)
    {
        ReleaseRT(ref rt);
        var fmt = RenderTextureFormat.R8;
        if (!SystemInfo.SupportsRenderTextureFormat(fmt)) fmt = RenderTextureFormat.ARGB32;

        rt = new RenderTexture(size, size, 0, fmt, RenderTextureReadWrite.Linear)
        {
            name = "FOW_Mask",
            useMipMap = false,
            autoGenerateMips = false,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        rt.Create();
    }

    static void ReleaseRT(ref RenderTexture rt)
    {
        if (rt != null) { if (rt.IsCreated()) rt.Release(); Object.DestroyImmediate(rt); rt = null; }
    }

    // ---- Clear / mapping ----------------------------------------------------

    public void ClearMask(float value = 1f)
    {
        EnsureMat();
        _paintMat.SetFloat("_ClearValue", Mathf.Clamp01(value));
        Graphics.Blit(null, _rtA, _paintMat, 1);
        Graphics.Blit(null, _rtB, _paintMat, 1);
        _currentRT = _rtA; _otherRT = _rtB;
        _lastUVValid = false; _clearedOnce = true;

        if (diagLogs) Debug.Log($"[FOW] ClearMask({value}) -> RTs reset.");
    }

    /// <summary>
    /// Calibrates world->UV mapping from playable bounds (XZ) and marks calibrated.
    /// </summary>
    public void AutoCalibrateUV(Bounds playableBounds)
    {
        worldOriginXZ = new Vector2(playableBounds.min.x, playableBounds.min.z);
        worldSizeXZ = new Vector2(Mathf.Max(0f, playableBounds.size.x),
                                  Mathf.Max(0f, playableBounds.size.z));

        _isCalibrated = (worldSizeXZ.x > MAP_EPS && worldSizeXZ.y > MAP_EPS);

        if (mappingWarnings && !_isCalibrated && !_warnedMapping && (Time.realtimeSinceStartup - _enabledAt) > warningGraceSeconds)
        {
            _warnedMapping = true;
            Debug.LogWarning("[FOW] AutoCalibrateUV got degenerate bounds (size too small). Painting will be skipped.");
        }

        if (diagLogs)
            Debug.Log($"[FOW] AutoCalibrateUV -> origin:{worldOriginXZ} size:{worldSizeXZ} calibrated:{_isCalibrated}");
    }

    /// <summary>
    /// Convert world position to normalized mask UV. Optionally clamps to [0,1].
    /// Returns false if mapping is not calibrated.
    /// </summary>
    public bool TryWorldToUV(Vector3 worldPos, out Vector2 uv)
    {
        uv = default;
        if (!IsCalibrated) return false;

        float sx = Mathf.Max(MAP_EPS, worldSizeXZ.x);
        float sz = Mathf.Max(MAP_EPS, worldSizeXZ.y);
        float u = (worldPos.x - worldOriginXZ.x) / sx;
        float v = (worldPos.z - worldOriginXZ.y) / sz;

        if (clampUVTo01)
        {
            u = Mathf.Clamp01(u);
            v = Mathf.Clamp01(v);
        }

        uv = new Vector2(u, v);
        return true;
    }

    public float WorldRadiusToUV(float rWorld)
    {
        // Map width in X for scale; change to average if you prefer.
        return rWorld / Mathf.Max(MAP_EPS, worldSizeXZ.x);
    }

    // ---- Painting API (guarded) --------------------------------------------

    public void CutAtWorldPoint(Vector3 worldPos, float? radiusOverride = null)
    {
        if (!IsCalibrated) return; // guard: no painting until mapping is valid

        if (!TryWorldToUV(worldPos, out var uv)) return;
        var rUV = WorldRadiusToUV(radiusOverride ?? brushWorldRadius);
        PaintDiscUV(uv, rUV);
        _lastUV = uv; _lastUVValid = true;

        if (diagLogs)
            Debug.Log($"[FOW] CutAtWorldPoint world:{worldPos} -> uv:{uv} rUV:{rUV}");
    }

    public void CutAlongWorldSegment(Vector3 prevWorld, Vector3 currWorld, float? radiusOverride = null)
    {
        if (!IsCalibrated) return; // guard

        if (!TryWorldToUV(prevWorld, out var a)) return;
        if (!TryWorldToUV(currWorld, out var b)) return;
        var rUV = WorldRadiusToUV(radiusOverride ?? brushWorldRadius);

        float distUV = Vector2.Distance(a, b);
        int steps = Mathf.Max(1, Mathf.CeilToInt(distUV / WorldRadiusToUV(maxWorldStep)));
        for (int i = 0; i <= steps; i++)
        {
            float t = steps == 0 ? 1f : (float)i / steps;
            var p = Vector2.Lerp(a, b, t);
            PaintDiscUV(p, rUV);
        }
        _lastUV = b; _lastUVValid = true;

        if (diagLogs)
            Debug.Log($"[FOW] CutAlongWorldSegment prev:{prevWorld} curr:{currWorld} steps:{steps} rUV:{rUV}");
    }

    public void CutContinuous(Vector3 worldPos, float? radiusOverride = null)
    {
        if (!IsCalibrated) return; // guard

        if (_lastUVValid)
        {
            var lastWorld = new Vector3(
                worldOriginXZ.x + _lastUV.x * Mathf.Max(MAP_EPS, worldSizeXZ.x),
                worldPos.y,
                worldOriginXZ.y + _lastUV.y * Mathf.Max(MAP_EPS, worldSizeXZ.y)
            );
            CutAlongWorldSegment(lastWorld, worldPos, radiusOverride);
        }
        else
        {
            CutAtWorldPoint(worldPos, radiusOverride);
        }
    }

    void PaintDiscUV(Vector2 uv, float rUV)
    {
        EnsureMat();
        if (!_clearedOnce) ClearMask(forceFullFogOnStart ? 1f : initialFogValue);

        _paintMat.SetTexture("_MainTex", _currentRT);
        _paintMat.SetVector("_Brush", new Vector4(uv.x, uv.y, rUV, Mathf.Clamp01(brushHardness)));
        _paintMat.SetFloat("_Feather", Mathf.Clamp01(brushFeatherPercent));

        Graphics.Blit(_currentRT, _otherRT, _paintMat, 0); // pass 0 = paint disc
        var tmp = _currentRT; _currentRT = _otherRT; _otherRT = tmp;
    }

    void EnsureMat()
    {
        if (_paintMat == null)
        {
            paintShader = paintShader ?? Shader.Find("Hidden/FOW/Paint");
            _paintMat = new Material(paintShader) { hideFlags = HideFlags.HideAndDontSave };
        }
    }

    void SanityCheckMapping()
    {
        if (!mappingWarnings || _warnedMapping) return;
        if (IsCalibrated) return; // okay now

        _warnedMapping = true;
        Debug.LogWarning("[FOW] Mapping size is ~0. Did you call AutoCalibrateUV with valid bounds? Painting is paused until calibration.");
    }

    // Editor helpers
    [ContextMenu("FOW/Clear to FULL (1)")] void C1() => ClearMask(1f);
    [ContextMenu("FOW/Clear to HALF (0.5)")] void C2() => ClearMask(0.5f);
    [ContextMenu("FOW/Clear to NONE (0)")] void C3() => ClearMask(0f);
}
