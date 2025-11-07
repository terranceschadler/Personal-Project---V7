using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Integrated upgrade system that combines archetype-based upgrades with your existing weapon system.
/// This bridges the gap between the new upgrade framework and PlayerWeaponController.
/// FIXED VERSION: Handles UpgradeRarity enum properly
/// </summary>
public class IntegratedUpgradeSystem : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerWeaponController weaponController;
    [SerializeField] private UpgradeGenerator upgradeGenerator;
    [SerializeField] private UpgradeSelectionUI selectionUI;
    
    [Header("Upgrade Pickup Settings")]
    [SerializeField] private GameObject upgradePickupPrefab;
    [SerializeField] private float pickupLifetime = 30f;
    
    [Header("Drop Chances")]
    [Range(0f, 1f)] public float commonChance = 0.6f;
    [Range(0f, 1f)] public float rareChance = 0.25f;
    [Range(0f, 1f)] public float epicChance = 0.12f;
    [Range(0f, 1f)] public float legendaryChance = 0.03f;
    
    private List<GameObject> activePickups = new List<GameObject>();
    
    private void Start()
    {
        // Auto-find references if not assigned
        if (weaponController == null)
            weaponController = FindObjectOfType<PlayerWeaponController>();
        
        if (upgradeGenerator == null)
            upgradeGenerator = FindObjectOfType<UpgradeGenerator>();
        
        if (selectionUI == null)
            selectionUI = FindObjectOfType<UpgradeSelectionUI>();
        
        if (upgradeGenerator == null)
        {
            Debug.LogError("[IntegratedUpgradeSystem] No UpgradeGenerator found! Please add one to the scene.");
        }
    }
    
    /// <summary>
    /// Spawns an upgrade pickup at the specified position (e.g., when enemy dies)
    /// </summary>
    public GameObject SpawnUpgradePickup(Vector3 position)
    {
        UpgradeTier tier = GetRandomTier();
        return SpawnUpgradePickup(position, tier);
    }
    
    /// <summary>
    /// Spawns an upgrade pickup of a specific tier
    /// </summary>
    public GameObject SpawnUpgradePickup(Vector3 position, UpgradeTier tier)
    {
        if (upgradePickupPrefab == null)
        {
            Debug.LogError("[IntegratedUpgradeSystem] No pickup prefab assigned!");
            return null;
        }
        
        // Instantiate pickup
        GameObject pickup = Instantiate(upgradePickupPrefab, position, Quaternion.identity);
        
        // Configure the pickup
        IntegratedUpgradePickup pickupScript = pickup.GetComponent<IntegratedUpgradePickup>();
        if (pickupScript == null)
            pickupScript = pickup.AddComponent<IntegratedUpgradePickup>();
        
        pickupScript.Initialize(this, tier);
        
        // Add to active pickups list
        activePickups.Add(pickup);
        
        // Auto-destroy after lifetime
        Destroy(pickup, pickupLifetime);
        
        return pickup;
    }
    
    /// <summary>
    /// Called when player collects an upgrade pickup
    /// </summary>
    public void OnPickupCollected(IntegratedUpgradePickup pickup)
    {
        if (weaponController == null)
        {
            Debug.LogError("[IntegratedUpgradeSystem] No WeaponController found!");
            return;
        }
        
        // Generate 3 random upgrades of the pickup's tier
        List<UpgradeData> upgrades = new List<UpgradeData>();
        
        for (int i = 0; i < 3; i++)
        {
            UpgradeArchetype randomArchetype = GetRandomArchetype();
            UpgradeData upgrade = upgradeGenerator.GenerateUpgrade(randomArchetype, pickup.tier);
            upgrades.Add(upgrade);
        }
        
        // Show selection UI
        if (selectionUI != null)
        {
            selectionUI.ShowUpgradeSelection(upgrades, weaponController.gameObject);
        }
        else
        {
            // Fallback: Auto-apply first upgrade if no UI
            ApplyUpgradeToWeapon(upgrades[0]);
        }
        
        // Remove from active list
        activePickups.Remove(pickup.gameObject);
    }
    
    /// <summary>
    /// Applies an upgrade to the weapon system
    /// This converts UpgradeData to UpgradePickup format for compatibility
    /// </summary>
    public void ApplyUpgradeToWeapon(UpgradeData upgradeData)
    {
        if (weaponController == null)
        {
            Debug.LogError("[IntegratedUpgradeSystem] No WeaponController found!");
            return;
        }
        
        // Convert UpgradeData to UpgradePickup for weapon system
        UpgradePickup pickup = CreateUpgradePickupFromData(upgradeData);
        
        // Apply to weapon controller
        weaponController.ApplyUpgrade(pickup);
        
        Debug.Log($"[IntegratedUpgradeSystem] Applied {upgradeData.upgradeName} to weapon!");
    }
    
    /// <summary>
    /// Converts UpgradeData (archetype-based) to UpgradePickup (weapon-based)
    /// FIXED: No longer sets rarity field to avoid compilation errors
    /// </summary>
    private UpgradePickup CreateUpgradePickupFromData(UpgradeData data)
    {
        UpgradePickup pickup = ScriptableObject.CreateInstance<UpgradePickup>();
        
        pickup.pickupName = data.upgradeName;
        pickup.pickupColor = data.tierColor;
        pickup.effects = new List<UpgradeEffect>();
        
        // Convert archetype stats to weapon upgrade effects
        ConvertArchetypeToEffects(data, pickup.effects);
        
        return pickup;
    }
    
    /// <summary>
    /// Converts archetype-based stats to weapon-specific upgrade effects
    /// </summary>
    private void ConvertArchetypeToEffects(UpgradeData data, List<UpgradeEffect> effects)
    {
        // Damage upgrades
        if (data.damageMultiplier > 1f)
        {
            UpgradeEffect effect = new UpgradeEffect();
            effect.upgradeType = UpgradeType.DamagePercent;
            effect.value = (data.damageMultiplier - 1f) * 100f;
            effect.displayName = $"+{effect.value:F0}% Damage";
            effects.Add(effect);
        }
        
        // Critical hit
        if (data.criticalChance > 0f)
        {
            UpgradeEffect effect = new UpgradeEffect();
            effect.upgradeType = UpgradeType.CritChance;
            effect.value = data.criticalChance * 100f;
            effect.displayName = $"+{effect.value:F1}% Crit Chance";
            effects.Add(effect);
        }
        
        if (data.criticalDamage > 0f)
        {
            UpgradeEffect effect = new UpgradeEffect();
            effect.upgradeType = UpgradeType.CritDamageMultiplier;
            effect.value = data.criticalDamage * 100f;
            effect.displayName = $"+{effect.value:F0}% Crit Damage";
            effects.Add(effect);
        }
        
        // Fire rate (from cooldown reduction)
        if (data.cooldownReduction > 0f)
        {
            UpgradeEffect effect = new UpgradeEffect();
            effect.upgradeType = UpgradeType.FireRatePercent;
            effect.value = data.cooldownReduction * 100f;
            effect.displayName = $"+{effect.value:F0}% Fire Rate";
            effects.Add(effect);
        }
        
        // Projectiles
        if (data.projectileCount > 0f)
        {
            UpgradeEffect effect = new UpgradeEffect();
            effect.upgradeType = UpgradeType.BulletsPerShot;
            effect.value = data.projectileCount;
            effect.displayName = $"+{effect.value:F0} Projectiles";
            effects.Add(effect);
        }
        
        // Defense (converted to reload speed)
        if (data.defenseBonus > 0f)
        {
            UpgradeEffect effect = new UpgradeEffect();
            effect.upgradeType = UpgradeType.ReloadSpeedPercent;
            effect.value = 10f; // 10% faster reload per defense tier
            effect.displayName = "+10% Reload Speed";
            effects.Add(effect);
        }
        
        // Speed (converted to velocity)
        if (data.speedMultiplier > 1f)
        {
            UpgradeEffect effect = new UpgradeEffect();
            effect.upgradeType = UpgradeType.BulletVelocityPercent;
            effect.value = (data.speedMultiplier - 1f) * 100f;
            effect.displayName = $"+{effect.value:F0}% Bullet Speed";
            effects.Add(effect);
        }
        
        // Area of effect (converted to explosion)
        if (data.areaOfEffect > 0.2f && !effects.Exists(e => e.upgradeType == UpgradeType.ExplosiveUnlock))
        {
            UpgradeEffect unlockEffect = new UpgradeEffect();
            unlockEffect.upgradeType = UpgradeType.ExplosiveUnlock;
            unlockEffect.value = 1f;
            unlockEffect.displayName = "Unlock: Explosive Rounds";
            effects.Add(unlockEffect);
            
            UpgradeEffect radiusEffect = new UpgradeEffect();
            radiusEffect.upgradeType = UpgradeType.ExplosionRadius;
            radiusEffect.value = 2f + data.areaOfEffect * 3f;
            radiusEffect.displayName = $"Explosion Radius: {radiusEffect.value:F1}m";
            effects.Add(radiusEffect);
            
            UpgradeEffect damageEffect = new UpgradeEffect();
            damageEffect.upgradeType = UpgradeType.ExplosionDamage;
            damageEffect.value = 5f + data.areaOfEffect * 10f;
            damageEffect.displayName = $"Explosion Damage: {damageEffect.value:F0}";
            effects.Add(damageEffect);
        }
        
        // Lifesteal (converted to magazine size)
        if (data.lifesteal > 0f)
        {
            UpgradeEffect effect = new UpgradeEffect();
            effect.upgradeType = UpgradeType.MagazineSize;
            effect.value = Mathf.RoundToInt(data.lifesteal * 50f); // Convert lifesteal to magazine bonus
            effect.displayName = $"+{effect.value:F0} Magazine Size";
            effects.Add(effect);
        }
        
        // Special effects based on archetype
        ApplyArchetypeSpecificEffects(data, effects);
    }
    
    /// <summary>
    /// Applies archetype-specific special effects
    /// </summary>
    private void ApplyArchetypeSpecificEffects(UpgradeData data, List<UpgradeEffect> effects)
    {
        // Each archetype gets unique bonuses
        switch (data.archetype)
        {
            case UpgradeArchetype.Damage:
                // High damage = piercing
                if (data.tier >= UpgradeTier.Rare)
                {
                    UpgradeEffect effect = new UpgradeEffect();
                    effect.upgradeType = UpgradeType.PiercingCount;
                    effect.value = (int)data.tier; // 0-3 piercing based on tier
                    effect.displayName = $"+{effect.value} Piercing";
                    effects.Add(effect);
                }
                break;
                
            case UpgradeArchetype.Speed:
                // Speed = homing
                if (data.tier >= UpgradeTier.Epic)
                {
                    UpgradeEffect unlockEffect = new UpgradeEffect();
                    unlockEffect.upgradeType = UpgradeType.HomingUnlock;
                    unlockEffect.value = 1f;
                    unlockEffect.displayName = "Unlock: Homing";
                    effects.Add(unlockEffect);
                    
                    UpgradeEffect strengthEffect = new UpgradeEffect();
                    strengthEffect.upgradeType = UpgradeType.HomingStrength;
                    strengthEffect.value = 2f + (int)data.tier;
                    strengthEffect.displayName = $"Homing Strength: {strengthEffect.value:F1}";
                    effects.Add(strengthEffect);
                }
                break;
                
            case UpgradeArchetype.Elemental:
                // Elemental = burn effect
                if (data.tier >= UpgradeTier.Rare)
                {
                    UpgradeEffect unlockEffect = new UpgradeEffect();
                    unlockEffect.upgradeType = UpgradeType.BurnUnlock;
                    unlockEffect.value = 1f;
                    unlockEffect.displayName = "Unlock: Burn Effect";
                    effects.Add(unlockEffect);
                    
                    UpgradeEffect dpsEffect = new UpgradeEffect();
                    dpsEffect.upgradeType = UpgradeType.BurnDamagePerSecond;
                    dpsEffect.value = 2f + (int)data.tier * 2f;
                    dpsEffect.displayName = $"Burn: {dpsEffect.value:F0} DPS";
                    effects.Add(dpsEffect);
                    
                    UpgradeEffect durationEffect = new UpgradeEffect();
                    durationEffect.upgradeType = UpgradeType.BurnDuration;
                    durationEffect.value = 3f;
                    durationEffect.displayName = "Duration: 3s";
                    effects.Add(durationEffect);
                }
                break;
                
            case UpgradeArchetype.Utility:
                // Utility = bouncing
                if (data.tier >= UpgradeTier.Rare)
                {
                    UpgradeEffect effect = new UpgradeEffect();
                    effect.upgradeType = UpgradeType.BounceCount;
                    effect.value = 1 + ((int)data.tier - 1);
                    effect.displayName = $"+{effect.value} Bounces";
                    effects.Add(effect);
                }
                break;
                
            case UpgradeArchetype.Summon:
                // Summon = more projectiles
                if (data.tier >= UpgradeTier.Rare)
                {
                    UpgradeEffect effect = new UpgradeEffect();
                    effect.upgradeType = UpgradeType.BulletsPerShot;
                    effect.value = 1 + (int)data.tier / 2;
                    effect.displayName = $"+{effect.value} Bullets Per Shot";
                    effects.Add(effect);
                }
                break;
                
            case UpgradeArchetype.Defense:
                // Defense = shock chains
                if (data.tier >= UpgradeTier.Epic)
                {
                    UpgradeEffect unlockEffect = new UpgradeEffect();
                    unlockEffect.upgradeType = UpgradeType.ShockUnlock;
                    unlockEffect.value = 1f;
                    unlockEffect.displayName = "Unlock: Shock Chain";
                    effects.Add(unlockEffect);
                    
                    UpgradeEffect countEffect = new UpgradeEffect();
                    countEffect.upgradeType = UpgradeType.ShockChainCount;
                    countEffect.value = 2 + (int)data.tier;
                    countEffect.displayName = $"Chains: {countEffect.value:F0}x";
                    effects.Add(countEffect);
                    
                    UpgradeEffect damageEffect = new UpgradeEffect();
                    damageEffect.upgradeType = UpgradeType.ShockDamage;
                    damageEffect.value = 5f + (int)data.tier * 3f;
                    damageEffect.displayName = $"Chain Damage: {damageEffect.value:F0}";
                    effects.Add(damageEffect);
                }
                break;
        }
    }
    
    private UpgradeTier GetRandomTier()
    {
        float roll = Random.value;
        float cumulative = 0f;
        
        cumulative += commonChance;
        if (roll < cumulative) return UpgradeTier.Common;
        
        cumulative += rareChance;
        if (roll < cumulative) return UpgradeTier.Rare;
        
        cumulative += epicChance;
        if (roll < cumulative) return UpgradeTier.Epic;
        
        return UpgradeTier.Legendary;
    }
    
    private UpgradeArchetype GetRandomArchetype()
    {
        return (UpgradeArchetype)Random.Range(0, 6);
    }
    
    /// <summary>
    /// Clears all active pickups
    /// </summary>
    public void ClearAllPickups()
    {
        foreach (GameObject pickup in activePickups)
        {
            if (pickup != null)
                Destroy(pickup);
        }
        activePickups.Clear();
    }
}
