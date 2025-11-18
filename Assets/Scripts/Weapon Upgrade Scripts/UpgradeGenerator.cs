using UnityEngine;
using System.Collections.Generic;

public class UpgradeGenerator : MonoBehaviour
{
    [Header("Tier Settings")]
    [SerializeField] private TierSettings commonTier;
    [SerializeField] private TierSettings rareTier;
    [SerializeField] private TierSettings epicTier;
    [SerializeField] private TierSettings legendaryTier;
    
    [Header("Default Icons")]
    [SerializeField] private Sprite damageIcon;
    [SerializeField] private Sprite defenseIcon;
    [SerializeField] private Sprite speedIcon;
    [SerializeField] private Sprite utilityIcon;
    [SerializeField] private Sprite elementalIcon;
    [SerializeField] private Sprite summonIcon;
    
    private Dictionary<UpgradeArchetype, string[]> archetypeNames = new Dictionary<UpgradeArchetype, string[]>()
    {
        { UpgradeArchetype.Damage, new[] { "Power Strike", "Fury", "Devastation", "Rampage", "Carnage", "Destruction" } },
        { UpgradeArchetype.Defense, new[] { "Iron Skin", "Fortification", "Bulwark", "Guardian", "Resilience", "Aegis" } },
        { UpgradeArchetype.Speed, new[] { "Swift Step", "Velocity", "Haste", "Lightning", "Acceleration", "Momentum" } },
        { UpgradeArchetype.Utility, new[] { "Efficiency", "Mastery", "Precision", "Focus", "Enhancement", "Synergy" } },
        { UpgradeArchetype.Elemental, new[] { "Inferno", "Frost", "Storm", "Venom", "Radiance", "Shadow" } },
        { UpgradeArchetype.Summon, new[] { "Companion", "Minion", "Familiar", "Conjuration", "Manifestation", "Legion" } }
    };
    
    private Dictionary<UpgradeArchetype, string[]> specialEffects = new Dictionary<UpgradeArchetype, string[]>()
    {
        { UpgradeArchetype.Damage, new[] { "Attacks have a chance to deal double damage", "Consecutive hits increase damage", "Low health enemies take extra damage", "Damage increases with missing health" } },
        { UpgradeArchetype.Defense, new[] { "Absorb a fatal blow once per battle", "Reduce damage when health is low", "Reflect a portion of damage taken", "Gain temporary shield on kill" } },
        { UpgradeArchetype.Speed, new[] { "Dodge chance increases with speed", "Movement leaves a damaging trail", "Dash ability cooldown reduced", "Attack speed increases when moving" } },
        { UpgradeArchetype.Utility, new[] { "Collect experience from further away", "Gain gold on critical hits", "Reduce all ability cooldowns on kill", "Chance to not consume resources" } },
        { UpgradeArchetype.Elemental, new[] { "Attacks chain to nearby enemies", "Enemies explode on death", "Apply burning/freezing status", "Create elemental zones on hit" } },
        { UpgradeArchetype.Summon, new[] { "Summon orbital projectiles", "Spawn minions periodically", "Allies inherit your stats", "Summons explode on expiration" } }
    };
    
    public UpgradeData GenerateUpgrade(UpgradeArchetype archetype, UpgradeTier tier)
    {
        UpgradeData upgrade = new UpgradeData();
        upgrade.archetype = archetype;
        upgrade.tier = tier;
        
        TierSettings settings = GetTierSettings(tier);
        upgrade.tierColor = settings.tierColor;
        
        // Generate name
        string[] names = archetypeNames[archetype];
        upgrade.upgradeName = names[Random.Range(0, names.Length)] + " " + GetTierSuffix(tier);
        
        // Generate stats based on archetype and tier
        GenerateStatsForArchetype(upgrade, archetype, settings);
        
        // Add special effect for higher tiers
        if (tier >= UpgradeTier.Epic && Random.value < settings.specialEffectChance)
        {
            upgrade.hasSpecialEffect = true;
            string[] effects = specialEffects[archetype];
            upgrade.specialEffectDescription = effects[Random.Range(0, effects.Length)];
        }
        
        // Set icon
        upgrade.icon = GetIconForArchetype(archetype);
        
        // Generate description
        upgrade.description = upgrade.GetFormattedDescription();
        
        return upgrade;
    }
    
