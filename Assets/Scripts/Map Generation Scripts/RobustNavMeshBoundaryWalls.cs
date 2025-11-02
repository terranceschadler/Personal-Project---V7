using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Builds walls only on true outer NavMesh boundaries, never crossing walkable areas.
/// Robust across tiled NavMeshes via snapped edge keys, determines interior side
/// from triangle orientation, tries both outward sides, and spawns only non-intruding segments.
/// </summary>
public class RobustNavMeshBoundaryWalls : MonoBehaviour
{
    [Header("Wall Settings")]
    public GameObject wallPrefab;                 // Prefab with collider + mesh
    public float wallHeight = 4f;
    public float wallThickness = 0.4f;
    public float outwardOffset = 0.05f;

    [Tooltip("Use triangle orientation to offset away from NavMesh interior.")]
    public bool smartOutwardOffset = true;

    [Tooltip("Skip spawning for edges shorter than this (meters).")]
    public float minEdgeLength = 0.15f;

    [Tooltip("Maximum allowed Y value for edges (global cutoff).")]
    public float maxNavMeshHeight = 100f;

    public bool hideRenderers = false;
    [Tooltip("Optional forced layer for spawned walls (-1 keeps prefab's layer).")]
    public int wallLayer = -1;

    [Header("Ignore Objects")]
    [Tooltip("Walls will NOT be spawned along boundary edges overlapping these layers.")]
    public LayerMask ignoreLayers;
    [Tooltip("Radius used for OverlapCapsule against ignoreLayers.")]
    public float ignoreCheckRadius = 0.25f;
    public QueryTriggerInteraction ignoreTriggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("Performance / Robustness")]
    [Tooltip("If true, do one capsule per edge. If false, sample along long edges.")]
    public bool singleCapsulePerEdge = true;
    [Tooltip("Step used when sampling capsules along long edges (multi-sample mode).")]
    public float sampleStep = 2f;

    [Header("Snap / Merge (CRITICAL)")]
    [Tooltip("Grid snap size used to merge near-identical vertices across tiles/areas (meters).")]
    public float snapEpsilon = 0.05f;
    [Tooltip("Separate vertical snap (meters). Use a bit larger if your NavMesh has small Y jitters).")]
    public float snapEpsilonY = 0.02f;

    [Header("Anti-Intrusion (hard guarantee)")]
    [Tooltip("Spacing between samples along the edge to find safe outside intervals (m).")]
    public float intrusionCheckStep = 0.6f;
    [Tooltip("Radius for NavMesh.SamplePosition when testing just inside the wall’s interior face (m).")]
    public float intrusionCheckRadius = 0.02f;
    [Tooltip("Tiny epsilon to place the sample just inside the wall's interior face.")]
    public float intrusionSurfaceEpsilon = 0.005f;
    [Tooltip("Minimum segment length to actually spawn (meters).")]
    public float minSpawnSegment = 0.25f;
    [Tooltip("Try both outward signs and use the one that gives more non-intruding coverage.")]
    public bool chooseBestOutwardSide = true;

    [Header("Safety Auto-Fix")]
    [Tooltip("Auto-bump the effective outward offset used for tests/spawn so the interior face cannot lie inside the NavMesh.")]
    public bool autoFixOffsetIfTooSmall = true;
    [Tooltip("Extra clearance beyond half-thickness to ensure interior face is off the NavMesh.")]
    public float minInteriorClearance = 0.01f;

    [Header("NavMesh Wait Settings")]
    [Tooltip("Give up if no NavMesh appears after this many seconds (real time). 0 = never timeout.")]
    public float waitTimeoutSeconds = 20f;
    [Tooltip("Initial delay between triangulation polls.")]
    public float waitBackoffInitial = 0.1f;
    [Tooltip("Maximum delay between triangulation polls.")]
    public float waitBackoffMax = 0.75f;

    [Header("Debug")]
    public bool debugLogging = false;
    public bool drawGizmos = false;
    public float gizmoLift = 0.03f;

    private bool builtWalls = false;
    private Coroutine _waitCo;
    private float _invSnap, _invSnapY;

    // For gizmos (spawned segments)
    private struct GizSeg { public Vector3 a, b; }
    private readonly List<GizSeg> _gizmoSegments = new List<GizSeg>();

    // ---------- Quantized keys ----------
    private struct VKey
    {
        public int x, y, z;
        public VKey(int x, int y, int z) { this.x = x; this.y = y; this.z = z; }
        public override int GetHashCode() => (x * 73856093) ^ (y * 19349663) ^ (z * 83492791);
        public override bool Equals(object o)
        {
            if (!(o is VKey)) return false;
            var k = (VKey)o; return x == k.x && y == k.y && z == k.z;
        }
    }

    private struct EdgeKey
    {
        public VKey a, b; // sorted
        public EdgeKey(VKey k0, VKey k1)
        {
            if (k0.x < k1.x || (k0.x == k1.x && (k0.z < k1.z || (k0.z == k1.z && k0.y <= k1.y))))
            { a = k0; b = k1; }
            else { a = k1; b = k0; }
        }
        public override int GetHashCode() => a.GetHashCode() ^ (b.GetHashCode() * 486187739);
        public override bool Equals(object o)
        {
            if (!(o is EdgeKey)) return false;
            var e = (EdgeKey)o; return a.Equals(e.a) && b.Equals(e.b);
        }
    }

    private class EdgeAccum
    {
        public EdgeKey key;
        public int useCount = 0;

        public Vector3 aSum; public int aN;
        public Vector3 bSum; public int bN;

        // Interior side accumulation: -1 => left, +1 => right (relative to key.a -> key.b)
        public int interiorSignSum = 0;

        public EdgeAccum(EdgeKey k) { key = k; }
        public Vector3 AAvg => aN > 0 ? aSum / aN : Vector3.zero;
        public Vector3 BAvg => bN > 0 ? bSum / bN : Vector3.zero;
        public int InteriorSign => interiorSignSum < 0 ? -1 : +1; // for boundaries useCount==1
    }

    private void OnValidate()
    {
        if (snapEpsilon < 1e-5f) snapEpsilon = 1e-5f;
        if (snapEpsilonY < 1e-5f) snapEpsilonY = 1e-5f;
        if (minEdgeLength < 0f) minEdgeLength = 0f;
        if (sampleStep < 0.05f) sampleStep = 0.05f;
        if (ignoreCheckRadius < 0f) ignoreCheckRadius = 0f;

        if (intrusionCheckStep < 0.05f) intrusionCheckStep = 0.05f;
        if (intrusionCheckRadius < 0.005f) intrusionCheckRadius = 0.005f;
        if (intrusionSurfaceEpsilon < 0.0f) intrusionSurfaceEpsilon = 0.0f;
        if (minSpawnSegment < 0.05f) minSpawnSegment = 0.05f;
        if (minInteriorClearance < 0f) minInteriorClearance = 0f;
    }

    private void Start()
    {
        _invSnap = 1f / snapEpsilon;
        _invSnapY = 1f / snapEpsilonY;

        if (_waitCo == null) _waitCo = StartCoroutine(WaitForNavMeshCo());
    }

    private void OnDisable()
    {
        if (_waitCo != null) { StopCoroutine(_waitCo); _waitCo = null; }
    }

    private IEnumerator WaitForNavMeshCo()
    {
        float startT = Time.realtimeSinceStartup;
        float delay = Mathf.Max(0.02f, waitBackoffInitial);

        while (true)
        {
            var tri = NavMesh.CalculateTriangulation();
            if (tri.vertices != null && tri.vertices.Length > 0 && tri.indices != null && tri.indices.Length > 0)
                break;

            if (waitTimeoutSeconds > 0f && (Time.realtimeSinceStartup - startT) >= waitTimeoutSeconds)
            {
                if (debugLogging) Debug.LogWarning("[BoundaryWalls] Timed out waiting for NavMesh. No walls will be built yet.");
                _waitCo = null;
                yield break;
            }

            yield return new WaitForSeconds(delay);
            delay = Mathf.Min(delay * 1.5f, waitBackoffMax);
        }

        yield return null; // settle one frame
        BuildWalls();
        _waitCo = null;
    }

    private void BuildWalls()
    {
        if (builtWalls) return;
        builtWalls = true;
        _gizmoSegments.Clear();

        var tri = NavMesh.CalculateTriangulation();
        var verts = tri.vertices;
        var idx = tri.indices;

        var map = new Dictionary<EdgeKey, EdgeAccum>(idx.Length);

        // Accumulate edges using quantized keys (shared across tiles/areas)
        for (int i = 0; i < idx.Length; i += 3)
        {
            Vector3 v0 = verts[idx[i]];
            Vector3 v1 = verts[idx[i + 1]];
            Vector3 v2 = verts[idx[i + 2]];

            AddEdge(map, v0, v1, v2); // edge v0->v1, opposite v2
            AddEdge(map, v1, v2, v0); // edge v1->v2, opposite v0
            AddEdge(map, v2, v0, v1); // edge v2->v0, opposite v1
        }

        int edgesConsidered = 0, builtSegments = 0, skippedHeight = 0, skippedIgnore = 0, skippedShort = 0;

        foreach (var kv in map)
        {
            var acc = kv.Value;
            if (acc.useCount != 1) continue; // interior/shared edges appear >= 2
            edgesConsidered++;

            Vector3 a = acc.AAvg;
            Vector3 b = acc.BAvg;

            if (!IsFinite(a) || !IsFinite(b)) continue;

            if (a.y > maxNavMeshHeight || b.y > maxNavMeshHeight)
            { skippedHeight++; continue; }

            float len = (b - a).magnitude;
            if (len < minEdgeLength)
            { skippedShort++; continue; }

            if (ignoreLayers.value != 0 && EdgeOverlapsIgnoredObjects(a, b))
            { skippedIgnore++; continue; }

            // Decide outward side sign
            float sign = +1f;
            if (smartOutwardOffset)
                sign = (acc.InteriorSign == -1) ? +1f : -1f; // push away from interior

            if (chooseBestOutwardSide)
                sign = PickBestOutwardSign(a, b, sign);

            // Spawn only safe (non-intruding) intervals along this edge
            builtSegments += SpawnSafeSegments(a, b, sign);
        }

        if (debugLogging)
        {
            Debug.Log($"[BoundaryWalls] Considered {edgesConsidered} boundary edges. " +
                      $"Spawned segments: {builtSegments}. Skips: short={skippedShort}, height={skippedHeight}, ignoreLayers={skippedIgnore}.");
        }
    }

    // ----- Accumulation helpers -----

    private void AddEdge(Dictionary<EdgeKey, EdgeAccum> map, Vector3 aOrig, Vector3 bOrig, Vector3 cOpposite)
    {
        VKey qa = Quantize(aOrig);
        VKey qb = Quantize(bOrig);
        var key = new EdgeKey(qa, qb);

        if (!map.TryGetValue(key, out var acc))
        {
            acc = new EdgeAccum(key);
            map.Add(key, acc);
        }

        acc.useCount++;

        // Average the original endpoints per quantized endpoint slot
        if (qa.Equals(key.a))
        {
            acc.aSum += aOrig; acc.aN++;
            acc.bSum += bOrig; acc.bN++;
        }
        else
        {
            acc.aSum += bOrig; acc.aN++;
            acc.bSum += aOrig; acc.bN++;
        }

        // Compute interior side for this triangle relative to oriented edge aOrig -> bOrig
        int signForAB = InteriorSign2D(aOrig, bOrig, cOpposite); // -1 left, +1 right

        // If the key sorting flipped the edge direction, flip the sign as well
        bool sameDirectionAsKey = qa.Equals(key.a) && qb.Equals(key.b);
        int signForKeyDir = sameDirectionAsKey ? signForAB : -signForAB;

        acc.interiorSignSum += signForKeyDir;
    }

    // Returns -1 if 'c' is to the LEFT of the directed edge a->b (in XZ), +1 if to the RIGHT, 0 if collinear.
    private static int InteriorSign2D(Vector3 a, Vector3 b, Vector3 c)
    {
        float x1 = b.x - a.x, z1 = b.z - a.z;
        float x2 = c.x - a.x, z2 = c.z - a.z;
        float cross = x1 * z2 - z1 * x2;
        if (cross > 1e-6f) return -1; // left side = interior => -1 (convention)
        if (cross < -1e-6f) return +1; // right side
        return 0; // degenerate; treated as 0 contribution
    }

    private VKey Quantize(Vector3 v)
    {
        int qx = Mathf.RoundToInt(v.x * _invSnap);
        int qy = Mathf.RoundToInt(v.y * _invSnapY);
        int qz = Mathf.RoundToInt(v.z * _invSnap);
        return new VKey(qx, qy, qz);
    }

    // ----- Filters -----

    private bool EdgeOverlapsIgnoredObjects(Vector3 start, Vector3 end)
    {
        Vector3 lift = Vector3.up * 0.05f;
        Vector3 a = start + lift;
        Vector3 b = end + lift;

        if (singleCapsulePerEdge)
        {
            var hits = Physics.OverlapCapsule(a, b, ignoreCheckRadius, ignoreLayers, ignoreTriggerInteraction);
            return hits != null && hits.Length > 0;
        }
        else
        {
            float length = Vector3.Distance(a, b);
            if (length < Mathf.Epsilon) return false;

            int steps = Mathf.Max(1, Mathf.CeilToInt(length / Mathf.Max(0.1f, sampleStep)));
            Vector3 dir = (b - a) / steps;

            Vector3 segStart = a;
            for (int i = 0; i < steps; i++)
            {
                Vector3 segEnd = (i == steps - 1) ? b : (segStart + dir);
                var hits = Physics.OverlapCapsule(segStart, segEnd, ignoreCheckRadius, ignoreLayers, ignoreTriggerInteraction);
                if (hits != null && hits.Length > 0) return true;
                segStart = segEnd;
            }
            return false;
        }
    }

    // ----- Intrusion evaluation / segmentation -----

    private float PickBestOutwardSign(Vector3 start, Vector3 end, float initialSign)
    {
        float len = (end - start).magnitude;
        if (len < 1e-5f) return initialSign;

        float a = MeasureFreeLength(start, end, +Mathf.Abs(initialSign));
        float b = MeasureFreeLength(start, end, -Mathf.Abs(initialSign));

        // Prefer the side with more safe length; if near-tie, keep initialSign for stability
        if (b > a * 1.05f) return -Mathf.Abs(initialSign);
        return +Mathf.Abs(initialSign);
    }

    private float MeasureFreeLength(Vector3 start, Vector3 end, float outwardSign)
    {
        var intervals = ComputeSafeIntervals(start, end, outwardSign);
        float sum = 0f;
        foreach (var iv in intervals) sum += (iv.y - iv.x) * (end - start).magnitude;
        return sum;
    }

    /// <summary>
    /// Returns [t0,t1] intervals (in 0..1 along edge) where sampling just inside the interior face
    /// finds NO NavMesh => safe to spawn a wall segment outside without intruding.
    /// </summary>
    private List<Vector2> ComputeSafeIntervals(Vector3 start, Vector3 end, float outwardSign)
    {
        List<Vector2> result = new List<Vector2>();

        Vector3 ab = end - start;
        float len = ab.magnitude;
        if (len < 1e-5f) return result;

        Vector3 fwd = ab / len;
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        if (right.sqrMagnitude < 1e-6f) right = Vector3.right;

        float step = Mathf.Max(0.05f, intrusionCheckStep);
        int samples = Mathf.Max(1, Mathf.CeilToInt(len / step));
        float half = wallThickness * 0.5f;

        // Ensure the interior face isn't inside the NavMesh due to too-small outwardOffset
        float required = half + Mathf.Max(0.0f, minInteriorClearance);
        float effOutward = (autoFixOffsetIfTooSmall && outwardOffset < required) ? required : outwardOffset;

        bool open = false;
        float tStart = 0f;

        for (int i = 0; i <= samples; i++)
        {
            float t = (samples == 0) ? 0f : (i / (float)samples);
            Vector3 onEdge = Vector3.Lerp(start, end, t);

            // centerline offset outward, then move to the INTERIOR face by subtracting half thickness
            Vector3 center = onEdge + right * (outwardSign * effOutward);
            Vector3 interiorFacePoint = center - right * (outwardSign * (half - intrusionSurfaceEpsilon));

            // If point is on/near NavMesh => placing the wall here would intrude (NOT safe)
            bool intrudes = NavMesh.SamplePosition(interiorFacePoint, out _, intrusionCheckRadius, NavMesh.AllAreas);

            if (!intrudes)
            {
                if (!open) { open = true; tStart = t; }
            }
            else
            {
                if (open)
                {
                    open = false;
                    result.Add(new Vector2(tStart, t));
                }
            }
        }

        if (open) result.Add(new Vector2(tStart, 1f));
        return result;
    }

    private int SpawnSafeSegments(Vector3 start, Vector3 end, float outwardSign)
    {
        int spawned = 0;
        var intervals = ComputeSafeIntervals(start, end, outwardSign);
        if (intervals.Count == 0) return 0;

        foreach (var iv in intervals)
        {
            float t0 = Mathf.Clamp01(iv.x);
            float t1 = Mathf.Clamp01(iv.y);
            if (t1 <= t0) continue;

            Vector3 p0 = Vector3.Lerp(start, end, t0);
            Vector3 p1 = Vector3.Lerp(start, end, t1);
            float segLen = Vector3.Distance(p0, p1);
            if (segLen < Mathf.Max(minSpawnSegment, 0.01f)) continue;

            SpawnWallSegment(p0, p1, outwardSign);
            spawned++;

            if (drawGizmos)
                _gizmoSegments.Add(new GizSeg { a = p0, b = p1 });
        }
        return spawned;
    }

    // ----- Spawn -----

    private float GetEffectiveOutwardOffset()
    {
        float half = wallThickness * 0.5f;
        float required = half + Mathf.Max(0.0f, minInteriorClearance);
        return (autoFixOffsetIfTooSmall && outwardOffset < required) ? required : outwardOffset;
    }

    private void SpawnWallSegment(Vector3 start, Vector3 end, float outwardSign)
    {
        Vector3 mid = (start + end) * 0.5f;
        Vector3 dir = end - start;
        float length = dir.magnitude;
        if (length < 0.01f) return;

        Vector3 fwd = dir / Mathf.Max(length, 1e-6f);
        Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);

        GameObject wall = Instantiate(wallPrefab, mid, rot);
        wall.transform.localScale = new Vector3(wallThickness, wallHeight, length);

        // Right vector and outward push (use same effective offset as in tests)
        Vector3 right = wall.transform.right;
        wall.transform.position = wall.transform.position + right * (outwardSign * GetEffectiveOutwardOffset());

        if (wallLayer >= 0 && wallLayer <= 31)
            SetLayerRecursively(wall, wallLayer);

        wall.transform.SetParent(transform, worldPositionStays: true);

        if (hideRenderers)
        {
            foreach (var r in wall.GetComponentsInChildren<Renderer>())
                r.enabled = false;
        }
    }

    private void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos || _gizmoSegments.Count == 0) return;
        Gizmos.color = Color.green;
        foreach (var s in _gizmoSegments)
        {
            Vector3 a = s.a + Vector3.up * gizmoLift;
            Vector3 b = s.b + Vector3.up * gizmoLift;
            Gizmos.DrawLine(a, b);
        }
    }

    // ======= Helpers (single copy) =======

    // Unity/.NET compatibility version (no float.IsFinite in older runtimes)
    private static bool IsFinite(Vector3 v) => IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
    private static bool IsFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
}
