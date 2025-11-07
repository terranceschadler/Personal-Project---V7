using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Enhanced bullet class supporting all upgrade features:
/// - Piercing, bouncing, homing
/// - Explosive damage
/// - Elemental DoT effects
/// - Lifetime modifications
/// </summary>
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Rigidbody))]
public class EnhancedBullet : MonoBehaviour, IBullet
{
    [Header("Ownership (REQUIRED)")]
    public GameObject owner;
    
    // Stats applied from weapon
    private float speed;
    private float lifetime;
    private float damage;
    private int piercingRemaining;
    private int bouncesRemaining;
    private bool isHoming;
    private float homingStrength;
    private bool isExplosive;
    private float explosionRadius;
    private float explosionDamage;
    
    // Elemental effects
    private bool hasBurn;
    private float burnDPS;
    private float burnDuration;
    
    private bool hasPoison;
    private float poisonDPS;
    private float poisonDuration;
    
    private bool hasFreeze;
    private float freezeSlowPercent;
    private float freezeDuration;
    
    private bool hasShock;
    private float shockChainRange;
    private int shockChainCount;
    private float shockDamage;
    
    // Critical hit
    private bool isCritical;
    
    // Runtime
    private Rigidbody rb;
    private Collider col;
    private Transform currentTarget;
    private List<GameObject> hitObjects = new List<GameObject>();
    private Vector3 lastVelocity;
    
    public void Initialize(GameObject newOwner)
    {
        owner = newOwner;
    }
    
    /// <summary>
    /// Initialize bullet with weapon stats
    /// </summary>
    public void InitializeWithStats(GameObject newOwner, WeaponStats stats)
    {
        owner = newOwner;
        
        // Basic properties
        speed = stats.GetTotalVelocity();
        lifetime = stats.GetTotalLifetime();
        damage = stats.GetTotalDamage();
        
        // Calculate critical hit
        if (Random.value < stats.critChance)
        {
            isCritical = true;
            damage *= stats.critDamageMultiplier;
        }
        
        // Special effects
        piercingRemaining = stats.piercingCount;
        bouncesRemaining = stats.bounceCount;
        isHoming = stats.isHoming;
        homingStrength = stats.homingStrength;
        isExplosive = stats.isExplosive;
        explosionRadius = stats.explosionRadius;
        explosionDamage = stats.explosionDamage;
        
        // Elemental effects
        hasBurn = stats.hasBurnEffect;
        burnDPS = stats.burnDamagePerSecond;
        burnDuration = stats.burnDuration;
        
        hasPoison = stats.hasPoisonEffect;
        poisonDPS = stats.poisonDamagePerSecond;
        poisonDuration = stats.poisonDuration;
        
        hasFreeze = stats.hasFreezeEffect;
        freezeSlowPercent = stats.freezeSlowPercent;
        freezeDuration = stats.freezeDuration;
        
        hasShock = stats.hasShockEffect;
        shockChainRange = stats.shockChainRange;
        shockChainCount = stats.shockChainCount;
        shockDamage = stats.shockDamage;
    }
    
    void Start()
    {
        Destroy(gameObject, lifetime);
        
        // Setup physics
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.isKinematic = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.interpolation = RigidbodyInterpolation.None;
        
        col = GetComponent<Collider>();
        col.isTrigger = true;
        
        // Default owner
        if (owner == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                owner = playerObj;
            }
        }
        
        // Prevent self-collision
        if (owner != null)
        {
            var ownerCols = owner.GetComponentsInChildren<Collider>(true);
            foreach (var ownerCol in ownerCols)
                if (ownerCol) Physics.IgnoreCollision(col, ownerCol, true);
        }
        
