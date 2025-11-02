using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public class BoneforgeTitanBoss : BossEnemy
{
    [Header("General / Diagnostics")]
    [Tooltip("Enable verbose info logs in Console (warnings/errors always show).")]
    public bool debugLogs = true;
    public LayerMask worldMask = ~0;

    [Header("Armor Gate")]
    [Tooltip("While any plates remain, ALL incoming damage is redirected to an intact plate.")]
    public bool redirectAllDamageToPlatesWhileIntact = true;

    [Tooltip("If redirect is OFF, scale boss-body damage by this while plates remain.")]
    [Range(0f, 1f)] public float armoredDamageMultiplier = 0.35f;

    [Tooltip("Reference to plates (auto-fill if empty).")]
    public BonePlate[] plates;

    [Header("Shatter Nova (on plate break)")]
    public float shatterNovaRadius = 6f;
    public float shatterNovaDamage = 18f;
    public float shatterNovaImpulse = 8f;
    public AudioClip shatterSfx;
    public ParticleSystem shatterVfx;

    [Header("Marrow Spikes (Line Walls)")]
    public GameObject marrowSpikePrefab;
    public float spikeHalfLen_Base = 5f;
    public float spikeHalfLen_PerBroken = 1.5f;
    public float spikeSpacing = 1.4f;
    public float spikeForwardOffset = 5f;
    public float spikesCooldown = 6.5f;
    private float _nextSpikesAt;

    [Header("Skeletal Minion Forge")]
    public int forgeMinionsPerCast = 3;
    public float forgeSpawnRadius = 4f;
    public float forgeCooldown = 9f;
    private float _nextForgeAt;

    [Header("Enrage (after all plates broken)")]
    public float enrageCadenceMultiplier = 0.65f;
    public AnimationCurve armorReductionVsBroken = AnimationCurve.EaseInOut(0, 1f, 4, 0f);

    [Header("Targeting / LOS")]
    public float eyeHeight = 1.7f;
    public bool spikesRequireLOS = false;
    public bool forgeRequireLOS = false;

    // Internals
    private int _platesBroken;
    private bool _enraged;
    private AudioSource _sfx;

    // ---------- Debug helpers ----------
    private void DLog(string msg) { if (debugLogs) Debug.Log(msg, this); }
    private void DLogFormat(string fmt, params object[] args) { if (debugLogs) Debug.LogFormat(this, fmt, args); }

    protected override void Start()
    {
        base.Start();
        _sfx = GetComponent<AudioSource>();

        if (plates == null || plates.Length == 0)
        {
            plates = GetComponentsInChildren<BonePlate>(true);
            DLog($"[Boneforge] Auto-found {plates.Length} plates.");
        }
        for (int i = 0; i < plates.Length; i++)
        {
            if (!plates[i]) continue;
            plates[i].owner = this;
            plates[i].index = i;
        }

        _nextSpikesAt = Time.time + Random.Range(2f, 3.5f);
        _nextForgeAt = Time.time + Random.Range(5f, 7f);

        if (specialAttackCooldown < 0.1f) specialAttackCooldown = 2.5f;
    }

    // --- Intercept BOTH damage paths ---

    // 1) Payload path (bullets that call ApplyDamageFrom)
    public new void ApplyDamageFrom(object payload)
    {
        float amt = 0f; GameObject inst = null;

        switch (payload)
        {
            case DamageEnvelope env: amt = env.amount; inst = env.instigator; break;
            case System.ValueTuple<float, GameObject> tup: amt = tup.Item1; inst = tup.Item2; break;
            case float f: amt = f; break;
            default:
                base.ApplyDamageFrom(payload);
                return;
        }

        int intactCount = CountIntactPlates();

        if (intactCount > 0 && redirectAllDamageToPlatesWhileIntact)
        {
            var plate = PickPlateForDamage(inst);
            if (plate != null)
            {
                plate.TakeDamage(amt);
                DLog($"[Boneforge] Redirected {amt:0.#} dmg to plate {plate.index} (payload). Intact left: {intactCount}");
                return;
            }
        }

        if (intactCount > 0 && !redirectAllDamageToPlatesWhileIntact)
        {
            float t = Mathf.Clamp(plates.Length - intactCount, 0, 4);
            float reductionFactor = armorReductionVsBroken != null ? armorReductionVsBroken.Evaluate(t) : 1f;
            float mult = Mathf.Lerp(1f, armoredDamageMultiplier, reductionFactor);
            amt *= mult;
        }

        base.ApplyDamageFrom(new DamageEnvelope(amt, inst));
    }

    // 2) Float-only path (bullets that call TakeDamage(float))
    public override void TakeDamage(float damage)
    {
        int intactCount = CountIntactPlates();

        if (intactCount > 0 && redirectAllDamageToPlatesWhileIntact)
        {
            // No instigator info in this path—pick best by player direction, else any
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            var plate = PickPlateForDamage(player);
            if (plate != null)
            {
                plate.TakeDamage(damage);
                DLog($"[Boneforge] Redirected {damage:0.#} dmg to plate {plate.index} (float). Intact left: {intactCount}");
                return; // boss takes 0
            }
        }

        if (intactCount > 0 && !redirectAllDamageToPlatesWhileIntact)
        {
            float t = Mathf.Clamp(plates.Length - intactCount, 0, 4);
            float reductionFactor = armorReductionVsBroken != null ? armorReductionVsBroken.Evaluate(t) : 1f;
            float mult = Mathf.Lerp(1f, armoredDamageMultiplier, reductionFactor);
            damage *= mult;
        }

        base.TakeDamage(damage);
    }

    // --- Plate selection helpers ---
    private BonePlate PickPlateForDamage(GameObject instigator)
    {
        var intact = GetIntactPlates();
        if (intact.Count == 0) return null;

        Transform refT = instigator ? instigator.transform : null;
        if (!refT)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player) refT = player.transform;
        }
        if (!refT) return intact[0];

        Vector3 fromBossToInst = refT.position - transform.position;
        fromBossToInst.y = 0f;
        if (fromBossToInst.sqrMagnitude < 0.0001f) return intact[0];
        fromBossToInst.Normalize();

        BonePlate best = null;
        float bestDot = -2f;
        for (int i = 0; i < intact.Count; i++)
        {
            Vector3 toPlate = intact[i].WorldPosition - transform.position;
            toPlate.y = 0f;
            if (toPlate.sqrMagnitude < 0.0001f) continue;
            toPlate.Normalize();

            float dot = Vector3.Dot(fromBossToInst, toPlate);
            if (dot > bestDot) { bestDot = dot; best = intact[i]; }
        }
        return best ?? intact[0];
    }

    private int CountIntactPlates()
    {
        int c = 0;
        if (plates != null)
            for (int i = 0; i < plates.Length; i++)
                if (plates[i] && !plates[i].IsBroken) c++;
        return c;
    }

    private List<BonePlate> GetIntactPlates()
    {
        var list = new List<BonePlate>(4);
        if (plates != null)
            for (int i = 0; i < plates.Length; i++)
                if (plates[i] && !plates[i].IsBroken) list.Add(plates[i]);
        return list;
    }

    // --- Plate break feedback ---
    public void NotifyPlateBroken(BonePlate plate)
    {
        _platesBroken = Mathf.Clamp(_platesBroken + 1, 0, 4);

        DLog($"[Boneforge] Plate broken! Broken={_platesBroken}/4");

        DoShatterNova();

        if (_platesBroken >= 4 && !_enraged)
        {
            _enraged = true;
            specialAttackCooldown *= Mathf.Clamp(enrageCadenceMultiplier, 0.25f, 1f);
            spikesCooldown *= Mathf.Clamp(enrageCadenceMultiplier, 0.25f, 1f);
            forgeCooldown *= Mathf.Clamp(enrageCadenceMultiplier, 0.25f, 1f);
            DLog("[Boneforge] ENRAGED! Specials cadence increased.");
        }
    }

    private void DoShatterNova()
    {
        if (shatterVfx) Instantiate(shatterVfx, transform.position, Quaternion.identity);
        if (_sfx && shatterSfx) _sfx.PlayOneShot(shatterSfx, 0.9f);

        Collider[] hits = Physics.OverlapSphere(transform.position, shatterNovaRadius, ~0, QueryTriggerInteraction.Ignore);
        int applied = 0;
        for (int i = 0; i < hits.Length; i++)
        {
            var h = hits[i];
            if (!h) continue;
            if (h.transform.root == transform.root) continue;

            GameObject victim = h.attachedRigidbody ? h.attachedRigidbody.gameObject : h.gameObject;
            victim.SendMessage("ApplyDamageFrom", new DamageEnvelope(shatterNovaDamage, gameObject), SendMessageOptions.DontRequireReceiver);
            victim.SendMessage("TakeDamage", shatterNovaDamage, SendMessageOptions.DontRequireReceiver);
            applied++;

            if (shatterNovaImpulse > 0f && h.attachedRigidbody)
            {
                Vector3 dir = (h.transform.position - transform.position).normalized;
                h.attachedRigidbody.AddForce(dir * shatterNovaImpulse, ForceMode.Impulse);
            }
        }

        DLog($"[Boneforge] Shatter Nova hit {applied} target(s).");
    }

    // --- Specials (unchanged) ---
    protected override void SpecialAttack()
    {
        Transform target = FindPrimaryTarget();
        if (!target) return;

        bool spikesUnlocked = _platesBroken >= 1;
        bool forgeUnlocked = _platesBroken >= 3;

        bool didCast = false;

        if (spikesUnlocked && Time.time >= _nextSpikesAt)
        {
            CastMarrowSpikes(target);
            _nextSpikesAt = Time.time + spikesCooldown;
            didCast = true;
        }

        if (!didCast && forgeUnlocked && Time.time >= _nextForgeAt)
        {
            CastForgeMinions(target);
            _nextForgeAt = Time.time + forgeCooldown;
            didCast = true;
        }

        if (didCast) DLog("[Boneforge] Special cast.");
    }

    private void CastMarrowSpikes(Transform target)
    {
        if (!marrowSpikePrefab)
        {
            Debug.LogWarning("[Boneforge] MarrowSpike prefab not assigned.", this);
            return;
        }

        Vector3 bossPos = transform.position;
        Vector3 toT = target.position - bossPos; toT.y = 0f;
        if (toT.sqrMagnitude < 0.01f) toT = transform.forward;

        Vector3 fwd = toT.normalized;
        Vector3 center = bossPos + fwd * spikeForwardOffset;
        Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);

        float halfLen = spikeHalfLen_Base + spikeHalfLen_PerBroken * Mathf.Clamp(_platesBroken, 0, 3);
        int count = Mathf.Max(1, Mathf.CeilToInt((halfLen * 2f) / Mathf.Max(0.1f, spikeSpacing)));

        int spawned = 0;
        for (int i = -count / 2; i <= count / 2; i++)
        {
            Vector3 raw = center + right * (i * spikeSpacing);

            Vector3 pos = raw;
            if (NavMesh.SamplePosition(raw, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                pos = hit.position;

            GameObject spike = Instantiate(marrowSpikePrefab, pos, Quaternion.LookRotation(fwd, Vector3.up));
            if (spike.TryGetComponent<MarrowSpike>(out var ms)) ms.owner = this;
            spawned++;
        }

        DLog($"[Boneforge] Marrow Spikes spawned: {spawned} (broken={_platesBroken}).");
    }

    private void CastForgeMinions(Transform target)
    {
        if (zombiePrefabs == null || zombiePrefabs.Length == 0)
        {
            Debug.LogWarning("[Boneforge] No zombiePrefabs assigned for Forge.", this);
            return;
        }

        int spawned = 0;
        for (int i = 0; i < forgeMinionsPerCast; i++)
        {
            float ang = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            Vector3 raw = transform.position + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * forgeSpawnRadius;

            Vector3 pos = raw;
            if (NavMesh.SamplePosition(raw, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                pos = hit.position;

            GameObject prefab = zombiePrefabs[Random.Range(0, zombiePrefabs.Length)];
            Instantiate(prefab, pos, Quaternion.identity);
            spawned++;
        }

        DLog($"[Boneforge] Forge spawned {spawned} minion(s).");
    }

    private Transform FindPrimaryTarget()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj) return playerObj.transform;

        var friendlies = GameObject.FindGameObjectsWithTag("FriendlyAI");
        Transform best = null; float bestD = float.PositiveInfinity;
        for (int i = 0; i < friendlies.Length; i++)
        {
            float d = Vector3.Distance(transform.position, friendlies[i].transform.position);
            if (d < bestD) { best = friendlies[i].transform; bestD = d; }
        }
        return best;
    }

#if UNITY_EDITOR
    [ContextMenu("DEBUG: Damage Plate (10)")]
    private void DebugDamageNearestPlate()
    {
        var p = PickPlateForDamage(GameObject.FindGameObjectWithTag("Player"));
        if (p != null) { p.TakeDamage(10f); DLog("[Boneforge][DEBUG] Forced 10 dmg to a plate."); }
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 bossPos = transform.position;
        Vector3 fwd = transform.forward;
        Transform t = GameObject.FindGameObjectWithTag("Player")?.transform;
        if (t)
        {
            Vector3 toT = t.position - bossPos; toT.y = 0f;
            if (toT.sqrMagnitude > 0.01f) fwd = toT.normalized;
        }
        Vector3 center = bossPos + fwd * spikeForwardOffset;
        Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
        float halfLen = spikeHalfLen_Base + spikeHalfLen_PerBroken * Mathf.Clamp(_platesBroken, 0, 3);

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(center - right * halfLen, center + right * halfLen);
        Gizmos.DrawWireSphere(transform.position, shatterNovaRadius);
    }
#endif
}
