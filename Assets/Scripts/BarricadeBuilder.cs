using System; // for Array.Sort
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;

public class BarricadeBuilder : MonoBehaviour
{
    [Header("Settings")]
    public GameObject barricadePrefab;
    public GameObject previewPrefab;
    public int costInMaterials = 5;
    public float gridSize = 1f;
    public float maxBuildDistance = 80f;
    public float groundProbeUp = 5f;
    public float groundProbeDown = 80f;
    public bool requireNavMesh = true;
    public float navmeshSampleRadius = 1.0f;

    [Header("Removal / Refund")]
    public LayerMask removableMask = 0;        // auto-ensured to include Barricade layer
    public string removableTag = "Barricade";  // optional fallback
    [Range(0f, 1f)] public float refundPercent = 1.0f;
    public bool requireLineOfSight = true;
    public bool removalHitsTriggers = true;
    public bool verboseRemovalLogs = false;
    public bool rangeByRayDistance = true;
    public float cursorPickRadiusWorld = 0.5f;

    [Header("Hover Highlight")]
    public bool highlightOnHover = true;
    public Color highlightColor = new Color(1f, 0.85f, 0.25f, 0.6f);
    public bool highlightPulse = true;
    public float highlightPulseSpeed = 2.5f;

    [Header("Layers")]
    [SerializeField] private string barricadeLayerName = "Barricade";
    private int barricadeLayer = -1;

    [Header("Debug (read only)")]
    [SerializeField] string lastHitName = "(none)";
    [SerializeField] Vector3 lastMousePoint;
    [SerializeField] bool lastValid;

    private Camera mainCamera;
    private GameObject previewObject;
    private bool isBuilding = false;
    private int ignorePreviewMask; // everything except Ignore Raycast

    // Tinting/Highlighting without mutating shared materials
    private static MaterialPropertyBlock _mpb;

    // Hover highlight cache
    private GameObject _currentHighlightTarget;
    private List<Renderer> _currentHighlightRenderers = new List<Renderer>();

    void Awake()
    {
        mainCamera = GetActiveCamera();
        ignorePreviewMask = ~LayerMask.GetMask("Ignore Raycast");

        barricadeLayer = LayerMask.NameToLayer(barricadeLayerName);
        if (barricadeLayer == -1)
            Debug.LogWarning($"Layer '{barricadeLayerName}' not found. Create it in Project Settings > Tags and Layers.");

        if (barricadeLayer != -1)
        {
            int bit = 1 << barricadeLayer;
            if ((removableMask.value & bit) == 0)
            {
                removableMask |= bit;
                if (verboseRemovalLogs)
                    Debug.Log($"Auto-added Barricade layer to removableMask. Mask={removableMask.value}");
            }
        }
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        barricadeLayer = LayerMask.NameToLayer(barricadeLayerName);
        if (barricadeLayer != -1)
        {
            int bit = 1 << barricadeLayer;
            if ((removableMask.value & bit) == 0)
                removableMask |= bit;
        }
    }
#endif

    void Update()
    {
        // Toggle build mode
        if (Input.GetKeyDown(KeyCode.B))
        {
            isBuilding = !isBuilding;
            if (isBuilding) EnterBuildMode();
            else ExitBuildMode();
        }

        if (!isBuilding) return;

        // Keep cursor unlocked/visible while building
        if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
        if (!Cursor.visible) Cursor.visible = true;

        UpdatePreviewPosition();

        // Hover highlight (before clicks so state is fresh)
        if (highlightOnHover) UpdateHoverHighlight();

        // Place on LMB
        if (Input.GetMouseButtonDown(0) && !IsPointerOverUI())
            TryPlace();

        // Remove + refund on RMB
        if (Input.GetMouseButtonDown(1) && !IsPointerOverUI())
            TryRemoveAtMouse();

#if ENABLE_INPUT_SYSTEM
        // Fallback for New Input System only projects
        if (UnityEngine.InputSystem.Mouse.current != null &&
            UnityEngine.InputSystem.Mouse.current.rightButton.wasPressedThisFrame &&
            !IsPointerOverUI())
        {
            TryRemoveAtMouse();
        }
#endif
    }

    void EnterBuildMode()
    {
        if (previewObject == null && previewPrefab != null)
        {
            previewObject = Instantiate(previewPrefab);
            SetLayerRecursively(previewObject, LayerMask.NameToLayer("Ignore Raycast"));
            ToggleAllPreviewColliders(false);
            SetPreviewTint(new Color(0f, 1f, 0f, 0.5f));
        }
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (mainCamera == null) mainCamera = GetActiveCamera();
    }

