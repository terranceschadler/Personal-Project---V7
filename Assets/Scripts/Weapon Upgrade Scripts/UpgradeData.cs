using UnityEngine;
using System;
using System.Collections.Generic;

[Serializable]
public enum UpgradeArchetype
{
    Damage,
    Defense,
    Speed,
    Utility,
    Elemental,
    Summon
}

[Serializable]
public enum UpgradeTier
{
    Common,
    Rare,
    Epic,
    Legendary
}

[Serializable]
public class UpgradeData
{
    public string upgradeName;
    public string description;
    public UpgradeArchetype archetype;
    public UpgradeTier tier;
    public Sprite icon;
    public Color tierColor;
    
    // Stat modifications
    public float damageMultiplier = 1f;
    public float defenseBonus = 0f;
    public float speedMultiplier = 1f;
    public float healthBonus = 0f;
    public float criticalChance = 0f;
    public float criticalDamage = 0f;
    public float cooldownReduction = 0f;
    public float areaOfEffect = 0f;
    public float projectileCount = 0f;
    public float lifesteal = 0f;
    
    // Special effects
    public bool hasSpecialEffect;
    public string specialEffectDescription;
    
    public UpgradeData()
    {
        upgradeName = "";
        description = "";
    }
    
    public string GetFormattedDescription()
    {
        List<string> statLines = new List<string>();
        
        if (damageMultiplier != 1f)
            statLines.Add($"+{((damageMultiplier - 1f) * 100f):F0}% Damage");
        if (defenseBonus != 0f)
            statLines.Add($"+{defenseBonus:F0} Defense");
        if (speedMultiplier != 1f)
            statLines.Add($"+{((speedMultiplier - 1f) * 100f):F0}% Speed");
        if (healthBonus != 0f)
            statLines.Add($"+{healthBonus:F0} Health");
        if (criticalChance != 0f)
            statLines.Add($"+{(criticalChance * 100f):F1}% Critical Chance");
        if (criticalDamage != 0f)
            statLines.Add($"+{(criticalDamage * 100f):F0}% Critical Damage");
        if (cooldownReduction != 0f)
            statLines.Add($"-{(cooldownReduction * 100f):F0}% Cooldown");
        if (areaOfEffect != 0f)
            statLines.Add($"+{(areaOfEffect * 100f):F0}% Area of Effect");
        if (projectileCount != 0f)
            statLines.Add($"+{projectileCount:F0} Projectiles");
        if (lifesteal != 0f)
            statLines.Add($"+{(lifesteal * 100f):F1}% Lifesteal");
            
        if (hasSpecialEffect && !string.IsNullOrEmpty(specialEffectDescription))
            statLines.Add($"\n<i>{specialEffectDescription}</i>");
        
        return string.Join("\n", statLines);
    }
}
