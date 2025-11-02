// Assets/Scripts/Ai Scripts/BossBullet.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Boss projectile that moves via transform.Translate in a straight line parallel to the ground (XZ).
/// - Will not move unless InitFromMuzzle(...) is called
/// - Constant height: locks Y to muzzle height
/// - Kinematic Rigidbody + Trigger Collider
/// - Collider disabled until armed (owner-safe window)
/// - Sweep + trigger hit handling with explicit layer filters
/// - Passes through enemies (optional), damages Player/Friendly, destroys on blockers
/// - Back-compat shims: SetArmingDelay(), IgnoreOwnerColliders(...)
/// </summary>
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Rigidbody))]
public class BossBullet : MonoBehaviour
{
    [Header("Setup")]
    public float damage = 20f;
    public float speed = 24f;
    [Tooltip("Seconds before auto-destroy. Set <= 0 to disable.")]
    public float lifetime = 6f;

    [Header("Filtering (Damage)")]
    [Tooltip("Targets that can receive damage (destroy happens on ANY blocker or damage hit).")]
    public string playerTag = "Player";
    public string friendlyTag = "FriendlyAI";
    [Tooltip("Layers allowed to receive damage checks (damage only).")]
    public LayerMask hitMask = ~0;

    [Tooltip("Ignore hits briefly after Init to avoid muzzle/owner overlaps. Collider stays DISABLED until armed.")]
    public float ownerSafeSeconds = 0.05f;

    [Header("Pass-through")]
    [Tooltip("If true, bullet will IGNORE enemies (no damage, no destroy) and keep flying.")]
    public bool passThroughEnemies = true;
    [Tooltip("Tags considered 'enemy' for pass-through checks (checked on collider and its root).")]
    public string[] enemyTags = { "Enemy", "Boss", "Zombie" };
    [Tooltip("Layers considered 'enemy' for pass-through checks.")]
    public LayerMask enemyLayers = 0;

    [Header("Collision Filters")]
    [Tooltip("Layers that should STOP the projectile (world/obstacles/player/friendly).")]
    public LayerMask blockMask;  // e.g., Default | Environment | Player | Friendly
    [Tooltip("Should the sweep consider triggers? Usually NO to avoid gate/zone triggers.")]
    public bool includeTriggersInSweep = false;
    [Tooltip("Ignore trigger volumes in OnTriggerEnter unless in blockMask or valid damage target.")]
    public bool ignoreNonSolidTriggers = true;

    [Header("Sweep Detection")]
    [Tooltip("Enable continuous sweep tests so fast bullets still hit between frames.")]
    public bool sweepEnabled = true;
    [Tooltip("Extra padding added to the sweep distance each frame.")]
    public float sweepPadding = 0.05f;

    [Header("Spawn Stabilization")]
    [Tooltip("Extra frames (0–2) to hard-snap to the muzzle after Init to prevent first-frame drift.")]
    [Range(0, 2)] [SerializeField] private int extraSnapFrames = 1;

    [Header("Spawn Safety")]
    [Tooltip("Extra distance along flight direction applied in Init to clear boss bounds.")]
    public float pushForwardFromMuzzle = 0.6f;

    [Header("Diagnostics")]
    [Tooltip("Optional: while unarmed, force bullet onto this layer (no collisions). Set to -1 to disable.")]
    public int quarantineLayer = -1;  // assign to a no-collision layer if desired
    [Tooltip("Verbose logs for development only.")]
    public bool debugLogs = false;
    [Tooltip("Spawn/init logs for development only.")]
    public bool debugSpawnInfo = false;

    // ---- internals ----
    private Rigidbody _rb;
    private Collider _col;

    private GameObject _owner;
    private bool _initialized = false;
    private bool _hasHit = false;
    private float _safeUntil = 0f;     // time until we ARM
    private bool _armed = false;       // collider enabled & allowed to react
    private Vector3 _dirWorldXZ;       // normalized, flattened to XZ
    private float _lockedY;            // constant Y
    private Vector3 _lastPos;
    private float _enabledAtTime;

    // snap-to-muzzle guard (prevents first-frame offsets)
    private Transform _snapMuzzle;
    private int _snapFramesLeft;

    // collision ignore bookkeeping
    private readonly HashSet<Collider> _ignoredOwnerCols = new HashSet<Collider>();

    // diagnostics state
    private int _originalLayer = -1;
    private bool _wasArmedWhenDisabled = false;

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void EditorLog(string msg) => UnityEngine.Debug.Log(msg, this);

