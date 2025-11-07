using UnityEngine;

/// <summary>
/// Defines all possible upgrade types in the game
/// </summary>
public enum UpgradeType
{
    // Damage upgrades
    DamageFlat,
    DamagePercent,
    
    // Fire rate upgrades
    FireRateFlat,
    FireRatePercent,
    
    // Magazine upgrades
    MagazineSize,
    
    // Reload upgrades
    ReloadSpeedFlat,
    ReloadSpeedPercent,
    
    // Projectile upgrades
    BulletsPerShot,
    BulletSpread,
    BulletVelocityFlat,
    BulletVelocityPercent,
    ProjectileLifetimeFlat,
    ProjectileLifetimePercent,
    
    // Special effect upgrades
    CritChance,
    CritDamageMultiplier,
    PiercingCount,
    BounceCount,
    ExplosiveUnlock,
    ExplosionRadius,
    ExplosionDamage,
    HomingUnlock,
    HomingStrength,
    
    // Elemental upgrades - Burn
    BurnUnlock,
    BurnDamagePerSecond,
    BurnDuration,
    
    // Elemental upgrades - Poison
    PoisonUnlock,
    PoisonDamagePerSecond,
    PoisonDuration,
    
    // Elemental upgrades - Freeze
    FreezeUnlock,
    FreezeSlowPercent,
    FreezeDuration,
    
    // Elemental upgrades - Shock
    ShockUnlock,
    ShockChainRange,
    ShockChainCount,
    ShockDamage
}

/// <summary>
/// Represents a single upgrade effect that can be applied to weapon stats
/// </summary>
[System.Serializable]
public class UpgradeEffect
{
    [Tooltip("The type of upgrade this effect applies")]
    public UpgradeType upgradeType;
    
    [Tooltip("The value to add or multiply")]
    public float value;
    
    [Tooltip("Display name for UI")]
    public string displayName;
    
    [Tooltip("Description for UI")]
    [TextArea(2, 4)]
    public string description;
    
    /// <summary>
    /// Applies this upgrade effect to the given weapon stats
    /// </summary>
    public void ApplyToStats(WeaponStats stats)
    {
        switch (upgradeType)
        {
            // Damage
            case UpgradeType.DamageFlat:
                stats.damageBonus += value;
                break;
            case UpgradeType.DamagePercent:
                stats.damageMultiplier += value / 100f;
                break;
            
            // Fire Rate
            case UpgradeType.FireRateFlat:
                stats.fireRateBonus += value;
                break;
            case UpgradeType.FireRatePercent:
                stats.fireRateMultiplier += value / 100f;
                break;
            
            // Magazine
            case UpgradeType.MagazineSize:
                stats.magazineSizeBonus += (int)value;
                break;
            
            // Reload
            case UpgradeType.ReloadSpeedFlat:
                stats.reloadSpeedBonus -= value; // Negative because lower is better
                break;
            case UpgradeType.ReloadSpeedPercent:
                stats.reloadSpeedMultiplier -= value / 100f; // Negative because lower is better
                break;
            
            // Projectiles
            case UpgradeType.BulletsPerShot:
                stats.bulletsPerShot += (int)value;
                break;
            case UpgradeType.BulletSpread:
                stats.bulletSpread += value;
                break;
            case UpgradeType.BulletVelocityFlat:
                stats.velocityBonus += value;
                break;
            case UpgradeType.BulletVelocityPercent:
                stats.velocityMultiplier += value / 100f;
                break;
            case UpgradeType.ProjectileLifetimeFlat:
                stats.lifetimeBonus += value;
                break;
            case UpgradeType.ProjectileLifetimePercent:
                stats.lifetimeMultiplier += value / 100f;
                break;
            
            // Special Effects
            case UpgradeType.CritChance:
                stats.critChance = Mathf.Min(1f, stats.critChance + (value / 100f));
                break;
            case UpgradeType.CritDamageMultiplier:
                stats.critDamageMultiplier += value / 100f;
                break;
            case UpgradeType.PiercingCount:
                stats.piercingCount += (int)value;
                break;
            case UpgradeType.BounceCount:
                stats.bounceCount += (int)value;
                break;
            case UpgradeType.ExplosiveUnlock:
                stats.isExplosive = true;
                break;
            case UpgradeType.ExplosionRadius:
                stats.explosionRadius += value;
                break;
            case UpgradeType.ExplosionDamage:
                stats.explosionDamage += value;
                break;
            case UpgradeType.HomingUnlock:
                stats.isHoming = true;
                break;
            case UpgradeType.HomingStrength:
                stats.homingStrength += value;
                break;
            
            // Burn
            case UpgradeType.BurnUnlock:
                stats.hasBurnEffect = true;
                break;
            case UpgradeType.BurnDamagePerSecond:
                stats.burnDamagePerSecond += value;
                break;
            case UpgradeType.BurnDuration:
                stats.burnDuration += value;
                break;
            
            // Poison
            case UpgradeType.PoisonUnlock:
                stats.hasPoisonEffect = true;
                break;
            case UpgradeType.PoisonDamagePerSecond:
                stats.poisonDamagePerSecond += value;
                break;
            case UpgradeType.PoisonDuration:
                stats.poisonDuration += value;
                break;
            
            // Freeze
            case UpgradeType.FreezeUnlock:
                stats.hasFreezeEffect = true;
                break;
            case UpgradeType.FreezeSlowPercent:
                stats.freezeSlowPercent = Mathf.Min(0.95f, stats.freezeSlowPercent + (value / 100f));
                break;
            case UpgradeType.FreezeDuration:
                stats.freezeDuration += value;
                break;
            
            // Shock
            case UpgradeType.ShockUnlock:
                stats.hasShockEffect = true;
                break;
            case UpgradeType.ShockChainRange:
                stats.shockChainRange += value;
                break;
            case UpgradeType.ShockChainCount:
                stats.shockChainCount += (int)value;
                break;
            case UpgradeType.ShockDamage:
                stats.shockDamage += value;
                break;
        }
    }
    
    /// <summary>
    /// Gets a formatted description of this upgrade effect
    /// </summary>
    public string GetFormattedDescription()
    {
        if (!string.IsNullOrEmpty(description))
            return description;
        
        // Generate description if not provided
        string valueStr = value > 0 ? "+" + value.ToString("F1") : value.ToString("F1");
        
        switch (upgradeType)
        {
            case UpgradeType.DamageFlat:
                return $"{valueStr} Damage";
            case UpgradeType.DamagePercent:
                return $"{valueStr}% Damage";
            case UpgradeType.FireRateFlat:
                return $"{valueStr} Fire Rate";
            case UpgradeType.FireRatePercent:
                return $"{valueStr}% Fire Rate";
            case UpgradeType.MagazineSize:
                return $"{valueStr} Magazine Size";
            case UpgradeType.BulletsPerShot:
                return $"{valueStr} Bullets Per Shot";
            case UpgradeType.CritChance:
                return $"{valueStr}% Crit Chance";
            case UpgradeType.PiercingCount:
                return $"{valueStr} Piercing";
            case UpgradeType.ExplosiveUnlock:
                return "Unlock: Explosive Rounds";
            case UpgradeType.BurnUnlock:
                return "Unlock: Burn Effect";
            default:
                return upgradeType.ToString() + " " + valueStr;
        }
    }
}