    void ExitBuildMode()
    {
        ClearHighlight(); // just in case
        if (previewObject != null)
        {
            Destroy(previewObject);
            previewObject = null;
        }
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void UpdatePreviewPosition()
    {
        if (!previewObject) return;
        if (mainCamera == null) mainCamera = GetActiveCamera();
        if (mainCamera == null) return;

        // 1) Get mouse world point
        Vector3 mouseWorld;
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        Debug.DrawRay(ray.origin, ray.direction * maxBuildDistance, Color.yellow, 0f);

        Collider hitCol = null;
        if (Physics.Raycast(ray, out RaycastHit hit, maxBuildDistance, ignorePreviewMask, QueryTriggerInteraction.Ignore))
        {
            mouseWorld = hit.point;
            hitCol = hit.collider;
        }
        else if (Physics.Raycast(ray, out hit, maxBuildDistance, ~0, QueryTriggerInteraction.Ignore))
        {
            mouseWorld = hit.point;
            hitCol = hit.collider;
        }
        else
        {
            Plane p = new Plane(Vector3.up, Vector3.zero);
            if (p.Raycast(ray, out float enter))
                mouseWorld = ray.GetPoint(enter);
            else
                mouseWorld = previewObject.transform.position;
        }

        lastMousePoint = mouseWorld;
        lastHitName = hitCol ? hitCol.name : "(plane)";

        // 2) Drop to ground
        Vector3 pos = mouseWorld;
        Vector3 dropStart = pos + Vector3.up * groundProbeUp;
        if (Physics.Raycast(dropStart, Vector3.down, out RaycastHit groundHit, groundProbeDown, ignorePreviewMask, QueryTriggerInteraction.Ignore))
        {
            pos = groundHit.point;
        }

        // 3) Snap XZ to grid
        pos.x = Mathf.Round(pos.x / gridSize) * gridSize;
        pos.z = Mathf.Round(pos.z / gridSize) * gridSize;

        // 4) Validate/adjust on NavMesh
        bool navOK = true;
        if (requireNavMesh)
        {
            if (NavMesh.SamplePosition(new Vector3(pos.x, pos.y, pos.z), out NavMeshHit navHit, navmeshSampleRadius, NavMesh.AllAreas))
            {
                pos.x = Mathf.Round(navHit.position.x / gridSize) * gridSize;
                pos.z = Mathf.Round(navHit.position.z / gridSize) * gridSize;
            }
            else
            {
                navOK = false;
            }
        }

        // 5) Rotation (upright, snapped to 90°)
        float yaw = Mathf.Round(mainCamera.transform.eulerAngles.y / 90f) * 90f;
        Quaternion rot = Quaternion.Euler(0f, yaw, 0f);

        // 6) Apply transform, snap bottom to ground Y
        previewObject.transform.SetPositionAndRotation(pos, rot);
        SnapBottomToY(previewObject, pos.y);

        lastValid = navOK;
        SetPreviewTint((requireNavMesh && !navOK) ? new Color(1f, 0f, 0f, 0.5f) : new Color(0f, 1f, 0f, 0.5f));
    }

    void TryPlace()
    {
        if (!previewObject) return;
        if (!(lastValid || !requireNavMesh)) return;
        if (!IsPreviewGreen()) return;

        if (GameManager.Instance != null && GameManager.Instance.SpendMaterials(costInMaterials))
        {
            var go = Instantiate(barricadePrefab, previewObject.transform.position, previewObject.transform.rotation);
            // Ensure Barricade layer
            SetLayerRecursively(go, (barricadeLayer != -1) ? barricadeLayer : 0);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = true;
            Debug.Log("Barricade placed!");
        }
        else
        {
            Debug.Log("Not enough materials or GameManager missing!");
        }
    }

    // ---------- Hover highlight ----------
    void UpdateHoverHighlight()
    {
        if (mainCamera == null) return;

        // Find candidate using same hardened picking as removal
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        QueryTriggerInteraction qti = removalHitsTriggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore;

        bool gotHit = Physics.Raycast(ray, out RaycastHit firstHit, Mathf.Infinity, ignorePreviewMask, qti);

        GameObject target = null;
        RaycastHit barricadeHit = firstHit;

        if (gotHit)
        {
            target = FindBarricadeCandidateInHierarchy(firstHit.collider.transform);
            if (target == null)
            {
                var hits = Physics.RaycastAll(ray, Mathf.Infinity, ignorePreviewMask, qti);
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                foreach (var h in hits)
                {
                    var cand = FindBarricadeCandidateInHierarchy(h.collider.transform);
                    if (cand != null) { target = cand; barricadeHit = h; break; }
                }
            }
        }

        if (target == null)
        {
            Vector3 probePoint = gotHit ? firstHit.point : ray.GetPoint(10f);
            var overlaps = Physics.OverlapSphere(probePoint, cursorPickRadiusWorld, removableMask, qti);
            if (overlaps != null && overlaps.Length > 0)
            {
                target = FindBarricadeCandidateInHierarchy(overlaps[0].transform);
                barricadeHit = firstHit;
            }
        }

        // Decide if in range (either distance ok)
        bool shouldHighlight = false;
        if (target != null)
        {
            float rayDist = (barricadeHit.collider != null)
                            ? barricadeHit.distance
                            : Vector3.Distance(mainCamera.transform.position, target.transform.position);
            float builderDist = Vector3.Distance(transform.position, target.transform.position);

            bool inRangePrimary = rangeByRayDistance ? (rayDist <= maxBuildDistance)
                                                     : (builderDist <= maxBuildDistance);
            bool inRangeEither = inRangePrimary || (builderDist <= maxBuildDistance || rayDist <= maxBuildDistance);

            shouldHighlight = inRangeEither;
        }

        if (!shouldHighlight)
        {
            ClearHighlight();
            return;
        }

        // If target changed, clear old then apply new
        if (_currentHighlightTarget != target)
        {
            ClearHighlight();
            _currentHighlightTarget = target;
            _currentHighlightRenderers.Clear();
            _currentHighlightRenderers.AddRange(target.GetComponentsInChildren<Renderer>(true));
        }

        // Apply/pulse highlight
        float pulse = 1f;
        if (highlightPulse) pulse = 0.75f + 0.25f * (0.5f * (Mathf.Sin(Time.time * highlightPulseSpeed) + 1f));
        ApplyHighlight(_currentHighlightRenderers, highlightColor, pulse);
    }

    void ApplyHighlight(List<Renderer> rends, Color baseColor, float intensity)
    {
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        Color c = baseColor;
        c.a = baseColor.a; // alpha as set in inspector
        // Set both _Color and _BaseColor for broad shader coverage
        foreach (var r in rends)
        {
            if (!r) continue;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor("_Color", c * intensity);
            _mpb.SetColor("_BaseColor", c * intensity);
            r.SetPropertyBlock(_mpb);
        }
    }

    void ClearHighlight()
    {
        if (_currentHighlightTarget == null) return;
        // Clearing the PropertyBlock reverts to original materials
        foreach (var r in _currentHighlightRenderers)
        {
            if (!r) continue;
            r.SetPropertyBlock(null);
        }
        _currentHighlightTarget = null;
        _currentHighlightRenderers.Clear();
    }

    // ---------- Removal ----------
    GameObject FindBarricadeCandidateInHierarchy(Transform t)
    {
        while (t != null)
        {
            var go = t.gameObject;
            if (((1 << go.layer) & removableMask) != 0) return go;
            if (!string.IsNullOrEmpty(removableTag) && go.CompareTag(removableTag)) return go;
            if (go.GetComponent<BarricadeHealth>() != null) return go;
            t = t.parent;
        }
        return null;
    }

    void TryRemoveAtMouse()
    {
        if (mainCamera == null) mainCamera = GetActiveCamera();
        if (mainCamera == null) return;

        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        if (verboseRemovalLogs) Debug.DrawRay(ray.origin, ray.direction * 2000f, Color.cyan, 0.25f);

        QueryTriggerInteraction qti = removalHitsTriggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore;
        bool gotHit = Physics.Raycast(ray, out RaycastHit firstHit, Mathf.Infinity, ignorePreviewMask, qti);

        GameObject target = null;
        RaycastHit barricadeHit = firstHit;

        if (gotHit)
        {
            target = FindBarricadeCandidateInHierarchy(firstHit.collider.transform);
            if (target == null)
            {
                var hits = Physics.RaycastAll(ray, Mathf.Infinity, ignorePreviewMask, qti);
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                foreach (var h in hits)
                {
                    var cand = FindBarricadeCandidateInHierarchy(h.collider.transform);
                    if (cand != null) { target = cand; barricadeHit = h; break; }
                }
            }
        }

        if (target == null)
        {
            Vector3 probePoint = gotHit ? firstHit.point : ray.GetPoint(10f);
            var overlaps = Physics.OverlapSphere(probePoint, cursorPickRadiusWorld, removableMask, qti);
            if (overlaps != null && overlaps.Length > 0)
            {
                target = FindBarricadeCandidateInHierarchy(overlaps[0].transform);
                barricadeHit = firstHit;
            }
        }

        if (target == null)
        {
            if (verboseRemovalLogs) Debug.Log("RMB: No barricade found.");
            return;
        }

        // Range check (allow either distance)
        float rayDist = (barricadeHit.collider != null)
                        ? barricadeHit.distance
                        : Vector3.Distance(mainCamera.transform.position, target.transform.position);
        float builderDist = Vector3.Distance(transform.position, target.transform.position);

        bool inRange = rangeByRayDistance ? (rayDist <= maxBuildDistance)
                                          : (builderDist <= maxBuildDistance);

        if (!inRange && (builderDist <= maxBuildDistance || rayDist <= maxBuildDistance))
        {
            inRange = true;
            if (verboseRemovalLogs)
                Debug.Log($"RMB: Allowed by alternate range. rayDist={rayDist:F2}, builderDist={builderDist:F2}, max={maxBuildDistance}");
        }

        if (!inRange)
        {
            if (verboseRemovalLogs)
                Debug.Log($"RMB: Out of range. rayDist={rayDist:F2}, builderDist={builderDist:F2}, max={maxBuildDistance}");
            return;
        }

        int baseCost = costInMaterials;
        var custom = target.GetComponent<BarricadeCost>();
        if (custom != null && custom.cost > 0) baseCost = custom.cost;

        int refund = Mathf.RoundToInt(baseCost * Mathf.Clamp01(refundPercent));
        if (refund > 0 && GameManager.Instance != null)
            GameManager.Instance.AddMaterials(refund);

        if (verboseRemovalLogs)
            Debug.Log($"RMB: Removed '{target.name}' (refund {refund}).");

        ClearHighlight(); // remove visual before destroying
        Destroy(target);
    }

    // ---------- Helpers ----------
    void SnapBottomToY(GameObject go, float targetY)
    {
        if (!go) return;
        bool haveBounds = false;
        Bounds b = new Bounds(go.transform.position, Vector3.zero);
        foreach (var c in go.GetComponentsInChildren<Collider>(true))
        {
            if (!haveBounds) { b = c.bounds; haveBounds = true; }
            else b.Encapsulate(c.bounds);
        }
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (!haveBounds) { b = r.bounds; haveBounds = true; }
            else b.Encapsulate(r.bounds);
        }
        if (!haveBounds) return;
        float deltaY = targetY - b.min.y;
        if (Mathf.Abs(deltaY) > 0.0001f)
        {
            var p = go.transform.position;
            go.transform.position = new Vector3(p.x, p.y + deltaY, p.z);
        }
    }

