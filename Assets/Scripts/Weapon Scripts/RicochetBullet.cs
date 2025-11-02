using UnityEngine;
using System;
using System.Reflection;

[DisallowMultipleComponent]
public class RicochetBullet : MonoBehaviour, IBullet
{
    [Header("Debug")]
    [SerializeField] private bool debugRico = false;

    [Header("Runtime (debug)")]
    [SerializeField] private Vector3 velocity;
    [SerializeField] private float damage;
    [SerializeField] private int bouncesUsed;
    [SerializeField] private GameObject owner;

    // Config from InitializeRicochet()
    private int maxBounces;
    private float speedLossPerBounce;
    private float damageLossPerBounce;
    private LayerMask ricochetSurfaces;
    private LayerMask enemyLayers;
    private LayerMask ignoreLayers;
    private bool biasRicochetTowardTargets;
    private float ricochetAimCone;
    private float ricochetTargetSearchRadius;
    private float minSpeedToContinue;
    private float maxLifeSeconds;
    private GameObject bounceVfxPrefab;
    private AudioClip bounceSfx;

    private float life;
    private AudioSource audioSource;

    // Preferred types for “owner” discovery
    private readonly string[] preferTypeNames = { "EnemyController", "BossEnemy", "BoneforgeTitanBoss" };

    // ------------- IBullet -------------
    public void Initialize(GameObject newOwner) => owner = newOwner;

