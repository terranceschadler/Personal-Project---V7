using UnityEngine;

/// <summary>
/// Extension for your enemy system to drop upgrade pickups.
/// This can be added to existing enemies or used as a reference to modify your EnemyController.
/// </summary>
public class EnemyUpgradeDropper : MonoBehaviour
{
    [Header("Upgrade Drop Settings")]
    [Tooltip("Reference to the integrated upgrade system")]
    public IntegratedUpgradeSystem upgradeSystem;
    
    [Tooltip("Chance to drop an upgrade on death (0-1)")]
    [Range(0f, 1f)]
    public float dropChance = 0.15f;
    
    [Tooltip("Offset from enemy position where pickup spawns")]
    public Vector3 dropOffset = new Vector3(0f, 0.5f, 0f);
    
    [Tooltip("Force a specific tier? Leave as Common for random")]
    public bool forceSpecificTier = false;
    public UpgradeTier forcedTier = UpgradeTier.Common;
    
    [Header("Boss Settings")]
    [Tooltip("Is this a boss enemy? Bosses have better drop rates")]
    public bool isBoss = false;
    
    [Tooltip("Boss drop chance (0-1)")]
    [Range(0f, 1f)]
    public float bossDropChance = 0.8f;
    
    [Tooltip("Bosses drop higher tier upgrades")]
    public bool bossDropsHigherTier = true;
    
    private void Start()
    {
        // Auto-find upgrade system if not assigned
        if (upgradeSystem == null)
        {
            upgradeSystem = FindObjectOfType<IntegratedUpgradeSystem>();
        }
        
        if (upgradeSystem == null)
        {
            Debug.LogWarning("[EnemyUpgradeDropper] No IntegratedUpgradeSystem found in scene!");
        }
    }
    
    /// <summary>
    /// Call this method when the enemy dies (from your existing death logic)
    /// </summary>
    public void OnEnemyDeath()
    {
        if (upgradeSystem == null) return;
        
        // Determine if we should drop
        float actualDropChance = isBoss ? bossDropChance : dropChance;
        
        if (Random.value < actualDropChance)
        {
            Vector3 dropPosition = transform.position + dropOffset;
            
            if (forceSpecificTier)
            {
                upgradeSystem.SpawnUpgradePickup(dropPosition, forcedTier);
            }
            else if (isBoss && bossDropsHigherTier)
            {
                // Bosses drop Epic or Legendary
                UpgradeTier tier = Random.value < 0.5f ? UpgradeTier.Epic : UpgradeTier.Legendary;
                upgradeSystem.SpawnUpgradePickup(dropPosition, tier);
            }
            else
            {
                // Normal weighted random
                upgradeSystem.SpawnUpgradePickup(dropPosition);
            }
            
            Debug.Log($"[EnemyUpgradeDropper] {gameObject.name} dropped an upgrade!");
        }
    }
    
    /// <summary>
    /// Alternative: Automatically detect death by checking for EnemyController component
    /// </summary>
    private void OnDestroy()
    {
        // If this GameObject is being destroyed and we're in play mode, try to drop
        if (Application.isPlaying && upgradeSystem != null)
        {
            // Check if this was a legitimate death (not scene unload)
            if (Time.time > 0.1f) // Simple check to avoid spawning on scene load
            {
                OnEnemyDeath();
            }
        }
    }
}

/// <summary>
/// Example integration with your existing EnemyController.
/// Add this method to your EnemyController.cs:
/// 
/// void Die()
/// {
///     // Your existing death logic...
///     
///     // NEW: Check for upgrade dropper
///     EnemyUpgradeDropper dropper = GetComponent<EnemyUpgradeDropper>();
///     if (dropper != null)
///     {
///         dropper.OnEnemyDeath();
///     }
///     
///     Destroy(gameObject);
/// }
/// </summary>
public class EnemyControllerIntegrationExample : MonoBehaviour
{
    // This is just documentation - see the comment above
}
