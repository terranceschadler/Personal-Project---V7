using UnityEngine;

/// <summary>
/// Priority order:
///   1) If helicopter completion reached: permanently follow the helicopter.
///   2) Else, if any helicopter parts exist in the scene: follow the closest part.
///   3) Else: follow the closest boss.
/// Uses GameManager as source-of-truth for completion.
/// </summary>
public class GoalFollower : MonoBehaviour
{
    [Header("References")]
    public Transform player;                 // Auto-finds by "Player" tag if null
    public GameManager gm;                   // Auto-gets GameManager.Instance if null

    [Header("Targets")]
    [Tooltip("Tag used to find bosses BEFORE completion.")]
    public string bossTag = "Boss";

    [Tooltip("Helicopter Transform. If null, will be found by tag below.")]
    public Transform helicopter;

    [Tooltip("Tag used to find the helicopter if Transform is not assigned.")]
    public string helicopterTag = "Helicopter";

    [Header("Helicopter Parts")]
    [Tooltip("Tag used to find helicopter parts that still exist in the scene (uncollected).")]
    public string helicopterPartTag = "HelicopterPart";

    [Header("Completion Rule (fallback when GM has no unique list)")]
    [Tooltip("If GameManager.helicopterPartPrefabs is empty, this is the 'needed' count to consider complete.")]
    public int targetCountIfNoUniqueList = 1;

    [Header("Update Cadence")]
    [Tooltip("How often (seconds) to rescan for targets before completion.")]
    public float rescanInterval = 0.35f;

    [Tooltip("How often (seconds) to retry finding helicopter by tag, once complete.")]
    public float helicopterLookupInterval = 0.5f;

    [Header("Placement")]
    public float heightOffset = 0.0f;
    public bool smoothFollow = true;
    public float followLerp = 12f;

    [Header("Debug")]
    public bool debugLogs = true;

    // Internals
    private Transform _currentBoss;
    private Transform _currentPart;
    private float _nextBossScanTime = 0f;
    private float _nextPartScanTime = 0f;
    private float _nextHeliScanTime = 0f;
    private bool _lockedToHelicopter = false; // once true, never reverts

    private void Awake()
    {
        if (player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }
        if (gm == null) gm = GameManager.Instance;

        if (helicopter == null && !string.IsNullOrEmpty(helicopterTag))
        {
            var heli = GameObject.FindGameObjectWithTag(helicopterTag);
            if (heli != null) helicopter = heli.transform;
        }
    }

    private void OnEnable()
    {
        // If already complete (e.g., scene reload), lock immediately
        if (IsHelicopterComplete())
        {
            _lockedToHelicopter = true;
            if (debugLogs) Debug.Log("[GoalFollower] Already complete at enable -> locking to helicopter.");
        }

        // Force initial scans so we don't wait for the first interval
        _currentPart = FindClosestPart();
        _currentBoss = FindClosestBoss();

        ForceImmediatePlacement();
    }