    public void InitializeRicochet(
        GameObject owner,
        float startDamage,
        float startSpeed,
        Vector3 dir,
        int maxBounces,
        float speedLossPerBounce,
        float damageLossPerBounce,
        LayerMask ricochetSurfaces,
        LayerMask enemyLayers,
        LayerMask ignoreLayers,
        bool biasRicochetTowardTargets,
        float ricochetAimCone,
        float ricochetTargetSearchRadius,
        float minSpeedToContinue,
        float maxLifeSeconds,
        GameObject bounceVfxPrefab = null,
        AudioClip bounceSfx = null
    )
    {
        this.owner = owner;
        this.damage = Mathf.Max(0f, startDamage);
        this.velocity = dir.normalized * Mathf.Max(0.5f, startSpeed);
        this.maxBounces = Mathf.Max(0, maxBounces);
        this.speedLossPerBounce = Mathf.Clamp01(speedLossPerBounce);
        this.damageLossPerBounce = Mathf.Clamp01(damageLossPerBounce);
        this.ricochetSurfaces = ricochetSurfaces;
        this.enemyLayers = enemyLayers;
        this.ignoreLayers = ignoreLayers;
        this.biasRicochetTowardTargets = biasRicochetTowardTargets;
        this.ricochetAimCone = Mathf.Max(0f, ricochetAimCone);
        this.ricochetTargetSearchRadius = Mathf.Max(0f, ricochetTargetSearchRadius);
        this.minSpeedToContinue = Mathf.Max(0.1f, minSpeedToContinue);
        this.maxLifeSeconds = Mathf.Max(0.01f, maxLifeSeconds);
        this.bounceVfxPrefab = bounceVfxPrefab;
        this.bounceSfx = bounceSfx;

        audioSource = GetComponent<AudioSource>();
        if (!audioSource && bounceSfx) audioSource = gameObject.AddComponent<AudioSource>();
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        life += dt;
        if (life >= maxLifeSeconds || velocity.sqrMagnitude < (minSpeedToContinue * minSpeedToContinue))
        {
            if (debugRico) Debug.Log($"[Ricochet] Destroy (life/speed cutoff) life={life:F2} v={velocity.magnitude:F2}", this);
            Destroy(gameObject);
            return;
        }

        Vector3 start = transform.position;
        Vector3 step = velocity * dt;
        float dist = step.magnitude;

        // Fail-safe masks: if user forgot to set either mask, treat it as default scene layers (so hits still register)
        int enemyMaskVal = (enemyLayers.value == 0) ? Physics.DefaultRaycastLayers : enemyLayers.value;
        int surfaceMaskVal = (ricochetSurfaces.value == 0) ? Physics.DefaultRaycastLayers : ricochetSurfaces.value;

        int combinedMask = (enemyMaskVal | surfaceMaskVal) & ~ignoreLayers.value;

        if (Physics.Raycast(start, velocity.normalized, out RaycastHit hit, dist + 0.02f, combinedMask, QueryTriggerInteraction.Collide))
        {
            transform.position = hit.point;

            int L = hit.collider.gameObject.layer;
            bool isEnemy = (enemyMaskVal & (1 << L)) != 0;
            bool isSurface = (surfaceMaskVal & (1 << L)) != 0 && !hit.collider.isTrigger;

            if (debugRico)
                Debug.Log($"[Ricochet] Hit '{hit.collider.name}' layer={LayerMask.LayerToName(L)} isTrig={hit.collider.isTrigger} enemy={isEnemy} surface={isSurface}", hit.collider);

            if (isEnemy && !IsOwnerOrAlly(hit.collider.gameObject))
            {
                ApplyDamageGuaranteed(hit.collider);
                Destroy(gameObject);
                return;
            }

            if (isSurface)
            {
                HandleBounce(hit);
                return;
            }

            // Uninteresting hit: keep flying
            transform.position = start + step;
        }
        else
        {
            transform.position += step;
        }

        if (velocity.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(velocity.normalized, Vector3.up);
    }

    private void HandleBounce(RaycastHit hit)
    {
        if (bouncesUsed >= maxBounces)
        {
            if (debugRico) Debug.Log("[Ricochet] Max bounces reached – destroy", this);
            Destroy(gameObject);
            return;
        }

        Vector3 reflected = Vector3.Reflect(velocity.normalized, hit.normal);

        if (biasRicochetTowardTargets && ricochetTargetSearchRadius > 0f && ricochetAimCone > 0f)
        {
            Vector3 biased = TryBiasDirectionTowardTarget(hit.point, reflected);
            reflected = Vector3.Slerp(reflected, biased, 0.85f);
        }

        float newSpeed = velocity.magnitude * (1f - speedLossPerBounce);
        damage *= (1f - damageLossPerBounce);
        velocity = reflected * Mathf.Max(minSpeedToContinue * 0.5f, newSpeed);
        bouncesUsed++;

        transform.position = hit.point + reflected * 0.015f;

        if (bounceVfxPrefab) Instantiate(bounceVfxPrefab, hit.point, Quaternion.LookRotation(hit.normal));
        if (audioSource && bounceSfx) audioSource.PlayOneShot(bounceSfx);

        if (debugRico) Debug.Log($"[Ricochet] Bounce {bouncesUsed}/{maxBounces} speed={velocity.magnitude:F1} damage={damage:F1}", this);
    }

    private Vector3 TryBiasDirectionTowardTarget(Vector3 origin, Vector3 currentDir)
    {
        int enemyMaskVal = (enemyLayers.value == 0) ? Physics.DefaultRaycastLayers : enemyLayers.value;
        Collider[] cols = Physics.OverlapSphere(origin, ricochetTargetSearchRadius, enemyMaskVal, QueryTriggerInteraction.Collide);
        float bestScore = Mathf.Cos(ricochetAimCone * Mathf.Deg2Rad);
        Transform best = null;

        foreach (var c in cols)
        {
            if (!c || IsOwnerOrAlly(c.gameObject)) continue;
            Vector3 to = (c.bounds.center - origin).normalized;
            float d = Vector3.Dot(currentDir, to);
            if (d > bestScore) { bestScore = d; best = c.transform; }
        }

        return best ? (best.position - origin).normalized : currentDir;
    }

    private bool IsOwnerOrAlly(GameObject other)
    {
        if (!owner) return false;
        string ot = other.tag;
        string my = owner.tag;

        if (ot == "Pickup") return true;
        if (my == "Player" && (ot == "Player" || ot == "FriendlyAI")) return true;
        if (my == "FriendlyAI" && (ot == "Player" || ot == "FriendlyAI")) return true;
        if (my == "Enemy" && ot == "Enemy") return true;
        if (other == owner) return true;

        return false;
    }

    // ===================== DAMAGE CORE =====================
    private void ApplyDamageGuaranteed(Collider hitCol)
    {
        // Prefer a root that actually owns HP/logic (EnemyController/BossEnemy/BoneforgeTitanBoss)
        Transform healthOwner = FindHealthOwner(hitCol.transform);
        GameObject targetGO = healthOwner.gameObject;

        bool handled = false;

        // 1) Preferred pipeline: ApplyDamageFrom (any signature we know about)
        handled |= TryApplyDamageFromReflection(healthOwner, damage, owner);

        // 2) If no ApplyDamageFrom receivers found anywhere, fall back to TakeDamage methods
        if (!handled)
            handled |= TryTakeDamageReflection(healthOwner, damage, owner);

        // 3) Still nothing? Fire messages upwards so loose scripts can catch them.
        if (!handled)
        {
            // Tuple payload first (BoneforgeTitanBoss supports tuple/float)
            targetGO.SendMessageUpwards("ApplyDamageFrom", (damage, owner), SendMessageOptions.DontRequireReceiver);
            targetGO.SendMessageUpwards("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);
            // Some projects listen to different names:
            targetGO.SendMessageUpwards("ApplyDamage", damage, SendMessageOptions.DontRequireReceiver);
            targetGO.SendMessageUpwards("Damage", damage, SendMessageOptions.DontRequireReceiver);

            if (debugRico) Debug.Log("[Ricochet] Sent damage via SendMessageUpwards fallbacks.", targetGO);
        }

        if (debugRico) Debug.Log($"[Ricochet] Damage routed (handled={handled}) ? {targetGO.name}", targetGO);
    }

    private Transform FindHealthOwner(Transform start)
    {
        // Pass 1: find a parent that has any of the preferred type names
        Transform scan = start;
        while (scan != null)
        {
            var mbs = scan.GetComponents<MonoBehaviour>();
            foreach (var mb in mbs)
            {
                if (!mb) continue;
                if (IsPreferredType(mb.GetType()))
                    return scan;
            }
            scan = scan.parent;
        }

        // Pass 2: fall back to root
        scan = start; Transform top = start;
        while (scan.parent != null) { top = scan.parent; scan = scan.parent; }
        return top;
    }

    private bool IsPreferredType(Type t)
    {
        for (Type cur = t; cur != null; cur = cur.BaseType)
        {
            string name = cur.Name;
            for (int i = 0; i < preferTypeNames.Length; i++)
                if (name == preferTypeNames[i]) return true;
        }
        return false;
    }

    // -------- reflection helpers --------
    private bool TryApplyDamageFromReflection(Transform root, float amount, GameObject instigator)
    {
        bool any = false;
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        Transform scan = root;
        while (scan != null)
        {
            var mbs = scan.GetComponents<MonoBehaviour>();
            foreach (var mb in mbs)
            {
                if (!mb) continue;
                var type = mb.GetType();

                // ApplyDamageFrom(object)
                var mObj = type.GetMethod("ApplyDamageFrom", flags, null, new Type[] { typeof(object) }, null);
                if (mObj != null)
                {
                    any = true;
                    if (debugRico) Debug.Log($"[Ricochet] ApplyDamageFrom(object) via {type.Name}", mb);
                    mObj.Invoke(mb, new object[] { (amount, instigator) }); // tuple gets unpacked by your boss
                    continue;
                }

                // ApplyDamageFrom(float, GameObject)
                var mFG = type.GetMethod("ApplyDamageFrom", flags, null, new Type[] { typeof(float), typeof(GameObject) }, null);
                if (mFG != null)
                {
                    any = true;
                    if (debugRico) Debug.Log($"[Ricochet] ApplyDamageFrom(float, GameObject) via {type.Name}", mb);
                    mFG.Invoke(mb, new object[] { amount, instigator });
                    continue;
                }

                // ApplyDamageFrom(float)
                var mF = type.GetMethod("ApplyDamageFrom", flags, null, new Type[] { typeof(float) }, null);
                if (mF != null)
                {
                    any = true;
                    if (debugRico) Debug.Log($"[Ricochet] ApplyDamageFrom(float) via {type.Name}", mb);
                    mF.Invoke(mb, new object[] { amount });
                    continue;
                }
            }
            scan = scan.parent;
        }

        return any;
    }

    private bool TryTakeDamageReflection(Transform root, float amount, GameObject instigator)
    {
        bool any = false;
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        Transform scan = root;
        while (scan != null)
        {
            var mbs = scan.GetComponents<MonoBehaviour>();
            foreach (var mb in mbs)
            {
                if (!mb) continue;
                var type = mb.GetType();

                // TakeDamage(float, GameObject)
                var mFG = type.GetMethod("TakeDamage", flags, null, new Type[] { typeof(float), typeof(GameObject) }, null);
                if (mFG != null)
                {
                    any = true;
                    if (debugRico) Debug.Log($"[Ricochet] TakeDamage(float, GameObject) via {type.Name}", mb);
                    mFG.Invoke(mb, new object[] { amount, instigator });
                    continue;
                }

                // TakeDamage(float)
                var mF = type.GetMethod("TakeDamage", flags, null, new Type[] { typeof(float) }, null);
                if (mF != null)
                {
                    any = true;
                    if (debugRico) Debug.Log($"[Ricochet] TakeDamage(float) via {type.Name}", mb);
                    mF.Invoke(mb, new object[] { amount });
                    continue;
                }
            }
            scan = scan.parent;
        }

        return any;
    }
    // =================== /DAMAGE CORE ===================

}
