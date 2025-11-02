// Assets/Scripts/Fog of War/FowAfterMapGenBinder.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class FowAfterMapGenBinder : MonoBehaviour
{
    [Header("References")]
    public FogOfWarManager manager;          // If null, will use FogOfWarManager.Instance
    public RandomMapGenerator mapGen;        // If null, auto-find

    [Header("Bounds Discovery")]
    [Tooltip("Tiles placed by your generator should carry this tag on their root or prefab.")]
    public string tileTag = "RoomTile";

    [Tooltip("If non-negative, only include objects on these layers. (-1 = ignore filter)")]
    public LayerMask includeLayers = ~0;

    [Tooltip("Optional object tagged 'MapBounds' to force bounds. Used if tile scan fails.")]
    public string mapBoundsTag = "MapBounds";

    [Tooltip("Extra padding in world meters added around discovered bounds.")]
    public float extraMargin = 0.5f;

    [Header("Timing")]
    [Tooltip("Wait this long after the map completes before scanning bounds.")]
    public float postCompleteDelay = 0.15f;

    [Tooltip("Re-run binding every time the generator fires OnMapCompleted (e.g., after RegenerateMap).")]
    public bool rebindOnEveryCompletion = true;

    [Header("Diagnostics")]
    public bool diagLogs = true;

    void OnEnable()
    {
        if (!manager) manager = FogOfWarManager.Instance;
        if (!mapGen) mapGen = FindObjectOfType<RandomMapGenerator>();

        if (mapGen != null)
        {
            mapGen.OnMapCompleted += HandleMapCompleted;

            // If the map has already completed before this script enabled, bind once now.
            if (mapGen.MapCompleted) StartCoroutine(BindSoon());
        }
        else if (diagLogs)
        {
            Debug.LogWarning("[FOW] FowAfterMapGenBinder: No RandomMapGenerator found. Waiting is harmless, but auto-bind won't run.");
        }
    }

    void OnDisable()
    {
        if (mapGen != null)
            mapGen.OnMapCompleted -= HandleMapCompleted;
    }

    void HandleMapCompleted()
    {
        if (!rebindOnEveryCompletion)
        {
            // Unsubscribe if we only want to bind once
            mapGen.OnMapCompleted -= HandleMapCompleted;
        }
        StartCoroutine(BindSoon());
    }

    IEnumerator BindSoon()
    {
        // Give instantiation a moment to settle
        if (postCompleteDelay > 0f) yield return new WaitForSeconds(postCompleteDelay);

        if (!manager) manager = FogOfWarManager.Instance;
        if (!manager)
        {
            Debug.LogWarning("[FOW] No FogOfWarManager available; cannot bind.");
            yield break;
        }

        Bounds b;
        if (!TryFindTilesBounds(out b))
        {
            if (diagLogs) Debug.LogWarning("[FOW] Tile bounds not found via tag/layers. Trying MapBounds tag…");

            var tagged = !string.IsNullOrEmpty(mapBoundsTag) ? GameObject.FindGameObjectWithTag(mapBoundsTag) : null;
            if (tagged && TryBoundsFromObject(tagged, out b))
            {
                // ok
            }
            else
            {
                if (diagLogs) Debug.LogWarning("[FOW] MapBounds tag not found; falling back to largest Renderer in scene.");
                if (!TryLargestRendererBounds(out b))
                {
                    // Absolute last resort: default box
                    b = new Bounds(Vector3.zero, new Vector3(100f, 1f, 100f));
                    if (diagLogs) Debug.LogWarning("[FOW] Using default 100x100 bounds at origin.");
                }
            }
        }

        // Expand by extra margin (XZ only)
        if (extraMargin > 0f)
        {
            var size = b.size;
            size.x += extraMargin * 2f;
            size.z += extraMargin * 2f;
            b.extents = new Vector3(size.x * 0.5f, b.extents.y, size.z * 0.5f);
        }

        manager.AutoCalibrateUV(b);
        if (diagLogs) Debug.Log($"[FOW] Bound to map. originXZ={manager.worldOriginXZ} sizeXZ={manager.worldSizeXZ}");
    }

    bool TryFindTilesBounds(out Bounds total)
    {
        total = default;

        if (string.IsNullOrEmpty(tileTag))
            return false;

        var tiles = GameObject.FindGameObjectsWithTag(tileTag);
        if (tiles == null || tiles.Length == 0)
            return false;

        bool hasAny = false;
        for (int i = 0; i < tiles.Length; i++)
        {
            var go = tiles[i];
            if (!go || !go.activeInHierarchy) continue;
            if ((includeLayers.value & (1 << go.layer)) == 0) continue; // layer filter

            Bounds b;
            if (!TryBoundsFromObject(go, out b)) continue;

            if (!hasAny) { total = b; hasAny = true; }
            else { total.Encapsulate(b); }
        }
        return hasAny;
    }

    static bool TryBoundsFromObject(GameObject root, out Bounds b)
    {
        b = default;

        // Prefer Renderers (visual playfield)
        var rends = root.GetComponentsInChildren<Renderer>(true);
        if (rends != null && rends.Length > 0)
        {
            bool started = false;
            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i];
                if (!r || !r.enabled) continue;
                if (!started) { b = r.bounds; started = true; }
                else b.Encapsulate(r.bounds);
            }
            if (started) return true;
        }

        // Fallback: Colliders
        var cols = root.GetComponentsInChildren<Collider>(true);
        if (cols != null && cols.Length > 0)
        {
            bool started = false;
            for (int i = 0; i < cols.Length; i++)
            {
                var c = cols[i];
                if (!c || !c.enabled) continue;
                if (!started) { b = c.bounds; started = true; }
                else b.Encapsulate(c.bounds);
            }
            if (started) return true;
        }

        return false;
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
                bestArea = areaXZ;
                best = b;
                found = true;
            }
        }
        return found;
    }
}