    // ---------- Factory spawn helpers ----------
    public static BossBullet SpawnFromMuzzle(GameObject prefab, Transform muzzle, GameObject owner, Vector3 direction)
    {
        if (prefab == null || muzzle == null)
        {
            UnityEngine.Debug.LogError("[BossBullet] SpawnFromMuzzle: prefab or muzzle missing.");
            return null;
        }

        Vector3 flatDir = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (flatDir.sqrMagnitude < 1e-6f) flatDir = muzzle.forward;
        flatDir = flatDir.normalized;
        Quaternion rot = Quaternion.LookRotation(flatDir, Vector3.up);

        var go = Object.Instantiate(prefab, muzzle.position, rot);

        var bb = go.GetComponent<BossBullet>() ?? go.GetComponentInChildren<BossBullet>(true);
        if (bb == null)
        {
            UnityEngine.Debug.LogError("[BossBullet] Prefab missing BossBullet component (root or children).", go);
            return null;
        }

        bb.InitFromMuzzle(owner, muzzle, flatDir);
        return bb;
    }

    public static BossBullet SpawnTowards(GameObject prefab, Transform muzzle, GameObject owner, Vector3 targetWorldPos)
    {
        Vector3 dir = targetWorldPos - muzzle.position;
        return SpawnFromMuzzle(prefab, muzzle, owner, dir);
    }
    // -------------------------------------------

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();

        // Trigger + kinematic so we can move transform directly
        if (_col != null) _col.isTrigger = true;
        _rb.isKinematic = true;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void OnEnable()
    {
        _enabledAtTime = Time.time;
        _hasHit = false;
        _armed = false;
        _lastPos = transform.position;

        if (lifetime > 0f) Destroy(gameObject, lifetime);

        // Ensure collider starts disabled until we arm
        if (_col != null) _col.enabled = false;

        _wasArmedWhenDisabled = false;
        _originalLayer = gameObject.layer;
        if (quarantineLayer >= 0) gameObject.layer = quarantineLayer;

        if (debugSpawnInfo)
            EditorLog($"[BossBullet] Enabled at {transform.position} | layer={gameObject.layer} isTrigger={_col?.isTrigger} rbKinematic={_rb.isKinematic} speed={speed}");
    }

    /// <summary>Initializes the bullet at the muzzle. MUST be called right after instantiation.</summary>
    public void InitFromMuzzle(GameObject owner, Transform muzzle, Vector3 direction)
    {
        _owner = owner;

        // Flatten direction to XZ (parallel to ground)
        _dirWorldXZ = MakeHorizontal(direction, muzzle);

        // Place just beyond the muzzle to clear owner bounds, lock height to muzzle Y
        Vector3 start = muzzle.position + _dirWorldXZ * Mathf.Max(0f, pushForwardFromMuzzle);
        transform.position = start;
        _lockedY = start.y;

        // Ignore owner's colliders to prevent immediate self-hits
        if (_owner && _col)
        {
            var ownerCols = _owner.GetComponentsInChildren<Collider>(true);
            foreach (var oc in ownerCols)
                if (oc) { Physics.IgnoreCollision(_col, oc, true); _ignoredOwnerCols.Add(oc); }
        }

        // Face movement direction (horizontal)
        transform.rotation = Quaternion.LookRotation(_dirWorldXZ, Vector3.up);

        if (speed <= 0f)
        {
            if (debugLogs) EditorLog($"[BossBullet] Non-positive speed {speed}. Clamping to 0.01.");
            speed = 0.01f;
        }

        // Arm after safe window: collider remains DISABLED until then
        _safeUntil = Time.time + Mathf.Max(0f, ownerSafeSeconds);
        _armed = false;
        if (_col != null) _col.enabled = false;

        _initialized = true;
        _lastPos = transform.position;

        // Snap guard: hold to muzzle for a couple frames in case anything nudges on enable
        _snapMuzzle = muzzle;
        _snapFramesLeft = Mathf.Max(0, extraSnapFrames);

        if (debugSpawnInfo)
            EditorLog($"[BossBullet] InitFromMuzzle at {transform.position} dir={_dirWorldXZ} owner={(_owner ? _owner.name : "null")}");
    }