        // Find initial target for homing
        if (isHoming)
        {
            FindNearestTarget();
        }
    }
    
    void Update()
    {
        // Homing behavior
        if (isHoming && currentTarget != null)
        {
            Vector3 direction = (currentTarget.position - transform.position).normalized;
            Vector3 currentDirection = transform.forward;
            Vector3 newDirection = Vector3.Lerp(currentDirection, direction, homingStrength * Time.deltaTime);
            transform.forward = newDirection;
        }
        
        // Move forward
        Vector3 movement = transform.forward * speed * Time.deltaTime;
        transform.position += movement;
        lastVelocity = movement / Time.deltaTime;
    }
    
    private void OnTriggerEnter(Collider other)
    {
        // Ignore owner
        if (owner && other.transform.IsChildOf(owner.transform)) return;
        
        // Check if already hit (for piercing)
        if (hitObjects.Contains(other.gameObject)) return;
        
        bool hitSomething = false;
        
        // 1) Bone plates
        var plate = other.GetComponent<BonePlate>();
        if (plate != null && !plate.IsBroken)
        {
            plate.ApplyDamageFrom(new BossEnemy.DamageEnvelope(damage, owner));
            ApplyElementalEffects(other.gameObject);
            hitSomething = true;
        }
        
        // 2) Bosses
        var boss = other.GetComponentInParent<BossEnemy>();
        if (boss != null)
        {
            boss.gameObject.SendMessage("ApplyDamageFrom", (damage, owner), SendMessageOptions.DontRequireReceiver);
            ApplyElementalEffects(boss.gameObject);
            hitSomething = true;
        }
        
        // 3) Regular enemies
        var enemy = other.GetComponentInParent<EnemyController>();
        if (enemy != null)
        {
            enemy.TakeDamage(damage);
            ApplyElementalEffects(enemy.gameObject);
            hitSomething = true;
        }
        
        // 4) Friendlies / Player
        if (other.CompareTag("FriendlyAI") || other.CompareTag("Player"))
        {
            Destroy(gameObject);
            return;
        }
        
        if (hitSomething)
        {
            hitObjects.Add(other.gameObject);
            
            // Explosive damage
            if (isExplosive)
            {
                ApplyExplosiveDamage(other.transform.position);
            }
            
            // Shock chain
            if (hasShock && shockChainCount > 0)
            {
                ApplyShockChain(other.gameObject);
            }
            
            // Piercing
            if (piercingRemaining > 0)
            {
                piercingRemaining--;
                return; // Don't destroy
            }
            
            Destroy(gameObject);
            return;
        }
        
        // 5) World collision - attempt bounce
        if (!other.isTrigger)
        {
            if (bouncesRemaining > 0)
            {
                BounceBullet(other);
                bouncesRemaining--;
                return;
            }
            
            Destroy(gameObject);
        }
    }
    
    private void ApplyElementalEffects(GameObject target)
    {
        if (hasBurn && burnDuration > 0)
        {
            var burnEffect = target.GetComponent<BurnEffect>() ?? target.AddComponent<BurnEffect>();
            burnEffect.ApplyBurn(burnDPS, burnDuration, owner);
        }
        
        if (hasPoison && poisonDuration > 0)
        {
            var poisonEffect = target.GetComponent<PoisonEffect>() ?? target.AddComponent<PoisonEffect>();
            poisonEffect.ApplyPoison(poisonDPS, poisonDuration, owner);
        }
        
        if (hasFreeze && freezeDuration > 0)
        {
            var freezeEffect = target.GetComponent<FreezeEffect>() ?? target.AddComponent<FreezeEffect>();
            freezeEffect.ApplyFreeze(freezeSlowPercent, freezeDuration);
        }
    }
    
    private void ApplyExplosiveDamage(Vector3 center)
    {
        Collider[] colliders = Physics.OverlapSphere(center, explosionRadius);
        
        foreach (var hit in colliders)
        {
            if (hit.gameObject == owner || hitObjects.Contains(hit.gameObject)) continue;
            
            var enemy = hit.GetComponentInParent<EnemyController>();
            if (enemy != null)
            {
                enemy.TakeDamage(explosionDamage);
            }
            
            var boss = hit.GetComponentInParent<BossEnemy>();
            if (boss != null)
            {
                boss.gameObject.SendMessage("ApplyDamageFrom", (explosionDamage, owner), SendMessageOptions.DontRequireReceiver);
            }
        }
        
        // Optional: Spawn explosion VFX here
    }
    
    private void ApplyShockChain(GameObject startTarget)
    {
        List<GameObject> chainedTargets = new List<GameObject> { startTarget };
        GameObject currentSource = startTarget;
        
        for (int i = 0; i < shockChainCount; i++)
        {
            Collider[] nearbyColliders = Physics.OverlapSphere(currentSource.transform.position, shockChainRange);
            GameObject nextTarget = null;
            float closestDistance = float.MaxValue;
            
            foreach (var hit in nearbyColliders)
            {
                if (chainedTargets.Contains(hit.gameObject)) continue;
                if (hit.gameObject == owner) continue;
                
                var enemy = hit.GetComponentInParent<EnemyController>();
                var boss = hit.GetComponentInParent<BossEnemy>();
                
                if (enemy != null || boss != null)
                {
                    float distance = Vector3.Distance(currentSource.transform.position, hit.transform.position);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        nextTarget = hit.gameObject;
                    }
                }
            }
            
            if (nextTarget != null)
            {
                chainedTargets.Add(nextTarget);
                
                var enemy = nextTarget.GetComponentInParent<EnemyController>();
                if (enemy != null)
                {
                    enemy.TakeDamage(shockDamage);
                }
                
                var boss = nextTarget.GetComponentInParent<BossEnemy>();
                if (boss != null)
                {
                    boss.gameObject.SendMessage("ApplyDamageFrom", (shockDamage, owner), SendMessageOptions.DontRequireReceiver);
                }
                
                currentSource = nextTarget;
                
                // Optional: Draw lightning effect between targets
            }
            else
            {
                break; // No more targets in range
            }
        }
    }
    
    private void BounceBullet(Collider surface)
    {
        Vector3 normal = Vector3.up; // Default normal
        
        // Try to get actual surface normal
        RaycastHit hit;
        if (Physics.Raycast(transform.position - lastVelocity.normalized * 0.1f, lastVelocity.normalized, out hit, 0.2f))
        {
            normal = hit.normal;
        }
        
        // Reflect direction
        Vector3 reflectedDirection = Vector3.Reflect(transform.forward, normal);
        transform.forward = reflectedDirection;
        
        // Find new target after bounce if homing
        if (isHoming)
        {
            FindNearestTarget();
        }
    }
    
    private void FindNearestTarget()
    {
        GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");
        float closestDistance = float.MaxValue;
        Transform closestEnemy = null;
        
        foreach (var enemy in enemies)
        {
            float distance = Vector3.Distance(transform.position, enemy.transform.position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestEnemy = enemy.transform;
            }
        }
        
        currentTarget = closestEnemy;
    }
}
