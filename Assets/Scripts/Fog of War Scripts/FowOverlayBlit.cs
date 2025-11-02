// Assets/Scripts/Fog of War/FowOverlayBlit.cs
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(1000)] // run late so manager has time to init
public class FowOverlayBlit : MonoBehaviour
{
    [Header("Material (auto if empty)")]
    public Material overlayMat;   // Shader: Hidden/FOW/OverlayBlit

    [Header("Manager (shared mask + mapping)")]
    public bool autoFindManager = true;
    public string managerTag = "FOWManager";
    public FogOfWarManager manager;

    [Header("Appearance")]
    public Color fogColor = Color.black;
    [Range(0f, 1f)] public float fogOpacity = 1f;

    [Header("Mapping")]
    [Tooltip("If true, samples mask in screen UVs (works with perspective cams).")]
    public bool screenSpaceFallback = false;

    [Header("Diagnostics")]
    public DebugMode debugMode = DebugMode.Off;
    public bool showMaskPreview = true;
    [Range(64, 512)] public int maskPreviewSize = 128;
    [Tooltip("Seconds to wait after enabling before logging 'not ready'.")]
    [Min(0f)] public float readyGraceSeconds = 1.0f;

    private Camera _cam;
    private bool _warnedOnce;
    private float _enabledAt;

    public enum DebugMode { Off = 0, ForceBlack = 1, ShowMask = 2 }

    // Shader property IDs
    static readonly int _MainTexID = Shader.PropertyToID("_MainTex");
    static readonly int _FogMaskID = Shader.PropertyToID("_FogMask");
    static readonly int _FogColorID = Shader.PropertyToID("_FogColor");
    static readonly int _FogOpacityID = Shader.PropertyToID("_FogOpacity");
    static readonly int _DebugModeID = Shader.PropertyToID("_DebugMode");
    static readonly int _ScreenSpaceToggleID = Shader.PropertyToID("_ScreenSpaceToggle");
    static readonly int _FogWorldRectID = Shader.PropertyToID("_FogWorldRect");
    static readonly int _CamWorldRectID = Shader.PropertyToID("_CamWorldRect");

    void Reset() { TryAutobindManager(); }

    void OnEnable()
    {
        _cam = GetComponent<Camera>();
        _enabledAt = Time.realtimeSinceStartup;
        _warnedOnce = false;
        EnsureMaterial();
        if (autoFindManager && manager == null) TryAutobindManager();
    }

    void OnValidate()
    {
        EnsureMaterial();
        if (autoFindManager && manager == null) TryAutobindManager();
    }

    void EnsureMaterial()
    {
        if (overlayMat == null)
        {
            var sh = Shader.Find("Hidden/FOW/OverlayBlit");
            if (sh != null) overlayMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        }
    }

    void TryAutobindManager()
    {
        if (!string.IsNullOrEmpty(managerTag))
        {
            var go = GameObject.FindGameObjectWithTag(managerTag);
            if (go)
            {
                var m = go.GetComponent<FogOfWarManager>();
                if (m) { manager = m; return; }
            }
        }

        // Find even if inactive
#if UNITY_2023_1_OR_NEWER
        manager = manager ?? FindAnyObjectByType<FogOfWarManager>(FindObjectsInactive.Include);
#else
        manager = manager ?? FindObjectOfType<FogOfWarManager>(true);
#endif
    }

    bool ManagerAndMaskReady()
    {
        return manager != null && manager.FogMask != null;
    }

    void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
#if UNITY_EDITOR
        // Skip warnings while authoring in Edit Mode
        if (!Application.isPlaying)
        {
            Graphics.Blit(src, dst);
            return;
        }
#endif

        EnsureMaterial();
        if (overlayMat == null)
        {
            if (!_warnedOnce)
            {
                Debug.LogWarning("[FOW] OverlayBlit: overlay material missing (Hidden/FOW/OverlayBlit).", this);
                _warnedOnce = true;
            }
            Graphics.Blit(src, dst);
            return;
        }

        if (!ManagerAndMaskReady())
        {
            // Allow a short startup window before warning
            if (!_warnedOnce && (Time.realtimeSinceStartup - _enabledAt) > readyGraceSeconds)
            {
                Debug.LogWarning("[FOW] OverlayBlit: FogOfWarManager or FogMask not ready.\n" +
                                 " • Make sure a FogOfWarManager exists and creates FogMask in Awake/OnEnable.\n" +
                                 " • Tag it 'FOWManager' or assign explicitly in the inspector.", this);
                _warnedOnce = true;
            }
            Graphics.Blit(src, dst);
            return;
        }

        // Auto switch to screen-space if cam is perspective
        bool useScreenSpace = screenSpaceFallback || !_cam.orthographic;

        // Bind props
        overlayMat.SetTexture(_MainTexID, src);
        overlayMat.SetTexture(_FogMaskID, manager.FogMask);
        overlayMat.SetColor(_FogColorID, fogColor);
        overlayMat.SetFloat(_FogOpacityID, Mathf.Clamp01(fogOpacity));
        overlayMat.SetFloat(_DebugModeID, (float)debugMode);
        overlayMat.SetVector(_ScreenSpaceToggleID, new Vector4(useScreenSpace ? 1f : 0f, 0, 0, 0));

        if (useScreenSpace)
        {
            // Identity rects => fogUV = screenUV
            overlayMat.SetVector(_FogWorldRectID, new Vector4(0, 0, 1, 1));
            overlayMat.SetVector(_CamWorldRectID, new Vector4(0, 0, 1, 1));
        }
        else
        {
            float viewHalfH = _cam.orthographicSize;
            float viewHalfW = viewHalfH * _cam.aspect;
            float left = _cam.transform.position.x - viewHalfW;
            float bottom = _cam.transform.position.z - viewHalfH;
            float sizeX = 2f * viewHalfW;
            float sizeZ = 2f * viewHalfH;

            var origin = manager.worldOriginXZ; // Vector2
            var size = manager.worldSizeXZ;   // Vector2

            overlayMat.SetVector(_FogWorldRectID,
                new Vector4(origin.x, origin.y,
                            Mathf.Max(0.0001f, size.x),
                            Mathf.Max(0.0001f, size.y)));

            overlayMat.SetVector(_CamWorldRectID,
                new Vector4(left, bottom,
                            Mathf.Max(0.0001f, sizeX),
                            Mathf.Max(0.0001f, sizeZ)));
        }

        Graphics.Blit(src, dst, overlayMat, 0);
    }

#if UNITY_EDITOR
    void OnGUI()
    {
        if (!Application.isPlaying) return;
        if (!showMaskPreview || !ManagerAndMaskReady()) return;

        int size = maskPreviewSize;
        GUI.DrawTexture(new Rect(8, 8, size, size), manager.FogMask, ScaleMode.ScaleToFit, false);
        GUI.Label(new Rect(10, size + 10, 320, 20), $"FOW Mask ({manager.FogMask.width}x{manager.FogMask.height})");
    }
#endif
}