    private void Update()
    {
        // If somehow enabled without Init, self-destruct quickly (prevents ghost bullets).
        if (!_initialized)
        {
            if (Time.time - _enabledAtTime > 0.25f)
            {
                if (debugLogs) EditorLog("[BossBullet] Destroying uninitialized bullet (InitFromMuzzle was never called).");
                Destroy(gameObject);
            }
            return;
        }
        if (_hasHit) return;

        // Arm when safe window passes
        if (!_armed && Time.time >= _safeUntil)
        {
            _armed = true;
            if (_col != null) _col.enabled = true;
            if (quarantineLayer >= 0) gameObject.layer = _originalLayer;
        }

        // First-frame hard snap to eliminate spawn jitter
        if (_snapFramesLeft > 0 && _snapMuzzle != null)
        {
            Vector3 snap = _snapMuzzle.position + _dirWorldXZ * Mathf.Max(0f, pushForwardFromMuzzle);
            transform.position = snap;
            _lockedY = snap.y;
            _snapFramesLeft--;
        }

        // Ensure direction remains valid (defensive)
        if (_dirWorldXZ.sqrMagnitude < 1e-6f)
            _dirWorldXZ = MakeHorizontal(transform.forward, transform);

        // Movement (horizontal only)
        Vector3 delta = _dirWorldXZ * speed * Time.deltaTime;

        // Sweep AFTER we are armed
        if (_armed && sweepEnabled && delta.sqrMagnitude > 0f)
        {
            Vector3 from = transform.position;
            Vector3 to = from + delta;
            DoSweep(from, to);
        }

        // Apply movement
        transform.Translate(delta, Space.World);

        // Keep altitude locked (parallel to ground plane)
        var p = transform.position;
        if (!Mathf.Approximately(p.y, _lockedY))
        {
            p.y = _lockedY;
            transform.position = p;
        }

        // Maintain forward for VFX/trails
        transform.rotation = Quaternion.LookRotation(_dirWorldXZ, Vector3.up);

        _lastPos = transform.position;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!_initialized || !_armed || _hasHit) return;

        var target = other.attachedRigidbody ? other.attachedRigidbody.gameObject : other.gameObject;

        // Ignore generic triggers unless they’re real blockers or damage targets
        if (ignoreNonSolidTriggers && other.isTrigger)
        {
            bool isBlocker = ((blockMask.value & (1 << other.gameObject.layer)) != 0);
            if (!isBlocker && !ShouldDamage(target) && !(passThroughEnemies && IsEnemy(target)))
                return;
        }

        // Never react to owner
        if (IsOwnerOrChild(target)) return;

        // Pass through enemies
        if (passThroughEnemies && IsEnemy(target)) return;

        // Damage ? destroy
        if (ShouldDamage(target))
        {
            if (debugLogs) EditorLog($"[BossBullet] OnTriggerEnter HIT (damage) {target.name} at {transform.position}");
            ApplyDamage(target);
            _hasHit = true;
            Destroy(gameObject);
            return;
        }

        // Blockers only if in blockMask
        if ((blockMask.value & (1 << target.layer)) != 0)
        {
            if (debugLogs) EditorLog($"[BossBullet] OnTriggerEnter BLOCK {target.name} at {transform.position}");
            _hasHit = true;
            Destroy(gameObject);
            return;
        }

