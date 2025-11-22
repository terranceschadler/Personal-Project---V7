using UnityEngine;
using System.Collections;

/// <summary>
/// Handles dropping upgrade pickups when enemies die.
/// Now includes safety checks to prevent spawning during scene unload.
/// Called directly by EnemyController.Die() at line 689
/// </summary>
public class EnemyUpgradeDropper : MonoBehaviour
{
    [Header("Drop Settings")]
    [SerializeField] private GameObject upgradePrefab;
    [SerializeField] [Range(0f, 1f)] private float dropChance = 0.3f;
    [SerializeField] private bool alwaysDropFromBosses = true;
    [SerializeField] private bool debugLogs = false;

    [Header("Spawn Settings")]
    [SerializeField] private Vector3 spawnOffset = new Vector3(0, 0.5f, 0);
    [SerializeField] private float spawnForce = 2f;
    [SerializeField] private Vector3 spawnTorque = new Vector3(1, 1, 1);

    private bool isSceneUnloading = false;
    private bool hasDroppedUpgrade = false;

    void Awake()
    {
        // Reset flags
        isSceneUnloading = false;
        hasDroppedUpgrade = false;

        // CRITICAL: Verify upgrade prefab is assigned
        if (upgradePrefab == null)
        {
            Debug.LogError($"[EnemyUpgradeDropper] ⚠️ CRITICAL: No upgrade prefab assigned on {gameObject.name}! " +
                          $"This enemy will NOT drop upgrades. Assign a prefab in the Inspector!");
        }
        else
        {
            if (debugLogs)
            {
                Debug.Log($"[EnemyUpgradeDropper] {gameObject.name} initialized with prefab: {upgradePrefab.name}, " +
                         $"dropChance: {dropChance:F2}");
            }
        }
    }

    void OnDestroy()
    {
        // Check if scene is unloading
        if (!gameObject.scene.isLoaded)
        {
            isSceneUnloading = true;
            if (debugLogs) Debug.Log($"[EnemyUpgradeDropper] {gameObject.name} - Scene unloading, skipping drop");
        }
    }

    /// <summary>
    /// Called by EnemyController.Die() when enemy dies (line 689).
    /// PUBLIC so EnemyController can call it.
    /// </summary>
    public void OnEnemyDeath()
    {
        if (debugLogs) Debug.Log($"[EnemyUpgradeDropper] OnEnemyDeath called for {gameObject.name}");

        // CRITICAL SAFETY CHECKS - Don't spawn anything if:
        if (!Application.isPlaying)
        {
            if (debugLogs) Debug.Log("[EnemyUpgradeDropper] Not in play mode, skipping drop");
            return;
        }

        if (!gameObject.scene.isLoaded || isSceneUnloading)
        {
            if (debugLogs) Debug.Log("[EnemyUpgradeDropper] Scene unloading, skipping drop");
            return;
        }

        if (hasDroppedUpgrade)
        {
            if (debugLogs) Debug.Log("[EnemyUpgradeDropper] Already dropped upgrade, skipping");
            return;
        }

        if (this == null || gameObject == null)
        {
            if (debugLogs) Debug.Log("[EnemyUpgradeDropper] Component/GameObject destroyed, skipping drop");
            return;
        }

        // Check if prefab is assigned - ALWAYS LOG THIS
        if (upgradePrefab == null)
        {
            Debug.LogWarning($"[EnemyUpgradeDropper] No upgrade prefab assigned on {gameObject.name}! Check Inspector.");
            return;
        }

        // Determine if should drop
        if (ShouldDrop())
        {
            DropUpgrade();
        }
        else
        {
            if (debugLogs) Debug.Log($"[EnemyUpgradeDropper] {gameObject.name} failed drop chance roll");
        }
    }

    private bool ShouldDrop()
    {
        // Check if this is a boss (based on name or tag)
        bool isBoss = gameObject.name.Contains("Boss") ||
                      gameObject.CompareTag("Boss") ||
                      gameObject.name.Contains("Mini");

        // Always drop from bosses if enabled
        if (alwaysDropFromBosses && isBoss)
        {
            if (debugLogs) Debug.Log($"[EnemyUpgradeDropper] {gameObject.name} is a boss - GUARANTEED drop!");
            return true;
        }

        // Roll for random drop
        float roll = Random.value;
        bool shouldDrop = roll <= dropChance;

        if (debugLogs) Debug.Log($"[EnemyUpgradeDropper] {gameObject.name} drop roll: {roll:F3} <= {dropChance:F2} ? {(shouldDrop ? "YES" : "NO")}");

        return shouldDrop;
    }

    private void DropUpgrade()
    {
        // Mark as dropped to prevent duplicates
        hasDroppedUpgrade = true;

        // Calculate spawn position
        Vector3 spawnPosition = transform.position + spawnOffset;

        // Instantiate the upgrade
        GameObject upgrade = Instantiate(upgradePrefab, spawnPosition, Quaternion.identity);

        if (upgrade != null)
        {
            if (debugLogs) Debug.Log($"[EnemyUpgradeDropper] {gameObject.name} dropped an upgrade!");

            // Add some physics force if there's a rigidbody
            Rigidbody rb = upgrade.GetComponent<Rigidbody>();
            if (rb != null)
            {
                // Add upward force
                Vector3 randomDirection = Random.insideUnitSphere;
                randomDirection.y = Mathf.Abs(randomDirection.y); // Ensure upward
                rb.AddForce(randomDirection * spawnForce, ForceMode.Impulse);

                // Add spin
                rb.AddTorque(spawnTorque, ForceMode.Impulse);
            }
        }
        else
        {
            Debug.LogError($"[EnemyUpgradeDropper] Failed to instantiate upgrade prefab!");
        }
    }

    /// <summary>
    /// Public method to force drop an upgrade (for testing or special cases)
    /// </summary>
    public void ForceDropUpgrade()
    {
        if (!Application.isPlaying || isSceneUnloading || !gameObject.scene.isLoaded)
        {
            Debug.LogWarning("[EnemyUpgradeDropper] Cannot force drop - invalid state");
            return;
        }

        if (upgradePrefab == null)
        {
            Debug.LogWarning("[EnemyUpgradeDropper] Cannot force drop - no prefab assigned");
            return;
        }

        DropUpgrade();
    }

    /// <summary>
    /// Set drop chance at runtime
    /// </summary>
    public void SetDropChance(float chance)
    {
        dropChance = Mathf.Clamp01(chance);
    }

    /// <summary>
    /// Get current drop chance
    /// </summary>
    public float GetDropChance()
    {
        return dropChance;
    }

#if UNITY_EDITOR
    // Visualize drop position in editor
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Vector3 dropPos = transform.position + spawnOffset;
        Gizmos.DrawWireSphere(dropPos, 0.3f);
        Gizmos.DrawLine(transform.position, dropPos);
    }
#endif
}