    private void Update()
    {
        if (player == null) return;

        // Check completion each frame until locked
        if (!_lockedToHelicopter && IsHelicopterComplete())
        {
            _lockedToHelicopter = true;
            _currentBoss = null;
            _currentPart = null;
            if (debugLogs) Debug.Log("[GoalFollower] Completion detected -> locking to helicopter.");
        }

        if (_lockedToHelicopter)
        {
            // Helicopter forever; keep trying to find it if missing
            if (helicopter == null && Time.time >= _nextHeliScanTime)
            {
                TryFindHelicopter();
                _nextHeliScanTime = Time.time + helicopterLookupInterval;
            }
        }
        else
        {
            // Not complete: prefer nearest helicopter part; else nearest boss.

            // Rescan parts on cadence
            if (Time.time >= _nextPartScanTime)
            {
                _currentPart = FindClosestPart();
                _nextPartScanTime = Time.time + rescanInterval;

                if (debugLogs)
                {
                    if (_currentPart != null)
                        Debug.Log($"[GoalFollower] Closest helicopter part: {_currentPart.name}");
                    else
                        Debug.Log("[GoalFollower] No helicopter parts found.");
                }
            }

            // Only if no parts found, rescan bosses on cadence
            if (_currentPart == null && Time.time >= _nextBossScanTime)
            {
                _currentBoss = FindClosestBoss();
                _nextBossScanTime = Time.time + rescanInterval;

                if (debugLogs)
                {
                    if (_currentBoss != null)
                        Debug.Log($"[GoalFollower] Closest boss: {_currentBoss.name}");
                    else
                        Debug.Log("[GoalFollower] No bosses found.");
                }
            }
        }

        // Choose target by priority
        Transform target =
            _lockedToHelicopter ? helicopter :
            (_currentPart != null ? _currentPart : _currentBoss);

        if (target == null) return;

        Vector3 targetPos = target.position + Vector3.up * heightOffset;
        if (smoothFollow)
            transform.position = Vector3.Lerp(transform.position, targetPos, 1f - Mathf.Exp(-followLerp * Time.deltaTime));
        else
            transform.position = targetPos;
    }

    // ---------- Helpers ----------

    private bool IsHelicopterComplete()
    {
        // Prefer GM unique parts list length if present; else use fallback
        if (gm == null) gm = GameManager.Instance;
        if (gm == null) return false;

        int have = gm.CollectedHelicopterParts != null ? gm.CollectedHelicopterParts.Count : 0;
        int need = (gm.helicopterPartPrefabs != null && gm.helicopterPartPrefabs.Length > 0)
            ? gm.helicopterPartPrefabs.Length
            : Mathf.Max(1, targetCountIfNoUniqueList);

        return have >= need;
    }

    private void TryFindHelicopter()
    {
        if (helicopter != null) return;
        if (string.IsNullOrEmpty(helicopterTag)) return;

        var go = GameObject.FindGameObjectWithTag(helicopterTag);
        if (go != null)
        {
            helicopter = go.transform;
            if (debugLogs) Debug.Log($"[GoalFollower] Found helicopter by tag '{helicopterTag}': {helicopter.name}");
        }
        else if (debugLogs)
        {
            Debug.Log($"[GoalFollower] Helicopter with tag '{helicopterTag}' not found yet.");
        }
    }

    private Transform FindClosestBoss()
    {
        if (string.IsNullOrEmpty(bossTag)) return null;

        var bosses = GameObject.FindGameObjectsWithTag(bossTag);
        if (bosses == null || bosses.Length == 0) return null;

        Transform closest = null;
        float minDistSq = float.PositiveInfinity;
        Vector3 p = player.position;

        foreach (var b in bosses)
        {
            if (b == null || !b.activeInHierarchy) continue;
            Vector3 d = b.transform.position - p;
            d.y = 0f;
            float dsq = d.sqrMagnitude;
            if (dsq < minDistSq)
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

        // If your parts have a component with an "IsCollected" flag, you could filter it here:
        // var comp = parts[i].GetComponent<HelicopterPart>();
        // if (comp != null && comp.IsCollected) continue;

        Transform closest = null;
        float minDistSq = float.PositiveInfinity;
        Vector3 p = player.position;

        foreach (var part in parts)
        {
            if (part == null || !part.activeInHierarchy) continue;
            Vector3 d = part.transform.position - p;
            d.y = 0f;
            float dsq = d.sqrMagnitude;
            if (dsq < minDistSq)
            {
                minDistSq = dsq;
                closest = part.transform;
            }
        }
        return closest;
    }

    private void ForceImmediatePlacement()
    {
        Transform t =
            _lockedToHelicopter ? helicopter :
            (_currentPart != null ? _currentPart : _currentBoss);

        if (t != null)
            transform.position = t.position + Vector3.up * heightOffset;
    }
}
