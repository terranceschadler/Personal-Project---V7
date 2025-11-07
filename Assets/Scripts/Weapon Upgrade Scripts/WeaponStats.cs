using UnityEngine;

/// <summary>
/// Central class that holds all weapon statistics and modifiers.
/// This is the core data structure for the weapon system.
/// </summary>
[System.Serializable]
public class WeaponStats
{
    [Header("Base Damage")]
    public float baseDamage = 10f;
    public float damageMultiplier = 1f;
    public float damageBonus = 0f;
    
    [Header("Fire Rate")]
    public float baseFireRate = 1f; // Shots per second
    public float fireRateMultiplier = 1f;
    public float fireRateBonus = 0f;
    
    [Header("Magazine")]
    public int baseMagazineSize = 30;
    public int magazineSizeBonus = 0;
    
    [Header("Reload")]
    public float baseReloadSpeed = 2f; // Seconds
    public float reloadSpeedMultiplier = 1f;
    public float reloadSpeedBonus = 0f;
    
    [Header("Projectile Properties")]
    public int bulletsPerShot = 1;
    public float bulletSpread = 0f; // Degrees
    public float bulletVelocity = 20f;
    public float velocityMultiplier = 1f;
    public float velocityBonus = 0f;
    public float projectileLifetime = 5f;
    public float lifetimeMultiplier = 1f;
    public float lifetimeBonus = 0f;
    
    [Header("Special Effects")]
    public float critChance = 0f; // 0-1 range
    public float critDamageMultiplier = 2f;
    public int piercingCount = 0;
    public int bounceCount = 0;
    public bool isExplosive = false;
    public float explosionRadius = 0f;
    public float explosionDamage = 0f;
    public bool isHoming = false;
    public float homingStrength = 0f;
    
    [Header("Elemental Effects")]
    public bool hasBurnEffect = false;
    public float burnDamagePerSecond = 0f;
    public float burnDuration = 0f;
    
    public bool hasPoisonEffect = false;
    public float poisonDamagePerSecond = 0f;
    public float poisonDuration = 0f;
    
    public bool hasFreezeEffect = false;
    public float freezeSlowPercent = 0f;
    public float freezeDuration = 0f;
    
    public bool hasShockEffect = false;
    public float shockChainRange = 0f;
    public int shockChainCount = 0;
    public float shockDamage = 0f;
    
    // Calculated properties
    public float GetTotalDamage()
    {
        return (baseDamage * damageMultiplier) + damageBonus;
    }
    
    public float GetTotalFireRate()
    {
        return Mathf.Max(0.1f, (baseFireRate * fireRateMultiplier) + fireRateBonus);
    }
    
    public int GetTotalMagazineSize()
    {
        return Mathf.Max(1, baseMagazineSize + magazineSizeBonus);
    }
    
    public float GetTotalReloadTime()
    {
        return Mathf.Max(0.1f, (baseReloadSpeed * reloadSpeedMultiplier) + reloadSpeedBonus);
    }
    
    public float GetTotalVelocity()
    {
        return Mathf.Max(1f, (bulletVelocity * velocityMultiplier) + velocityBonus);
    }
    
    public float GetTotalLifetime()
    {
        return Mathf.Max(0.1f, (projectileLifetime * lifetimeMultiplier) + lifetimeBonus);
    }
    
    /// <summary>
    /// Creates a deep copy of the weapon stats
    /// </summary>
    public WeaponStats Clone()
    {
        return (WeaponStats)this.MemberwiseClone();
    }
}
