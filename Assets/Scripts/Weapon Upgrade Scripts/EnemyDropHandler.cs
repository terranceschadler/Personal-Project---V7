using UnityEngine;

/// <summary>
/// Component that handles dropping items when an enemy dies.
/// Add this to your enemy prefabs to enable ammo/loot drops.
/// </summary>
public class EnemyDropHandler : MonoBehaviour
{
    [Header("Ammo Drops")]
    [Tooltip("Prefab for ammo pickup")]
    public GameObject ammoPickupPrefab;
    
    [Tooltip("Chance to drop ammo (0-1)")]
    [Range(0f, 1f)]
    public float ammoDropChance = 0.3f;
    
    [Tooltip("Amount of ammo to drop")]
    public int ammoAmount = 10;
    
    [Tooltip("Use percentage of player's max magazine instead")]
    public bool usePercentage = false;
    
    [Tooltip("Percentage to drop (0-1)")]
    [Range(0f, 1f)]
    public float ammoPercentage = 0.5f;
    
    [Header("Drop Physics")]
    [Tooltip("Force to launch the pickup")]
    public float dropForce = 5f;
    
    [Tooltip("Random force variation")]
    public float forceVariation = 2f;
    
    [Tooltip("Upward force component")]
    public float upwardForce = 3f;
    
    [Header("Position Offset")]
    [Tooltip("Spawn offset from enemy position")]
    public Vector3 spawnOffset = Vector3.up;
    
    [Header("Advanced")]
    [Tooltip("Drop multiple pickups")]
    public bool dropMultiple = false;
    
    [Tooltip("Number of pickups to drop if dropMultiple is true")]
    [Range(1, 5)]
    public int dropCount = 1;
    
    private bool hasDropped = false;
    
    /// <summary>
    /// Call this when the enemy dies to spawn drops
    /// </summary>
    public void DropLoot()
    {
        if (hasDropped) return;
        hasDropped = true;
        
        // Check if should drop ammo
        if (ammoPickupPrefab != null && Random.value <= ammoDropChance)
        {
            int count = dropMultiple ? dropCount : 1;
            
            for (int i = 0; i < count; i++)
            {
                SpawnAmmoPickup(i, count);
            }
        }
    }
    
    private void SpawnAmmoPickup(int index, int totalCount)
    {
        Vector3 spawnPos = transform.position + spawnOffset;
        
        // Add slight random offset for multiple drops
        if (totalCount > 1)
        {
            Vector2 randomCircle = Random.insideUnitCircle * 0.5f;
            spawnPos += new Vector3(randomCircle.x, 0, randomCircle.y);
        }
        
        GameObject pickup = Instantiate(ammoPickupPrefab, spawnPos, Quaternion.identity);
        
        // Configure the pickup
        AmmoPickup ammoPickup = pickup.GetComponent<AmmoPickup>();
        if (ammoPickup != null)
        {
            ammoPickup.ammoAmount = ammoAmount;
            ammoPickup.usePercentage = usePercentage;
            ammoPickup.ammoPercentage = ammoPercentage;
        }
        
        // Apply physics force
        Rigidbody rb = pickup.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = pickup.AddComponent<Rigidbody>();
            rb.useGravity = true;
            rb.linearDamping = 2f;
        }
        
        // Calculate random direction
        Vector3 randomDir = Random.insideUnitSphere;
        randomDir.y = Mathf.Abs(randomDir.y); // Ensure upward component
        randomDir = randomDir.normalized;
        
        // Apply force
        float force = dropForce + Random.Range(-forceVariation, forceVariation);
        Vector3 forceVector = randomDir * force;
        forceVector.y += upwardForce;
        
        rb.AddForce(forceVector, ForceMode.Impulse);
        
        // Add random torque for spinning
        rb.AddTorque(Random.insideUnitSphere * 5f, ForceMode.Impulse);
        
        // Disable rigidbody after a moment (pickup will handle movement)
        StartCoroutine(DisablePhysicsAfterDelay(rb, 1f));
    }
    
    private System.Collections.IEnumerator DisablePhysicsAfterDelay(Rigidbody rb, float delay)
    {
        yield return new WaitForSeconds(delay);
        
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }
    
    // Auto-detect enemy death if using EnemyController
    private void Start()
    {
        EnemyController enemy = GetComponent<EnemyController>();
        if (enemy != null)
        {
            // You might need to add an OnDeath event to your EnemyController
            // For now, we'll check for death in Update
        }
    }
}
