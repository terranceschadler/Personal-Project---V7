// Assets/Scripts/Fog of War Scripts/FowMapBoundsBinder.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class FowMapBoundsBinder : MonoBehaviour
{
    public FogOfWarManager manager;

    // ---------- Primary: compute bounds from spawned RoomTiles ----------
    [Header("RoomTile Scan (primary)")]
    [Tooltip("Tag used by spawned room tiles that make up the playable area.")]
    public string roomTileTag = "RoomTile";
    [Tooltip("Only include objects on these layers (Everything = no filter).")]
    public LayerMask includeLayers = ~0;
    [Tooltip("Extra padding (meters) added around discovered bounds on X/Z.")]
    public float extraMargin = 0.5f;

    // ---------- Tile Geometry Hints ----------
    [Header("Tile Geometry Hints")]
    [Tooltip("Prefer tight bounds from tile root positions (best for axis-aligned tile grids).")]
    public bool computeFromTilePositions = true;
    [Tooltip("Fallback half-size for a tile if renderers are missing/unreliable.")]
    public float tileHalfSizeGuess = 4f;
    [Tooltip("Ignore tile renderers whose XZ area is > median * this factor (defends against stray huge meshes).")]
    public float rendererOutlierAreaMultiplier = 6f;

    // ---------- Brush sizing ----------
    public enum BrushSizingMode { PercentOfView, PixelsOnMask, PercentOfMapWidth }

    [Header("Brush Sizing")]
    [Tooltip("How to choose the brush radius so holes look right.")]
    public BrushSizingMode sizingMode = BrushSizingMode.PercentOfView;

    [Tooltip("Camera used when sizing by PercentOfView. If null, uses Camera.main.")]
    public Camera referenceCameraForSizing;

    [Tooltip("PERCENT OF DIAMETER of the SMALLER orthographic view dimension.\n" +
             "Example: 0.80 = brush DIAMETER is 80% of min(viewW,viewH). Radius = 0.5 * that.")]
    [Range(0.02f, 2.0f)] public float viewPercentDiameter = 0.80f; // bigger default + allows >100% of view

    [Tooltip("Brush radius as % of map width (used when sizingMode = PercentOfMapWidth).")]
    [Range(0.001f, 2.0f)] public float brushPercentOfWidth = 0.25f; // bigger default

    [Tooltip("Minimum brush radius in meters (applies to EVERY sizing mode).")]
    public float minBrushWorldRadius = 8.0f; // bigger default

    [Tooltip("Never let the brush be smaller than this many mask pixels (also used in PixelsOnMask mode).")]
    public int minBrushPixelRadius = 96; // bigger default

    [Tooltip("Soft cap in mask pixels (0 = no cap).")]
    public int maxBrushPixelRadius = 0;

    [Tooltip("Set max step to this fraction of brush radius (keeps continuous trails solid).")]
    [Range(0.25f, 1f)] public float stepAsFractionOfBrush = 0.95f; // denser sweep for larger brushes

    [Header("Extra Boost (applied after mode calc)")]
    [Tooltip("Multiply the computed brush radius to make it even larger.")]
    public float brushScale = 1.75f; // global multiplier
    [Tooltip("Add this many meters to the computed brush radius.")]
    public float additiveWorldRadius = 2.0f; // additive boost
    [Tooltip("Optional hard cap in meters (0 = no cap).")]
    public float finalBrushMaxWorld = 0f;

    // ---------- Mask reset ----------
    [Header("Mask Reset")]
    [Tooltip("Re-clear to FULL FOG after binding (recommended for regenerate flows).")]
    public bool clearMaskAfterBind = true;

    // ---------- Map generator hook ----------
    [Header("Map Generator Hook")]
    [Tooltip("Auto-wait for RandomMapGenerator completion and rebind on RegenerateMap().")]
    public bool bindOnMapCompleted = true;
    [Tooltip("Delay (s) after map completion before scanning tiles (lets late tiles finish instantiating).")]
    public float postCompleteDelay = 0.15f;

    // ---------- Startup / legacy fallback ----------
    [Header("Start-up Fallback (if generator not found)")]
    public bool autoFindOnStart = true;
    [Tooltip("Look for tag first; if empty, falls back to largest Renderer in scene.")]
    public string preferredTag = "MapBounds";
    [Tooltip("Delay seconds to give your MapGenerator time to finish (used only when not binding to generator event).")]
    public float waitSeconds = 0.25f;

    // ---------- Fallback sources kept from original ----------
    [Header("Where to read bounds from (fallback)")]
    public Renderer boundsFromRenderer;   // e.g., ground mesh / combined floor
    public Collider boundsFromCollider;   // or a BoxCollider that frames the map

    // ---------- Diagnostics ----------
    [Header("Diagnostics")]
    public bool diagLogs = true;

    // ---------- Gizmos ----------
    [Header("Gizmos")]
    [Tooltip("Master toggle: requires the global Gizmos button ON in Scene/Game view.")]
    public bool showGizmos = true;
    [Tooltip("If ON, draws only when this GameObject is selected.")]
    public bool drawWhenSelectedOnly = true;
    [Tooltip("If ON, also draws in the Game view (Unity's Game view must have Gizmos enabled).")]
    public bool drawInGameView = true;
    public Color gizmoFill = new Color(0f, 1f, 0.4f, 0.25f);
    public Color gizmoLine = new Color(0f, 0.8f, 0.25f, 1f);

    // ---------- Internals ----------
    private RandomMapGenerator _mapGen;
    private Bounds _lastBound;
    private bool _hasLastBound;

    void OnValidate()
    {
        extraMargin = Mathf.Max(0f, extraMargin);
        tileHalfSizeGuess = Mathf.Max(0.01f, tileHalfSizeGuess);
        rendererOutlierAreaMultiplier = Mathf.Max(1f, rendererOutlierAreaMultiplier);

        minBrushWorldRadius = Mathf.Max(0f, minBrushWorldRadius);
        stepAsFractionOfBrush = Mathf.Clamp01(stepAsFractionOfBrush);

        minBrushPixelRadius = Mathf.Max(1, minBrushPixelRadius);
        maxBrushPixelRadius = Mathf.Max(0, maxBrushPixelRadius);

        waitSeconds = Mathf.Max(0f, waitSeconds);
        postCompleteDelay = Mathf.Max(0f, postCompleteDelay);

        brushScale = Mathf.Max(0.01f, brushScale);
        additiveWorldRadius = Mathf.Max(0f, additiveWorldRadius);
        finalBrushMaxWorld = Mathf.Max(0f, finalBrushMaxWorld);
    }

    void Start()
    {
        if (!manager) manager = FogOfWarManager.Instance;

        if (bindOnMapCompleted)
        {
            _mapGen = FindObjectOfType<RandomMapGenerator>();
            if (_mapGen != null)
            {
                _mapGen.OnMapCompleted += HandleMapCompleted;
                if (_mapGen.MapCompleted)
                {
                    if (diagLogs) Debug.Log("[FOW] Map already completed; binding now.");
                    StartCoroutine(BindRoutine(postCompleteDelay));
                }
                return; // bind via event
            }
            else if (diagLogs)
            {
                Debug.LogWarning("[FOW] RandomMapGenerator not found; using fallback wait path.");
            }
        }
        StartCoroutine(BindRoutine(waitSeconds));
    }

    void OnDisable()
    {
        if (_mapGen != null) _mapGen.OnMapCompleted -= HandleMapCompleted;
    }

    void HandleMapCompleted() => StartCoroutine(BindRoutine(postCompleteDelay));

    IEnumerator BindRoutine(float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        ApplyIfReady();
    }

    // ---------- Core binding ----------
    void ApplyIfReady()
    {
        if (!manager)
        {
            if (diagLogs) Debug.LogWarning("[FOW] No FogOfWarManager; cannot bind.");
            return;
        }

        Bounds bounds;
        if (!TryBoundsFromRoomTiles(out bounds))
        {
            if (autoFindOnStart)
            {
                if (boundsFromRenderer == null && boundsFromCollider == null)
                {
                    if (!string.IsNullOrEmpty(preferredTag))
                    {
                        var tagged = GameObject.FindGameObjectWithTag(preferredTag);
                        if (tagged)
                        {
                            boundsFromRenderer = tagged.GetComponentInChildren<Renderer>();
                            boundsFromCollider = tagged.GetComponentInChildren<Collider>();
                            if (diagLogs && (boundsFromRenderer || boundsFromCollider))
                                Debug.Log("[FOW] Using object tagged 'MapBounds' for fallback bounds.");
                        }
                    }
                }
                if (boundsFromRenderer == null && boundsFromCollider == null)
                {
                    if (TryLargestRendererBounds(out bounds))
                        goto HAVE_BOUNDS;
                }
            }

            if (boundsFromCollider != null) bounds = boundsFromCollider.bounds;
            else if (boundsFromRenderer != null) bounds = boundsFromRenderer.bounds;
            else
            {
                if (diagLogs) Debug.LogWarning("[FOW] No bounds source found; using default 100x100 at origin.");
                bounds = new Bounds(Vector3.zero, new Vector3(100, 1, 100));
            }
        }

    HAVE_BOUNDS:

        if (extraMargin > 0f)
        {
            var s = bounds.size;
            s.x += 2f * extraMargin;
            s.z += 2f * extraMargin;
            bounds.size = new Vector3(s.x, Mathf.Max(s.y, 1f), s.z);
        }

        _lastBound = bounds;
        _hasLastBound = true;

        manager.AutoCalibrateUV(bounds);

        // Compute base radius in WORLD units using the selected mode
        float worldRadius = ComputeWorldBrushRadius();

        // Apply global boost
        worldRadius = worldRadius * brushScale + additiveWorldRadius;
        if (finalBrushMaxWorld > 0f) worldRadius = Mathf.Min(worldRadius, finalBrushMaxWorld);

        manager.brushWorldRadius = worldRadius;
        manager.maxWorldStep = Mathf.Max(0.1f, worldRadius * stepAsFractionOfBrush);

        if (clearMaskAfterBind) manager.ClearMask(1f);

        if (diagLogs)
            Debug.Log($"[FOW] Bound: originXZ={manager.worldOriginXZ} sizeXZ={manager.worldSizeXZ} mode={sizingMode} brushR={manager.brushWorldRadius:0.00} step={manager.maxWorldStep:0.00}");
    }

    float ComputeWorldBrushRadius()
    {
        float worldR = minBrushWorldRadius;

        switch (sizingMode)
        {
            case BrushSizingMode.PercentOfView:
                {
                    Camera cam = referenceCameraForSizing ? referenceCameraForSizing : Camera.main;

                    if (cam && cam.orthographic)
                    {
                        float viewH = 2f * cam.orthographicSize;
                        float viewW = viewH * Mathf.Max(0.0001f, cam.aspect);
                        float minDim = Mathf.Min(viewW, viewH);
                        float radiusFromView = 0.5f * Mathf.Max(0.0f, viewPercentDiameter) * minDim;
                        worldR = Mathf.Max(worldR, radiusFromView);
                    }
                    else
                    {
                        if (diagLogs) Debug.LogWarning("[FOW] PercentOfView selected but no orthographic camera found. Falling back to PixelsOnMask.");
                        goto case BrushSizingMode.PixelsOnMask;
                    }
                    break;
                }

            case BrushSizingMode.PixelsOnMask:
                {
                    int pxFromPercent = Mathf.RoundToInt(0.5f * brushPercentOfWidth * manager.textureSize);
                    int px = Mathf.Max(minBrushPixelRadius, pxFromPercent);
                    if (maxBrushPixelRadius > 0) px = Mathf.Min(px, maxBrushPixelRadius);
                    float worldFromPx = (px / (float)manager.textureSize) * Mathf.Max(0.0001f, manager.worldSizeXZ.x);
                    worldR = Mathf.Max(worldR, worldFromPx);
                    break;
                }

            case BrushSizingMode.PercentOfMapWidth:
            default:
                {
                    float worldFromPercent = brushPercentOfWidth * Mathf.Max(0.0001f, manager.worldSizeXZ.x);
                    worldR = Mathf.Max(worldR, worldFromPercent);
                    break;
                }
        }

        return worldR;
    }

    // ---------- Helpers ----------
    bool TryBoundsFromRoomTiles(out Bounds total)
    {
        total = default;

        if (string.IsNullOrEmpty(roomTileTag))
            return false;

        var tiles = GameObject.FindGameObjectsWithTag(roomTileTag);
        if (tiles == null || tiles.Length == 0)
        {
            if (diagLogs) Debug.Log("[FOW] No RoomTile objects found for bounds scan.");
            return false;
        }

        if (computeFromTilePositions && TryBoundsFromTilePositions(tiles, out total))
            return true;

        bool hasAny = false;
        for (int i = 0; i < tiles.Length; i++)
        {
            var go = tiles[i];
            if (!go || !go.activeInHierarchy) continue;
            if ((includeLayers.value & (1 << go.layer)) == 0) continue;

            Bounds tb;
            if (!TryBoundsFromObject(go, out tb)) continue;

            if (!hasAny) { total = tb; hasAny = true; }
            else { total.Encapsulate(tb); }
        }

        if (!hasAny) return false;
        if (total.size.y < 0.1f) total.size = new Vector3(total.size.x, 1f, total.size.z);
        return true;
    }

    bool TryBoundsFromTilePositions(GameObject[] tiles, out Bounds b)
    {
        b = default;

        float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;

        List<float> halfSizes = new List<float>(tiles.Length);

        for (int i = 0; i < tiles.Length; i++)
        {
            var go = tiles[i];
            if (!go || !go.activeInHierarchy) continue;
            if ((includeLayers.value & (1 << go.layer)) == 0) continue;

            var p = go.transform.position;
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.z < minZ) minZ = p.z;
            if (p.z > maxZ) maxZ = p.z;

            float hs = EstimateHalfSizeFromRenderers(go, out _);
            if (hs > 0f) halfSizes.Add(hs);
        }

        if (float.IsInfinity(minX) || float.IsInfinity(minZ))
            return false;

        float halfSize = tileHalfSizeGuess;
        if (halfSizes.Count > 0)
        {
            halfSizes.Sort();
            float median = halfSizes[halfSizes.Count / 2];
            halfSize = Mathf.Max(halfSize, median);
        }

        float width = (maxX - minX) + 2f * halfSize;
        float depth = (maxZ - minZ) + 2f * halfSize;
        Vector3 center = new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f);

        b = new Bounds(center, new Vector3(Mathf.Max(0.01f, width), 1f, Mathf.Max(0.01f, depth)));
        return true;
    }

    float EstimateHalfSizeFromRenderers(GameObject root, out float areaXZ)
    {
        areaXZ = 0f;
        var rends = root.GetComponentsInChildren<Renderer>(true);
        if (rends == null || rends.Length == 0) return -1f;

        List<float> areas = new List<float>(rends.Length);
        foreach (var r in rends)
        {
            if (!r || !r.enabled) continue;
            var s = r.bounds.size;
            float a = Mathf.Abs(s.x * s.z);
            if (a > 0f) areas.Add(a);
        }
        if (areas.Count == 0) return -1f;
        areas.Sort();
        float medianArea = areas[areas.Count / 2];

        bool started = false;
        Bounds u = default;
        foreach (var r in rends)
        {
            if (!r || !r.enabled) continue;
            var s = r.bounds.size;
            float a = Mathf.Abs(s.x * s.z);
            if (a <= 0f) continue;
            if (a > medianArea * rendererOutlierAreaMultiplier) continue;

            if (!started) { u = r.bounds; started = true; }
            else u.Encapsulate(r.bounds);
        }
        if (!started) return -1f;

        areaXZ = Mathf.Abs(u.size.x * u.size.z);
        return 0.5f * Mathf.Max(u.size.x, u.size.z);
    }

    static bool TryBoundsFromObject(GameObject root, out Bounds b)
    {
        b = default;

        var rends = root.GetComponentsInChildren<Renderer>(true);
        bool got = false;
        for (int i = 0; i < rends.Length; i++)
        {
            var r = rends[i];
            if (!r || !r.enabled) continue;
            if (!got) { b = r.bounds; got = true; }
            else { b.Encapsulate(r.bounds); }
        }
        if (got) return true;

        var cols = root.GetComponentsInChildren<Collider>(true);
        got = false;
        for (int i = 0; i < cols.Length; i++)
        {
            var c = cols[i];
            if (!c || !c.enabled) continue;
            if (!got) { b = c.bounds; got = true; }
            else { b.Encapsulate(c.bounds); }
        }
        return got;
    }

    static bool TryLargestRendererBounds(out Bounds best)
    {
        best = default;
        var all = Object.FindObjectsOfType<Renderer>();
        float bestArea = -1f;
        bool found = false;
        for (int i = 0; i < all.Length; i++)
        {
            var r = all[i];
            if (!r || !r.enabled) continue;
            var b = r.bounds;
            float areaXZ = b.size.x * b.size.z;
            if (areaXZ > bestArea)
            {
                bestArea = areaXZ; best = b; found = true;
            }
        }
        return found;
    }

    // ---------- GIZMOS ----------
    void OnDrawGizmos()
    {
        if (!showGizmos) return;
        if (drawWhenSelectedOnly) return;
        if (!drawInGameView && Application.isPlaying && Camera.current != null && Camera.current.cameraType == CameraType.Game) return;
        DrawFogRectGizmo();
    }

    void OnDrawGizmosSelected()
    {
        if (!showGizmos) return;
        if (!drawWhenSelectedOnly) return;
        if (!drawInGameView && Application.isPlaying && Camera.current != null && Camera.current.cameraType == CameraType.Game) return;
        DrawFogRectGizmo();
    }

    void DrawFogRectGizmo()
    {
        Bounds b;
        if (_hasLastBound) b = _lastBound;
        else if (manager != null && manager.worldSizeXZ.x > 0.0001f && manager.worldSizeXZ.y > 0.0001f)
        {
            var c = new Vector3(manager.worldOriginXZ.x + manager.worldSizeXZ.x * 0.5f, 0f,
                                manager.worldOriginXZ.y + manager.worldSizeXZ.y * 0.5f);
            b = new Bounds(c, new Vector3(manager.worldSizeXZ.x, 1f, manager.worldSizeXZ.y));
        }
        else return;

        Vector3 center = new Vector3(b.center.x, 0.05f, b.center.z);
        Vector3 size = new Vector3(b.size.x, 0.05f, b.size.z);

        var old = Gizmos.color;
        Gizmos.color = gizmoFill; Gizmos.DrawCube(center, size);
        Gizmos.color = gizmoLine; Gizmos.DrawWireCube(center, size);
        Gizmos.color = old;
    }
}
