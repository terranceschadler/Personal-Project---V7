using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// ScriptableObject that defines a weapon upgrade pickup.
/// Can contain multiple upgrade effects in a single pickup.
/// Fully configurable in the Inspector.
/// </summary>
[CreateAssetMenu(fileName = "New Upgrade Pickup", menuName = "Weapon System/Upgrade Pickup")]
public class UpgradePickup : ScriptableObject
{
    [Header("Pickup Identity")]
    [Tooltip("Display name shown to player")]
    public string pickupName = "Weapon Upgrade";
    
    [Tooltip("Icon shown in UI")]
    public Sprite icon;
    
    [Tooltip("Rarity/tier of this upgrade")]
    public UpgradeRarity rarity = UpgradeRarity.Common;
    
    [Header("Visual Settings")]
    [Tooltip("Color tint for pickup GameObject")]
    public Color pickupColor = Color.white;
    
    [Tooltip("Particle effect prefab (optional)")]
    public GameObject particleEffectPrefab;
    
    [Header("Upgrade Effects")]
    [Tooltip("All effects this pickup will apply when collected")]
    public List<UpgradeEffect> effects = new List<UpgradeEffect>();
    
    [Header("Audio")]
    [Tooltip("Sound played when pickup is collected")]
    public AudioClip pickupSound;
    
    /// <summary>
    /// Applies all upgrade effects to the given weapon stats
    /// </summary>
    public void ApplyUpgrade(WeaponStats stats)
    {
        foreach (var effect in effects)
        {
            effect.ApplyToStats(stats);
        }
    }
    
    /// <summary>
    /// Gets a complete description of all effects
    /// </summary>
    public string GetFullDescription()
    {
        if (effects.Count == 0)
            return "No effects";
        
        string description = "";
        foreach (var effect in effects)
        {
            if (description.Length > 0)
                description += "\n";
            description += "• " + effect.GetFormattedDescription();
        }
        return description;
    }
}

/// <summary>
/// Rarity/tier system for upgrades
/// </summary>
public enum UpgradeRarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary
}
