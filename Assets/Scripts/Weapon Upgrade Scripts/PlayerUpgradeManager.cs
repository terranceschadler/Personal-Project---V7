using UnityEngine;
using System.Collections.Generic;

public class PlayerUpgradeManager : MonoBehaviour
{
    [Header("Player Stats")]
    public float baseDamage = 10f;
    public float baseDefense = 0f;
    public float baseSpeed = 5f;
    public float baseHealth = 100f;
    
    [Header("Current Stats")]
    public float currentDamageMultiplier = 1f;
    public float currentDefense = 0f;
    public float currentSpeedMultiplier = 1f;
    public float currentMaxHealth = 100f;
    public float currentCriticalChance = 0f;
    public float currentCriticalDamage = 1.5f;
    public float currentCooldownReduction = 0f;
    public float currentAreaOfEffect = 1f;
    public float currentProjectileCount = 1f;
    public float currentLifesteal = 0f;
    
    [Header("Applied Upgrades")]
    public List<UpgradeData> appliedUpgrades = new List<UpgradeData>();
    
    [Header("Events")]
    public UnityEngine.Events.UnityEvent<UpgradeData> onUpgradeApplied;
    
    private void Awake()
    {
        // Initialize stats
        currentMaxHealth = baseHealth;
    }
    
    public void ApplyUpgrade(UpgradeData upgrade)
    {
        if (upgrade == null) return;
        
        // Apply stat modifications
        currentDamageMultiplier *= upgrade.damageMultiplier;
        currentDefense += upgrade.defenseBonus;
        currentSpeedMultiplier *= upgrade.speedMultiplier;
        currentMaxHealth += upgrade.healthBonus;
        currentCriticalChance += upgrade.criticalChance;
        currentCriticalDamage += upgrade.criticalDamage;
        currentCooldownReduction += upgrade.cooldownReduction;
        currentAreaOfEffect += upgrade.areaOfEffect;
        currentProjectileCount += upgrade.projectileCount;
        currentLifesteal += upgrade.lifesteal;
        
        // Clamp values to reasonable ranges
        currentCriticalChance = Mathf.Clamp01(currentCriticalChance);
        currentCooldownReduction = Mathf.Clamp(currentCooldownReduction, 0f, 0.9f);
        currentLifesteal = Mathf.Clamp01(currentLifesteal);
        
        // Store the upgrade
        appliedUpgrades.Add(upgrade);
        
        // Trigger event
        onUpgradeApplied?.Invoke(upgrade);
        
        // Apply to player components
        UpdatePlayerComponents();
        
        Debug.Log($"Applied upgrade: {upgrade.upgradeName} ({upgrade.archetype} - {upgrade.tier})");
        PrintCurrentStats();
    }
    
    private void UpdatePlayerComponents()
    {
        // Update movement speed
        var characterController = GetComponent<CharacterController>();
        if (characterController != null)
        {
            // Movement scripts should reference currentSpeedMultiplier
        }
        
        var rigidbody = GetComponent<Rigidbody>();
        if (rigidbody != null)
        {
            // Movement scripts should reference currentSpeedMultiplier
        }
        
        // Update health
        var healthComponent = GetComponent<PlayerHealth>();
        if (healthComponent != null)
        {
            healthComponent.maxHealth = currentMaxHealth;
        }
        
        // You can add more component updates here based on your game's architecture
    }
    
    public float CalculateFinalDamage(float baseDamageValue)
    {
        float damage = baseDamageValue * currentDamageMultiplier;
        
        // Apply critical hit
        if (Random.value < currentCriticalChance)
        {
            damage *= currentCriticalDamage;
        }
        
        return damage;
    }
    
    public float GetFinalSpeed()
    {
        return baseSpeed * currentSpeedMultiplier;
    }
    
    public float GetFinalDefense()
    {
        return baseDefense + currentDefense;
    }
    
    public int GetProjectileCount()
    {
        return Mathf.RoundToInt(currentProjectileCount);
    }
    
    public float GetAreaOfEffectMultiplier()
    {
        return 1f + currentAreaOfEffect;
    }
    
    public float GetCooldownMultiplier()
    {
        return 1f - currentCooldownReduction;
    }
    
    public void HealFromLifesteal(float damageDealt)
    {
        if (currentLifesteal > 0f)
        {
            float healAmount = damageDealt * currentLifesteal;
            var healthComponent = GetComponent<PlayerHealth>();
            if (healthComponent != null)
            {
                healthComponent.Heal(healAmount);
            }
        }
    }
    
    private void PrintCurrentStats()
    {
        Debug.Log("=== Current Player Stats ===");
        Debug.Log($"Damage Multiplier: {currentDamageMultiplier:F2}x");
        Debug.Log($"Defense: {currentDefense:F0}");
        Debug.Log($"Speed Multiplier: {currentSpeedMultiplier:F2}x");
        Debug.Log($"Max Health: {currentMaxHealth:F0}");
        Debug.Log($"Critical Chance: {(currentCriticalChance * 100f):F1}%");
        Debug.Log($"Critical Damage: {(currentCriticalDamage * 100f):F0}%");
        Debug.Log($"Cooldown Reduction: {(currentCooldownReduction * 100f):F0}%");
        Debug.Log($"Area of Effect: +{(currentAreaOfEffect * 100f):F0}%");
        Debug.Log($"Projectile Count: {currentProjectileCount:F0}");
        Debug.Log($"Lifesteal: {(currentLifesteal * 100f):F1}%");
        Debug.Log($"Total Upgrades: {appliedUpgrades.Count}");
    }
    
    public List<UpgradeData> GetUpgradesByArchetype(UpgradeArchetype archetype)
    {
        return appliedUpgrades.FindAll(u => u.archetype == archetype);
    }
    
    public List<UpgradeData> GetUpgradesByTier(UpgradeTier tier)
    {
        return appliedUpgrades.FindAll(u => u.tier == tier);
    }
}

// Simple health component example
public class PlayerHealth : MonoBehaviour
{
    public float maxHealth = 100f;
    public float currentHealth = 100f;
    
    private void Start()
    {
        currentHealth = maxHealth;
    }
    
    public void Heal(float amount)
    {
        currentHealth = Mathf.Min(currentHealth + amount, maxHealth);
    }
    
    public void TakeDamage(float damage)
    {
        // Apply defense reduction
        PlayerUpgradeManager upgradeManager = GetComponent<PlayerUpgradeManager>();
        if (upgradeManager != null)
        {
            float defense = upgradeManager.GetFinalDefense();
            damage = Mathf.Max(0, damage - defense);
        }
        
        currentHealth -= damage;
        
        if (currentHealth <= 0)
        {
            Die();
        }
    }
    
    private void Die()
    {
        Debug.Log("Player died!");
        // Implement death logic
    }
}
