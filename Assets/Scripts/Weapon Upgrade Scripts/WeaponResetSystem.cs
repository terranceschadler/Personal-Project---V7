using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages weapon state reset when levels restart.
/// Add this component to your player GameObject alongside PlayerWeaponController.
/// </summary>
public class WeaponResetSystem : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Auto-finds if not assigned")]
    public PlayerWeaponController weaponController;
    public PlayerUpgradeManager upgradeManager;
    
    [Header("Reset Settings")]
    [Tooltip("Reset to weapon's default state from WeaponData")]
    public bool resetToDefaults = true;
    
    [Tooltip("Clear all applied upgrades")]
    public bool clearUpgrades = true;
    
    [Tooltip("Reset reserve ammo to starting amount")]
    public bool resetReserveAmmo = true;
    
    [Tooltip("Reset player upgrades as well")]
    public bool resetPlayerUpgrades = true;
    
    [Header("Preserved Settings (Optional)")]
    [Tooltip("If set, these values override the weapon's defaults on reset")]
    public int? overrideStartingReserveAmmo = null;
    public int? overrideMaxReserveAmmo = null;
    
    // Store initial state
    private InitialWeaponState initialState;
    private InitialPlayerState initialPlayerState;
    
    private void Awake()
    {
        // Auto-find components
        if (weaponController == null)
            weaponController = GetComponent<PlayerWeaponController>();
            
        if (upgradeManager == null)
            upgradeManager = GetComponent<PlayerUpgradeManager>();
    }
    
    private void Start()
    {
        // Capture initial state after everything is initialized
        CaptureInitialState();
        
        // Auto-integrate with GameManager if it exists
        if (GameManager.Instance != null)
        {
            Debug.Log("[WeaponResetSystem] Integrated with GameManager");
        }
    }
    
    /// <summary>
    /// Called by GameManager when level restarts
    /// </summary>
    public void OnLevelRestart()
    {
        ResetWeapon();
    }
    
    /// <summary>
    /// Captures the initial weapon state for restoration
    /// </summary>
    private void CaptureInitialState()
    {
        if (weaponController == null)
        {
            Debug.LogWarning("[WeaponResetSystem] No WeaponController found!");
            return;
        }
        
        initialState = new InitialWeaponState
        {
            initialReserveAmmo = weaponController.GetReserveAmmo(),
            initialMaxReserveAmmo = weaponController.GetMaxReserveAmmo(),
            weaponData = weaponController.weaponData
        };
        
        if (upgradeManager != null)
        {
            initialPlayerState = new InitialPlayerState
            {
                damageMultiplier = upgradeManager.currentDamageMultiplier,
                defense = upgradeManager.currentDefense,
                speedMultiplier = upgradeManager.currentSpeedMultiplier,
                maxHealth = upgradeManager.currentMaxHealth,
                criticalChance = upgradeManager.currentCriticalChance,
                criticalDamage = upgradeManager.currentCriticalDamage,
                cooldownReduction = upgradeManager.currentCooldownReduction,
                areaOfEffect = upgradeManager.currentAreaOfEffect,
                projectileCount = upgradeManager.currentProjectileCount,
                lifesteal = upgradeManager.currentLifesteal
            };
        }
        
        Debug.Log("[WeaponResetSystem] Initial state captured successfully");
    }
    
    /// <summary>
    /// Call this method when the level restarts
    /// </summary>
    public void ResetWeapon()
    {
        if (weaponController == null)
        {
            Debug.LogError("[WeaponResetSystem] Cannot reset - no WeaponController!");
            return;
        }
        
        Debug.Log("[WeaponResetSystem] Resetting weapon system...");
        
        // Stop any ongoing reload
        StopAllCoroutines();
        
        if (resetToDefaults && initialState.weaponData != null)
        {
            // Recreate stats from weapon data
            WeaponStats freshStats = initialState.weaponData.CreateRuntimeStats();
            SetWeaponStats(weaponController, freshStats);
            
            // Reset ammo to full magazine
            int magSize = freshStats.GetTotalMagazineSize();
            SetCurrentAmmo(weaponController, magSize);
            
            // Reset reserve ammo
            if (resetReserveAmmo)
            {
                int reserveAmount = overrideStartingReserveAmmo ?? initialState.initialReserveAmmo;
                int maxReserve = overrideMaxReserveAmmo ?? initialState.initialMaxReserveAmmo;
                SetReserveAmmo(weaponController, reserveAmount, maxReserve);
            }
            
            Debug.Log($"[WeaponResetSystem] Reset complete: Clip={magSize}, Reserve={weaponController.GetReserveAmmo()}");
        }
        
        // Clear upgrades
        if (clearUpgrades)
        {
            ClearAppliedUpgrades(weaponController);
            Debug.Log("[WeaponResetSystem] Cleared all weapon upgrades");
        }
        
        // Reset player upgrades
        if (resetPlayerUpgrades && upgradeManager != null && initialPlayerState != null)
        {
            ResetPlayerUpgrades();
            Debug.Log("[WeaponResetSystem] Reset player upgrades");
        }
        
        // Reset reload state
        SetReloadingState(weaponController, false);
        
        // Notify UI/systems of the reset
        weaponController.OnAmmoChanged?.Invoke(
            weaponController.GetCurrentAmmo(), 
            weaponController.GetReserveAmmo()
        );
        
        Debug.Log("[WeaponResetSystem] ✓ Weapon reset complete!");
    }
    
    /// <summary>
    /// Resets player upgrades to initial state
    /// </summary>
    private void ResetPlayerUpgrades()
    {
        if (upgradeManager == null || initialPlayerState == null) return;
        
        upgradeManager.currentDamageMultiplier = initialPlayerState.damageMultiplier;
        upgradeManager.currentDefense = initialPlayerState.defense;
        upgradeManager.currentSpeedMultiplier = initialPlayerState.speedMultiplier;
        upgradeManager.currentMaxHealth = initialPlayerState.maxHealth;
        upgradeManager.currentCriticalChance = initialPlayerState.criticalChance;
        upgradeManager.currentCriticalDamage = initialPlayerState.criticalDamage;
        upgradeManager.currentCooldownReduction = initialPlayerState.cooldownReduction;
        upgradeManager.currentAreaOfEffect = initialPlayerState.areaOfEffect;
        upgradeManager.currentProjectileCount = initialPlayerState.projectileCount;
        upgradeManager.currentLifesteal = initialPlayerState.lifesteal;
        
        upgradeManager.appliedUpgrades.Clear();
    }
    
    // ============ Reflection-based setters (since fields are private) ============
    
    private void SetWeaponStats(PlayerWeaponController controller, WeaponStats stats)
    {
        var field = typeof(PlayerWeaponController).GetField("currentStats", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(controller, stats);
    }
    
    private void SetCurrentAmmo(PlayerWeaponController controller, int amount)
    {
        var field = typeof(PlayerWeaponController).GetField("currentAmmo", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(controller, amount);
    }
    
    private void SetReserveAmmo(PlayerWeaponController controller, int amount, int max)
    {
        var reserveField = typeof(PlayerWeaponController).GetField("reserveAmmo", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var maxReserveField = typeof(PlayerWeaponController).GetField("maxReserveAmmo", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        reserveField?.SetValue(controller, amount);
        maxReserveField?.SetValue(controller, max);
    }
    
    private void SetReloadingState(PlayerWeaponController controller, bool state)
    {
        var field = typeof(PlayerWeaponController).GetField("isReloading", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(controller, state);
    }
    
    private void ClearAppliedUpgrades(PlayerWeaponController controller)
    {
        var field = typeof(PlayerWeaponController).GetField("appliedUpgrades", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (field != null)
        {
            var list = field.GetValue(controller) as List<UpgradePickup>;
            list?.Clear();
        }
    }
    
    // ============ Helper Classes ============
    
    [System.Serializable]
    private class InitialWeaponState
    {
        public int initialReserveAmmo;
        public int initialMaxReserveAmmo;
        public WeaponData weaponData;
    }
    
    [System.Serializable]
    private class InitialPlayerState
    {
        public float damageMultiplier;
        public float defense;
        public float speedMultiplier;
        public float maxHealth;
        public float criticalChance;
        public float criticalDamage;
        public float cooldownReduction;
        public float areaOfEffect;
        public float projectileCount;
        public float lifesteal;
    }
}
