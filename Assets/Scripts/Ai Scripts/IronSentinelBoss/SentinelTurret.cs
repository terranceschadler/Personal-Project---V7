using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class SentinelTurret : MonoBehaviour
{
    [Header("Ownership")]
    public IronSentinelBoss owner;
    [Tooltip("World/walls mask for LOS checks.")]
    public LayerMask worldMask = ~0;

    [Header("Targeting")]
    public float range = 24f;
    public bool requireLOS = true;
    public float losTargetHeight = 1.2f;
    public float eyeHeight = 1.3f;

    [Header("Missile Barrage")]
    public GameObject missilePrefab;
    public Transform missileMuzzle;     // optional, if null uses offsets
    public float muzzleUp = 0.6f;
    public float muzzleForward = 0.4f;
    [Tooltip("Extra push forward from muzzle so we don't overlap the turret collider.")]
    public float spawnClearRadius = 0.5f;
    public int missilesPerBarrage = 2;
    public float missileInterval = 0.18f;

    [Header("Cooldowns")]
    public float localCooldown = 1.2f; // turret's own cooldown
    private float _nextAllowedFireAt = 0f;

    [Header("Anti-FF / Arming")]
    [Tooltip("Seconds after spawn to keep missile colliders disabled (prevents instant self-hit).")]
    public float missileArmTime = 0.12f;
    [Tooltip("Also ignore collisions with the boss (owner root).")]
    public bool ignoreBossColliders = true;
    [Tooltip("Also ignore collisions with sibling turrets under the same boss.")]
    public bool ignoreSiblingTurrets = true;

    [Header("Audio (optional)")]
    public AudioSource sfx;
    public AudioClip fireSfx;

    [Header("Diagnostics")]
    public bool diagLogs = true;

    // -------- Boss entry point --------
    public void OrderBarrage(Transform suggestedTarget)
    {
        if (!enabled) { if (diagLogs) Debug.Log("[Turret][DIAG] Disabled; ignoring order.", this); return; }
        if (missilePrefab == null) { if (diagLogs) Debug.LogWarning("[Turret][DIAG] missilePrefab is NULL.", this); return; }
        if (Time.time < _nextAllowedFireAt) { if (diagLogs) Debug.Log("[Turret][DIAG] On cooldown.", this); return; }

        Transform t = suggestedTarget ? suggestedTarget : FindTarget();
        if (t == null) { if (diagLogs) Debug.Log("[Turret][DIAG] No target.", this); return; }

        float d = Vector3.Distance(transform.position, t.position);
        if (d > range) { if (diagLogs) Debug.Log($"[Turret][DIAG] Target out of range ({d:0.0}>{range}).", this); return; }

        if (requireLOS && !HasLOS(t)) { if (diagLogs) Debug.Log("[Turret][DIAG] No LOS.", this); return; }

        StartCoroutine(CoBarrage(t));
        _nextAllowedFireAt = Time.time + localCooldown;
    }

    private IEnumerator CoBarrage(Transform target)
    {
        for (int i = 0; i < missilesPerBarrage; i++)
        {
            if (!missilePrefab) yield break;
            if (!target) yield break;

            // Muzzle & direction
            Vector3 muzzle = missileMuzzle
                ? missileMuzzle.position
                : transform.position + Vector3.up * muzzleUp + transform.forward * muzzleForward;

            Vector3 dir = (target.position + Vector3.up * 1.0f - muzzle).normalized;
            Vector3 spawn = muzzle + dir * Mathf.Max(0.01f, spawnClearRadius);

            // Instantiate
            GameObject m = Instantiate(missilePrefab, spawn, Quaternion.LookRotation(dir, Vector3.up));

            // Tell the missile about target/instigator if it exposes these (SendMessage is optional-safe)
            m.SendMessage("SetTarget", target, SendMessageOptions.DontRequireReceiver);
            m.SendMessage("SetInstigator", owner ? owner.gameObject : gameObject, SendMessageOptions.DontRequireReceiver);

            // Anti-friendly-fire: ignore collisions with turret, boss, and sibling turrets
            ApplyCollisionIgnores(m);

            // Short arming window: missile colliders disabled for a moment, then re-enabled
            StartCoroutine(TemporarilyDisableMissileColliders(m, missileArmTime));

            if (sfx && fireSfx) sfx.PlayOneShot(fireSfx, 0.85f);
            if (diagLogs) Debug.Log($"[Turret][DIAG] Missile spawned at {spawn} toward {target.name}", this);

            yield return new WaitForSeconds(missileInterval);
        }
    }

    // --- Helper: temporarily disable missile colliders, then re-enable (prevents instant self-hit)
    private IEnumerator TemporarilyDisableMissileColliders(GameObject missile, float seconds)
    {
        if (!missile || seconds <= 0f) yield break;

        var cols = missile.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
            if (cols[i]) cols[i].enabled = false;

        yield return new WaitForSeconds(seconds);

        for (int i = 0; i < cols.Length; i++)
            if (cols[i]) cols[i].enabled = true;
    }

    // --- Helper: apply collision ignores between missile and turret/boss/sibling turrets
    private void ApplyCollisionIgnores(GameObject missile)
    {
        if (!missile) return;

        var missileCols = missile.GetComponentsInChildren<Collider>(true);
        if (missileCols == null || missileCols.Length == 0) return;

        int ignored = 0;

        // Ignore turret's own colliders
        var selfCols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < missileCols.Length; i++)
            for (int j = 0; j < selfCols.Length; j++)
                if (missileCols[i] && selfCols[j] && missileCols[i] != selfCols[j])
                { Physics.IgnoreCollision(missileCols[i], selfCols[j], true); ignored++; }

        // Ignore boss/root colliders
        if (ignoreBossColliders && owner)
        {
            var bossCols = owner.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < missileCols.Length; i++)
                for (int j = 0; j < bossCols.Length; j++)
                    if (missileCols[i] && bossCols[j] && missileCols[i] != bossCols[j])
                    { Physics.IgnoreCollision(missileCols[i], bossCols[j], true); ignored++; }
        }

        // Ignore sibling turrets (other SentinelTurret under same owner)
        if (ignoreSiblingTurrets && owner)
        {
            var siblings = owner.GetComponentsInChildren<SentinelTurret>(true);
            for (int s = 0; s < siblings.Length; s++)
            {
                var sib = siblings[s];
                if (sib == null || sib == this) continue;
                var sibCols = sib.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < missileCols.Length; i++)
                    for (int j = 0; j < sibCols.Length; j++)
                        if (missileCols[i] && sibCols[j] && missileCols[i] != sibCols[j])
                        { Physics.IgnoreCollision(missileCols[i], sibCols[j], true); ignored++; }
            }
        }

        if (diagLogs) Debug.Log($"[Turret][DIAG] Set {ignored} collision ignores on missile.", this);
    }

    // ---------- Targeting helpers ----------
    private Transform FindTarget()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        Transform best = null; float bestD = float.PositiveInfinity;

        if (playerObj != null)
        {
            best = playerObj.transform;
            bestD = Vector3.Distance(transform.position, best.position);
        }

        var friendlies = GameObject.FindGameObjectsWithTag("FriendlyAI");
        for (int i = 0; i < friendlies.Length; i++)
        {
            float d = Vector3.Distance(transform.position, friendlies[i].transform.position);
            if (d < bestD) { best = friendlies[i].transform; bestD = d; }
        }
        return best;
    }

    private bool HasLOS(Transform t)
    {
        if (t == null) return false;
        Vector3 from = transform.position + Vector3.up * eyeHeight;
        Vector3 to = t.position + Vector3.up * losTargetHeight;
        if (Physics.Linecast(from, to, out RaycastHit hit, worldMask, QueryTriggerInteraction.Ignore))
            return hit.collider.transform.root == t.root;
        return true;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        UnityEditor.Handles.color = new Color(0.2f, 0.8f, 1f, 0.3f);
        UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, range);

        Vector3 muzzle = missileMuzzle ? missileMuzzle.position : transform.position + Vector3.up * muzzleUp + transform.forward * muzzleForward;
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(muzzle, 0.05f);
    }

    private void OnValidate()
    {
        range = Mathf.Max(0.1f, range);
        missilesPerBarrage = Mathf.Max(0, missilesPerBarrage);
        missileInterval = Mathf.Max(0.01f, missileInterval);
        localCooldown = Mathf.Max(0f, localCooldown);
        spawnClearRadius = Mathf.Clamp(spawnClearRadius, 0.01f, 3f);
        missileArmTime = Mathf.Clamp(missileArmTime, 0f, 1f);
    }
#endif
}
