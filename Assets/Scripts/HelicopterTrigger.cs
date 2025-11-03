using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class HelicopterTrigger : MonoBehaviour
{
    [Header("Identification")]
    [Tooltip("Tag on your player object (the collider or a parent must have this).")]
    public string playerTag = "Player";

    [Tooltip("Friendlies are detected by tag and/or by having a Friendly-like component (type name contains 'Friendly' or common aliases).")]
    public string friendlyTag = "FriendlyAI";
    [Tooltip("If true, a friendly must ALSO have the friendlyTag to be rescued.")]
    public bool requireFriendlyTag = false;

    [Header("Rescue Rules (in this trigger)")]
    [Tooltip("Automatically rescue friendlies that enter this trigger, independent of win state.")]
    public bool rescueFriendliesOnTrigger = true;

    [Tooltip("If > 0, only rescue a friendly if the player is within this distance of that friendly (escort rule).")]
    public float requirePlayerWithinMeters = 0f;

    [Tooltip("On win, also rescue any friendlies within this radius of the helicopter center.")]
    public float sweepRescueRadiusOnWin = 6f;

    [Header("Win Behavior")]
    [Tooltip("If a Win UI overlay exists, hand off to it; otherwise just pause.")]
    public bool handOffToWinUI = true;
    [Tooltip("Pause the game on win when not handing to Win UI.")]
    public bool pauseOnWin = true;

    [Header("SFX (optional)")]
    public AudioClip blockedSfx;   // played if parts are not complete when player touches heli
    public AudioClip successSfx;   // played when win condition is met
    [Range(0f, 1f)] public float sfxVolume = 1f;

    [Header("Proximity Win (Player)")]
    [Tooltip("If true, also check distance every frame and win when close enough (if parts complete).")]
    public bool useProximityWin = true;
    public float proximityRadius = 3.0f;
    public float proximityCheckInterval = 0.10f;
    [Tooltip("Optional line-of-sight check from helicopter to player before winning.")]
    public bool requireLineOfSight = false;
    [Tooltip("Layers considered as LOS blockers if requireLineOfSight is true.")]
    public LayerMask losBlockers = ~0;

    [Header("Debug")]
    public bool verboseLogs = true;
    public Color gizmoColor = new Color(0f, 1f, 0.6f, 0.25f);

    // --- cached refs ---
    private Collider _col;
    private Rigidbody _rb; // kinematic rigidbody to ensure trigger events fire
    private Transform _player;

    // guards
    private bool _won = false;
    private float _nextProxCheckTime = 0f;

    // exactly-once rescue per friendly instance
    private readonly HashSet<int> _rescuedIds = new HashSet<int>();

    // friendly type name heuristics for reflection-based detection
    private static readonly string[] FriendlyTypeNameHints = {
        "FriendlyAI", "AllyAI", "CivilianAI", "CompanionAI", "NPCFriendly", "FollowerAI", "EscortAI", "CitizenAI"
    };

    private void Reset()
    {
        var c = GetComponent<Collider>();
        c.isTrigger = true;
    }

    private void Awake()
    {
        _col = GetComponent<Collider>() ?? gameObject.AddComponent<BoxCollider>();
        if (!_col.isTrigger)
        {
            if (verboseLogs) Debug.LogWarning($"[HelicopterTrigger] Collider was not a trigger. Forcing trigger=true on '{name}'.");
            _col.isTrigger = true;
        }

        // Ensure one side has a Rigidbody for trigger messages to fire reliably
        _rb = GetComponent<Rigidbody>();
        if (_rb == null)
        {
            _rb = gameObject.AddComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.useGravity = false;
            if (verboseLogs) Debug.Log($"[HelicopterTrigger] Added kinematic Rigidbody to '{name}' so triggers work.");
        }

        TryCachePlayer();
    }

    private void OnEnable()
    {
        if (_col != null && !_col.isTrigger)
        {
            _col.isTrigger = true;
            if (verboseLogs) Debug.LogWarning("[HelicopterTrigger] Collider flipped to non-trigger; forcing trigger=true.");
        }
        if (_rb != null)
        {
            _rb.isKinematic = true;
            _rb.useGravity = false;
        }
        _won = false;
        _nextProxCheckTime = 0f;
    }

    private void Update()
    {
        if (!_won && useProximityWin)
            TryProximityWin();
    }

    // --- TRIGGER PATHS ---
    private void OnTriggerEnter(Collider other)
    {
        if (MatchesPlayer(other.transform))
        {
            HandlePlayerTouch();
            return;
        }

        if (rescueFriendliesOnTrigger)
            TryRescueFriendly(other.transform, "TriggerEnter");
    }

    private void OnTriggerStay(Collider other)
    {
        // Only useful if escort rule requires player proximity; otherwise OnTriggerEnter already rescued
        if (rescueFriendliesOnTrigger && requirePlayerWithinMeters > 0f)
            TryRescueFriendly(other.transform, "TriggerStay");
    }

    // --- COLLISION FALLBACK ---
    private void OnCollisionEnter(Collision collision)
    {
        if (_col != null && _col.isTrigger) return; // normal trigger path should handle
        if (MatchesPlayer(collision.transform))
        {
            HandlePlayerTouch();
            return;
        }

        if (rescueFriendliesOnTrigger)
            TryRescueFriendly(collision.transform, "CollisionEnter");
    }

    // ---------- Player helpers ----------
    private void HandlePlayerTouch()
    {
        if (_won) return;

        if (!IsHelicopterCompleteViaGM())
        {
            if (verboseLogs) Debug.LogWarning("[HelicopterTrigger] Player touched helicopter but parts are NOT complete.");
            PlayOneShot(blockedSfx);
            return;
        }

        if (verboseLogs) Debug.Log("[HelicopterTrigger] WIN via player touch.");
        DoWin();
    }

    private void TryProximityWin()
    {
        if (Time.unscaledTime < _nextProxCheckTime) return;
        _nextProxCheckTime = Time.unscaledTime + Mathf.Max(0.01f, proximityCheckInterval);

        if (_won) return;
        if (_player == null) TryCachePlayer();
        if (_player == null) return;

        if (!IsHelicopterCompleteViaGM()) return;

        float dist = Vector3.Distance(GetPlayerCenter(_player), GetHeliCenter());
        if (dist <= proximityRadius)
        {
            if (requireLineOfSight && !HasLineOfSightToPlayer())
            {
                if (verboseLogs) Debug.Log($"[HelicopterTrigger] Proximity met ({dist:0.00}m) but LOS blocked.");
                return;
            }

            if (verboseLogs) Debug.Log($"[HelicopterTrigger] WIN via proximity ({dist:0.00}m <= {proximityRadius:0.00}m).");
            DoWin();
        }
    }

    // ---------- Friendly helpers ----------
    private void TryRescueFriendly(Transform t, string reason)
    {
        var friendlyComp = FindFriendlyRootComponent(t);
        if (friendlyComp == null)
        {
            if (verboseLogs) Debug.Log($"[HelicopterTrigger] Not a friendly ({reason}): {t.name}");
            return;
        }

        // Optional tag gate
        if (requireFriendlyTag && !HasFriendlyTag(friendlyComp.transform))
        {
            if (verboseLogs) Debug.Log($"[HelicopterTrigger] Friendly '{friendlyComp.name}' rejected: requireFriendlyTag=true but tag '{friendlyTag}' missing.");
            return;
        }

        int id = friendlyComp.gameObject.GetInstanceID();
        if (_rescuedIds.Contains(id))
        {
            if (verboseLogs) Debug.Log($"[HelicopterTrigger] Friendly '{friendlyComp.name}' already rescued (ignored).");
            return;
        }

        // Escort rule (player proximity)
        if (requirePlayerWithinMeters > 0f)
        {
            if (_player == null) TryCachePlayer();
            if (_player == null)
            {
                if (verboseLogs) Debug.Log($"[HelicopterTrigger] Escort rule active but player not found; cannot rescue '{friendlyComp.name}'.");
                return;
            }
            float d = Vector3.Distance(friendlyComp.transform.position, _player.position);
            if (d > requirePlayerWithinMeters)
            {
                if (verboseLogs) Debug.Log($"[HelicopterTrigger] Escort rule blocked: Player {d:0.0}m away (> {requirePlayerWithinMeters}). '{friendlyComp.name}' not rescued.");
                return;
            }
        }

        GameManager.Instance?.RegisterFriendlyRescue(1);
        _rescuedIds.Add(id);

        if (verboseLogs)
            Debug.Log($"[HelicopterTrigger] Friendly '{friendlyComp.name}' rescued ({reason}). Total={GameManager.Instance?.FriendliesRescued}");

        // Optional: hide to avoid re-enter spam if you don’t destroy friendlies on extraction
        friendlyComp.gameObject.SetActive(false);
    }

    private Component FindFriendlyRootComponent(Transform t)
    {
        // 1) If tag is allowed and present anywhere up the chain, accept that object
        if (!requireFriendlyTag && HasFriendlyTag(t))
            return GetTopmostWithTag(t, friendlyTag);

        // 2) Search up the hierarchy for a component whose type name matches our hints
        Transform p = t;
        while (p != null)
        {
            var comps = p.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                var c = comps[i];
                if (c == null) continue;
                string typeName = c.GetType().Name;
                if (LooksLikeFriendlyTypeName(typeName))
                    return c;
            }
            p = p.parent;
        }

        // 3) If tag gating is required, and tag exists on chain, accept that transform
        if (requireFriendlyTag && HasFriendlyTag(t))
            return GetTopmostWithTag(t, friendlyTag);

        return null;
    }

    private bool LooksLikeFriendlyTypeName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return false;
        // quick contains for "Friendly"
        if (typeName.IndexOf("Friendly", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        for (int i = 0; i < FriendlyTypeNameHints.Length; i++)
        {
            if (string.Equals(typeName, FriendlyTypeNameHints[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private bool HasFriendlyTag(Transform t)
    {
        if (string.IsNullOrEmpty(friendlyTag)) return false;
        Transform p = t;
        while (p != null)
        {
            if (p.CompareTag(friendlyTag)) return true;
            p = p.parent;
        }
        return false;
    }

    private Component GetTopmostWithTag(Transform t, string tagName)
    {
        if (string.IsNullOrEmpty(tagName)) return null;
        Transform top = null;
        Transform p = t;
        while (p != null)
        {
            if (p.CompareTag(tagName)) top = p;
            p = p.parent;
        }
        return top != null ? (Component)top : null;
    }

    // ---------- Core helpers (single definitions) ----------
    private bool MatchesPlayer(Transform t)
    {
        if (!string.IsNullOrEmpty(playerTag) && t.CompareTag(playerTag)) return true;
        var p = t;
        while (p != null)
        {
            if (!string.IsNullOrEmpty(playerTag) && p.CompareTag(playerTag)) return true;
            p = p.parent;
        }
        return false;
    }

    private void TryCachePlayer()
    {
        if (string.IsNullOrEmpty(playerTag)) return;
        var go = GameObject.FindGameObjectWithTag(playerTag);
        if (go != null) _player = go.transform;
    }

    // Use GameManager as the source of truth: collected >= required
    private bool IsHelicopterCompleteViaGM()
    {
        var gm = GameManager.Instance;
        if (gm == null) return false;
        var prog = gm.GetHelicopterProgress(); // (collected, required)
        return prog.required > 0 && prog.collected >= prog.required;
    }

    private Vector3 GetPlayerCenter(Transform player)
    {
        var cc = player.GetComponent<CharacterController>();
        if (cc != null)
            return player.position + Vector3.up * (cc.height * 0.5f);
        return player.position + Vector3.up; // approximate chest height
    }

    private Vector3 GetHeliCenter()
    {
        if (_col is BoxCollider b) return transform.TransformPoint(b.center);
        if (_col is SphereCollider s) return transform.TransformPoint(s.center);
        if (_col is CapsuleCollider c) return transform.TransformPoint(c.center);
        return transform.position;
    }

    private bool HasLineOfSightToPlayer()
    {
        if (_player == null) return false;
        Vector3 origin = GetHeliCenter();
        Vector3 target = GetPlayerCenter(_player);
        Vector3 dir = (target - origin);
        float dist = dir.magnitude;
        if (dist <= Mathf.Epsilon) return true;

        dir /= dist;
        if (Physics.Raycast(origin, dir, out RaycastHit hit, dist, losBlockers, QueryTriggerInteraction.Ignore))
        {
            if (!MatchesPlayer(hit.transform))
                return false;
        }
        return true;
    }

    private void DoWin()
    {
        if (_won) return;
        _won = true;

        PlayOneShot(successSfx);

        // 1) Final rescue sweep (pick up any friendlies very near the heli)
        if (sweepRescueRadiusOnWin > 0.01f)
        {
            Vector3 c = GetHeliCenter();
            float r = sweepRescueRadiusOnWin;

            var cols = Physics.OverlapSphere(c, r, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < cols.Length; i++)
            {
                TryRescueFriendly(cols[i].transform, "WinSweep");
            }
        }

        // 2) Count ALL friendlies currently following the player, anywhere on the map.
        MonoBehaviour[] allBehaviours;
#if UNITY_2023_1_OR_NEWER
        allBehaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        allBehaviours = FindObjectsOfType<MonoBehaviour>(includeInactive: false);
#endif

        for (int i = 0; i < allBehaviours.Length; i++)
        {
            var comp = allBehaviours[i];
            if (comp == null) continue;
            var typeName = comp.GetType().Name;
            if (!LooksLikeFriendlyTypeName(typeName)) continue; // only friendly-like scripts

            var go = comp.gameObject;
            int id = go.GetInstanceID();
            if (_rescuedIds.Contains(id)) continue; // already rescued

            bool isFollower = IsCurrentlyFollowingPlayer(comp);
            if (!isFollower) continue;

            // Optional escort rule: still require player proximity for followers
            if (requirePlayerWithinMeters > 0f && _player != null)
            {
                float d = Vector3.Distance(go.transform.position, _player.position);
                if (d > requirePlayerWithinMeters) continue;
            }

            GameManager.Instance?.RegisterFriendlyRescue(1);
            _rescuedIds.Add(id);

            if (verboseLogs)
                Debug.Log($"[HelicopterTrigger] Friendly '{go.name}' rescued via FOLLOWER count. Total={GameManager.Instance?.FriendliesRescued}");

            // optional: hide them on extraction
            go.SetActive(false);
        }

        // 3) Win UI / pause
        if (handOffToWinUI && WinUIController.Instance != null)
        {
            WinUIController.Instance.ShowWinAuto();
        }
        else if (pauseOnWin)
        {
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (_col != null) _col.enabled = false;
        enabled = false;
    }

    // Heuristic: detect if this friendly component is currently following the player
    private bool IsCurrentlyFollowingPlayer(Component friendlyComp)
    {
        var t = friendlyComp.GetType();

        // 1) Common boolean flags
        string[] boolNames = { "IsFollowingPlayer", "isFollowingPlayer", "IsFollowing", "isFollowing", "Following", "following" };
        for (int i = 0; i < boolNames.Length; i++)
        {
            if (TryGetBoolFieldOrProperty(friendlyComp, t, boolNames[i], out bool val) && val)
                return true;
        }

        // 2) Common leader/target references
        string[] leaderNames = { "player", "Player", "followTarget", "FollowTarget", "leader", "Leader", "follow", "Follow" };
        for (int i = 0; i < leaderNames.Length; i++)
        {
            if (TryGetTransformFieldOrProperty(friendlyComp, t, leaderNames[i], out Transform trg) && trg != null)
            {
                if (MatchesPlayer(trg))
                    return true;
            }
        }

        // 3) If tagged as friendly and very near the player, optionally treat as follower
        if (!string.IsNullOrEmpty(friendlyTag) && friendlyComp.transform.CompareTag(friendlyTag) && _player != null)
        {
            float near = Mathf.Max(2f, requirePlayerWithinMeters > 0f ? requirePlayerWithinMeters : 3f);
            if (Vector3.Distance(friendlyComp.transform.position, _player.position) <= near)
                return true;
        }

        return false;
    }

    private bool TryGetBoolFieldOrProperty(object obj, Type type, string name, out bool value)
    {
        value = false;
        try
        {
            var f = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null && f.FieldType == typeof(bool))
            {
                value = (bool)f.GetValue(obj);
                return true;
            }
            var p = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p != null && p.PropertyType == typeof(bool))
            {
                value = (bool)p.GetValue(obj, null);
                return true;
            }
        }
        catch { }
        return false;
    }

    private bool TryGetTransformFieldOrProperty(object obj, Type type, string name, out Transform tr)
    {
        tr = null;
        try
        {
            var f = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null)
            {
                object val = f.GetValue(obj);
                if (val is Transform tf) { tr = tf; return true; }
                if (val is Component c1) { tr = c1.transform; return true; }
                if (val is GameObject go1) { tr = go1.transform; return true; }
            }
            var p = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p != null)
            {
                object val = p.GetValue(obj, null);
                if (val is Transform tp) { tr = tp; return true; }
                if (val is Component c2) { tr = c2.transform; return true; }
                if (val is GameObject go2) { tr = go2.transform; return true; }
            }
        }
        catch { }
        return false;
    }

    private void PlayOneShot(AudioClip clip)
    {
        if (!clip) return;
        AudioSource.PlayClipAtPoint(clip, transform.position, Mathf.Clamp01(sfxVolume));
    }

    [ContextMenu("TEST: Force Win Now (if parts complete)")]
    private void _DEV_TestWinNow()
    {
        if (IsHelicopterCompleteViaGM())
        {
            if (verboseLogs) Debug.Log("[HelicopterTrigger] DEV: Force win.");
            DoWin();
        }
        else
        {
            if (verboseLogs) Debug.LogWarning("[HelicopterTrigger] DEV: Parts not complete; force win blocked.");
            PlayOneShot(blockedSfx);
        }
    }

    private void OnDrawGizmosSelected()
    {
        var prev = Gizmos.color;
        Gizmos.color = gizmoColor;

        // Trigger volume wire
        var c = GetComponent<Collider>();
        if (c is BoxCollider bc)
        {
            var m = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
            Gizmos.matrix = m;
            Gizmos.DrawWireCube(bc.center, bc.size);
        }
        else if (c is SphereCollider sc)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireSphere(sc.center, sc.radius);
        }
        else if (c is CapsuleCollider cc)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            var center = cc.center;
            float r = cc.radius;
            float h = Mathf.Max(cc.height, r * 2f);
            Vector3 axis = Vector3.up;
            if (cc.direction == 0) axis = Vector3.right;
            else if (cc.direction == 2) axis = Vector3.forward;
            Vector3 off = axis * (h * 0.5f - r);
            Gizmos.DrawWireSphere(center + off, r);
            Gizmos.DrawWireSphere(center - off, r);
        }

        // Proximity win + rescue sweep visual
        if (useProximityWin)
            Gizmos.DrawWireSphere(GetHeliCenter(), Mathf.Max(0.01f, proximityRadius));
        if (sweepRescueRadiusOnWin > 0.01f)
        {
            Gizmos.color = new Color(0.1f, 0.8f, 0.3f, 0.25f);
            Gizmos.DrawWireSphere(GetHeliCenter(), sweepRescueRadiusOnWin);
        }

        Gizmos.color = prev;
    }
}