    void ToggleAllPreviewColliders(bool enabled)
    {
        if (!previewObject) return;
        foreach (var c in previewObject.GetComponentsInChildren<Collider>(true))
            c.enabled = enabled;
    }

    void SetLayerRecursively(GameObject obj, int layer)
    {
        if (!obj) return;
        obj.layer = layer;
        foreach (Transform t in obj.transform) SetLayerRecursively(t.gameObject, layer);
    }

    void SetPreviewTint(Color color)
    {
        if (!previewObject) return;
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        var rends = previewObject.GetComponentsInChildren<Renderer>(true);
        foreach (var r in rends)
        {
            r.GetPropertyBlock(_mpb);
            // Also set _BaseColor for URP/HDRP
            _mpb.SetColor("_Color", color);
            _mpb.SetColor("_BaseColor", color);
            r.SetPropertyBlock(_mpb);
        }
    }

    bool IsPreviewGreen()
    {
        if (!previewObject) return false;
        var r = previewObject.GetComponentInChildren<Renderer>(true);
        if (!r) return false;
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        r.GetPropertyBlock(_mpb);
        Vector4 c = _mpb.GetVector("_Color"); // x=r, y=g, z=b, w=a
        return c.y >= 0.5f && c.x < 0.5f;
    }

    Camera GetActiveCamera()
    {
        var cam = Camera.main;
        if (cam && cam.isActiveAndEnabled) return cam;
        foreach (var c in Camera.allCameras) if (c.isActiveAndEnabled) return c;
        return null;
    }

    bool IsPointerOverUI()
    {
        if (EventSystem.current == null) return false;
        return EventSystem.current.IsPointerOverGameObject();
    }
}

public class BarricadeCost : MonoBehaviour
{
    public int cost = 5;
}