    private void GenerateStatsForArchetype(UpgradeData upgrade, UpgradeArchetype archetype, TierSettings settings)
    {
        float multiplier = settings.statMultiplier;
        
        switch (archetype)
        {
            case UpgradeArchetype.Damage:
                upgrade.damageMultiplier = 1f + Random.Range(0.1f, 0.3f) * multiplier;
                upgrade.criticalChance = Random.Range(0.02f, 0.08f) * multiplier;
                upgrade.criticalDamage = Random.Range(0.2f, 0.5f) * multiplier;
                if (Random.value > 0.5f)
                    upgrade.projectileCount = Random.Range(1, 3) * Mathf.Floor(multiplier);
                break;
                
            case UpgradeArchetype.Defense:
                upgrade.defenseBonus = Random.Range(5f, 15f) * multiplier;
                upgrade.healthBonus = Random.Range(20f, 50f) * multiplier;
                if (Random.value > 0.5f)
                    upgrade.damageMultiplier = 1f - Random.Range(0.05f, 0.15f); // Trade damage for defense
                break;
                
            case UpgradeArchetype.Speed:
                upgrade.speedMultiplier = 1f + Random.Range(0.1f, 0.25f) * multiplier;
                upgrade.cooldownReduction = Random.Range(0.05f, 0.15f) * multiplier;
                if (Random.value > 0.5f)
                    upgrade.damageMultiplier = 1f + Random.Range(0.05f, 0.15f) * multiplier;
                break;
                
            case UpgradeArchetype.Utility:
                upgrade.cooldownReduction = Random.Range(0.1f, 0.25f) * multiplier;
                upgrade.lifesteal = Random.Range(0.03f, 0.1f) * multiplier;
                upgrade.criticalChance = Random.Range(0.03f, 0.1f) * multiplier;
                if (Random.value > 0.5f)
                    upgrade.areaOfEffect = Random.Range(0.1f, 0.3f) * multiplier;
                break;
                
            case UpgradeArchetype.Elemental:
                upgrade.damageMultiplier = 1f + Random.Range(0.15f, 0.35f) * multiplier;
                upgrade.areaOfEffect = Random.Range(0.15f, 0.4f) * multiplier;
                if (Random.value > 0.5f)
                    upgrade.criticalDamage = Random.Range(0.3f, 0.6f) * multiplier;
                break;
                
            case UpgradeArchetype.Summon:
                upgrade.damageMultiplier = 1f + Random.Range(0.05f, 0.15f) * multiplier;
                upgrade.projectileCount = Random.Range(1, 4) * Mathf.Floor(multiplier);
                upgrade.healthBonus = Random.Range(15f, 40f) * multiplier;
                break;
        }
    }
    
    private TierSettings GetTierSettings(UpgradeTier tier)
    {
        switch (tier)
        {
            case UpgradeTier.Common: return commonTier;
            case UpgradeTier.Rare: return rareTier;
            case UpgradeTier.Epic: return epicTier;
            case UpgradeTier.Legendary: return legendaryTier;
            default: return commonTier;
        }
    }
    
    private string GetTierSuffix(UpgradeTier tier)
    {
        switch (tier)
        {
            case UpgradeTier.Common: return "I";
            case UpgradeTier.Rare: return "II";
            case UpgradeTier.Epic: return "III";
            case UpgradeTier.Legendary: return "IV";
            default: return "";
        }
    }
    
    private Sprite GetIconForArchetype(UpgradeArchetype archetype)
    {
        switch (archetype)
        {
            case UpgradeArchetype.Damage: return damageIcon;
            case UpgradeArchetype.Defense: return defenseIcon;
            case UpgradeArchetype.Speed: return speedIcon;
            case UpgradeArchetype.Utility: return utilityIcon;
            case UpgradeArchetype.Elemental: return elementalIcon;
            case UpgradeArchetype.Summon: return summonIcon;
            default: return null;
        }
    }
}

[System.Serializable]
public class TierSettings
{
    public string tierName;
    public Color tierColor = Color.white;
    public float statMultiplier = 1f;
    [Range(0f, 1f)]
    public float specialEffectChance = 0f;
}
