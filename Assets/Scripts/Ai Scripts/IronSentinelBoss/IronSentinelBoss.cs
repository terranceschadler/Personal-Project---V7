using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// IRON SENTINEL — Fortify/Turrets/Shield Dome (turrets fire missiles)
/// Phases: Mobile ? Fortifying ? Fortified ? Uprooting ? Mobile (loops)
/// - While Fortified: deploys turrets and can spawn a shield dome.
/// - Missile barrages are FIRED BY TURRETS (boss only coordinates timing).
/// - Includes diagnostics & a test toggle to force Fortified on spawn.
/// - Uses BossEnemy damage gates and notifies GameManager + BossSpawner on death via base class.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public class IronSentinelBoss : BossEnemy
{
    public enum Phase { Mobile, Fortifying, Fortified, Uprooting }

    // ---------------- Inspector: General ----------------
    [Header("General")]
    [Tooltip("Extra debug prints for this boss (phase changes, reasons for special failures, etc.).")]
    public bool diagLogs = true;

    [Tooltip("Layers considered 'world' for LOS checks (walls, props). Exclude Player/Friendly layers.")]
    public LayerMask worldMask = ~0;

    [Tooltip("Eye height for LOS raycasts.")]
    public float eyeHeight = 1.8f;

    // ---------------- Inspector: Diagnostics / Testing ----------------
    [Header("Diagnostics / Testing")]
    [Tooltip("Skip Mobile and go straight into Fortified on Start to test specials.")]
    public bool forceFortifyOnSpawn = false;

    [Tooltip("When forcing Fortified on spawn, how long to remain Fortified.")]
    public float forceFortifiedDuration = 8f;

    // ---------------- Inspector: Fortify Cycle ----------------
    [Header("Fortify Cycle")]
    [Tooltip("How long the boss stays mobile before fortifying.")]
    public float mobileDuration = 7.5f;

    [Tooltip("How long it takes to enter Fortified (animation/VFX window).")]
    public float fortifyWindup = 0.9f;

    [Tooltip("How long the boss stays fortified before uprooting.")]
    public float fortifiedDuration = 10f;

    [Tooltip("How long uprooting / transition back to mobile takes.")]
    public float uprootWinddown = 0.9f;

    // ---------------- Inspector: Armor While Fortified ----------------
    [Header("Armor While Fortified")]
    [Tooltip("All incoming damage multiplied by this while Fortified.")]
    [Range(0f, 1f)] public float fortifiedDamageMultiplier = 0.3f;

    [Tooltip("Rear/back weakpoint multiplier (applied AFTER fortified multiplier by inverting it). Higher = takes more damage from the rear.")]
    public float rearWeakpointMultiplier = 1.4f;

    [Tooltip("Degrees behind the boss that count as 'rear' (e.g., 120 = wide back).")]
    [Range(0f, 180f)] public float rearArcDegrees = 120f;

    // ---------------- Inspector: Turret Deployment ----------------
    [Header("Turret Deployment")]
    [Tooltip("Auto-turret prefab (must have SentinelTurret component).")]
    public GameObject turretPrefab;

    [Tooltip("How many turrets to deploy while fortified.")]
    [Range(0, 6)] public int turretCount = 2;

    [Tooltip("Ring radius to place turrets around boss.")]
    public float turretRingRadius = 4.5f;

    [Tooltip("Minimum angular spacing check helper (advisory).")]
    public float minTurretSeparationDeg = 45f;

    [Tooltip("Delay between spawning each turret (seconds).")]
    public float turretSpawnInterval = 0.2f;

    // ---------------- Inspector: Barrage Coordination ----------------
    [Header("Barrage Coordination (Turrets Fire)")]
    [Tooltip("Cooldown between boss-ordered turret barrages while Fortified.")]
    public float barrageOrderCooldown = 2.5f;

    // ---------------- Inspector: Shield Dome (optional) ----------------
    [Header("Shield Dome (optional)")]
    [Tooltip("If assigned, a shield dome spawns while fortified and soaks/destroys player bullets. Use ShieldDome component.")]
    public GameObject shieldDomePrefab;

    [Tooltip("Local offset for dome center (usually 0,0,0).")]
    public Vector3 shieldDomeLocalOffset = Vector3.zero;

    [Tooltip("Spawn the shield dome a short moment after we finish fortifying (for timing/animation sync).")]
    public float shieldDomeDelay = 0.25f;

    // ---------------- Inspector: Audio (optional) ----------------
    [Header("Audio (optional)")]
    public AudioSource sfx;
    public AudioClip fortifySfx;
    public AudioClip uprootSfx;
    public AudioClip deployTurretSfx;
    public AudioClip barrageSfx;   // played when boss orders turrets to fire

    // ---------------- Internals ----------------
    private Phase _phase = Phase.Mobile;
    private float _phaseEndsAt;
    private float _nextBarrageOrderAt;
    private readonly List<GameObject> _spawnedTurrets = new List<GameObject>(6);
    private GameObject _activeDome;

    private bool IsFortified => _phase == Phase.Fortified || _phase == Phase.Fortifying;

    // ---------------- Lifecycle ----------------
    protected override void Start()
    {
        base.Start();

        EnterPhase(Phase.Mobile, mobileDuration);

        if (forceFortifyOnSpawn)
        {
            if (diagLogs) Debug.Log("[IronSentinel][DIAG] Forcing Fortified on spawn.", this);
            StopAllCoroutines();
            StartCoroutine(CoEnterFortifiedImmediately());
        }
    }

    protected override void Update()
    {
        base.Update();

        switch (_phase)
        {
            case Phase.Mobile:
                if (Time.time >= _phaseEndsAt)
                    StartCoroutine(CoEnterFortified());
                break;

            case Phase.Fortified:
                if (Time.time >= _nextBarrageOrderAt)
                {
                    OrderTurretBarrage();
                    _nextBarrageOrderAt = Time.time + barrageOrderCooldown;
                }
                if (Time.time >= _phaseEndsAt)
                    StartCoroutine(CoExitFortified());
                break;
        }
    }

    private void OnDisable()
    {
        DespawnShieldDome();
        DespawnTurrets();
    }

    protected override void Die()
    {
        DespawnShieldDome();
        DespawnTurrets();
        base.Die();
    }

    // ---------------- Phase Management ----------------
    private void EnterPhase(Phase p, float duration)
    {
        _phase = p;
        _phaseEndsAt = Time.time + Mathf.Max(0.01f, duration);
        if (diagLogs) Debug.Log($"[IronSentinel] Phase -> {p} (ends in {duration:0.00}s)", this);

        if (p == Phase.Mobile) SafeSetStopped(false);
        else SafeSetStopped(true);
    }

    private IEnumerator CoEnterFortified()
    {
        EnterPhase(Phase.Fortifying, fortifyWindup);
        if (sfx && fortifySfx) sfx.PlayOneShot(fortifySfx, 0.9f);

        yield return new WaitForSeconds(fortifyWindup);

        // Turrets
        yield return StartCoroutine(CoSpawnTurrets());

        // Dome
        if (shieldDomePrefab != null)
        {
            yield return new WaitForSeconds(shieldDomeDelay);
            SpawnShieldDome();
        }
        else if (diagLogs)
        {
            Debug.LogWarning("[IronSentinel][DIAG] No ShieldDome prefab assigned, skipping dome.", this);
        }

        // First barrage order soon after planting
        _nextBarrageOrderAt = Time.time + 0.4f;

        EnterPhase(Phase.Fortified, fortifiedDuration);
    }

    private IEnumerator CoExitFortified()
    {
        EnterPhase(Phase.Uprooting, uprootWinddown);

        DespawnShieldDome();
        DespawnTurrets();

        if (sfx && uprootSfx) sfx.PlayOneShot(uprootSfx, 0.9f);

        yield return new WaitForSeconds(uprootWinddown);

        EnterPhase(Phase.Mobile, mobileDuration);

        // Optional: reacquire destination
        var p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) SafeSetDestination(p.transform.position);
    }

    private IEnumerator CoEnterFortifiedImmediately()
    {
        _phase = Phase.Fortifying;
        SafeSetStopped(true);

        yield return new WaitForSeconds(Mathf.Max(0.05f, fortifyWindup));

        yield return StartCoroutine(CoSpawnTurrets());

        if (shieldDomePrefab != null)
        {
            yield return new WaitForSeconds(shieldDomeDelay);
            SpawnShieldDome();
        }
        else if (diagLogs)
        {
            Debug.LogWarning("[IronSentinel][DIAG] No ShieldDome prefab assigned, skipping dome.", this);
        }

        _nextBarrageOrderAt = Time.time + 0.4f;
        EnterPhase(Phase.Fortified, Mathf.Max(2f, forceFortifiedDuration));
    }

    // ---------------- Turrets ----------------
    private IEnumerator CoSpawnTurrets()
    {
        DespawnTurrets();

        if (turretPrefab == null)
        {
            if (diagLogs) Debug.LogWarning("[IronSentinel][DIAG] turretPrefab is NULL. No turrets will spawn.", this);
            yield break;
        }
        if (turretCount <= 0)
        {
            if (diagLogs) Debug.LogWarning("[IronSentinel][DIAG] turretCount <= 0. No turrets will spawn.", this);
            yield break;
        }

        float angleStep = 360f / Mathf.Max(1, turretCount);
        int placed = 0;

        for (int i = 0; i < turretCount; i++)
        {
            float angle = i * angleStep;
            Vector3 dir = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad));
            Vector3 wanted = transform.position + dir * turretRingRadius;

            Vector3 pos = wanted;
            if (NavMesh.SamplePosition(wanted, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                pos = hit.position;

            // spacing guard
            bool tooClose = false;
            for (int j = 0; j < _spawnedTurrets.Count; j++)
            {
                var t = _spawnedTurrets[j];
                if (t == null) continue;
                if ((pos - t.transform.position).sqrMagnitude < (turretRingRadius * 0.5f))
                { tooClose = true; break; }
            }
            if (tooClose)
            {
                if (diagLogs) Debug.LogWarning("[IronSentinel][DIAG] Skipping turret slot (too close).", this);
                continue;
            }

            GameObject tGO = Instantiate(turretPrefab, pos, Quaternion.LookRotation(-dir, Vector3.up));
            _spawnedTurrets.Add(tGO);

            if (tGO.TryGetComponent<SentinelTurret>(out var turret))
            {
                turret.owner = this;
                turret.worldMask = worldMask;
            }
            else if (diagLogs)
            {
                Debug.LogWarning("[IronSentinel][DIAG] Spawned turret prefab lacks SentinelTurret component.", this);
            }

            if (diagLogs) Debug.Log($"[IronSentinel][DIAG] Turret spawned at {pos}", this);
            if (sfx && deployTurretSfx) sfx.PlayOneShot(deployTurretSfx, 0.8f);

            placed++;
            yield return new WaitForSeconds(turretSpawnInterval);
        }

        if (diagLogs) Debug.Log($"[IronSentinel] Deployed {placed} turret(s).", this);
    }

    private void DespawnTurrets()
    {
        for (int i = 0; i < _spawnedTurrets.Count; i++)
            if (_spawnedTurrets[i] != null) Destroy(_spawnedTurrets[i]);
        _spawnedTurrets.Clear();
    }

    // Boss ordering function (turrets do the actual firing)
    private void OrderTurretBarrage()
    {
        if (sfx && barrageSfx) sfx.PlayOneShot(barrageSfx, 0.8f);

        // Pick one shared target for this order (simple & readable), or let turrets pick their own.
        Transform suggestedTarget = PickTarget();

        for (int i = 0; i < _spawnedTurrets.Count; i++)
        {
            var t = _spawnedTurrets[i];
            if (t == null) continue;

            if (t.TryGetComponent<SentinelTurret>(out var turret))
            {
                turret.OrderBarrage(suggestedTarget);
            }
        }

        if (diagLogs) Debug.Log("[IronSentinel][DIAG] Ordered turret barrage.", this);
    }

    // ---------------- Shield Dome ----------------
    private void SpawnShieldDome()
    {
        if (shieldDomePrefab == null)
        {
            if (diagLogs) Debug.LogWarning("[IronSentinel][DIAG] shieldDomePrefab is NULL. Dome will not spawn.", this);
            return;
        }
        if (_activeDome != null) Destroy(_activeDome);

        Vector3 pos = transform.TransformPoint(shieldDomeLocalOffset);
        _activeDome = Instantiate(shieldDomePrefab, pos, Quaternion.identity, transform);

        if (_activeDome.TryGetComponent<ShieldDome>(out var dome))
        {
            dome.owner = this;
        }
        else if (diagLogs)
        {
            Debug.LogWarning("[IronSentinel][DIAG] Shield dome prefab lacks ShieldDome component.", this);
        }

        if (diagLogs) Debug.Log("[IronSentinel][DIAG] Shield Dome spawned.", this);
    }

    private void DespawnShieldDome()
    {
        if (_activeDome != null)
        {
            Destroy(_activeDome);
            _activeDome = null;
            if (diagLogs) Debug.Log("[IronSentinel][DIAG] Shield Dome despawned.", this);
        }
    }

    // ---------------- Targeting / Utilities ----------------
    private Transform PickTarget()
    {
        // Prefer Player; fallback to nearest FriendlyAI
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        Transform best = null;
        float bestD = float.PositiveInfinity;

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

    private bool HasClearLOS(Transform t)
    {
        Vector3 from = transform.position + Vector3.up * eyeHeight;
        Vector3 to = t.position + Vector3.up * 1.2f;
        if (Physics.Linecast(from, to, out RaycastHit hit, worldMask))
            return hit.collider.transform.root == t.root;
        return true;
    }

    public new void ApplyDamageFrom(object payload)
    {
        if (!enabled) return;

        float amt = 0f; GameObject inst = null;
        switch (payload)
        {
            case DamageEnvelope env: amt = env.amount; inst = env.instigator; break;
            case System.ValueTuple<float, GameObject> tup: amt = tup.Item1; inst = tup.Item2; break;
            case float f: amt = f; break;
            default: base.ApplyDamageFrom(payload); return;
        }

        if (IsFortified)
        {
            float mult = fortifiedDamageMultiplier;
            if (inst != null && IsInRearArc(inst.transform.position))
                mult *= 1f / Mathf.Max(0.0001f, rearWeakpointMultiplier); // take more from rear
            amt *= mult;
        }

        base.ApplyDamageFrom(new DamageEnvelope(amt, inst));
    }

    private bool IsInRearArc(Vector3 attackerPos)
    {
        Vector3 toAttacker = attackerPos - transform.position; toAttacker.y = 0;
        if (toAttacker.sqrMagnitude < 0.001f) return false;
        float ang = Vector3.Angle(transform.forward, toAttacker);
        return ang > (180f - rearArcDegrees * 0.5f);
    }

    private bool AgentReady() => agent && agent.enabled && agent.isOnNavMesh && gameObject.activeInHierarchy;
    private void SafeSetStopped(bool stopped) { if (AgentReady()) agent.isStopped = stopped; }
    private void SafeSetDestination(Vector3 pos) { if (AgentReady()) agent.SetDestination(pos); }

    // Prevent base SpecialAttack (zombie ring) so the Sentinel has only turret/missile identity.
    protected override void SpecialAttack() { /* no-op for Iron Sentinel */ }
}