        // Otherwise ignore (prevents gate/zone triggers from killing the shot)
    }

    private void OnDisable()
    {
        // Clean up ignore pairs (good hygiene; if you pool, you can keep them)
        if (_col != null && _ignoredOwnerCols.Count > 0)
        {
            foreach (var oc in _ignoredOwnerCols)
            {
                if (oc != null) Physics.IgnoreCollision(_col, oc, false);
            }
        }
        _ignoredOwnerCols.Clear();

        if (debugLogs || debugSpawnInfo)
            EditorLog($"[BossBullet] DISABLED initialized={_initialized} armed={_armed} hasHit={_hasHit} pos={transform.position}");

        _wasArmedWhenDisabled = _armed;
    }

    private void OnDestroy()
    {
        if (debugLogs || debugSpawnInfo)
            EditorLog($"[BossBullet] DESTROYED initialized={_initialized} armed={_wasArmedWhenDisabled} hasHit={_hasHit} name={name}");
    }

    // ---------- Back-compat shims ----------
    public void SetArmingDelay(float seconds)
    {
        ownerSafeSeconds = Mathf.Max(0f, seconds);
        _safeUntil = Time.time + ownerSafeSeconds;
        _armed = false;
        if (_col != null) _col.enabled = false;
    }

    public void IgnoreOwnerColliders(IEnumerable<Collider> ownerCols)
    {
        if (_col == null || ownerCols == null) return;
        foreach (var oc in ownerCols)
        {
            if (oc == null) continue;
            if (_ignoredOwnerCols.Add(oc))
                Physics.IgnoreCollision(_col, oc, true);
        }
    }

    // ---------- helpers ----------
    private static Vector3 MakeHorizontal(Vector3 dir, Transform fallback)
    {
        if (dir.sqrMagnitude < 1e-6f) dir = fallback ? fallback.forward : Vector3.forward;
        dir = Vector3.ProjectOnPlane(dir, Vector3.up);
        if (dir.sqrMagnitude < 1e-6f)
        {
            var fwd = fallback ? fallback.forward : Vector3.forward;
            var alt = Vector3.ProjectOnPlane(fwd, Vector3.up);
            dir = alt.sqrMagnitude > 1e-6f ? alt.normalized : Vector3.right;
        }
        else
        {
            dir.Normalize();
        }
        return dir;
    }

    private void DoSweep(Vector3 from, Vector3 to)
    {
        if (_col == null || !_armed) return;

        float dist = Vector3.Distance(from, to);
        if (dist <= 0f) return;

        float radius = GetSweepRadius();
        Vector3 dir = (to - from) / dist;

        // Only check layers that can block or be damaged. Exclude our own layer.
        int detectionMask = (blockMask.value | hitMask.value) & ~(1 << gameObject.layer);

        var hits = Physics.SphereCastAll(
            from, radius, dir, dist + sweepPadding, detectionMask,
            includeTriggersInSweep ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits.Length && !_hasHit; i++)
        {
            var h = hits[i];

            // --- skip self/children ---
            if (h.collider == _col || h.rigidbody == _rb || h.collider.transform.IsChildOf(transform))
                continue;

            var targetGO = h.rigidbody ? h.rigidbody.gameObject : h.collider.gameObject;
            int layerBit = 1 << targetGO.layer;

            // Skip owner
            if (IsOwnerOrChild(targetGO)) continue;

            // Pass-through enemies
            if (passThroughEnemies && IsEnemy(targetGO)) continue;

            // Damage?
            if (ShouldDamage(targetGO))
            {
                if (debugLogs) EditorLog($"[BossBullet] Sweep HIT (damage) {targetGO.name} at {h.point}");
                ApplyDamage(targetGO);
                _hasHit = true;
                Destroy(gameObject);
                break;
            }

            // Blockers (world/obstacles) – only if in blockMask
            if ((blockMask.value & layerBit) != 0)
            {
                if (debugLogs) EditorLog($"[BossBullet] Sweep BLOCK {targetGO.name} at {h.point}");
                _hasHit = true;
                Destroy(gameObject);
                break;
            }

            // Otherwise ignore
        }
    }

    private bool IsOwnerOrChild(GameObject target)
    {
        return _owner && (target == _owner || target.transform.IsChildOf(_owner.transform));
    }

    private bool IsEnemy(GameObject target)
    {
        if (!passThroughEnemies || target == null) return false;

        // Layer check
        if (((1 << target.layer) & enemyLayers) != 0) return true;

        // Tag on target and its root
        for (int i = 0; i < enemyTags.Length; i++)
        {
            string t = enemyTags[i];
            if (target.CompareTag(t)) return true;

            var root = target.transform.root;
            if (root != null && root.CompareTag(t)) return true;
        }

        // Heuristic: treat objects with EnemyController as enemies
        return target.GetComponentInParent<EnemyController>() != null;
    }

    private bool ShouldDamage(GameObject target)
    {
        if (target == null) return false;

        // DAMAGE gating: layer mask + tag filter
        if (((1 << target.layer) & hitMask) == 0) return false;

        var root = target.transform.root ? target.transform.root.gameObject : target;
        bool isPlayer = root.CompareTag(playerTag) || target.CompareTag(playerTag);
        bool isFriendly = root.CompareTag(friendlyTag) || target.CompareTag(friendlyTag);
        if (!isPlayer && !isFriendly) return false;

        return true;
    }

    private void ApplyDamage(GameObject target)
    {
        if (debugLogs)
        {
            var root = target.transform.root ? target.transform.root.gameObject : target;
            EditorLog($"[BossBullet] APPLY DAMAGE {damage} to {root.name} (via {target.name}) at t={Time.time:F2}");
        }

        var pc = target.GetComponentInParent<PlayerController>();
        if (pc != null) { pc.TakeDamage(damage); return; }

        var fa = target.GetComponentInParent<FriendlyAI>();
        if (fa != null) { fa.TakeDamage(damage); return; }

        target.SendMessage("ApplyDamage", damage, SendMessageOptions.DontRequireReceiver);
    }

    private float GetSweepRadius()
    {
        var sphere = _col as SphereCollider;
        if (sphere != null)
        {
            var s = transform.lossyScale;
            float maxAxis = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            return Mathf.Max(0.01f, sphere.radius * maxAxis);
        }

        var e = _col.bounds.extents;
        float approx = Mathf.Max(e.x, e.y, e.z);
        return Mathf.Max(0.01f, approx);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_col == null) _col = GetComponent<Collider>();
        if (_rb == null) _rb = GetComponent<Rigidbody>();
        if (_col != null) _col.isTrigger = true;
        if (_rb != null)
        {
            _rb.isKinematic = true;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }
        if (speed < 0f) speed = 0f;
        extraSnapFrames = Mathf.Clamp(extraSnapFrames, 0, 2);
        if (pushForwardFromMuzzle < 0f) pushForwardFromMuzzle = 0f;
    }
#endif
}
